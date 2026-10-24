using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Utility.Extensions;
using Utility.Files;
using Utility.Units;

namespace Utility.Communication;

public class S3TransferHelper : IDisposable
{
	public class TransferError
	{
		public string File;

		public Exception Exception;

		public TransferError(string fn, Exception ex)
		{
			File = fn;
			Exception = ex;
		}
	}

	private class TransferRequestWrapper
	{
		public string Filename { get; set; }

		public S3Object Object { get; set; }

		public GetObjectMetadataResponse Response { get; set; }

		public TransferRequest TransferRequest { get; set; }

		public TransferRequestWrapper(string fn, S3Object s3Object, GetObjectMetadataResponse resp, TransferRequest tr)
		{
			Filename = fn;
			Object = s3Object;
			Response = resp;
			TransferRequest = tr;
		}
	}

	private const string TimeMetadataKey = "mtime";

	private const string OwnerKey = "owner";

	private const string HashKey = "hash_md5";

	private const string CompressionKey = "compression";

	private const string CompressionValueNone = "none";

	private const string CompressionValueDeflate = "deflate";

	private const string CompressionOriginalSizeKey = "uncompressed-size";

	private const string MetadataReplacedKey = "metadata-replaced";

	private const string CompressedEntryName = "__FILE";

	private const int RetryDownloadCount = 10;

	private const int RetryWaitPeriodMs = 1000;

	private readonly string _currentOwner;

	public static Dictionary<string, string> FriendlyOwnerNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		{ "SKM9IWUQ6KZ5YRVZA5XR", "Kennedy" },
		{ "BJTCIJVBFCAAX5Y8CW9U", "Mark" },
		{ "YJQQQT10LIDODENWFLH1", "Anton" },
		{ "FZBOMM7NTTE31BB8QSWK", "Zach" },
		{ "C40H3Z78V5ZIOG7DARF4", "Sanchez" },
		{ "FRYBXUDR19XT0F9TGQHZ", "Cam" },
		{ "Q8SEOQTOLJHXMXVDD6E6", "Steve" },
		{ "GGBJJIF49DVBGSFA5KNJ", "TeamCity" }
	};

	private readonly string _bucket;

	private readonly S3Client _client;

	private readonly TransferUtility _transfer;

	private bool _isDisposed;

	private Dictionary<string, Tuple<S3Object, GetObjectMetadataResponse>> _mostRecentQueries = new Dictionary<string, Tuple<S3Object, GetObjectMetadataResponse>>();

	private LocalFileCache _fileCache = new LocalFileCache();

	public string Bucket => _bucket;

	public S3TransferHelper(string url, string bucket, string key, string secret, bool forcePathStyle = false)
	{
		_currentOwner = key;
		_client = new S3Client(url, key, secret, forcePathStyle);
		_transfer = new TransferUtility(_client.AmazonClient);
		_bucket = bucket;
	}

	public async Task PushSingleFileAsync(string filename, string key)
	{
		TransferUtilityUploadRequest request = new TransferUtilityUploadRequest
		{
			BucketName = _bucket,
			FilePath = filename,
			Key = key
		};
		await AddPushMetadataAsync(filename, request.Metadata);
		await _transfer.UploadAsync(request);
	}

	public async Task PullSingleFileAsync(string filename, string key)
	{
		TransferUtilityDownloadRequest request = new TransferUtilityDownloadRequest
		{
			BucketName = _bucket,
			FilePath = filename,
			Key = key
		};
		Task task = _transfer.DownloadAsync(request);
		Task<GetObjectMetadataResponse> headerTask = CheckHeader(key);
		_003C_003Ey__InlineArray2<Task> buffer = default;
		buffer[0] = task;
		buffer[1] = headerTask;
		await Task.WhenAll(buffer);
		if (TryParseDateTime(headerTask.Result, out var dt))
		{
			File.SetLastWriteTime(filename, dt);
		}
	}

	public async Task CopyKeyAsync(string key, string newKey)
	{
		CopyObjectRequest request = new CopyObjectRequest
		{
			SourceBucket = _bucket,
			DestinationBucket = _bucket,
			SourceKey = key,
			DestinationKey = newKey
		};
		await _client.AmazonClient.CopyObjectAsync(request);
	}

	public async Task MoveKeyAsync(string key, string newKey)
	{
		await CopyKeyAsync(key, newKey);
		DeleteObjectRequest request = new DeleteObjectRequest
		{
			BucketName = _bucket,
			Key = key
		};
		await _client.AmazonClient.DeleteObjectAsync(request);
	}

	public async Task DeleteFileAsync(TransferRequest request, bool isRemote)
	{
		if (!isRemote)
		{
			File.Delete(request.LocalFile.Filename);
			return;
		}
		DeleteObjectRequest request2 = new DeleteObjectRequest
		{
			BucketName = _bucket,
			Key = request.RemoteFile.Filename
		};
		await _client.AmazonClient.DeleteObjectAsync(request2);
	}

	public static bool ShouldCompressBeforeUpload(FileProxy localFile)
	{
		string filename = localFile.Filename;
		if (Path.GetFileName(filename).RoughlyEquals("Example_Projects.exe"))
		{
			return false;
		}
		if (filename.RoughlyEndsWith(".dll") || filename.RoughlyEndsWith(".exe"))
		{
			return true;
		}
		return false;
	}

	public async Task TransferFileAsync(TransferRequest transfer)
	{
		if (transfer.TransferDirection == TransferDirection.LocalToRemote)
		{
			if (ShouldCompressBeforeUpload(transfer.LocalFile))
			{
				string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
				await Task.Run(() =>
				{
					using ZipArchive destination = ZipFile.Open(tmp, ZipArchiveMode.Create);
					destination.CreateEntryFromFile(transfer.LocalFile.Filename, "__FILE");
				});
				TransferUtilityUploadRequest transferUtilityUploadRequest = new TransferUtilityUploadRequest
				{
					BucketName = _bucket,
					FilePath = tmp,
					Key = transfer.RemoteFile.Filename
				};
				AddZippedPushMetadata(transfer.LocalFile, tmp, transferUtilityUploadRequest.Metadata);
				if (transfer.TransferProgressCallback != null)
				{
					transferUtilityUploadRequest.UploadProgressEvent += transfer.S3Request_UploadProgressEvent;
				}
				await _transfer.UploadAsync(transferUtilityUploadRequest);
				FileHelpers.TryDelete(tmp);
			}
			else
			{
				TransferUtilityUploadRequest request = new TransferUtilityUploadRequest
				{
					BucketName = _bucket,
					FilePath = transfer.LocalFile.Filename,
					Key = transfer.RemoteFile.Filename
				};
				await AddPushMetadataAsync(transfer.LocalFile.Filename, request.Metadata);
				if (transfer.TransferProgressCallback != null)
				{
					request.UploadProgressEvent += transfer.S3Request_UploadProgressEvent;
				}
				await _transfer.UploadAsync(request);
			}
		}
		else
		{
			if (transfer.TransferDirection != TransferDirection.RemoteToLocal)
			{
				return;
			}
			TransferUtilityDownloadRequest getReq;
			bool success;
			if (transfer.RemoteFile.IsCompressed)
			{
				string tmp2 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
				getReq = new TransferUtilityDownloadRequest
				{
					BucketName = _bucket,
					FilePath = tmp2,
					Key = transfer.RemoteFile.Filename
				};
				if (transfer.TransferProgressCallback != null)
				{
					getReq.WriteObjectProgressEvent += transfer.S3Request_WriteObjectProgressEvent;
				}
				success = false;
				for (int i = 0; i < 10; i++)
				{
					try
					{
						await _transfer.DownloadAsync(getReq);
						success = true;
					}
					catch (Exception ex)
					{
						if (ex is IOException ex2 && ex2.Message.Contains("The response ended prematurely"))
						{
							throw new Exception("Could not download " + transfer.LocalFile.Filename + " - there appears to be an error on the server." + Environment.NewLine + ex2.Message);
						}
						await Task.Delay(1000);
						continue;
					}
					break;
				}
				if (!success)
				{
					throw new Exception("Could not download " + transfer.LocalFile.Filename + " - some process is likely holding onto it.");
				}
				string tmp3 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
				await Task.Run(() =>
				{
					using ZipArchive zipArchive = ZipFile.Open(tmp2, ZipArchiveMode.Read);
					zipArchive.GetEntry("__FILE").ExtractToFile(tmp3);
				});
				if (File.Exists(transfer.LocalFile.Filename))
				{
					File.Delete(transfer.LocalFile.Filename);
				}
				string directoryName = Path.GetDirectoryName(transfer.LocalFile.Filename);
				if (!Directory.Exists(directoryName))
				{
					Directory.CreateDirectory(directoryName);
				}
				File.Copy(tmp3, transfer.LocalFile.Filename);
				File.SetLastWriteTime(transfer.LocalFile.Filename, transfer.RemoteFile.Timestamp);
				File.Delete(tmp2);
				File.Delete(tmp3);
				return;
			}
			getReq = new TransferUtilityDownloadRequest
			{
				BucketName = _bucket,
				FilePath = transfer.LocalFile.Filename,
				Key = transfer.RemoteFile.Filename
			};
			if (transfer.TransferProgressCallback != null)
			{
				getReq.WriteObjectProgressEvent += transfer.S3Request_WriteObjectProgressEvent;
			}
			success = false;
			for (int i = 0; i < 10; i++)
			{
				try
				{
					await _transfer.DownloadAsync(getReq);
					success = true;
				}
				catch
				{
					await Task.Delay(1000);
					continue;
				}
				break;
			}
			if (!success)
			{
				throw new Exception("Could not download " + transfer.LocalFile.Filename + " - some process is likely holding onto it.");
			}
			File.SetLastWriteTime(transfer.LocalFile.Filename, transfer.RemoteFile.Timestamp);
		}
	}

	public async Task TransferFilesAsync(List<TransferRequest> transfers)
	{
		List<Task> tasks = new List<Task>();
		foreach (TransferRequest transfer in transfers)
		{
			if (transfer.TransferDirection != TransferDirection.Unknown)
			{
				tasks.Add(TransferFileAsync(transfer));
			}
		}
		await Task.WhenAll(tasks);
	}

	private async Task AddPushMetadataAsync(string filename, MetadataCollection metadata)
	{
		await AddPushMetadataAsync(filename, metadata, _currentOwner);
	}

	public static async Task AddPushMetadataAsync(string filename, MetadataCollection metadata, string ownerName)
	{
		metadata.Add("mtime", Time.ConvertToUnixTime(File.GetLastWriteTime(filename)).ToString());
		metadata.Add("owner", ownerName);
		metadata.Add("hash_md5", await FileHelpers.ComputeFileMD5Async(filename));
	}

	public static void ReadPushMetadata(GetObjectMetadataResponse resp, out DateTime dateLastModified, out string owner, out string md5)
	{
		if (TryParseDateTime(resp, out var dt))
		{
			dateLastModified = dt;
		}
		else
		{
			dateLastModified = DateTime.MinValue;
		}
		owner = TryParseAmzKey(resp, "owner");
		md5 = TryParseAmzKey(resp, "hash_md5");
	}

	private void AddZippedPushMetadata(FileProxy localFile, string compressedFile, MetadataCollection metadata)
	{
		metadata.Add("mtime", Time.ConvertToUnixTime(localFile.Timestamp).ToString());
		metadata.Add("owner", _currentOwner);
		metadata.Add("hash_md5", localFile.Hash);
		metadata.Add("compression", "deflate");
		metadata.Add("uncompressed-size", new FileInfo(localFile.Filename).Length.ToString());
	}

	private static FileProxy GetPullMetadata(S3Object s3Object, GetObjectMetadataResponse md)
	{
		string key = s3Object.Key;
		if (!TryParseDateTime(md, out var dt))
		{
			dt = s3Object.LastModified.GetValueOrDefault();
		}
		string owner = TryParseAmzKey(md, "owner");
		string hash = TryParseAmzKey(md, "hash_md5");
		if (TryParseAmzKey(md, "compression").RoughlyEquals("deflate"))
		{
			string tryParseAmzKey = TryParseAmzKey(md, "uncompressed-size");
			long result = 0L;
			long valueOrDefault = s3Object.Size.GetValueOrDefault();
			if (string.IsNullOrEmpty(tryParseAmzKey))
			{
				result = valueOrDefault;
			}
			else if (!long.TryParse(tryParseAmzKey, out result))
			{
				result = valueOrDefault;
			}
			return new FileProxy(key, exists: true, hash, dt, owner, result)
			{
				IsCompressed = true,
				CompressedFileSize = valueOrDefault
			};
		}
		return new FileProxy(key, exists: true, hash, dt, owner, s3Object.Size.GetValueOrDefault());
	}

	private static string TryParseAmzKey(GetObjectMetadataResponse md, string mdkey)
	{
		string x = "x-amz-meta-" + mdkey;
		foreach (string key in md.Metadata.Keys)
		{
			if (x.RoughlyEquals(key))
			{
				return md.Metadata[key];
			}
		}
		return string.Empty;
	}

	public async Task ForceTransferMetadata(IList<TransferRequest> requests)
	{
		foreach (TransferRequest request in requests)
		{
			await ForceTransferMetadata(request);
		}
	}

	public async Task ForceTransferMetadata(TransferRequest req)
	{
		if (req.TransferDirection == TransferDirection.LocalToRemote)
		{
			CopyObjectRequest coReq = new CopyObjectRequest
			{
				SourceBucket = _bucket,
				DestinationBucket = _bucket,
				SourceKey = req.RemoteFile.Filename,
				DestinationKey = req.RemoteFile.Filename
			};
			await AddPushMetadataAsync(req.LocalFile.Filename, coReq.Metadata);
			coReq.MetadataDirective = S3MetadataDirective.REPLACE;
			coReq.Metadata["metadata-replaced"] = "true";
			await _client.AmazonClient.CopyObjectAsync(coReq);
		}
		else
		{
			File.SetLastWriteTime(req.LocalFile.Filename, req.RemoteFile.Timestamp);
		}
	}

	public async Task ForcePushRemoteHash(string key, string hash, MetadataCollection originalMd)
	{
		CopyObjectRequest copyObjectRequest = new CopyObjectRequest
		{
			SourceBucket = _bucket,
			DestinationBucket = _bucket,
			SourceKey = key,
			DestinationKey = key
		};
		foreach (string key2 in originalMd.Keys)
		{
			copyObjectRequest.Metadata[key2] = originalMd[key2];
		}
		copyObjectRequest.Metadata["hash_md5"] = hash;
		copyObjectRequest.MetadataDirective = S3MetadataDirective.REPLACE;
		copyObjectRequest.Metadata["metadata-replaced"] = "true";
		await _client.AmazonClient.CopyObjectAsync(copyObjectRequest);
	}

	public async Task<ListObjectsV2Response> CheckBucket(string continuationToken = "")
	{
		ListObjectsV2Request listObjectsV2Request = new ListObjectsV2Request
		{
			BucketName = _bucket
		};
		if (!string.IsNullOrEmpty(continuationToken))
		{
			listObjectsV2Request.ContinuationToken = continuationToken;
		}
		return await _client.AmazonClient.ListObjectsV2Async(listObjectsV2Request);
	}

	public async Task<GetObjectMetadataResponse> CheckHeader(string key)
	{
		return await _client.CheckHeader(_bucket, key);
	}

	public async Task<bool> ObjectExists(string key)
	{
		return await _client.ObjectExists(_bucket, key);
	}

	private static bool TryParseDateTime(GetObjectMetadataResponse resp, out DateTime dt)
	{
		string tryParseAmzKey = TryParseAmzKey(resp, "mtime");
		if (string.IsNullOrEmpty(tryParseAmzKey))
		{
			dt = DateTime.MinValue;
			return false;
		}
		if (!long.TryParse(tryParseAmzKey, out var result))
		{
			dt = DateTime.MinValue;
			return false;
		}
		dt = Time.ConvertToLocalTime(result);
		return true;
	}

	public async Task<List<TransferRequest>> DiffAsync(string directoryRoot)
	{
		List<TransferRequest> retn = new List<TransferRequest>();
		await foreach (TransferRequest diffAsyncEnumerable in DiffAsyncEnumerable(directoryRoot))
		{
			retn.Add(diffAsyncEnumerable);
		}
		return retn;
	}

	public async IAsyncEnumerable<TransferRequest> DiffAsyncEnumerable(string directoryRoot, bool returnMatchingPairs = false, List<TransferError> errors = null)
	{
		if (directoryRoot[directoryRoot.Length - 1] != Path.DirectorySeparatorChar)
		{
			directoryRoot += Path.DirectorySeparatorChar;
		}
		ListObjectsV2Response listObjectsV2Response = await CheckBucket();
		string[] localFiles = Directory.GetFiles(directoryRoot, "*", SearchOption.AllDirectories);
		HashSet<string> hs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		List<Task<TransferRequestWrapper>> tasks = new List<Task<TransferRequestWrapper>>();
		foreach (S3Object s3Object in listObjectsV2Response.S3Objects)
		{
			if (_mostRecentQueries.TryGetValue(s3Object.Key, out var localValue) && localValue.Item1.LastModified == s3Object.LastModified)
			{
				tasks.Add(TryEvaluateTransferRequest(s3Object, localValue.Item2, directoryRoot, returnMatchingPairs));
				continue;
			}
			Task<Task<TransferRequestWrapper>> task = CheckHeader(s3Object.Key).ContinueWith((Task<GetObjectMetadataResponse> ht) => TryEvaluateTransferRequest(s3Object, ht.Result, directoryRoot, returnMatchingPairs));
			tasks.Add(task.Unwrap());
		}
		while (tasks.Count > 0)
		{
			Task<TransferRequestWrapper> task2 = await Task.WhenAny(tasks);
			tasks.Remove(task2);
			TransferRequestWrapper result = task2.Result;
			hs.Add(result.Filename);
			_mostRecentQueries[result.Object.Key] = new Tuple<S3Object, GetObjectMetadataResponse>(result.Object, result.Response);
			TransferRequest transferRequest = result.TransferRequest;
			if (transferRequest != null)
			{
				yield return transferRequest;
			}
		}
		string[] localFilesArray = localFiles;
		foreach (string fn in localFilesArray)
		{
			if (!hs.Contains(fn))
			{
				FileProxy fileProxy;
				try
				{
					fileProxy = await FileProxy.LocalFileAsync(fn);
				}
				catch (Exception ex)
				{
					errors?.Add(new TransferError(fn, ex));
					continue;
				}
				fileProxy.ShortFilename = fn.Replace(directoryRoot, "");
				string fn2 = fn.Substring(directoryRoot.Length);
				yield return new TransferRequest
				{
					TransferDirection = TransferDirection.LocalToRemote,
					LocalFile = fileProxy,
					RemoteFile = new FileProxy(fn2, exists: false, string.Empty, DateTime.MinValue, string.Empty, 0L)
				};
			}
		}
	}

	private async Task<TransferRequestWrapper> TryEvaluateTransferRequest(S3Object s3Object, GetObjectMetadataResponse md, string directoryRoot, bool returnMatchingFilesAsUnknownTransferDirection = false)
	{
		FileProxy remote = GetPullMetadata(s3Object, md);
		string key = s3Object.Key;
		string file = SanitizeForWindowsFilename(Path.Combine(directoryRoot, key));
		FileProxy fileProxy = await _fileCache.Acquire(file);
		fileProxy.ShortFilename = SanitizeForWindowsFilename(key);
		if (fileProxy.Exists)
		{
			if (AreFilesIdentical(fileProxy, remote))
			{
				if (returnMatchingFilesAsUnknownTransferDirection)
				{
					return new TransferRequestWrapper(file, s3Object, md, new TransferRequest
					{
						TransferDirection = TransferDirection.Unknown,
						LocalFile = fileProxy,
						RemoteFile = remote
					});
				}
				return new TransferRequestWrapper(file, s3Object, md, null);
			}
			long convertToUnixTime = Time.ConvertToUnixTime(fileProxy.Timestamp);
			long convertToUnixTime2 = Time.ConvertToUnixTime(remote.Timestamp);
			TransferDirection transferDirection = ((convertToUnixTime < convertToUnixTime2) ? TransferDirection.RemoteToLocal : ((convertToUnixTime <= convertToUnixTime2) ? TransferDirection.Unknown : TransferDirection.LocalToRemote));
			return new TransferRequestWrapper(file, s3Object, md, new TransferRequest
			{
				TransferDirection = transferDirection,
				LocalFile = fileProxy,
				RemoteFile = remote
			});
		}
		return new TransferRequestWrapper(file, s3Object, md, new TransferRequest
		{
			TransferDirection = TransferDirection.RemoteToLocal,
			LocalFile = fileProxy,
			RemoteFile = remote
		});
	}

	private static string SanitizeForWindowsFilename(string fn)
	{
		return fn.Replace('/', '\\');
	}

	private static bool AreFilesIdentical(FileProxy local, FileProxy remote)
	{
		return string.Equals(local.Hash, remote.Hash, StringComparison.OrdinalIgnoreCase);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_isDisposed)
		{
			if (disposing)
			{
				_transfer.Dispose();
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
