using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Geospatial.GDALAssist;
using Geospatial.GDALAssist.Vectors;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Progress;
using Utility.Web;

namespace Geospatial.IO.Network.ArcGisRest;

public static class ArcGisRestClient
{
	private readonly struct HttpFetch
	{
		public HttpFetchOutcome Outcome { get; }

		public int StatusCode { get; }

		public string Body { get; }

		public string TransientDetail { get; }

		public HttpFetch(HttpFetchOutcome outcome, int statusCode = 0, string body = null, string transientDetail = null)
		{
			Outcome = outcome;
			StatusCode = statusCode;
			Body = body;
			TransientDetail = transientDetail;
		}
	}

	private enum OidFetchStatus
	{
		Success,
		Reducible,
		Transient,
		Fatal,
		Canceled
	}

	private readonly struct OidFetchResult
	{
		public OidFetchStatus Status { get; }

		public ArcGisQueryResponse Response { get; }

		public string ErrorText { get; }

		public string Url { get; }

		public OidFetchResult(OidFetchStatus status, ArcGisQueryResponse response = null, string errorText = null, string url = null)
		{
			Status = status;
			Response = response;
			ErrorText = errorText;
			Url = url;
		}
	}

	public static class MessageCodes
	{
		public static readonly MessageClass EmptyResponse = new MessageClass("ArcGis_EmptyResponse", MessageSeverity.Information, "ArcGIS REST query returned no features for this area.");

		public static readonly MessageClass ResultTruncated = new MessageClass("ArcGis_ResultTruncated", MessageSeverity.Warning, "ArcGIS REST result truncated at the feature cap; not all features were retrieved.");

		public static readonly MessageClass MissingObjectIdField = new MessageClass("ArcGis_MissingObjectIdField", MessageSeverity.Error, "ArcGIS service did not report an object id field name for this layer; cannot verify result completeness.");

		public static readonly MessageClass FatalQueryError = new MessageClass("ArcGis_FatalQueryError", MessageSeverity.Error, "ArcGIS query error (not retryable): {Detail}");

		public static readonly MessageClass SingleFeatureFailure = new MessageClass("ArcGis_SingleFeatureFailure", MessageSeverity.Error, "ArcGIS failed to return feature OBJECTID={ObjectId}: {Detail}");

		public static readonly MessageClass IncompleteCoverage = new MessageClass("ArcGis_IncompleteCoverage", MessageSeverity.Error, "Complete-coverage ArcGIS query incomplete: targeted {Targeted} object ids, fetched {Fetched} features ({Distinct} distinct, {Missing} missing). Result is not trustworthy for authoritative use.");

		public static readonly MessageClass ServiceError = new MessageClass("ArcGis_ServiceError", MessageSeverity.Error, "Service error {Code}: {Message}");

		public static Message GetFatalQueryError(string detail)
		{
			return FatalQueryError.Create(detail);
		}

		public static Message GetSingleFeatureFailure(long objectId, string detail)
		{
			return SingleFeatureFailure.Create(objectId, string.IsNullOrEmpty(detail) ? "no additional detail" : detail);
		}

		public static Message GetIncompleteCoverage(int targeted, int fetched, int distinct, int missing)
		{
			return IncompleteCoverage.Create(targeted, fetched, distinct, missing);
		}

		public static Message GetServiceError(int code, string message)
		{
			return ServiceError.Create(code, message);
		}
	}

	private static readonly ILogger _logger = LogHelper.GetLogger("ArcGisRestClient");

	private const int DefaultBatchSize = 500;

	private const int MaxFeatures = 200000;

	private const int MaxTransientRetries = 4;

	private const int TransientRetryBackoffMs = 300;

	private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60L);

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true,
		Converters = { (JsonConverter)new ArcGisVertexConverter() }
	};

	private const int FilterSR = 4326;

	private static HttpClient Client => SharedWebResourceManager.Client;

	public static async Task<(OperationResult, ArcGisQueryResponse)> QueryByExtentCompleteAsync(string baseUrl, int layerId, Extent extent, Projection projection, int outSR, string where = "1=1", string outFields = "*", bool returnZ = false, int oidBatchSize = 500, ProgressReporter reporter = null, CancellationToken ct = default(CancellationToken))
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		oidBatchSize = Math.Max(1, oidBatchSize);
		string spatialFilter = BuildSpatialFilterFragment(extent, projection);
		string encodedWhere = Uri.EscapeDataString(string.IsNullOrEmpty(where) ? "1=1" : where);
		reporter.ReportMessage(new Message("Enumerating matching features…"));
		var (operationResult, idResponse) = await EnumerateObjectIdsAsync(baseUrl, layerId, spatialFilter, encodedWhere, reporter, ct);
		if (!operationResult)
		{
			return (operationResult, new ArcGisQueryResponse());
		}
		HashSet<long> longs = ((idResponse.ObjectIds == null) ? new HashSet<long>() : new HashSet<long>(idResponse.ObjectIds));
		if (longs.Count == 0)
		{
			Message msg = MessageCodes.EmptyResponse.Create();
			reporter.ReportMessage(msg);
			reporter.ReportProgress(100);
			return (OperationResult.Success(), new ArcGisQueryResponse
			{
				ObjectIdFieldName = idResponse.ObjectIdFieldName
			});
		}
		bool truncatedToCap = longs.Count > 200000;
		List<long> oids = new List<long>(longs);
		if (truncatedToCap)
		{
			oids = oids.GetRange(0, 200000);
		}
		int totalIds = oids.Count;
		reporter.ReportMessage(new Message("Found {Total} features; downloading…", totalIds));
		HashSet<long> target = new HashSet<long>(oids);
		string oidField = idResponse.ObjectIdFieldName;
		if (string.IsNullOrEmpty(oidField))
		{
			Message message = MessageCodes.MissingObjectIdField.Create();
			reporter.ReportMessage(message);
			return (OperationResult.Fail(message), new ArcGisQueryResponse());
		}
		string encodedOutFields = Uri.EscapeDataString(BuildOutFields(outFields, oidField));
		List<ArcGisFeature> collected = new List<ArcGisFeature>(totalIds);
		HashSet<long> fetchedOids = new HashSet<long>(totalIds);
		ArcGisQueryResponse meta = null;
		for (int start = 0; start < totalIds; start += oidBatchSize)
		{
			int count = Math.Min(oidBatchSize, totalIds - start);
			OperationResult operationResult2 = await ProcessBatchAsync(oids.GetRange(start, count));
			if (!operationResult2)
			{
				return (operationResult2, BuildAggregate(meta, idResponse.ObjectIdFieldName, collected));
			}
		}
		ArcGisQueryResponse aggregate = BuildAggregate(meta, oidField, collected);
		if (fetchedOids.Count != collected.Count || !fetchedOids.SetEquals(target))
		{
			int ordinal = 0;
			foreach (long target2 in target)
			{
				if (!fetchedOids.Contains(target2))
				{
					ordinal++;
				}
			}
			Message incompleteCoverage = MessageCodes.GetIncompleteCoverage(target.Count, collected.Count, fetchedOids.Count, ordinal);
			reporter.ReportMessage(incompleteCoverage);
			return (OperationResult.Fail(incompleteCoverage), aggregate);
		}
		OperationResult operationResult3 = OperationResult.Success();
		if (truncatedToCap)
		{
			Message msg2 = MessageCodes.ResultTruncated.Create();
			operationResult3.AddMessage(msg2);
			reporter.ReportMessage(msg2);
		}
		reporter.ReportProgress(100);
		return (operationResult3, aggregate);
		async Task<OperationResult> ProcessBatchAsync(List<long> batch)
		{
			OidFetchResult fetch = default;
			for (int attempt = 0; attempt <= 4; attempt++)
			{
				if (ct.IsCancellationRequested)
				{
					return OperationResult.Fail(GeneralMessageCodes.GetUserOperationCancelled("ArcGIS query"));
				}
				fetch = await TryFetchOidsAsync(baseUrl, layerId, batch, encodedOutFields, returnZ, outSR, ct);
				if (fetch.Status != OidFetchStatus.Transient)
				{
					break;
				}
				if (attempt < 4)
				{
					reporter.ReportMessage(NetworkMessageCodes.GetFetchRetry(fetch.ErrorText, attempt + 1, 4));
					if (!(await TryBackoffAsync(attempt, ct)))
					{
						return OperationResult.Fail(GeneralMessageCodes.GetUserOperationCancelled("ArcGIS query"));
					}
				}
			}
			if (fetch.Status == OidFetchStatus.Success)
			{
				if (meta == null)
				{
					meta = fetch.Response;
				}
				if (fetch.Response.Features != null)
				{
					collected.AddRange(fetch.Response.Features);
					foreach (ArcGisFeature feature in fetch.Response.Features)
					{
						if (TryGetFeatureOid(feature, oidField, out var oid))
						{
							fetchedOids.Add(oid);
						}
					}
				}
				reporter.ReportProgress(Math.Min(100, collected.Count * 100 / totalIds));
				reporter.ReportMessage(new Message("Retrieved {Count} of {Total} features", collected.Count, totalIds));
				return OperationResult.Success();
			}
			if (fetch.Status == OidFetchStatus.Canceled)
			{
				return OperationResult.Fail(GeneralMessageCodes.GetUserOperationCancelled("ArcGIS query"));
			}
			if (fetch.Status == OidFetchStatus.Transient)
			{
				Message downloadError = NetworkMessageCodes.GetDownloadError(fetch.Url, fetch.ErrorText);
				reporter.ReportMessage(downloadError);
				return OperationResult.Fail(downloadError);
			}
			if (fetch.Status == OidFetchStatus.Fatal)
			{
				Message fatalQueryError = MessageCodes.GetFatalQueryError(fetch.ErrorText);
				reporter.ReportMessage(fatalQueryError);
				return OperationResult.Fail(fatalQueryError);
			}
			if (batch.Count <= 1)
			{
				Message singleFeatureFailure = MessageCodes.GetSingleFeatureFailure(batch[0], fetch.ErrorText);
				reporter.ReportMessage(singleFeatureFailure);
				return OperationResult.Fail(singleFeatureFailure);
			}
			int mid = batch.Count / 2;
			OperationResult operationResult4 = await ProcessBatchAsync(batch.GetRange(0, mid));
			if (!operationResult4)
			{
				return operationResult4;
			}
			return await ProcessBatchAsync(batch.GetRange(mid, batch.Count - mid));
		}
	}

	private static string BuildSpatialFilterFragment(Extent extent, Projection projection)
	{
		Extent extent2 = extent.Reproject(projection, WellKnownProjections.WGS84);
		CultureInfo invariantCulture = CultureInfo.InvariantCulture;
		_003C_003Ey__InlineArray4<object> buffer = default;
		buffer[0] = extent2.Left;
		buffer[1] = extent2.Bottom;
		buffer[2] = extent2.Right;
		buffer[3] = extent2.Top;
		string format = string.Format((IFormatProvider?)invariantCulture, "{0},{1},{2},{3}", (ReadOnlySpan<object?>)buffer);
		return $"geometry={format}&geometryType=esriGeometryEnvelope&inSR={4326.ToString(CultureInfo.InvariantCulture)}&spatialRel=esriSpatialRelIntersects";
	}

	private static async Task<HttpFetch> SendWithTimeoutAsync(string url, CancellationToken ct)
	{
		string body = null;
		HttpFetchResult httpFetchResult = await ResilientHttp.SendAsync(Client, () => new HttpRequestMessage(HttpMethod.Get, url), RequestTimeout, async (HttpResponseMessage response, CancellationToken readCt) =>
		{
			body = await response.Content.ReadAsStringAsync(readCt);
			return true;
		}, ct);
		return httpFetchResult.Outcome switch
		{
			HttpFetchOutcome.Completed => new HttpFetch(HttpFetchOutcome.Completed, httpFetchResult.StatusCode, body), 
			HttpFetchOutcome.Canceled => new HttpFetch(HttpFetchOutcome.Canceled), 
			_ => new HttpFetch(HttpFetchOutcome.Transient, 0, null, httpFetchResult.FailureDetail), 
		};
	}

	private static bool IsTransientStatus(int status)
	{
		return ResilientHttp.IsTransientStatus(status);
	}

	private static bool IsSuccessStatus(int status)
	{
		return ResilientHttp.IsSuccessStatus(status);
	}

	private static Message ExhaustedRetryFailure(string url, int lastStatus, string transientDetail)
	{
		if (lastStatus != 0)
		{
			return NetworkMessageCodes.GetDownloadStatusError(url, lastStatus);
		}
		if (!string.IsNullOrEmpty(transientDetail))
		{
			return NetworkMessageCodes.GetDownloadError(url, transientDetail);
		}
		return NetworkMessageCodes.GetDownloadError(url);
	}

	private static Task<bool> TryBackoffAsync(int attempt, CancellationToken ct)
	{
		return ResilientHttp.TryDelayAsync(ResilientHttp.LinearBackoff(attempt, 300), ct);
	}

	private static async Task<(OperationResult, ArcGisObjectIdsResponse)> EnumerateObjectIdsAsync(string baseUrl, int layerId, string spatialFilter, string encodedWhere, ProgressReporter reporter, CancellationToken ct)
	{
		string url = $"{baseUrl}/{layerId}/query?{spatialFilter}&where={encodedWhere}&returnIdsOnly=true&f=json";
		_logger.LogDebug("Enumerating ArcGIS object ids at {URL}", url);
		int lastStatus = 0;
		string lastTransientDetail = null;
		int attempt = 0;
		ArcGisObjectIdsResponse arcGisObjectIdsResponse;
		ArcGisError arcGisError;
		while (true)
		{
			if (ct.IsCancellationRequested)
			{
				return (OperationResult.Fail(GeneralMessageCodes.GetUserOperationCancelled("ArcGIS query")), null);
			}
			HttpFetch httpFetch = await SendWithTimeoutAsync(url, ct);
			if (httpFetch.Outcome == HttpFetchOutcome.Canceled)
			{
				return (OperationResult.Fail(GeneralMessageCodes.GetUserOperationCancelled("ArcGIS query")), null);
			}
			string reason;
			if (httpFetch.Outcome == HttpFetchOutcome.Completed)
			{
				if (IsSuccessStatus(httpFetch.StatusCode))
				{
					arcGisObjectIdsResponse = JsonSerializer.Deserialize<ArcGisObjectIdsResponse>(httpFetch.Body, JsonOptions);
					arcGisError = arcGisObjectIdsResponse?.Error;
					break;
				}
				lastStatus = httpFetch.StatusCode;
				if (!IsTransientStatus(lastStatus))
				{
					return (OperationResult.Fail(NetworkMessageCodes.GetDownloadStatusError(url, lastStatus)), null);
				}
				reason = $"HTTP {lastStatus}";
			}
			else
			{
				lastTransientDetail = httpFetch.TransientDetail;
				reason = httpFetch.TransientDetail;
			}
			if (attempt >= 4)
			{
				return (OperationResult.Fail(ExhaustedRetryFailure(url, lastStatus, lastTransientDetail)), null);
			}
			reporter.ReportMessage(NetworkMessageCodes.GetFetchRetry(reason, attempt + 1, 4));
			if (!(await TryBackoffAsync(attempt, ct)))
			{
				return (OperationResult.Fail(GeneralMessageCodes.GetUserOperationCancelled("ArcGIS query")), null);
			}
			attempt++;
		}
		if (arcGisError != null)
		{
			return (OperationResult.Fail(MessageCodes.GetServiceError(arcGisObjectIdsResponse.Error.Code, arcGisObjectIdsResponse.Error.Message)), null);
		}
		return (OperationResult.Success(), arcGisObjectIdsResponse ?? new ArcGisObjectIdsResponse());
	}

	private static async Task<OidFetchResult> TryFetchOidsAsync(string baseUrl, int layerId, List<long> oids, string encodedOutFields, bool returnZ, int outSR, CancellationToken ct)
	{
		string url = $"{baseUrl}/{layerId}/query?objectIds={string.Join(",", oids)}&outFields={encodedOutFields}&returnGeometry=true&returnZ={(returnZ ? "true" : "false")}&returnM=false&outSR={outSR.ToString(CultureInfo.InvariantCulture)}&f=json";
		HttpFetch httpFetch = await SendWithTimeoutAsync(url, ct);
		if (httpFetch.Outcome == HttpFetchOutcome.Canceled)
		{
			return new OidFetchResult(OidFetchStatus.Canceled, null, null, url);
		}
		if (httpFetch.Outcome == HttpFetchOutcome.Transient)
		{
			return new OidFetchResult(OidFetchStatus.Transient, null, httpFetch.TransientDetail, url);
		}
		ArcGisQueryResponse arcGisQueryResponse = null;
		try
		{
			arcGisQueryResponse = JsonSerializer.Deserialize<ArcGisQueryResponse>(httpFetch.Body, JsonOptions);
		}
		catch
		{
		}
		if (arcGisQueryResponse?.Error != null)
		{
			string evaluatedMessage = MessageCodes.GetServiceError(arcGisQueryResponse.Error.Code, arcGisQueryResponse.Error.Message).EvaluatedMessage;
			OidFetchStatus status = ((arcGisQueryResponse.Error.Code != 400) ? OidFetchStatus.Reducible : OidFetchStatus.Fatal);
			return new OidFetchResult(status, null, evaluatedMessage, url);
		}
		if (IsSuccessStatus(httpFetch.StatusCode))
		{
			if (arcGisQueryResponse == null)
			{
				return new OidFetchResult(OidFetchStatus.Reducible, null, "Unparseable response", url);
			}
			return new OidFetchResult(OidFetchStatus.Success, arcGisQueryResponse, null, url);
		}
		if (IsTransientStatus(httpFetch.StatusCode))
		{
			return new OidFetchResult(OidFetchStatus.Transient, null, $"HTTP {httpFetch.StatusCode}", url);
		}
		return new OidFetchResult(OidFetchStatus.Reducible, null, $"HTTP {httpFetch.StatusCode}", url);
	}

	private static ArcGisQueryResponse BuildAggregate(ArcGisQueryResponse meta, string objectIdFieldName, List<ArcGisFeature> features)
	{
		return new ArcGisQueryResponse
		{
			ObjectIdFieldName = (meta?.ObjectIdFieldName ?? objectIdFieldName),
			GlobalIdFieldName = meta?.GlobalIdFieldName,
			GeometryType = meta?.GeometryType,
			SpatialReference = meta?.SpatialReference,
			Fields = meta?.Fields,
			Features = features,
			ExceededTransferLimit = false,
			HasZ = (meta?.HasZ ?? false),
			HasM = (meta?.HasM ?? false)
		};
	}

	private static string BuildOutFields(string outFields, string oidField)
	{
		if (string.IsNullOrEmpty(outFields) || outFields == "*")
		{
			return "*";
		}
		if (string.IsNullOrEmpty(oidField))
		{
			return outFields;
		}
		string[] parts = outFields.Split(',');
		for (int i = 0; i < parts.Length; i++)
		{
			if (string.Equals(parts[i].Trim(), oidField, StringComparison.OrdinalIgnoreCase))
			{
				return outFields;
			}
		}
		return outFields + "," + oidField;
	}

	private static bool TryGetFeatureOid(ArcGisFeature feature, string oidField, out long oid)
	{
		oid = 0L;
		if (feature?.Attributes == null || string.IsNullOrEmpty(oidField))
		{
			return false;
		}
		if (!feature.Attributes.TryGetValue(oidField, out var localValue))
		{
			return false;
		}
		if (localValue is JsonElement jsonElement)
		{
			if (jsonElement.ValueKind == JsonValueKind.Number)
			{
				return jsonElement.TryGetInt64(out oid);
			}
			return false;
		}
		try
		{
			oid = Convert.ToInt64(localValue, CultureInfo.InvariantCulture);
			return true;
		}
		catch
		{
			return false;
		}
	}
}
