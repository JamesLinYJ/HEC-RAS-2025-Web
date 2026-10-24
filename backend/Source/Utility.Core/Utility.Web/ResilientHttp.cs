using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Utility.Web;

public static class ResilientHttp
{
	public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(30L);

	public static bool IsSuccessStatus(int status)
	{
		if (status >= 200)
		{
			return status < 300;
		}
		return false;
	}

	public static bool IsTransientStatus(int status)
	{
		if (status != 429 && status != 502 && status != 503)
		{
			return status == 504;
		}
		return true;
	}

	public static TimeSpan ExponentialBackoff(int attempt, int baseMilliseconds)
	{
		return TimeSpan.FromMilliseconds(baseMilliseconds * (1 << attempt));
	}

	public static TimeSpan LinearBackoff(int attempt, int baseMilliseconds)
	{
		return TimeSpan.FromMilliseconds(baseMilliseconds * (attempt + 1));
	}

	public static TimeSpan GetRetryAfter(HttpResponseMessage response)
	{
		RetryConditionHeaderValue retryConditionHeaderValue = response?.Headers?.RetryAfter;
		if (retryConditionHeaderValue == null)
		{
			return TimeSpan.Zero;
		}
		if (retryConditionHeaderValue.Delta.HasValue)
		{
			return Clamp(retryConditionHeaderValue.Delta.Value);
		}
		if (retryConditionHeaderValue.Date.HasValue)
		{
			return Clamp(retryConditionHeaderValue.Date.Value - DateTimeOffset.UtcNow);
		}
		return TimeSpan.Zero;
	}

	private static TimeSpan Clamp(TimeSpan advised)
	{
		if (advised <= TimeSpan.Zero)
		{
			return TimeSpan.Zero;
		}
		if (!(advised > MaxRetryAfter))
		{
			return advised;
		}
		return MaxRetryAfter;
	}

	public static async Task<bool> TryDelayAsync(TimeSpan delay, CancellationToken ct)
	{
		if (delay <= TimeSpan.Zero)
		{
			return !ct.IsCancellationRequested;
		}
		try
		{
			await Task.Delay(delay, ct);
			return true;
		}
		catch (OperationCanceledException)
		{
			return false;
		}
	}

	public static async Task<HttpFetchResult> SendAsync(HttpClient client, Func<HttpRequestMessage> requestFactory, TimeSpan timeout, Func<HttpResponseMessage, CancellationToken, Task<bool>> readBody, CancellationToken ct, HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead)
	{
		using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		timeoutCts.CancelAfter(timeout);
		try
		{
			using HttpRequestMessage request = requestFactory();
			using HttpResponseMessage response = await client.SendAsync(request, completionOption, timeoutCts.Token);
			int status = (int)response.StatusCode;
			TimeSpan retryAfter = GetRetryAfter(response);
			bool hasReadBody = readBody != null;
			if (hasReadBody)
			{
				hasReadBody = !(await readBody(response, timeoutCts.Token));
			}
			if (hasReadBody)
			{
				return new HttpFetchResult(HttpFetchOutcome.Transient, status, "response body could not be read", retryAfter);
			}
			return new HttpFetchResult(HttpFetchOutcome.Completed, status, null, retryAfter);
		}
		catch (Exception ex)
		{
			return ClassifyException(ex, ct);
		}
	}

	public static HttpFetchResult ClassifyException(Exception ex, CancellationToken ct)
	{
		if (ex is OperationCanceledException)
		{
			if (ct.IsCancellationRequested)
			{
				return new HttpFetchResult(HttpFetchOutcome.Canceled);
			}
			return new HttpFetchResult(HttpFetchOutcome.TimedOut, 0, "request timed out");
		}
		if (ex is HttpRequestException { StatusCode: not null } ex2)
		{
			return new HttpFetchResult(HttpFetchOutcome.Completed, (int)ex2.StatusCode.Value);
		}
		return new HttpFetchResult(HttpFetchOutcome.Transient, 0, DescribeTransportFailure(ex));
	}

	private static string DescribeTransportFailure(Exception ex)
	{
		if (!(ex is HttpRequestException { HttpRequestError: var httpRequestError } ex2))
		{
			return ex.GetType().Name;
		}
		return httpRequestError switch
		{
			HttpRequestError.NameResolutionError => "server address could not be resolved", 
			HttpRequestError.ConnectionError => "could not connect to the server", 
			HttpRequestError.SecureConnectionError => "secure connection failed", 
			HttpRequestError.ResponseEnded => "the server closed the connection early", 
			HttpRequestError.Unknown => "connection failed", 
			_ => ex2.HttpRequestError.ToString(), 
		};
	}
}
