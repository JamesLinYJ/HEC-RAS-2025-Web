using System;
using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Memory;
using Utility.Progress;

namespace Utility.Web;

public static class WebHelper
{
	public struct RentedMemoryNetworkResult : IDisposable
	{
		public static RentedMemoryNetworkResult Empty = new RentedMemoryNetworkResult
		{
			MemoryOwner = null,
			Data = Memory<byte>.Empty
		};

		public IMemoryOwner<byte> MemoryOwner;

		public Memory<byte> Data;

		public int Length => Data.Length;

		public bool IsEmpty()
		{
			if (MemoryOwner != null)
			{
				return Data.IsEmpty;
			}
			return true;
		}

		public void Dispose()
		{
			MemoryOwner?.Dispose();
		}
	}

	private static readonly ILogger _logger = LogHelper.GetLogger("WebHelper");

	private const int DefaultBufferSize = 81920;

	public static async ValueTask<bool> DownloadWithProgress(HttpClient client, string downloadUrl, string destFile, ProgressReporter reporter, int bufferSize = 81920)
	{
		using Stream fileStream = new FileStream(destFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);
		return await DownloadWithProgress(client, downloadUrl, fileStream, reporter, bufferSize);
	}

	public static async ValueTask<bool> DownloadWithProgress(HttpClient client, string downloadUrl, Stream fileStream, ProgressReporter reporter, int bufferSize = 81920)
	{
		using HttpResponseMessage response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
		if (!response.IsSuccessStatusCode)
		{
			return false;
		}
		using Stream contentStream = await response.Content.ReadAsStreamAsync();
		long totalRead = 0L;
		long valueOrDefault = response.Content.Headers.ContentLength.GetValueOrDefault();
		float divisor = valueOrDefault;
		byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
		try
		{
			bool isMoreToRead = true;
			do
			{
				int read = await contentStream.ReadAsync(buffer, 0, bufferSize);
				if (read == 0)
				{
					isMoreToRead = false;
					continue;
				}
				await fileStream.WriteAsync(buffer, 0, read);
				totalRead += read;
				reporter.ReportProgressFraction((float)totalRead / divisor);
				if (reporter.CancelRequested)
				{
					return false;
				}
			}
			while (isMoreToRead);
			reporter.ReportProgress(100);
			return true;
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}

	public static async ValueTask<RentedMemoryNetworkResult> DownloadMemoryEfficient(string downloadUrl, Func<bool> isStillValid = null, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		try
		{
			using HttpResponseMessage response = await SharedWebResourceManager.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, isStillValid);
			if (response == null || !response.IsSuccessStatusCode)
			{
				return RentedMemoryNetworkResult.Empty;
			}
			response.EnsureSuccessStatusCode();
			return await DownloadMemoryEfficient(response, reporter);
		}
		catch
		{
			return RentedMemoryNetworkResult.Empty;
		}
	}

	public static RentedMemoryNetworkResult DownloadMemoryEfficientSync(string downloadUrl, Func<bool> isStillValid = null)
	{
		try
		{
			using HttpResponseMessage httpResponseMessage = SharedWebResourceManager.GetSync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, isStillValid);
			if (httpResponseMessage == null)
			{
				return RentedMemoryNetworkResult.Empty;
			}
			if (!httpResponseMessage.IsSuccessStatusCode)
			{
				return RentedMemoryNetworkResult.Empty;
			}
			httpResponseMessage.EnsureSuccessStatusCode();
			return DownloadMemoryEfficientSync(httpResponseMessage);
		}
		catch
		{
			return RentedMemoryNetworkResult.Empty;
		}
	}

	public static async ValueTask<RentedMemoryNetworkResult> DownloadMemoryEfficient(HttpResponseMessage response, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		IMemoryOwner<byte> memHandle = null;
		try
		{
			int contentLen = (int)response.Content.Headers.ContentLength.GetValueOrDefault();
			if (contentLen == 0)
			{
				return RentedMemoryNetworkResult.Empty;
			}
			using Stream responseStream = await response.Content.ReadAsStreamAsync();
			memHandle = SharedMemoryPool<byte>.RentNoFill(contentLen, out var mem);
			int readAsyncResponseStreamTerm = 0;
			while (readAsyncResponseStreamTerm < contentLen)
			{
				int ordinal = readAsyncResponseStreamTerm;
				readAsyncResponseStreamTerm = ordinal + await responseStream.ReadAsync(mem.Slice(readAsyncResponseStreamTerm));
				reporter.ReportProgressFraction((float)readAsyncResponseStreamTerm / (float)contentLen);
			}
			mem = mem.Slice(0, readAsyncResponseStreamTerm);
			return new RentedMemoryNetworkResult
			{
				MemoryOwner = memHandle,
				Data = mem
			};
		}
		catch (HttpRequestException exception)
		{
			memHandle?.Dispose();
			_logger.LogError(exception, "Web request failed.");
			return RentedMemoryNetworkResult.Empty;
		}
	}

	public static RentedMemoryNetworkResult DownloadMemoryEfficientSync(HttpResponseMessage response)
	{
		IMemoryOwner<byte> memoryOwner = null;
		try
		{
			int count = (int)response.Content.Headers.ContentLength.Value;
			if (count == 0)
			{
				return RentedMemoryNetworkResult.Empty;
			}
			using Stream stream = response.Content.ReadAsStream();
			memoryOwner = SharedMemoryPool<byte>.RentNoFill(count, out var mem);
			int i;
			for (i = 0; i < count; i += stream.Read(mem.Slice(i).Span))
			{
			}
			mem = mem.Slice(0, i);
			return new RentedMemoryNetworkResult
			{
				MemoryOwner = memoryOwner,
				Data = mem
			};
		}
		catch (HttpRequestException exception)
		{
			memoryOwner?.Dispose();
			_logger.LogError(exception, "Web request failed.");
			return RentedMemoryNetworkResult.Empty;
		}
	}

	public static async Task WaitForEndpointAsync(string url, TimeSpan timeout, TimeSpan? pollInterval = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		TimeSpan interval = pollInterval ?? TimeSpan.FromMilliseconds(250L);
		using HttpClient http = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(2L)
		};
		DateTimeOffset start = DateTimeOffset.UtcNow;
		Exception lastError = null;
		while (DateTimeOffset.UtcNow - start < timeout)
		{
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, url);
				using HttpResponseMessage httpResponseMessage = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				if (httpResponseMessage.StatusCode >= HttpStatusCode.OK && httpResponseMessage.StatusCode <= (HttpStatusCode)299)
				{
					return;
				}
				lastError = new HttpRequestException($"Non-success status code: {(int)httpResponseMessage.StatusCode} {httpResponseMessage.ReasonPhrase}");
			}
			catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
			{
				lastError = ex;
			}
			await Task.Delay(interval, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		throw new TimeoutException($"Timed out after {timeout} waiting for {url} to respond successfully. Last error: {lastError?.Message}", lastError);
	}
}
