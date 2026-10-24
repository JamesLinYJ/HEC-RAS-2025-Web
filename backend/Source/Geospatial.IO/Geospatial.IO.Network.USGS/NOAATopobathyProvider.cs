using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Progress;
using Utility.Web;

namespace Geospatial.IO.Network.USGS;

public sealed class NOAATopobathyProvider : TiffDataProviderBase
{
	private class TopobathyDatasetEntry
	{
		public string DirName { get; set; }

		public string NumericId { get; set; }

		public string Description { get; set; }

		public double MinX { get; set; }

		public double MaxX { get; set; }

		public double MinY { get; set; }

		public double MaxY { get; set; }

		public Extent ToExtent()
		{
			return new Extent(MinX, MaxX, MinY, MaxY);
		}
	}

	private static readonly ILogger _logger = LogHelper.GetLogger("NOAATopobathyProvider");

	private static readonly HttpClient _httpClient = SharedWebResourceManager.Client;

	private static string _s3BucketUrl;

	private static readonly string CacheDir = Path.Combine(Path.GetTempPath(), "NOAA_Topobathy_Cache");

	private static readonly string MasterIndexPath = Path.Combine(CacheDir, "master_index.json");

	private static readonly TimeSpan MasterIndexMaxAge = TimeSpan.FromDays(7);

	private List<TopobathyDatasetEntry> _masterIndex;

	private readonly SemaphoreSlim _masterIndexLock = new SemaphoreSlim(1, 1);

	private readonly ConcurrentDictionary<string, List<StacFeature>> _itemCache = new ConcurrentDictionary<string, List<StacFeature>>();

	private static readonly IReadOnlyList<DataResolution> Resolutions = new DataResolution[2]
	{
		DataResolution.OneMeter,
		DataResolution.FiveMeter
	};

	public override string ProviderId => "NOAATopobathy";

	public override string DisplayName => "NOAA Coastal Lidar";

	public override string Description => "NOAA Digital Coast lidar DEMs (topobathy, USACE, NGS, state lidar, and more)";

	public override USGSDataType SupportedDataTypes => USGSDataType.ElevationModel;

	public override IReadOnlyList<DataResolution> AvailableResolutions => Resolutions;

	private static async Task EnsureUrlResolvedAsync()
	{
		if (_s3BucketUrl == null)
		{
			_s3BucketUrl = await new FwLinkEndpoint("ras-2025-noaa-topobathy-s3", "NOAA Topobathy S3").GetUrlOrThrowAsync();
		}
	}

	protected override async Task<bool> PerformAvailabilityCheckAsync()
	{
		_ = 1;
		try
		{
			await EnsureUrlResolvedAsync();
			using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Head, _s3BucketUrl + "/dem/index.html");
			using HttpResponseMessage httpResponseMessage = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
			return httpResponseMessage.IsSuccessStatusCode;
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "Availability check failed for NOAATopobathy");
			return false;
		}
	}

	public override async Task<List<USGSItem>> QueryTilesAsync(Extent queryExtentWGS84, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		await EnsureUrlResolvedAsync();
		await EnsureMasterIndexAsync(reporter);
		if (_masterIndex == null || _masterIndex.Count == 0)
		{
			return new List<USGSItem>();
		}
		List<TopobathyDatasetEntry> overlapping = _masterIndex.Where((TopobathyDatasetEntry d) => d.ToExtent().Overlaps(in queryExtentWGS84)).ToList();
		if (overlapping.Count == 0)
		{
			reporter.ReportMessage("No NOAA topobathy datasets found in this area");
			return new List<USGSItem>();
		}
		reporter.ReportMessage($"Found {overlapping.Count} NOAA topobathy datasets overlapping area");
		ConcurrentBag<USGSItem> results = new ConcurrentBag<USGSItem>();
		int completedDatasets = 0;
		await Parallel.ForEachAsync(overlapping, new ParallelOptions
		{
			MaxDegreeOfParallelism = 4
		}, async (TopobathyDatasetEntry dataset, CancellationToken ct) =>
		{
			try
			{
				foreach (USGSItem queryDatasetTilesAsync in await QueryDatasetTilesAsync(dataset, queryExtentWGS84))
				{
					results.Add(queryDatasetTilesAsync);
				}
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "Failed to query dataset {Dir}", dataset.DirName);
			}
			int increment = Interlocked.Increment(ref completedDatasets);
			if (increment % 5 == 0 || increment == overlapping.Count)
			{
				reporter.ReportMessage($"Querying datasets: {increment}/{overlapping.Count} ({results.Count} tiles found)");
				reporter.ReportProgressFraction((float)increment / (float)overlapping.Count);
			}
		});
		List<USGSItem> resultsList = results.ToList();
		_logger.LogInformation("NOAA Topobathy: found {Count} tiles across {DatasetCount} datasets", resultsList.Count, overlapping.Count);
		return resultsList;
	}

	private async Task EnsureMasterIndexAsync(ProgressReporter reporter)
	{
		if (_masterIndex != null)
		{
			return;
		}
		await _masterIndexLock.WaitAsync();
		try
		{
			if (_masterIndex == null)
			{
				if (await TryLoadMasterIndexFromDiskAsync())
				{
					_logger.LogInformation("Loaded NOAA topobathy master index from cache ({Count} datasets)", _masterIndex.Count);
				}
				else
				{
					await BuildMasterIndexAsync(reporter);
				}
			}
		}
		finally
		{
			_masterIndexLock.Release();
		}
	}

	private async Task<bool> TryLoadMasterIndexFromDiskAsync()
	{
		if (!File.Exists(MasterIndexPath))
		{
			return false;
		}
		if (DateTime.Now - File.GetLastWriteTime(MasterIndexPath) > MasterIndexMaxAge)
		{
			return false;
		}
		try
		{
			_masterIndex = JsonSerializer.Deserialize<List<TopobathyDatasetEntry>>(await File.ReadAllTextAsync(MasterIndexPath));
			return _masterIndex != null && _masterIndex.Count > 0;
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "Failed to load master index cache");
			return false;
		}
	}

	private async Task BuildMasterIndexAsync(ProgressReporter reporter)
	{
		reporter.ReportMessage("Discovering NOAA topobathy datasets...");
		List<string> datasetDirs = await ListTopobathyDatasetsFromS3Async();
		if (datasetDirs.Count == 0)
		{
			_masterIndex = new List<TopobathyDatasetEntry>();
			_logger.LogWarning("No topobathy datasets found on S3");
			return;
		}
		reporter.ReportMessage($"Fetching spatial extents for {datasetDirs.Count} topobathy datasets...");
		ConcurrentBag<TopobathyDatasetEntry> entries = new ConcurrentBag<TopobathyDatasetEntry>();
		int completed = 0;
		await Parallel.ForEachAsync(datasetDirs, new ParallelOptions
		{
			MaxDegreeOfParallelism = 20
		}, async (string dirName, CancellationToken ct) =>
		{
			try
			{
				TopobathyDatasetEntry topobathyDatasetEntry = await FetchDatasetExtentAsync(dirName);
				if (topobathyDatasetEntry != null)
				{
					entries.Add(topobathyDatasetEntry);
				}
			}
			catch (Exception exception2)
			{
				_logger.LogTrace(exception2, "Failed to fetch extent for {Dir}", dirName);
			}
			int increment = Interlocked.Increment(ref completed);
			if (increment % 10 == 0 || increment == datasetDirs.Count)
			{
				reporter.ReportMessage($"Fetching extents: {increment}/{datasetDirs.Count} ({entries.Count} indexed)");
			}
		});
		_masterIndex = entries.ToList();
		try
		{
			Directory.CreateDirectory(CacheDir);
			string contents = JsonSerializer.Serialize(_masterIndex);
			await File.WriteAllTextAsync(MasterIndexPath, contents);
			_logger.LogInformation("Cached NOAA topobathy master index ({Count} datasets)", _masterIndex.Count);
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Failed to cache master index to disk");
		}
	}

	private async Task<List<string>> ListTopobathyDatasetsFromS3Async()
	{
		List<string> datasets = new List<string>();
		string firstOrDefaultDescendantsTerm = null;
		do
		{
			string escapeDataStringUriTerm = _s3BucketUrl + "?list-type=2&prefix=dem/&delimiter=/&max-keys=1000";
			if (firstOrDefaultDescendantsTerm != null)
			{
				escapeDataStringUriTerm = escapeDataStringUriTerm + "&continuation-token=" + Uri.EscapeDataString(firstOrDefaultDescendantsTerm);
			}
			using (HttpResponseMessage response = await _httpClient.GetAsync(escapeDataStringUriTerm))
			{
				if (!response.IsSuccessStatusCode)
				{
					_logger.LogWarning("S3 listing request failed with {Status}, returning {Count} datasets collected so far", response.StatusCode, datasets.Count);
					break;
				}
				string asStringAsync = await response.Content.ReadAsStringAsync();
				XDocument xDocument;
				try
				{
					xDocument = XDocument.Parse(asStringAsync);
				}
				catch (Exception exception)
				{
					_logger.LogWarning(exception, "Failed to parse S3 listing XML, returning {Count} datasets collected so far", datasets.Count);
					goto end_IL_00b8;
				}
				XNamespace xNamespace = xDocument.Root.Name.Namespace;
				foreach (XElement descendant in xDocument.Descendants(xNamespace + "CommonPrefixes"))
				{
					string elementDescendantTerm = descendant.Element(xNamespace + "Prefix")?.Value;
					if (!string.IsNullOrEmpty(elementDescendantTerm))
					{
						string trimEnd = elementDescendantTerm.TrimEnd('/');
						int lastIndexOf = trimEnd.LastIndexOf('/');
						if (lastIndexOf >= 0)
						{
							trimEnd = trimEnd.Substring(lastIndexOf + 1);
						}
						if (IsUsableDemDataset(trimEnd))
						{
							datasets.Add(trimEnd);
						}
					}
				}
				firstOrDefaultDescendantsTerm = ((!string.Equals(xDocument.Descendants(xNamespace + "IsTruncated").FirstOrDefault()?.Value, "true", StringComparison.OrdinalIgnoreCase)) ? null : xDocument.Descendants(xNamespace + "NextContinuationToken").FirstOrDefault()?.Value);
				continue;
				end_IL_00b8:;
			}
			break;
		}
		while (firstOrDefaultDescendantsTerm != null);
		_logger.LogInformation("Found {Count} topobathy dataset directories on S3", datasets.Count);
		return datasets;
	}

	private static bool IsUsableDemDataset(string dirName)
	{
		string toLowerInvariant = dirName.ToLowerInvariant();
		if (toLowerInvariant.StartsWith("slr_"))
		{
			return false;
		}
		if (toLowerInvariant.StartsWith("sf_mbs_") || toLowerInvariant.StartsWith("sf_sss_"))
		{
			return false;
		}
		if (toLowerInvariant.EndsWith("_retired"))
		{
			return false;
		}
		return true;
	}

	private static string ExtractNumericId(string dirName)
	{
		int lastIndexOf = dirName.LastIndexOf('_');
		if (lastIndexOf < 0 || lastIndexOf == dirName.Length - 1)
		{
			return null;
		}
		string substring = dirName.Substring(lastIndexOf + 1);
		if (!int.TryParse(substring, out var _))
		{
			return null;
		}
		return substring;
	}

	private async Task<TopobathyDatasetEntry> FetchDatasetExtentAsync(string dirName)
	{
		string numericId = ExtractNumericId(dirName);
		if (numericId == null)
		{
			return null;
		}
		string requestUri = $"{_s3BucketUrl}/dem/{dirName}/stac/noaa_dem_collection_m{numericId}.json";
		using HttpResponseMessage response = await _httpClient.GetAsync(requestUri);
		if (!response.IsSuccessStatusCode)
		{
			_logger.LogTrace("STAC collection not found for {Dir}: {Status}", dirName, response.StatusCode);
			return null;
		}
		StacCollection stacCollection = JsonSerializer.Deserialize<StacCollection>(await response.Content.ReadAsStringAsync());
		double[] doubleValues = stacCollection?.Extent?.Spatial?.Bbox?.FirstOrDefault();
		if (doubleValues == null || doubleValues.Length < 4)
		{
			return null;
		}
		return new TopobathyDatasetEntry
		{
			DirName = dirName,
			NumericId = numericId,
			Description = (stacCollection.Description ?? dirName),
			MinX = doubleValues[0],
			MinY = doubleValues[1],
			MaxX = doubleValues[2],
			MaxY = doubleValues[3]
		};
	}

	private async Task<List<USGSItem>> QueryDatasetTilesAsync(TopobathyDatasetEntry dataset, Extent queryExtentWGS84)
	{
		List<StacFeature> orFetchItemsAsync = await GetOrFetchItemsAsync(dataset);
		if (orFetchItemsAsync == null || orFetchItemsAsync.Count == 0)
		{
			return new List<USGSItem>();
		}
		List<USGSItem> usgsItems = new List<USGSItem>();
		foreach (StacFeature orFetchItemsAsync2 in orFetchItemsAsync)
		{
			Extent? bboxAsExtent = orFetchItemsAsync2.GetBboxAsExtent();
			if (bboxAsExtent.HasValue && bboxAsExtent.Value.Overlaps(in queryExtentWGS84))
			{
				string downloadUrl = orFetchItemsAsync2.GetDownloadUrl();
				if (!string.IsNullOrEmpty(downloadUrl))
				{
					DataResolution resolution = ParseResolution(orFetchItemsAsync2, dataset);
					string title = FormatDisplayName(orFetchItemsAsync2.Id, dataset.DirName);
					usgsItems.Add(new USGSItem
					{
						Title = title,
						DownloadURL = downloadUrl,
						Resolution = resolution,
						Extent = resolution.ToDisplayString(),
						PublicationDate = orFetchItemsAsync2.Properties?.Datetime,
						BoundingBox = new BoundingBoxJson
						{
							MinX = orFetchItemsAsync2.Bbox[0],
							MinY = orFetchItemsAsync2.Bbox[1],
							MaxX = orFetchItemsAsync2.Bbox[2],
							MaxY = orFetchItemsAsync2.Bbox[3]
						}
					});
				}
			}
		}
		return usgsItems;
	}

	private async Task<List<StacFeature>> GetOrFetchItemsAsync(TopobathyDatasetEntry dataset)
	{
		if (_itemCache.TryGetValue(dataset.DirName, out var localValue))
		{
			return localValue;
		}
		string requestUri = $"{_s3BucketUrl}/dem/{dataset.DirName}/stac/noaa_dem_item_collection_m{dataset.NumericId}.json";
		try
		{
			using HttpResponseMessage response = await _httpClient.GetAsync(requestUri);
			if (!response.IsSuccessStatusCode)
			{
				_logger.LogTrace("STAC items not found for {Dir}: {Status}", dataset.DirName, response.StatusCode);
				return null;
			}
			StacItemCollection stacItemCollection = JsonSerializer.Deserialize<StacItemCollection>(await response.Content.ReadAsStringAsync());
			if (stacItemCollection?.Features == null)
			{
				return null;
			}
			_itemCache[dataset.DirName] = stacItemCollection.Features;
			return stacItemCollection.Features;
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Failed to fetch items for {Dir}", dataset.DirName);
			return null;
		}
	}

	private static DataResolution ParseResolution(StacFeature feature, TopobathyDatasetEntry dataset)
	{
		string id = feature.Id ?? dataset.DirName;
		if (id.Contains("1mGrid") || id.Contains("_1m"))
		{
			return DataResolution.OneMeter;
		}
		if (id.Contains("5mGrid") || id.Contains("_5m"))
		{
			return DataResolution.FiveMeter;
		}
		if (id.Contains("3mGrid") || id.Contains("_3m"))
		{
			return DataResolution.OneNinthArcSecond;
		}
		return DataResolution.OneMeter;
	}

	private static string FormatDisplayName(string featureId, string datasetName)
	{
		if (string.IsNullOrEmpty(featureId))
		{
			return datasetName;
		}
		string replace = datasetName.Replace("_DEM_", " ").Replace("_Topobathy_Lidar_", " ").Replace("_Topobathy_", " ")
			.Replace('_', ' ');
		int lastIndexOf = replace.LastIndexOf(' ');
		if (lastIndexOf > 0 && int.TryParse(replace.AsSpan(lastIndexOf + 1), out var _))
		{
			replace = replace.Substring(0, lastIndexOf);
		}
		return featureId + " [" + replace + "]";
	}

	protected override void Dispose(bool disposing)
	{
		if (!_disposed & disposing)
		{
			_masterIndexLock.Dispose();
			_itemCache.Clear();
		}
		base.Dispose(disposing);
	}
}
