using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Progress;
using Utility.Web;

namespace Geospatial.IO.Network.USGS;

public sealed class TNMApiProvider : TiffDataProviderBase
{
	private static readonly ILogger _logger = LogHelper.GetLogger("TNMApiProvider");

	private static readonly HttpClient _httpClient = SharedWebResourceManager.Client;

	private const string ProductsEndpoint = "products?";

	private const int MaxFilesToQuery = 1000;

	private static string _baseUrl;

	private static readonly Dictionary<USGSDataset, string> ElevationDatasetNames = new Dictionary<USGSDataset, string>
	{
		{
			USGSDataset.NED1ArcSecond,
			"National Elevation Dataset (NED) 1 arc-second"
		},
		{
			USGSDataset.NED1ThirdArcSecond,
			"National Elevation Dataset (NED) 1/3 arc-second"
		},
		{
			USGSDataset.DEM1Meter,
			"Digital Elevation Model (DEM) 1 meter"
		},
		{
			USGSDataset.NED1NinthArcSecond,
			"National Elevation Dataset (NED) 1/9 arc-second"
		},
		{
			USGSDataset.NEDAlaska2ArcSecond,
			"National Elevation Dataset (NED) Alaska 2 arc-second"
		},
		{
			USGSDataset.IfsarDEMAlaska5Meter,
			"Alaska IFSAR 5 meter DEM"
		},
		{
			USGSDataset.DEMOriginalProductResolution,
			"Original Product Resolution (OPR) Digital Elevation Model (DEM)"
		},
		{
			USGSDataset.IfsarDSM,
			"Ifsar Digital Surface Model (DSM)"
		}
	};

	private static readonly IReadOnlyList<DataResolution> _availableResolutions = new DataResolution[6]
	{
		DataResolution.OneMeter,
		DataResolution.OneNinthArcSecond,
		DataResolution.FiveMeter,
		DataResolution.OneThirdArcSecond,
		DataResolution.OneArcSecond,
		DataResolution.TwoArcSecond
	};

	public override string ProviderId => "TNM_API";

	public override string DisplayName => "USGS National Map";

	public override string Description => "The National Map Access API - comprehensive catalog of all published USGS elevation products";

	public override USGSDataType SupportedDataTypes => (USGSDataType)3;

	public override IReadOnlyList<DataResolution> AvailableResolutions => _availableResolutions;

	private static async Task EnsureUrlResolvedAsync()
	{
		if (_baseUrl == null)
		{
			_baseUrl = await new FwLinkEndpoint("ras-2025-tnm-access-rest-api", "TNM Access REST API").GetUrlOrThrowAsync();
		}
	}

	protected override async Task<bool> PerformAvailabilityCheckAsync()
	{
		_ = 1;
		try
		{
			await EnsureUrlResolvedAsync();
			string requestUri = _baseUrl + "products?max=1&datasets=National+Elevation+Dataset+(NED)+1+arc-second";
			using HttpResponseMessage httpResponseMessage = await _httpClient.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead);
			return httpResponseMessage.IsSuccessStatusCode;
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "Availability check failed for TNM API");
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
		string baseUrlTerm = _baseUrl + "products?";
		string message = $"&bbox={queryExtentWGS84.MinX},{queryExtentWGS84.MinY},{queryExtentWGS84.MaxX},{queryExtentWGS84.MaxY}";
		baseUrlTerm += message;
		baseUrlTerm += $"&max={1000}";
		baseUrlTerm += "&prodFormats=GeoTIFF";
		Dictionary<USGSDataset, string>.ValueCollection values = ElevationDatasetNames.Values;
		reporter.ReportMessage("Querying TNM API...");
		List<Task<USGSProductQuery>> queryTasks = new List<Task<USGSProductQuery>>();
		foreach (string textValue in values)
		{
			string requestUri = baseUrlTerm + "&datasets=" + textValue;
			queryTasks.Add(_httpClient.GetFromJsonAsync<USGSProductQuery>(requestUri));
		}
		try
		{
			await Task.WhenAll(queryTasks);
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "One or more TNM API dataset queries failed");
		}
		List<USGSItem> usgsItems = new List<USGSItem>();
		int ordinal = 0;
		Exception ex = null;
		foreach (Task<USGSProductQuery> queryTask in queryTasks)
		{
			if (queryTask.IsCompletedSuccessfully)
			{
				if (queryTask.Result?.Items != null)
				{
					usgsItems.AddRange(queryTask.Result.Items);
				}
				continue;
			}
			ordinal++;
			if (ex == null)
			{
				ex = queryTask.Exception?.GetBaseException();
			}
		}
		if (ordinal == queryTasks.Count && ex != null)
		{
			throw new InvalidOperationException($"All {ordinal} TNM API dataset queries failed against {_baseUrl}: {ex.Message}", ex);
		}
		if (ordinal > 0)
		{
			_logger.LogWarning("{Failed} of {Total} TNM API dataset queries failed", ordinal, queryTasks.Count);
		}
		foreach (USGSItem usgsItem in usgsItems)
		{
			if (usgsItem.Resolution == DataResolution.Unknown)
			{
				usgsItem.Resolution = DataResolution.ParseFromText(usgsItem.Title + " " + usgsItem.Extent);
			}
		}
		_logger.LogInformation("TNM API returned {Count} items", usgsItems.Count);
		reporter.ReportProgress(100);
		return usgsItems;
	}
}
