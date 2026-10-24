using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Geospatial.GDALAssist;
using Geospatial.GDALAssist.Vectors;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using Utility.Logging;
using Utility.Progress;
using Utility.Web;

namespace Geospatial.IO.Network.USGS;

public sealed class SeamlessDEMProvider : TiffDataProviderBase
{
	private readonly struct CandidateTile(string url, DataResolution resolution)
	{
		public string Url { get; } = url;

		public DataResolution Resolution { get; } = resolution;
	}

	[Flags]
	public enum DatasetFlags
	{
		None = 0,
		OneArcSecond = 1,
		OneThirdArcSecond = 2,
		TwoArcSecond = 4,
		S1M = 8,
		All = OneArcSecond | OneThirdArcSecond | TwoArcSecond | S1M
	}

	private static readonly ILogger _logger = LogHelper.GetLogger("SeamlessDEMProvider");

	private static readonly HttpClient _httpClient = SharedWebResourceManager.Client;

	private static string _s3BaseUrl;

	private static string _s1mIndexUrl;

	private VectorDataset _s1mIndex;

	private string _s1mIndexPath;

	private readonly SemaphoreSlim _s1mLock = new SemaphoreSlim(1, 1);

	private readonly ConcurrentDictionary<string, (Extent Bounds, long SizeInBytes)> _tileBoundsCache = new ConcurrentDictionary<string, (Extent, long)>();

	private static readonly Projection S1MProjection = new EPSGProjection(6350);

	private static readonly IReadOnlyList<DataResolution> _availableResolutions = new DataResolution[4]
	{
		DataResolution.OneMeter,
		DataResolution.OneThirdArcSecond,
		DataResolution.OneArcSecond,
		DataResolution.TwoArcSecond
	};

	public override string ProviderId => "SeamlessDEM";

	public override string DisplayName => "USGS 3DEP Seamless";

	public override string Description => "Pre-processed national elevation datasets (1, 1/3, 2 arc-second, and 1-meter S1M)";

	public override USGSDataType SupportedDataTypes => USGSDataType.ElevationModel;

	public override IReadOnlyList<DataResolution> AvailableResolutions => _availableResolutions;

	public DatasetFlags EnabledDatasets { get; set; } = DatasetFlags.All;

	private static async Task EnsureUrlsResolvedAsync()
	{
		if (_s3BaseUrl == null)
		{
			_s3BaseUrl = await new FwLinkEndpoint("ras-2025-seamless-dem-s3", "Seamless DEM S3").GetUrlOrThrowAsync();
		}
		if (_s1mIndexUrl == null)
		{
			_s1mIndexUrl = await new FwLinkEndpoint("ras-2025-s1m-index-s3", "S1M Index S3").GetUrlOrThrowAsync();
		}
	}

	protected override async Task<bool> PerformAvailabilityCheckAsync()
	{
		_ = 1;
		try
		{
			await EnsureUrlsResolvedAsync();
			string requestUri = _s3BaseUrl + "/1/TIFF/current/n38w122/USGS_1_n38w122.tif";
			using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Head, requestUri);
			using HttpResponseMessage httpResponseMessage = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
			return httpResponseMessage.IsSuccessStatusCode;
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "Availability check failed for SeamlessDEM");
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
		List<USGSItem> results = new List<USGSItem>();
		results.AddRange(await QueryArcSecondTilesAsync(queryExtentWGS84, reporter));
		results.AddRange(await QueryS1MTilesAsync(queryExtentWGS84, reporter));
		reporter.ReportProgress(100);
		_logger.LogInformation("Found {Count} seamless tiles overlapping query extent", results.Count);
		return results;
	}

	private async Task<List<USGSItem>> QueryArcSecondTilesAsync(Extent queryExtentWGS84, ProgressReporter reporter)
	{
		List<CandidateTile> candidateTiles = new List<CandidateTile>();
		if (EnabledDatasets.HasFlag(DatasetFlags.OneArcSecond))
		{
			candidateTiles.AddRange(GenerateArcSecondTiles(queryExtentWGS84, 1, DataResolution.OneArcSecond));
		}
		if (EnabledDatasets.HasFlag(DatasetFlags.OneThirdArcSecond))
		{
			candidateTiles.AddRange(GenerateArcSecondTiles(queryExtentWGS84, 13, DataResolution.OneThirdArcSecond));
		}
		if (EnabledDatasets.HasFlag(DatasetFlags.TwoArcSecond))
		{
			candidateTiles.AddRange(GenerateArcSecondTiles(queryExtentWGS84, 2, DataResolution.TwoArcSecond));
		}
		if (candidateTiles.Count == 0)
		{
			return new List<USGSItem>();
		}
		ConcurrentBag<USGSItem> results = new ConcurrentBag<USGSItem>();
		int completedTasks = 0;
		int totalTasks = candidateTiles.Count;
		reporter.ReportMessage($"Checking {candidateTiles.Count} seamless tiles...");
		await Parallel.ForEachAsync(candidateTiles, new ParallelOptions
		{
			MaxDegreeOfParallelism = 50
		}, async (CandidateTile tile, CancellationToken ct) =>
		{
			try
			{
				TileBoundsResult tileBoundsResult = await GetTileBoundsAsync(tile.Url);
				if (tileBoundsResult.Success && tileBoundsResult.Bounds.Overlaps(in queryExtentWGS84))
				{
					string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(tile.Url);
					results.Add(new USGSItem
					{
						Title = fileNameWithoutExtension + " (" + tile.Resolution.ToDisplayString() + ")",
						DownloadURL = tile.Url,
						SizeInBytes = tileBoundsResult.SizeInBytes,
						Extent = tile.Resolution.ToDisplayString(),
						Resolution = tile.Resolution,
						PublicationDate = null,
						BoundingBox = new BoundingBoxJson
						{
							MinX = tileBoundsResult.Bounds.MinX,
							MaxX = tileBoundsResult.Bounds.MaxX,
							MinY = tileBoundsResult.Bounds.MinY,
							MaxY = tileBoundsResult.Bounds.MaxY
						}
					});
				}
			}
			catch (Exception exception)
			{
				_logger.LogTrace(exception, "Failed to check tile {Url}", tile.Url);
			}
			int increment = Interlocked.Increment(ref completedTasks);
			if (increment % 10 == 0 || increment == totalTasks)
			{
				reporter.ReportMessage($"Checking tiles: {increment}/{totalTasks} ({results.Count} found)");
				reporter.ReportProgressFraction((float)increment / (float)totalTasks * 0.5f);
			}
		});
		return results.ToList();
	}

	private List<CandidateTile> GenerateArcSecondTiles(Extent queryExtentWGS84, int datasetCode, DataResolution resolution)
	{
		List<CandidateTile> candidateTiles = new List<CandidateTile>();
		int minYQueryExtentWGS84Term = (int)Math.Floor(queryExtentWGS84.MinY);
		int count = (int)Math.Ceiling(queryExtentWGS84.MaxY);
		int minXQueryExtentWGS84Term = (int)Math.Floor(queryExtentWGS84.MinX);
		int count2 = (int)Math.Ceiling(queryExtentWGS84.MaxX);
		for (int i = minYQueryExtentWGS84Term; i <= count; i++)
		{
			for (int j = minXQueryExtentWGS84Term; j <= count2; j++)
			{
				int ordinal = i + 1;
				int ordinal2 = j;
				string message = ((ordinal >= 0) ? $"n{ordinal:D2}" : $"s{Math.Abs(ordinal):D2}");
				string message2 = ((ordinal2 <= 0) ? $"w{Math.Abs(ordinal2):D3}" : $"e{ordinal2:D3}");
				string messagePlusMessage2 = message + message2;
				string url = $"{_s3BaseUrl}/{datasetCode}/TIFF/current/{messagePlusMessage2}/USGS_{datasetCode}_{messagePlusMessage2}.tif";
				candidateTiles.Add(new CandidateTile(url, resolution));
			}
		}
		return candidateTiles;
	}

	private async Task<List<USGSItem>> QueryS1MTilesAsync(Extent queryExtentWGS84, ProgressReporter reporter)
	{
		if (!EnabledDatasets.HasFlag(DatasetFlags.S1M))
		{
			return new List<USGSItem>();
		}
		reporter.ReportMessage("Querying S1M tiles...");
		List<USGSItem> results = new List<USGSItem>();
		try
		{
			await EnsureS1MIndexLoadedAsync(reporter);
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Failed to load S1M index");
			return results;
		}
		if (_s1mIndex == null)
		{
			return results;
		}
		try
		{
			using VectorLayer vectorLayer = _s1mIndex.GetLayer(0);
			Layer ogrLayer = vectorLayer.OgrLayer;
			Extent boundingBox = Projector.GetBoundingBox(queryExtentWGS84, WellKnownProjections.WGS84, S1MProjection);
			ogrLayer.SetSpatialFilterRect(boundingBox.MinX, boundingBox.MinY, boundingBox.MaxX, boundingBox.MaxY);
			ogrLayer.SetAttributeFilter("published = 1");
			Feature nextFeature;
			while ((nextFeature = ogrLayer.GetNextFeature()) != null)
			{
				try
				{
					string fieldAsString = nextFeature.GetFieldAsString("dataset");
					if (string.IsNullOrEmpty(fieldAsString))
					{
						continue;
					}
					nextFeature.GetFieldAsString("tile");
					long fieldAsInteger = nextFeature.GetFieldAsInteger64("byte_count");
					string fieldAsString2 = nextFeature.GetFieldAsString("pub_date");
					using Geometry geometry = nextFeature.GetGeometryRef();
					if (geometry != null)
					{
						Envelope envelope = new Envelope();
						geometry.GetEnvelope(envelope);
						Extent boundingBox2 = Projector.GetBoundingBox(new Extent(envelope.MinX, envelope.MaxX, envelope.MinY, envelope.MaxY), S1MProjection, WellKnownProjections.WGS84);
						string downloadURL = _s3BaseUrl + "/" + fieldAsString;
						results.Add(new USGSItem
						{
							Title = Path.GetFileName(fieldAsString),
							DownloadURL = downloadURL,
							SizeInBytes = fieldAsInteger,
							Extent = "1 meter",
							Resolution = DataResolution.OneMeter,
							PublicationDate = fieldAsString2,
							BoundingBox = new BoundingBoxJson
							{
								MinX = boundingBox2.MinX,
								MaxX = boundingBox2.MaxX,
								MinY = boundingBox2.MinY,
								MaxY = boundingBox2.MaxY
							}
						});
					}
				}
				finally
				{
					nextFeature.Dispose();
				}
			}
			ogrLayer.ResetReading();
			ogrLayer.SetSpatialFilter(null);
			ogrLayer.SetAttributeFilter(null);
			reporter.ReportMessage($"Found {results.Count} S1M tiles");
			reporter.ReportProgressFraction(0.9f);
		}
		catch (Exception exception2)
		{
			_logger.LogWarning(exception2, "Error querying S1M index");
		}
		return results;
	}

	private async Task EnsureS1MIndexLoadedAsync(ProgressReporter reporter)
	{
		if (_s1mIndex != null)
		{
			return;
		}
		await _s1mLock.WaitAsync();
		try
		{
			if (_s1mIndex != null)
			{
				return;
			}
			string path = Path.Combine(Path.GetTempPath(), "USGS_S1M_Cache");
			Directory.CreateDirectory(path);
			string cachePath = Path.Combine(path, "S1M_Products.gpkg");
			if (!File.Exists(cachePath) || (DateTime.Now - File.GetLastWriteTime(cachePath)).TotalDays > 7.0)
			{
				reporter.ReportMessage("Downloading S1M index (1.5 MB)...");
				using HttpResponseMessage response = await _httpClient.GetAsync(_s1mIndexUrl, HttpCompletionOption.ResponseHeadersRead);
				response.EnsureSuccessStatusCode();
				using Stream stream = await response.Content.ReadAsStreamAsync();
				using FileStream fileStream = File.Create(cachePath);
				await stream.CopyToAsync(fileStream);
				_logger.LogInformation("Downloaded S1M index to {Path}", cachePath);
			}
			_s1mIndexPath = cachePath;
			_s1mIndex = new VectorDataset(cachePath);
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Failed to load S1M index");
		}
		finally
		{
			_s1mLock.Release();
		}
	}

	private async Task<TileBoundsResult> GetTileBoundsAsync(string url)
	{
		if (_tileBoundsCache.TryGetValue(url, out (Extent, long) valueTuple))
		{
			return new TileBoundsResult(valueTuple.Item1, valueTuple.Item2, success: true);
		}
		TileBoundsResult result = await TiffHeaderReader.ReadBoundsAndSizeAsync(url);
		if (result.Success)
		{
			_tileBoundsCache[url] = (result.Bounds, result.SizeInBytes);
		}
		return result;
	}

	protected override void Dispose(bool disposing)
	{
		if (!_disposed & disposing)
		{
			lock (_s1mLock)
			{
				_s1mIndex?.Dispose();
				_s1mIndex = null;
			}
		}
		base.Dispose(disposing);
	}
}
