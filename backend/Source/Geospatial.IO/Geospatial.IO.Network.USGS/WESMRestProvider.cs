using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Amazon.S3.Model;
using Geospatial.GDALAssist;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility.Communication;
using Utility.Logging;
using Utility.Progress;
using Utility.Web;

namespace Geospatial.IO.Network.USGS;

public sealed class WESMRestProvider : TiffDataProviderBase
{
	private static readonly ILogger _logger = LogHelper.GetLogger("WESMRestProvider");

	private static readonly HttpClient _httpClient = SharedWebResourceManager.Client;

	private const string S3BucketName = "prd-tnm";

	private const int MaxRecordsPerQuery = 2000;

	private const int MaxTilesToScan = 100000;

	private const int MaxS3Folders = 50;

	private static string _baseQueryUrl;

	private static string _s3BaseUrl;

	private static string _awsS3Root;

	private S3Client _s3;

	private readonly ConcurrentDictionary<string, List<S3TileInfo>> _projectTileCache = new ConcurrentDictionary<string, List<S3TileInfo>>();

	private readonly ConcurrentDictionary<string, (Extent Bounds, long Size)> _tileBoundsCache = new ConcurrentDictionary<string, (Extent, long)>();

	private static readonly Projection WebMercator = new EPSGProjection(3857);

	private static readonly IReadOnlyList<DataResolution> _availableResolutions = new DataResolution[4]
	{
		DataResolution.OneMeter,
		DataResolution.OneNinthArcSecond,
		DataResolution.OneThirdArcSecond,
		DataResolution.OneArcSecond
	};

	public override string ProviderId => "WESM_REST";

	public override string DisplayName => "USGS Projects (WESM)";

	public override string Description => "High-resolution lidar DEMs via USGS 3DEP Elevation Index REST API (no file download required)";

	public override USGSDataType SupportedDataTypes => USGSDataType.ElevationModel;

	public override IReadOnlyList<DataResolution> AvailableResolutions => _availableResolutions;

	private static async Task EnsureUrlsResolvedAsync()
	{
		if (_baseQueryUrl == null)
		{
			_baseQueryUrl = await new FwLinkEndpoint("ras-2025-3dep-index-rest-api", "3DEP Index REST API").GetUrlOrThrowAsync();
		}
		if (_s3BaseUrl == null)
		{
			_s3BaseUrl = await new FwLinkEndpoint("ras-2025-tnm-s3-bucket", "TNM S3 bucket").GetUrlOrThrowAsync();
		}
		if (_awsS3Root == null)
		{
			_awsS3Root = await new FwLinkEndpoint("ras-2025-aws-s3-root", "AWS S3 root").GetUrlOrThrowAsync();
		}
	}

	private async Task EnsureS3ClientAsync()
	{
		if (_s3 == null)
		{
			await EnsureUrlsResolvedAsync();
			_s3 = new S3Client(_awsS3Root);
		}
	}

	protected override async Task<bool> PerformAvailabilityCheckAsync()
	{
		_ = 1;
		try
		{
			await EnsureUrlsResolvedAsync();
			using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Head, _baseQueryUrl);
			using HttpResponseMessage httpResponseMessage = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
			return httpResponseMessage.IsSuccessStatusCode;
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "Availability check failed for WESM REST API");
			return false;
		}
	}

	public override async Task<List<USGSItem>> QueryTilesAsync(Extent queryExtentWGS84, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		await EnsureUrlsResolvedAsync();
		await EnsureS3ClientAsync();
		reporter.ReportMessage("Querying USGS 3DEP index...");
		List<WESMProject> projects = await QueryOverlappingProjectsAsync(queryExtentWGS84, reporter);
		_logger.LogInformation("Found {Count} overlapping projects from REST API", projects.Count);
		if (projects.Count == 0)
		{
			return new List<USGSItem>();
		}
		if (projects.Count > 50)
		{
			throw new InvalidOperationException($"Query returned {projects.Count} S3 folders (max {50}). Zoom in to a smaller area.");
		}
		ConcurrentBag<WESMTileCandidate> allTiles = new ConcurrentBag<WESMTileCandidate>();
		int projectIdx = 0;
		await Parallel.ForEachAsync(projects, new ParallelOptions
		{
			MaxDegreeOfParallelism = 20
		}, async (WESMProject proj, CancellationToken ct) =>
		{
			int increment = Interlocked.Increment(ref projectIdx);
			reporter.ReportMessage($"Listing S3 folder {increment}/{projects.Count}: {proj.Project}");
			try
			{
				foreach (WESMTileCandidate projectTilesAsync in await GetProjectTilesAsync(proj.SourcedemLink, proj.Project, proj.Resolution))
				{
					allTiles.Add(projectTilesAsync);
				}
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "Failed to list tiles for project {Project}", proj.Project);
			}
		});
		_logger.LogInformation("Found {Count} total tiles to check", allTiles.Count);
		reporter.ReportMessage($"Found {allTiles.Count} tiles across {projects.Count} projects. Preparing to scan...");
		if (allTiles.Count == 0)
		{
			return new List<USGSItem>();
		}
		if (allTiles.Count > 100000)
		{
			throw new InvalidOperationException($"Query returned {allTiles.Count} tiles (max {100000}). Zoom in to a smaller area.");
		}
		WESMTileCandidate[] allTilesArray = allTiles.ToArray();
		ConcurrentBag<USGSItem> results = new ConcurrentBag<USGSItem>();
		int processedCount = 0;
		int matchCount = 0;
		int totalCount = allTilesArray.Length;
		await Parallel.ForEachAsync(allTilesArray, new ParallelOptions
		{
			MaxDegreeOfParallelism = 1000
		}, async (WESMTileCandidate tile, CancellationToken _) =>
		{
			try
			{
				TileBoundsResult tileBoundsResult = await GetTileBoundsAsync(tile.Url);
				if (tileBoundsResult.Success && tileBoundsResult.Bounds.Overlaps(in queryExtentWGS84))
				{
					DataResolution resolution = DataResolution.FromMeters(tile.ResolutionMeters);
					results.Add(new USGSItem
					{
						Title = Path.GetFileName(tile.Url),
						DownloadURL = tile.Url,
						SizeInBytes = ((tileBoundsResult.SizeInBytes > 0) ? tileBoundsResult.SizeInBytes : tile.Size),
						Extent = resolution.ToDisplayString(),
						Resolution = resolution,
						PublicationDate = null,
						BoundingBox = new BoundingBoxJson
						{
							MinX = tileBoundsResult.Bounds.MinX,
							MaxX = tileBoundsResult.Bounds.MaxX,
							MinY = tileBoundsResult.Bounds.MinY,
							MaxY = tileBoundsResult.Bounds.MaxY
						}
					});
					Interlocked.Increment(ref matchCount);
				}
			}
			catch (Exception exception)
			{
				_logger.LogTrace(exception, "Failed to read bounds for {Url}", tile.Url);
			}
			int increment2 = Interlocked.Increment(ref processedCount);
			if (increment2 % 100 == 0 || increment2 == totalCount)
			{
				reporter.ReportMessage($"Reading tile headers: {increment2}/{totalCount} ({Volatile.Read(in matchCount)} match)");
				reporter.ReportProgressFraction(0.3f + (float)increment2 / (float)totalCount * 0.7f);
			}
		});
		reporter.ReportProgress(100);
		_logger.LogInformation("Found {Count} tiles overlapping query extent", matchCount);
		return results.ToList();
	}

	private async Task<List<WESMProject>> QueryOverlappingProjectsAsync(Extent extentWGS84, ProgressReporter reporter)
	{
		List<WESMProject> projects = new List<WESMProject>();
		Extent boundingBox = Projector.GetBoundingBox(extentWGS84, WellKnownProjections.WGS84, WebMercator);
		string geometryJson = $"{boundingBox.MinX},{boundingBox.MinY},{boundingBox.MaxX},{boundingBox.MaxY}";
		int offset = 0;
		bool condition = true;
		while (condition)
		{
			Dictionary<string, string> source = new Dictionary<string, string>
			{
				["where"] = "sourcedem_link IS NOT NULL AND sourcedem_link <> ''",
				["geometry"] = geometryJson,
				["geometryType"] = "esriGeometryEnvelope",
				["spatialRel"] = "esriSpatialRelIntersects",
				["inSR"] = "3857",
				["outFields"] = "project,workunit,sourcedem_link,dem_gsd_meters",
				["returnGeometry"] = "false",
				["resultOffset"] = offset.ToString(),
				["resultRecordCount"] = 2000.ToString(),
				["f"] = "json"
			};
			string sourceKvpTerm = string.Join("&", source.Select((KeyValuePair<string, string> kvp) => kvp.Key + "=" + HttpUtility.UrlEncode(kvp.Value)));
			string requestUri = _baseQueryUrl + "?" + sourceKvpTerm;
			try
			{
				JsonElement rootElement = JsonDocument.Parse(await _httpClient.GetStringAsync(requestUri)).RootElement;
				if (rootElement.TryGetProperty("features", out var localValue))
				{
					int ordinal = 0;
					foreach (JsonElement enumerateArray in localValue.EnumerateArray())
					{
						if (enumerateArray.TryGetProperty("attributes", out var localValue2))
						{
							projects.Add(new WESMProject
							{
								Project = GetStringProperty(localValue2, "project"),
								Workunit = GetStringProperty(localValue2, "workunit"),
								SourcedemLink = GetStringProperty(localValue2, "sourcedem_link"),
								Resolution = GetDoubleProperty(localValue2, "dem_gsd_meters")
							});
							ordinal++;
						}
					}
					condition = ordinal == 2000;
					offset += ordinal;
					if (condition)
					{
						reporter.ReportMessage($"Querying USGS 3DEP index... ({projects.Count} projects found, fetching more...)");
					}
				}
				else
				{
					condition = false;
				}
				if (rootElement.TryGetProperty("error", out var localValue3))
				{
					string getString = (localValue3.TryGetProperty("message", out var localValue4) ? localValue4.GetString() : "Unknown error");
					_logger.LogWarning("REST API error: {Message}", getString);
					condition = false;
				}
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "Failed to query REST API at offset {Offset}", offset);
				condition = false;
			}
		}
		return (from p in projects
			group p by p.SourcedemLink into g
			select g.First()).ToList();
	}

	private static string GetStringProperty(JsonElement element, string propertyName)
	{
		if (element.TryGetProperty(propertyName, out var localValue) && localValue.ValueKind == JsonValueKind.String)
		{
			return localValue.GetString();
		}
		return null;
	}

	private static double GetDoubleProperty(JsonElement element, string propertyName)
	{
		if (element.TryGetProperty(propertyName, out var localValue) && localValue.ValueKind == JsonValueKind.Number)
		{
			return localValue.GetDouble();
		}
		return 0.0;
	}

	private async Task<List<WESMTileCandidate>> GetProjectTilesAsync(string sourcedemLink, string project, double resolution)
	{
		if (string.IsNullOrEmpty(sourcedemLink))
		{
			return new List<WESMTileCandidate>();
		}
		if (_projectTileCache.TryGetValue(sourcedemLink, out var localValue))
		{
			return localValue.Select((S3TileInfo t) => new WESMTileCandidate(t.Url, t.Size, project, resolution)).ToList();
		}
		string prefix = ExtractS3Prefix(sourcedemLink);
		if (string.IsNullOrEmpty(prefix))
		{
			_logger.LogTrace("Could not extract S3 prefix from {Link}", sourcedemLink);
			return new List<WESMTileCandidate>();
		}
		try
		{
			List<S3TileInfo> projectTileCache = (from o in await _s3.ListObjectsAsync("prd-tnm", prefix)
				where o.Key.EndsWith(".tif", StringComparison.OrdinalIgnoreCase)
				select new S3TileInfo
				{
					Url = _s3BaseUrl + "/" + o.Key,
					Size = o.Size.GetValueOrDefault()
				}).ToList();
			_projectTileCache[sourcedemLink] = projectTileCache;
			return projectTileCache.Select((S3TileInfo t) => new WESMTileCandidate(t.Url, t.Size, project, resolution)).ToList();
		}
		catch (Exception exception)
		{
			_logger.LogTrace(exception, "Failed to list S3 objects for prefix {Prefix}", prefix);
			return new List<WESMTileCandidate>();
		}
	}

	private async Task<TileBoundsResult> GetTileBoundsAsync(string url, CancellationToken ct = default(CancellationToken))
	{
		if (_tileBoundsCache.TryGetValue(url, out (Extent, long) valueTuple))
		{
			return new TileBoundsResult(valueTuple.Item1, valueTuple.Item2, success: true);
		}
		TileBoundsResult result = await TiffHeaderReader.ReadBoundsAndSizeAsync(url, ct);
		if (result.Success)
		{
			_tileBoundsCache[url] = (result.Bounds, result.SizeInBytes);
		}
		return result;
	}

	private static string ExtractS3Prefix(string sourcedemLink)
	{
		if (string.IsNullOrEmpty(sourcedemLink))
		{
			return null;
		}
		if (!Uri.TryCreate(sourcedemLink, UriKind.Absolute, out Uri result))
		{
			return null;
		}
		string parseQueryString = HttpUtility.ParseQueryString(result.Query)["prefix"];
		if (!string.IsNullOrEmpty(parseQueryString))
		{
			return parseQueryString;
		}
		if (result.Host.Contains("prd-tnm.s3.amazonaws.com", StringComparison.OrdinalIgnoreCase))
		{
			return result.AbsolutePath.TrimStart('/');
		}
		return null;
	}

	protected override void Dispose(bool disposing)
	{
		if (!_disposed & disposing)
		{
			_s3?.Dispose();
		}
		base.Dispose(disposing);
	}
}
