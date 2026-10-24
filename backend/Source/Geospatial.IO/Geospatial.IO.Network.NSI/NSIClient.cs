using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Geospatial.IO.Vectors;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Progress;
using Utility.Web;

namespace Geospatial.IO.Network.NSI;

public static class NSIClient
{
	public static class MessageCodes
	{
		public static readonly MessageClass EmptyResponse = new MessageClass("NSI_EmptyResponse", MessageSeverity.Information, "NSI query returned no structures for this area.");

		public static readonly MessageClass UnavailableAPI = new MessageClass("NSI_Unavailable", MessageSeverity.Error, "NSI API availability check failed");

		public static readonly MessageClass QueryTimedOut = new MessageClass("NSI_QueryTimedOut", MessageSeverity.Error, "NSI query timed out for {URL}. A smaller query extent may succeed.");
	}

	private static readonly ILogger _logger = LogHelper.GetLogger("NSIClient");

	private static readonly HttpClient _httpClient = SharedWebResourceManager.Client;

	private const string StructuresEndpoint = "/structures?bbox=";

	private static readonly FwLinkEndpoint _endpoint = new FwLinkEndpoint("1", "National Structure Inventory API");

	private static string _baseUrl;

	public static async Task<(OperationResult, List<NSIStructure>)> QueryStructuresAsync(Extent extent, ProgressReporter progressReporter = null, CancellationToken ct = default(CancellationToken))
	{
		return await QueryStructuresAsync(GetNSIBoundingBox(extent), progressReporter, ct);
	}

	public static async Task<(OperationResult, List<NSIStructure>)> QueryStructuresAsync(string bbox, ProgressReporter reporter = null, CancellationToken ct = default(CancellationToken))
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		bool baseUrlIsNull = _baseUrl == null;
		if (baseUrlIsNull)
		{
			baseUrlIsNull = !(await TryEnsureUrlResolvedAsync());
		}
		if (baseUrlIsNull)
		{
			Message error = MessageCodes.UnavailableAPI.Create();
			return (OperationResult.Fail(error), new List<NSIStructure>());
		}
		string url = _baseUrl + "/structures?bbox=" + bbox;
		Message msg = new Message("Querying NSI at \"{URL}\"", url);
		reporter.ReportMessage(msg);
		try
		{
			NSIFeatureCollection nSIFeatureCollection = await _httpClient.GetFromJsonAsync<NSIFeatureCollection>(url, ct);
			if (nSIFeatureCollection?.Features == null)
			{
				Message msg2 = MessageCodes.EmptyResponse.Create();
				reporter.ReportMessage(msg2);
				reporter.ReportProgress(100);
				return (OperationResult.Success(), new List<NSIStructure>());
			}
			List<NSIStructure> nsiStructures = new List<NSIStructure>(nSIFeatureCollection.Features.Count);
			foreach (NSIFeature feature in nSIFeatureCollection.Features)
			{
				if (feature.Properties != null)
				{
					GeoJsonGeometry geometry = feature.Geometry;
					if (geometry != null && geometry.Coordinates.ValueKind == JsonValueKind.Array && feature.Geometry.Coordinates.GetArrayLength() >= 2)
					{
						feature.Properties.X = feature.Geometry.Coordinates[0].GetDouble();
						feature.Properties.Y = feature.Geometry.Coordinates[1].GetDouble();
					}
					nsiStructures.Add(feature.Properties);
				}
			}
			Message msg3 = new Message("NSI returned \"{Count}\" structures", nsiStructures.Count);
			reporter.ReportMessage(msg3);
			reporter.ReportProgress(100);
			return (OperationResult.Success(), nsiStructures);
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			Message userOperationCancelled = GeneralMessageCodes.GetUserOperationCancelled("NSI query");
			reporter.ReportMessage(userOperationCancelled);
			return (OperationResult.Fail(userOperationCancelled), new List<NSIStructure>());
		}
		catch (Exception ex2)
		{
			_logger.LogError(ex2, "NSI query failed for {URL}", url);
			Message message = DescribeQueryFailure(url, ex2, ct);
			reporter.ReportMessage(message);
			return (OperationResult.Fail(message), new List<NSIStructure>());
		}
	}

	private static Message DescribeQueryFailure(string url, Exception ex, CancellationToken ct)
	{
		if (ex is JsonException)
		{
			return NetworkMessageCodes.GetDownloadError(url, "response was not valid NSI GeoJSON");
		}
		HttpFetchResult httpFetchResult = ResilientHttp.ClassifyException(ex, ct);
		if (httpFetchResult.Outcome == HttpFetchOutcome.TimedOut || httpFetchResult.StatusCode == 504)
		{
			return MessageCodes.QueryTimedOut.Create(url);
		}
		if (httpFetchResult.StatusCode != 0)
		{
			return NetworkMessageCodes.GetDownloadStatusError(url, httpFetchResult.StatusCode);
		}
		if (!string.IsNullOrEmpty(httpFetchResult.FailureDetail))
		{
			return NetworkMessageCodes.GetDownloadError(url, httpFetchResult.FailureDetail);
		}
		return NetworkMessageCodes.GetDownloadError(url);
	}

	public static string GetNSIBoundingBox(Extent extentWGS84)
	{
		CultureInfo invariantCulture = CultureInfo.InvariantCulture;
		_003C_003Ey__InlineArray4<object> buffer = default;
		buffer[0] = extentWGS84.MinX;
		buffer[1] = extentWGS84.MinY;
		buffer[2] = extentWGS84.MaxX;
		buffer[3] = extentWGS84.MaxY;
		return string.Format((IFormatProvider?)invariantCulture, "{0},{1},{2},{1},{2},{3},{0},{3},{0},{1}", (ReadOnlySpan<object?>)buffer);
	}

	public static string GetNSIBoundingBox(Polygon polygonWGS84)
	{
		IList<Point> points = polygonWGS84.Points;
		StringBuilder stringBuilder = new StringBuilder(points.Count * 20);
		for (int i = 0; i < points.Count; i++)
		{
			if (i > 0)
			{
				stringBuilder.Append(',');
			}
			stringBuilder.Append(points[i].X.ToString(CultureInfo.InvariantCulture));
			stringBuilder.Append(',');
			stringBuilder.Append(points[i].Y.ToString(CultureInfo.InvariantCulture));
		}
		if (points.Count > 0)
		{
			stringBuilder.Append(',');
			stringBuilder.Append(points[0].X.ToString(CultureInfo.InvariantCulture));
			stringBuilder.Append(',');
			stringBuilder.Append(points[0].Y.ToString(CultureInfo.InvariantCulture));
		}
		return stringBuilder.ToString();
	}

	private static async Task<bool> TryEnsureUrlResolvedAsync()
	{
		var (localValue, baseUrl) = await _endpoint.TryGetUrlAsync();
		if (localValue)
		{
			_baseUrl = baseUrl;
		}
		return localValue;
	}
}
