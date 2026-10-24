using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Progress;
using Utility.Web;

namespace Geospatial.IO.Network.OSM;

public static class OverpassClient
{
	private enum OverpassFetchOutcome
	{
		Completed,
		Canceled,
		TimedOut,
		TooLarge,
		Failed
	}

	private readonly struct OverpassFetch
	{
		public OverpassFetchOutcome Outcome { get; }

		public int StatusCode { get; }

		public string FailureDetail { get; }

		public TimeSpan RetryAfter { get; }

		public OverpassFetch(OverpassFetchOutcome outcome, int statusCode = 0, string failureDetail = null, TimeSpan retryAfter = default(TimeSpan))
		{
			Outcome = outcome;
			StatusCode = statusCode;
			FailureDetail = failureDetail;
			RetryAfter = retryAfter;
		}
	}

	public static class MessageCodes
	{
		public static readonly MessageClass EndpointUnavailable = new MessageClass("Overpass_EndpointUnavailable", MessageSeverity.Error, "Could not resolve the Overpass API address. Check your network connection and try again.");

		public static readonly MessageClass EmptyQuery = new MessageClass("Overpass_EmptyQuery", MessageSeverity.Error, "No Overpass query was provided.");

		public static readonly MessageClass EmptyResponse = new MessageClass("Overpass_EmptyResponse", MessageSeverity.Information, "Overpass returned no features for this query.");

		public static readonly MessageClass UnparseableResponse = new MessageClass("Overpass_UnparseableResponse", MessageSeverity.Error, "The Overpass response could not be read as JSON.");

		public static readonly MessageClass RuntimeFailure = new MessageClass("Overpass_RuntimeFailure", MessageSeverity.Error, "Overpass could not finish the query: {Remark}");

		public static readonly MessageClass ServerNote = new MessageClass("Overpass_ServerNote", MessageSeverity.Information, "Overpass note: {Remark}");

		public static readonly MessageClass QueryRejected = new MessageClass("Overpass_QueryRejected", MessageSeverity.Error, "Overpass rejected the query (HTTP {StatusCode}): {Detail}");

		public static readonly MessageClass RateLimited = new MessageClass("Overpass_RateLimited", MessageSeverity.Error, "Overpass is refusing further queries right now (HTTP 429). The Overpass instance limits concurrent queries per IP. Wait a moment and try again, or query a smaller area.");

		public static readonly MessageClass GatewayTimeout = new MessageClass("Overpass_GatewayTimeout", MessageSeverity.Error, "Overpass did not return the query in time (HTTP 504). Query a smaller area or a narrower feature set.");

		public static readonly MessageClass ResponseTooLarge = new MessageClass("Overpass_ResponseTooLarge", MessageSeverity.Error, "The Overpass response passed the {ByteCount} byte safety limit and was abandoned. Query a smaller area or a narrower feature set.");

		public static readonly MessageClass ResultTruncated = new MessageClass("Overpass_ResultTruncated", MessageSeverity.Warning, "Overpass returned {ElementCount} elements; only the first {ElementCap} were kept. Query a smaller area to import everything.");

		public static Message GetRuntimeFailure(string remark)
		{
			return RuntimeFailure.Create(remark);
		}

		public static Message GetServerNote(string remark)
		{
			return ServerNote.Create(remark);
		}

		public static Message GetQueryRejected(int statusCode, string detail)
		{
			return QueryRejected.Create(statusCode, detail);
		}

		public static Message GetResponseTooLarge(long capBytes)
		{
			return ResponseTooLarge.Create(capBytes);
		}

		public static Message GetResultTruncated(int returned, int kept)
		{
			return ResultTruncated.Create(returned, kept);
		}
	}

	private static readonly ILogger _logger = LogHelper.GetLogger("OverpassClient");

	private const string TaskName = "Overpass query";

	private const string QueryFormField = "data";

	private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(240L);

	private const int MaxRetries = 4;

	private const int RetryBackoffMs = 2000;

	private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(60L);

	private const long MaxResponseBytes = 134217728L;

	private const int MaxElements = 1000000;

	private const int ReadChunkSize = 81920;

	private const int MaxServerErrorBytes = 16384;

	private const int MaxServerErrorLength = 500;

	private const string RemarkErrorToken = "error";

	private const int TooManyRequestsStatus = 429;

	private const int GatewayTimeoutStatus = 504;

	private static readonly Regex _htmlTag = new Regex("<[^>]*>", RegexOptions.Compiled);

	private static readonly Regex _whitespaceRun = new Regex("\\s+", RegexOptions.Compiled);

	private static readonly Regex _serverErrorMarker = new Regex("\\bError\\s*:", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true
	};

	private static HttpClient Client => SharedWebResourceManager.Client;

	public static async Task<(OperationResult, OverpassResponse)> ExecuteAsync(string ql, ProgressReporter reporter = null, CancellationToken ct = default(CancellationToken))
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		if (string.IsNullOrWhiteSpace(ql))
		{
			Message message = MessageCodes.EmptyQuery.Create();
			reporter.ReportMessage(message);
			return (OperationResult.Fail(message), null);
		}
		var (localValue, endpoint) = await OsmEndpoints.TryGetOverpassUrlAsync();
		if (!localValue)
		{
			return (FailAndReport(MessageCodes.EndpointUnavailable.Create(), reporter), null);
		}
		_logger.LogDebug("Executing Overpass query against {URL}:\n{Query}", endpoint, ql);
		reporter.ReportMessage(new Message("Contacting Overpass…"));
		using MemoryStream buffer = new MemoryStream();
		int attempt = 0;
		while (true)
		{
			if (ct.IsCancellationRequested)
			{
				return (OperationResult.Fail(GeneralMessageCodes.GetUserOperationCancelled("Overpass query")), null);
			}
			buffer.SetLength(0L);
			OverpassFetch overpassFetch = await SendWithTimeoutAsync(endpoint, ql, buffer, ct);
			if (overpassFetch.Outcome == OverpassFetchOutcome.Canceled)
			{
				return (OperationResult.Fail(GeneralMessageCodes.GetUserOperationCancelled("Overpass query")), null);
			}
			if (overpassFetch.Outcome == OverpassFetchOutcome.TimedOut)
			{
				return (FailAndReport(GeneralMessageCodes.GetTimeoutOccurred("Overpass query"), reporter), null);
			}
			if (overpassFetch.Outcome == OverpassFetchOutcome.TooLarge)
			{
				return (FailAndReport(MessageCodes.GetResponseTooLarge(134217728L), reporter), null);
			}
			if (overpassFetch.Outcome == OverpassFetchOutcome.Failed)
			{
				return (FailAndReport(NetworkMessageCodes.GetDownloadError(endpoint, overpassFetch.FailureDetail), reporter), null);
			}
			if (IsSuccessStatus(overpassFetch.StatusCode))
			{
				break;
			}
			if (!IsRetryableStatus(overpassFetch.StatusCode))
			{
				return (FailAndReport(DescribeRejection(endpoint, overpassFetch.StatusCode, buffer), reporter), null);
			}
			if (attempt >= 4)
			{
				Message msg = ((overpassFetch.StatusCode == 429) ? MessageCodes.RateLimited.Create() : MessageCodes.GatewayTimeout.Create());
				return (FailAndReport(msg, reporter), null);
			}
			reporter.ReportMessage(NetworkMessageCodes.GetFetchRetry($"HTTP {overpassFetch.StatusCode}", attempt + 1, 4));
			if (!(await ResilientHttp.TryDelayAsync((overpassFetch.RetryAfter > TimeSpan.Zero) ? overpassFetch.RetryAfter : GetBackoff(attempt), ct)))
			{
				return (OperationResult.Fail(GeneralMessageCodes.GetUserOperationCancelled("Overpass query")), null);
			}
			attempt++;
		}
		OverpassResponse overpassResponse;
		try
		{
			buffer.Position = 0L;
			overpassResponse = await JsonSerializer.DeserializeAsync<OverpassResponse>((Stream)buffer, JsonOptions, ct);
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			return (OperationResult.Fail(GeneralMessageCodes.GetUserOperationCancelled("Overpass query")), null);
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Failed to parse the Overpass response.");
			return (FailAndReport(MessageCodes.UnparseableResponse.Create(), reporter), null);
		}
		if (overpassResponse == null)
		{
			return (FailAndReport(MessageCodes.UnparseableResponse.Create(), reporter), null);
		}
		OperationResult operationResult = OperationResult.Success();
		if (!string.IsNullOrWhiteSpace(overpassResponse.Remark))
		{
			string trim = overpassResponse.Remark.Trim();
			if (trim.Contains("error", StringComparison.OrdinalIgnoreCase))
			{
				return (FailAndReport(MessageCodes.GetRuntimeFailure(trim), reporter), null);
			}
			Message serverNote = MessageCodes.GetServerNote(trim);
			reporter.ReportMessage(serverNote);
			operationResult.AddMessage(serverNote);
		}
		OverpassResponse overpassResponse2 = overpassResponse;
		if (overpassResponse2.Elements == null)
		{
			overpassResponse2.Elements = new List<OsmElement>();
		}
		if (overpassResponse.Elements.Count > 1000000)
		{
			Message resultTruncated = MessageCodes.GetResultTruncated(overpassResponse.Elements.Count, 1000000);
			overpassResponse.Elements.RemoveRange(1000000, overpassResponse.Elements.Count - 1000000);
			reporter.ReportMessage(resultTruncated);
			operationResult.AddMessage(resultTruncated);
		}
		if (overpassResponse.Elements.Count == 0)
		{
			Message msg2 = MessageCodes.EmptyResponse.Create();
			reporter.ReportMessage(msg2);
			operationResult.AddMessage(msg2);
		}
		else
		{
			reporter.ReportMessage(new Message("Parsing {ElementCount} elements…", overpassResponse.Elements.Count));
		}
		reporter.ReportProgress(100);
		return (operationResult, overpassResponse);
	}

	private static async Task<OverpassFetch> SendWithTimeoutAsync(string endpoint, string ql, MemoryStream destination, CancellationToken ct)
	{
		bool overSize = false;
		HttpFetchResult httpFetchResult = await ResilientHttp.SendAsync(Client, () => new HttpRequestMessage(HttpMethod.Post, endpoint)
		{
			Content = new FormUrlEncodedContent(new KeyValuePair<string, string>[1]
			{
				new KeyValuePair<string, string>("data", ql)
			})
		}, RequestTimeout, async (HttpResponseMessage response, CancellationToken readCt) =>
		{
			overSize = !(await TryBufferBodyAsync(response, destination, readCt));
			return !overSize;
		}, ct, HttpCompletionOption.ResponseHeadersRead);
		if (overSize)
		{
			return new OverpassFetch(OverpassFetchOutcome.TooLarge, httpFetchResult.StatusCode);
		}
		switch (httpFetchResult.Outcome)
		{
		case HttpFetchOutcome.Completed:
			return new OverpassFetch(OverpassFetchOutcome.Completed, httpFetchResult.StatusCode, null, httpFetchResult.RetryAfter);
		case HttpFetchOutcome.Canceled:
			return new OverpassFetch(OverpassFetchOutcome.Canceled);
		case HttpFetchOutcome.TimedOut:
			return new OverpassFetch(OverpassFetchOutcome.TimedOut);
		default:
			_logger.LogError("Overpass request failed: {Detail}", httpFetchResult.FailureDetail);
			return new OverpassFetch(OverpassFetchOutcome.Failed, 0, httpFetchResult.FailureDetail);
		}
	}

	private static async Task<bool> TryBufferBodyAsync(HttpResponseMessage response, MemoryStream destination, CancellationToken ct)
	{
		using Stream content = await response.Content.ReadAsStreamAsync(ct);
		byte[] chunk = ArrayPool<byte>.Shared.Rent(81920);
		try
		{
			while (true)
			{
				int readAsync = await content.ReadAsync(chunk.AsMemory(), ct);
				if (readAsync == 0)
				{
					return true;
				}
				if (destination.Length + readAsync > 134217728)
				{
					break;
				}
				destination.Write(chunk, 0, readAsync);
			}
			return false;
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(chunk);
		}
	}

	private static bool IsSuccessStatus(int status)
	{
		return ResilientHttp.IsSuccessStatus(status);
	}

	private static bool IsRetryableStatus(int status)
	{
		if (status != 429)
		{
			return status == 504;
		}
		return true;
	}

	private static TimeSpan GetBackoff(int attempt)
	{
		return ResilientHttp.ExponentialBackoff(attempt, 2000);
	}

	private static Message DescribeRejection(string endpoint, int status, MemoryStream buffer)
	{
		string extractServerError = ExtractServerError(buffer);
		if (string.IsNullOrEmpty(extractServerError))
		{
			return NetworkMessageCodes.GetDownloadStatusError(endpoint, status);
		}
		return MessageCodes.GetQueryRejected(status, extractServerError);
	}

	private static string ExtractServerError(MemoryStream buffer)
	{
		if (buffer.Length == 0L)
		{
			return null;
		}
		int count = (int)Math.Min(buffer.Length, 16384L);
		string input = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, count);
		string input2 = WebUtility.HtmlDecode(_htmlTag.Replace(input, " "));
		input2 = _whitespaceRun.Replace(input2, " ").Trim();
		Match match = _serverErrorMarker.Match(input2);
		if (match.Success && match.Index > 0)
		{
			input2 = input2.Substring(match.Index);
		}
		if (input2.Length > 500)
		{
			input2 = input2.Substring(0, 500) + "…";
		}
		if (input2.Length != 0)
		{
			return input2;
		}
		return null;
	}

	private static OperationResult FailAndReport(Message msg, ProgressReporter reporter)
	{
		reporter.ReportMessage(msg);
		return OperationResult.Fail(msg);
	}
}
