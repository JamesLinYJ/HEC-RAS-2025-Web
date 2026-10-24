using System;

namespace Utility.Web;

public readonly struct HttpFetchResult(HttpFetchOutcome outcome, int statusCode = 0, string failureDetail = null, TimeSpan retryAfter = default(TimeSpan))
{
	public HttpFetchOutcome Outcome { get; } = outcome;

	public int StatusCode { get; } = statusCode;

	public string FailureDetail { get; } = failureDetail;

	public TimeSpan RetryAfter { get; } = retryAfter;
}
