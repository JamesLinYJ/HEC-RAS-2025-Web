using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using Utility.Logging;
using Utility.Progress;

namespace Utility.Communication;

public class S3Client : IDisposable
{
	private static readonly ILogger _logger = LogHelper.GetLogger("S3Client");

	private static readonly HttpClient _httpClient = new HttpClient();

	private readonly bool _shouldDispose;

	private readonly AmazonS3Client _client;

	private bool _isDisposed;

	private readonly Amazon.S3.Protocol _protocol;

	private int _readCt;

	private long _readBytes;

	private double _readMs;

	private readonly object _readLock = new object();

	private static readonly Dictionary<(string, string, HttpVerb), string> _urls = new Dictionary<(string, string, HttpVerb), string>();

	public AmazonS3Client AmazonClient => _client;

	public string ServerUrl { get; }

	public S3Client(string url, string accessKey, string secretKey, bool forcePathStyleQueries = false)
	{
		AmazonS3Config clientConfig = new AmazonS3Config
		{
			ServiceURL = url,
			ForcePathStyle = forcePathStyleQueries
		};
		ServerUrl = url;
		_client = new AmazonS3Client(accessKey, secretKey, clientConfig);
		_shouldDispose = true;
		if (url.StartsWith("https", StringComparison.OrdinalIgnoreCase))
		{
			_protocol = Amazon.S3.Protocol.HTTPS;
		}
		else
		{
			_protocol = Amazon.S3.Protocol.HTTP;
		}
	}

	public S3Client(AmazonS3Client client, Amazon.S3.Protocol protocol)
	{
		_client = client;
		_shouldDispose = false;
		_protocol = protocol;
	}

	public S3Client(string serviceUrl, bool forcePathStyle = false)
	{
		AmazonS3Config clientConfig = new AmazonS3Config
		{
			ServiceURL = serviceUrl,
			ForcePathStyle = forcePathStyle
		};
		ServerUrl = serviceUrl;
		_client = new AmazonS3Client(new AnonymousAWSCredentials(), clientConfig);
		_shouldDispose = true;
		if (serviceUrl.StartsWith("https", StringComparison.OrdinalIgnoreCase))
		{
			_protocol = Amazon.S3.Protocol.HTTPS;
		}
		else
		{
			_protocol = Amazon.S3.Protocol.HTTP;
		}
	}

	public async Task<ListBucketsResponse> GetBucketsAsync()
	{
		return await _client.ListBucketsAsync();
	}

	public async Task<List<string>> GetBucketNamesAsync()
	{
		return (await GetBucketsAsync()).Buckets?.Select((S3Bucket b) => b.BucketName).ToList() ?? new List<string>();
	}

	public async Task<GetBucketLocationResponse> GetBucketLocation(string bucketName)
	{
		return await _client.GetBucketLocationAsync(bucketName);
	}

	public async Task<List<S3Object>> ListObjectsAsync(string bucketName, string keyPrefix = "", bool fetchOwner = false)
	{
		ListObjectsV2Request req = new ListObjectsV2Request
		{
			BucketName = bucketName,
			FetchOwner = fetchOwner,
			Prefix = keyPrefix
		};
		ListObjectsV2Response listObjectsV2Response = await _client.ListObjectsV2Async(req);
		List<S3Object> retn = new List<S3Object>();
		if (listObjectsV2Response.S3Objects != null)
		{
			retn.AddRange(listObjectsV2Response.S3Objects);
		}
		while (listObjectsV2Response.IsTruncated == true)
		{
			req.ContinuationToken = listObjectsV2Response.NextContinuationToken;
			listObjectsV2Response = await _client.ListObjectsV2Async(req);
			if (listObjectsV2Response.S3Objects != null)
			{
				retn.AddRange(listObjectsV2Response.S3Objects);
			}
		}
		return retn;
	}

	public async IAsyncEnumerable<S3Object> EnumerateObjectsAsync(string bucketName, string keyPrefix = "", int maxKeysPerRequest = 1000, [EnumeratorCancellation] CancellationToken ct = default(CancellationToken))
	{
		string continuationToken = null;
		do
		{
			ListObjectsV2Request request = new ListObjectsV2Request
			{
				BucketName = bucketName,
				Prefix = keyPrefix,
				ContinuationToken = continuationToken,
				MaxKeys = maxKeysPerRequest
			};
			ListObjectsV2Response resp = await _client.ListObjectsV2Async(request, ct);
			IEnumerable<S3Object> s3Objects = resp.S3Objects;
			foreach (S3Object s3Object in s3Objects ?? Enumerable.Empty<S3Object>())
			{
				yield return s3Object;
			}
			continuationToken = resp.NextContinuationToken;
		}
		while (!string.IsNullOrEmpty(continuationToken));
	}

	public long GetLength(string bktName, string key)
	{
		string cachedUrl = GetCachedUrl(bktName, key, HttpVerb.HEAD);
		HttpClient client = GetClient();
		HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Head, cachedUrl);
		return client.Send(request).Content.Headers.ContentLength.GetValueOrDefault();
	}

	public async Task<long> GetLengthAsync(string bktName, string key, CancellationToken ct = default(CancellationToken))
	{
		return (await _client.GetObjectMetadataAsync(new GetObjectMetadataRequest
		{
			BucketName = bktName,
			Key = key
		}, ct)).ContentLength;
	}

	public async Task<bool> ObjectExists(string bucket, string key)
	{
		try
		{
			await CheckHeader(bucket, key);
			return true;
		}
		catch
		{
			return false;
		}
	}

	public async Task<GetObjectMetadataResponse> CheckHeader(string bucket, string key)
	{
		GetObjectMetadataRequest request = new GetObjectMetadataRequest
		{
			BucketName = bucket,
			Key = key
		};
		return await _client.GetObjectMetadataAsync(request);
	}

	public async Task PushSingleFileAsync(string bucket, string key, string filename, string owner = "", ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		CancellationTokenSource src;
		using (TransferUtility transfer = new TransferUtility(_client))
		{
			TransferUtilityUploadRequest request = new TransferUtilityUploadRequest
			{
				BucketName = bucket,
				FilePath = filename,
				Key = key
			};
			if (!string.IsNullOrEmpty(owner))
			{
				MetadataCollection metadata = new MetadataCollection();
				await S3TransferHelper.AddPushMetadataAsync(filename, metadata, owner);
			}
			src = new CancellationTokenSource();
			reporter.ReportMessage("Uploading {fn}", filename);
			request.UploadProgressEvent += UploadObjectProgress;
			await transfer.UploadAsync(request, src.Token);
			request.UploadProgressEvent -= UploadObjectProgress;
			reporter.ReportMessage("Upload comleted.");
			reporter.ReportProgressFraction(1f);
		}
		void UploadObjectProgress(object sender, UploadProgressArgs e)
		{
			reporter.ReportProgressFraction((float)e.PercentDone / 100f);
			if (reporter.CancelRequested)
			{
				src.Cancel();
			}
		}
	}

	public async Task PullSingleFileAsync(string bucket, string key, string filename, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		CancellationTokenSource src;
		using (TransferUtility transfer = new TransferUtility(_client))
		{
			TransferUtilityDownloadRequest getReq = new TransferUtilityDownloadRequest
			{
				BucketName = bucket,
				FilePath = filename,
				Key = key
			};
			src = new CancellationTokenSource();
			reporter.ReportMessage("Downloading {fn}", filename);
			getReq.WriteObjectProgressEvent += WriteObjectProgress;
			await transfer.DownloadAsync(getReq, src.Token);
			getReq.WriteObjectProgressEvent -= WriteObjectProgress;
			reporter.ReportMessage("Download completed.");
			reporter.ReportProgressFraction(1f);
		}
		void WriteObjectProgress(object sender, WriteObjectProgressArgs e)
		{
			reporter.ReportProgressFraction((float)e.PercentDone / 100f);
			if (reporter.CancelRequested)
			{
				src.Cancel();
			}
		}
	}

	public async Task PullSingleFileParallelAsync(string bucket, string key, string filename, ProgressReporter reporter = null, int maxParallelism = 8, long chunkSize = 8388608L, CancellationToken ct = default(CancellationToken))
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		long length = await GetLengthAsync(bucket, key, ct);
		if (length <= chunkSize)
		{
			await PullSingleFileAsync(bucket, key, filename, reporter);
			return;
		}
		string directoryName = Path.GetDirectoryName(filename);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		int lengthChunkSizeTerm = (int)((length + chunkSize - 1) / chunkSize);
		reporter.ReportMessage("Downloading {fn} ({n} parts, {p}-way)", filename, lengthChunkSizeTerm, maxParallelism);
		SafeFileHandle handle = File.OpenHandle(filename, FileMode.Create, FileAccess.Write, FileShare.None, FileOptions.Asynchronous, length);
		try
		{
			RandomAccess.SetLength(handle, length);
			long bytesCompleted = 0L;
			CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			try
			{
				ParallelOptions parallelOptions = new ParallelOptions
				{
					MaxDegreeOfParallelism = maxParallelism,
					CancellationToken = linkedCts.Token
				};
				await System.Threading.Tasks.Parallel.ForEachAsync(Enumerable.Range(0, lengthChunkSizeTerm), parallelOptions, async (int i, CancellationToken token) =>
				{
					long chunkSizeTerm = i * chunkSize;
					long count = Math.Min(chunkSize, length - chunkSizeTerm);
					await DownloadRangeToFileAsync(bucket, key, chunkSizeTerm, count, handle, token);
					long interlockedBytesCompletedTerm = Interlocked.Add(ref bytesCompleted, count);
					reporter.ReportProgressFraction((float)interlockedBytesCompletedTerm / (float)length);
					if (reporter.CancelRequested)
					{
						linkedCts.Cancel();
					}
				});
				reporter.ReportMessage("Download completed.");
				reporter.ReportProgressFraction(1f);
			}
			finally
			{
				if (linkedCts != null)
				{
					((IDisposable)linkedCts).Dispose();
				}
			}
		}
		finally
		{
			if (handle != null)
			{
				((IDisposable)handle).Dispose();
			}
		}
	}

	private async Task DownloadRangeToFileAsync(string bucket, string key, long start, long count, SafeFileHandle handle, CancellationToken ct)
	{
		GetObjectRequest request = new GetObjectRequest
		{
			BucketName = bucket,
			Key = key,
			ByteRange = new ByteRange(start, start + count - 1)
		};
		using GetObjectResponse resp = await _client.GetObjectAsync(request, ct);
		using Stream stream = resp.ResponseStream;
		int bufferSize = (int)Math.Min(count, 1048576L);
		byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
		try
		{
			long offset = start;
			while (true)
			{
				int readAsync;
				int read = (readAsync = await stream.ReadAsync(buffer.AsMemory(0, bufferSize), ct));
				if (readAsync <= 0)
				{
					break;
				}
				await RandomAccess.WriteAsync(handle, buffer.AsMemory(0, read), offset, ct);
				offset += read;
			}
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}

	public static void ForceCacheURL(string bktName, string key, HttpVerb verb, string url)
	{
		_urls[(bktName, key, verb)] = url;
	}

	private string GetCachedUrl(string bktName, string key, HttpVerb verb)
	{
		(string, string, HttpVerb) key2 = (bktName, key, verb);
		lock (_urls)
		{
			if (_urls.TryGetValue(key2, out var urls))
			{
				return urls;
			}
			urls = GetPresignedURLInternal(bktName, key, verb);
			_urls[key2] = urls;
			return urls;
		}
	}

	public void GetRange(string bktName, string key, long start, Memory<byte> buffer)
	{
		try
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			string cachedUrl = GetCachedUrl(bktName, key, HttpVerb.GET);
			HttpClient client = GetClient();
			using HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, cachedUrl);
			int length = buffer.Length;
			httpRequestMessage.Headers.Add("Range", "bytes=" + start + "-" + (start + length - 1));
			using HttpResponseMessage httpResponseMessage = client.Send(httpRequestMessage);
			httpResponseMessage.EnsureSuccessStatusCode();
			using Stream stream = httpResponseMessage.Content.ReadAsStream();
			stream.ReadExactly(buffer.Span);
			stopwatch.Stop();
			Record(start, length, stopwatch.Elapsed.TotalMilliseconds);
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Error getting range from S3 key");
			throw;
		}
	}

	public async Task GetRangeAsync(string bktName, string objName, long start, Memory<byte> buffer)
	{
		int length = buffer.Length;
		GetObjectRequest getObjectRequest = new GetObjectRequest();
		getObjectRequest.BucketName = bktName;
		getObjectRequest.Key = objName;
		getObjectRequest.ByteRange = new ByteRange(start, start + length - 1);
		using GetObjectResponse getObjectResponse = await _client.GetObjectAsync(getObjectRequest);
		using Stream stream = getObjectResponse.ResponseStream;
		await stream.ReadExactlyAsync(buffer).ConfigureAwait(false);
	}

	public string GetPresignedURL(string bktName, string key)
	{
		return GetPresignedURLInternal(bktName, key, HttpVerb.GET);
	}

	public string GetPresignedURL(string bktName, string key, HttpVerb verb)
	{
		return GetPresignedURLInternal(bktName, key, verb);
	}

	private string GetPresignedURLInternal(string bktName, string key, HttpVerb verb)
	{
		GetPreSignedUrlRequest request = new GetPreSignedUrlRequest
		{
			BucketName = bktName,
			Key = key,
			Verb = verb,
			Expires = DateTime.UtcNow.AddHours(168.0),
			Protocol = _protocol
		};
		return _client.GetPreSignedURL(request);
	}

	public async Task<string> GetPresignedPutURLAsync(string bucket, string key, TimeSpan? expireDuration = null)
	{
		TimeSpan expireDuration2 = expireDuration ?? TimeSpan.FromDays(999);
		GetPreSignedUrlRequest request = new GetPreSignedUrlRequest
		{
			BucketName = bucket,
			Key = key,
			Verb = HttpVerb.PUT,
			Expires = DateTime.UtcNow.Add(expireDuration2),
			Protocol = _protocol
		};
		return await _client.GetPreSignedURLAsync(request);
	}

	public async Task DeleteKeyAsync(string bucket, string key)
	{
		DeleteObjectRequest request = new DeleteObjectRequest
		{
			BucketName = bucket,
			Key = key
		};
		await _client.DeleteObjectAsync(request);
	}

	public async Task<CopyObjectResponse> CopyObjectAsync(string sourceBucket, string sourceKey, string destBucket, string destKey)
	{
		CopyObjectRequest request = new CopyObjectRequest
		{
			SourceBucket = sourceBucket,
			SourceKey = sourceKey,
			DestinationBucket = destBucket,
			DestinationKey = destKey
		};
		return await _client.CopyObjectAsync(request);
	}

	public async Task<CopyObjectResponse> MoveObjectAsync(string sourceBucket, string sourceKey, string destBucket, string destKey)
	{
		CopyObjectResponse copyResponse = await CopyObjectAsync(sourceBucket, sourceKey, destBucket, destKey);
		await DeleteKeyAsync(sourceBucket, sourceKey);
		return copyResponse;
	}

	public async Task<bool> IsAuthenticatedForBucket(string modelBucket)
	{
		try
		{
			await GetBucketLocation(modelBucket);
			return true;
		}
		catch (Exception)
		{
		}
		return false;
	}

	private static HttpClient GetClient()
	{
		return _httpClient;
	}

	private void Record(long start, long count, double ms)
	{
		_logger.LogTrace("Read Request {Start}: [{Count}], {TimeMs} ms", start, count, ms);
		lock (_readLock)
		{
			_readCt++;
			_readBytes += count;
			_readMs += ms;
			if (_readCt == 100)
			{
				_logger.LogDebug("Average S3 Reads: {Size}, {TimeMs} ms.", _readBytes / 100, _readMs / 100.0);
				_readCt = 0;
				_readMs = 0.0;
				_readBytes = 0L;
			}
		}
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_isDisposed)
		{
			if (disposing && _shouldDispose)
			{
				_client.Dispose();
			}
			_isDisposed = true;
		}
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
