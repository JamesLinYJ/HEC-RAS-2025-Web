using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Geospatial.Vectors;
using Microsoft.Data.Sqlite;

namespace Geospatial.IO.Network.USGS;

public class TiffMetadataBatchScanner
{
	public async Task<long> ScanAsync(TiffBatchScanConfig config, IProgress<TiffScanBatchProgress> progress = null, CancellationToken ct = default(CancellationToken))
	{
		using SqliteConnection conn = new SqliteConnection("Data Source=" + config.TrieDbPath);
		await conn.OpenAsync(ct);
		TiffMetadataSchema.CreateSchema(conn);
		long totalRemaining = await GetUnprocessedCountAsync(conn, ct);
		if (totalRemaining == 0L)
		{
			return 0L;
		}
		long totalProcessed = 0L;
		while (!ct.IsCancellationRequested)
		{
			Stopwatch sw = Stopwatch.StartNew();
			List<(long fileId, string url)> batch = await GetUnprocessedBatchAsync(conn, config.BatchSize, ct);
			if (batch.Count == 0)
			{
				break;
			}
			TiffFileScanResult[] results = await ProcessBatchAsync(batch, config.MaxConcurrency, ct);
			int batchSuccessCount = await CommitResultsAsync(conn, results, ct);
			sw.Stop();
			totalProcessed += batch.Count;
			int recordsPerSecond = ((sw.Elapsed.TotalSeconds > 0.0) ? ((int)((double)batch.Count / sw.Elapsed.TotalSeconds)) : batch.Count);
			progress?.Report(new TiffScanBatchProgress(totalRemaining, totalProcessed, batchSuccessCount, batch.Count, recordsPerSecond, results.FirstOrDefault()));
		}
		return totalProcessed;
	}

	public async Task<long> GetUnprocessedCountAsync(SqliteConnection conn, CancellationToken ct = default(CancellationToken))
	{
		using SqliteCommand cmd = conn.CreateCommand();
		cmd.CommandText = "\r\n            SELECT COUNT(*)\r\n            FROM files f\r\n            LEFT JOIN tiff_metadata tm ON tm.file_id = f.id\r\n            WHERE f.extension = 'tif' AND tm.file_id IS NULL";
		return (long)(await cmd.ExecuteScalarAsync(ct));
	}

	private async Task<List<(long fileId, string url)>> GetUnprocessedBatchAsync(SqliteConnection conn, int batchSize, CancellationToken ct)
	{
		List<(long fileId, string url)> batch = new List<(long, string)>();
		using SqliteCommand cmd = conn.CreateCommand();
		cmd.CommandText = "\r\n            SELECT f.id, f.download_url\r\n            FROM files f\r\n            LEFT JOIN tiff_metadata tm ON tm.file_id = f.id\r\n            WHERE f.extension = 'tif' AND tm.file_id IS NULL\r\n            ORDER BY f.id\r\n            LIMIT $limit";
		cmd.Parameters.AddWithValue("$limit", batchSize);
		using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct);
		while (await reader.ReadAsync(ct))
		{
			batch.Add((reader.GetInt64(0), reader.GetString(1)));
		}
		return batch;
	}

	private async Task<TiffFileScanResult[]> ProcessBatchAsync(List<(long fileId, string url)> batch, int maxConcurrency, CancellationToken ct)
	{
		SemaphoreSlim semaphore = new SemaphoreSlim(maxConcurrency);
		try
		{
			return await Task.WhenAll(batch.Select(async ((long fileId, string url) valueTuple) =>
			{
				await semaphore.WaitAsync(ct);
				try
				{
					return await ScanSingleTiffAsync(valueTuple.fileId, valueTuple.url);
				}
				finally
				{
					semaphore.Release();
				}
			}));
		}
		finally
		{
			if (semaphore != null)
			{
				((IDisposable)semaphore).Dispose();
			}
		}
	}

	private async Task<TiffFileScanResult> ScanSingleTiffAsync(long fileId, string url)
	{
		try
		{
			TiffScanResult tiffScanResult = await RemoteTiffScanner.ScanAsync(url);
			if (!tiffScanResult.Success)
			{
				return new TiffFileScanResult(Success: false, fileId, 0L, null, 0f, 0f, 0f, 0f, 0f);
			}
			Extent extent = tiffScanResult.NativeRaster.Extent;
			return new TiffFileScanResult(Success: true, fileId, tiffScanResult.FileSize, tiffScanResult.ProjectionWkt, (float)tiffScanResult.NativeRaster.CellSize, (float)extent.Left, (float)extent.Top, (float)extent.Right, (float)extent.Bottom);
		}
		catch
		{
			return new TiffFileScanResult(Success: false, fileId, 0L, null, 0f, 0f, 0f, 0f, 0f);
		}
	}

	private Task<int> CommitResultsAsync(SqliteConnection conn, TiffFileScanResult[] results, CancellationToken ct)
	{
		using TiffMetadataInserter tiffMetadataInserter = new TiffMetadataInserter(conn);
		int ordinal = 0;
		foreach (TiffFileScanResult tiffFileScanResult in results)
		{
			tiffMetadataInserter.Insert(tiffFileScanResult.FileId, tiffFileScanResult.Size, tiffFileScanResult.ProjectionWkt, tiffFileScanResult.CellSize, tiffFileScanResult.Left, tiffFileScanResult.Top, tiffFileScanResult.Right, tiffFileScanResult.Bottom);
			if (tiffFileScanResult.Success)
			{
				ordinal++;
			}
		}
		tiffMetadataInserter.Commit();
		return Task.FromResult(ordinal);
	}
}
