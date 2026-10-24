using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3.Model;
using Utility.Communication;

namespace Geospatial.IO.Network.S3;

public class S3BucketIndexBuilder
{
	public async Task<int> BuildIndexAsync(S3IndexConfig config, IProgress<S3IndexProgress> progress = null, CancellationToken ct = default(CancellationToken))
	{
		string directoryName = Path.GetDirectoryName(config.OutputDatabasePath);
		if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		using S3Client s3Client = new S3Client(config.ServiceUrl);
		using S3IndexSqliteWriter writer = new S3IndexSqliteWriter(config.OutputDatabasePath, config.BatchSize);
		IAsyncEnumerable<S3ObjectRecord> records = TransformToRecordsAsync(s3Client, config.BucketName, config.ServiceUrl, config.KeyPrefix, config.MaxKeysPerRequest, ct);
		int count = await writer.WriteRecordsAsync(records, progress, config.ProgressInterval, ct);
		progress?.Report(new S3IndexProgress(count, count, "Creating indexes..."));
		await writer.FinalizeAsync(ct);
		return count;
	}

	private static async IAsyncEnumerable<S3ObjectRecord> TransformToRecordsAsync(S3Client s3Client, string bucketName, string serviceUrl, string keyPrefix, int maxKeysPerRequest, [EnumeratorCancellation] CancellationToken ct)
	{
		string baseUrl = serviceUrl.TrimEnd('/');
		if (!baseUrl.Contains(bucketName))
		{
			baseUrl = baseUrl + "/" + bucketName;
		}
		await foreach (S3Object enumerateObjectsAsync in s3Client.EnumerateObjectsAsync(bucketName, keyPrefix, maxKeysPerRequest, ct))
		{
			string extension = GetExtension(enumerateObjectsAsync.Key);
			string downloadUrl = baseUrl + "/" + enumerateObjectsAsync.Key;
			yield return new S3ObjectRecord(enumerateObjectsAsync.Key, extension, downloadUrl);
		}
	}

	private static string GetExtension(string key)
	{
		int lastIndexOf = key.LastIndexOf('.');
		if (lastIndexOf < 0 || lastIndexOf == key.Length - 1)
		{
			return null;
		}
		if (key.LastIndexOf('/') > lastIndexOf)
		{
			return null;
		}
		return key.Substring(lastIndexOf + 1).ToLowerInvariant();
	}
}
