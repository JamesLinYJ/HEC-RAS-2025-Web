using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Geospatial.IO.Network.S3;

public class S3IndexSqliteWriter : IDisposable
{
	private readonly SqliteConnection _connection;

	private readonly int _batchSize;

	private bool _disposed;

	public S3IndexSqliteWriter(string databasePath, int batchSize = 1000)
	{
		_batchSize = batchSize;
		if (File.Exists(databasePath))
		{
			File.Delete(databasePath);
		}
		string connectionString = "Data Source=" + databasePath;
		_connection = new SqliteConnection(connectionString);
		_connection.Open();
		CreateSchema();
	}

	public async Task<int> WriteRecordsAsync(IAsyncEnumerable<S3ObjectRecord> records, IProgress<S3IndexProgress> progress = null, int progressInterval = 10000, CancellationToken ct = default(CancellationToken))
	{
		int count = 0;
		List<S3ObjectRecord> batch = new List<S3ObjectRecord>(_batchSize);
		string lastKey = null;
		await foreach (S3ObjectRecord withCancellation in records.WithCancellation(ct))
		{
			batch.Add(withCancellation);
			count++;
			lastKey = withCancellation.Key;
			if (batch.Count >= _batchSize)
			{
				await InsertBatchAsync(batch, ct);
				batch.Clear();
				if (count % progressInterval == 0)
				{
					progress?.Report(new S3IndexProgress(count, count, lastKey));
				}
			}
		}
		if (batch.Count > 0)
		{
			await InsertBatchAsync(batch, ct);
		}
		progress?.Report(new S3IndexProgress(count, count, lastKey));
		return count;
	}

	public async Task FinalizeAsync(CancellationToken ct = default(CancellationToken))
	{
		await CreateIndexesAsync(ct);
		await OptimizeDatabaseAsync(ct);
	}

	private void CreateSchema()
	{
		using SqliteCommand sqliteCommand = _connection.CreateCommand();
		sqliteCommand.CommandText = "\r\n            CREATE TABLE s3_objects (\r\n                id INTEGER PRIMARY KEY AUTOINCREMENT,\r\n                key TEXT NOT NULL,\r\n                extension TEXT,\r\n                download_url TEXT NOT NULL\r\n            );\r\n        ";
		sqliteCommand.ExecuteNonQuery();
	}

	private async Task InsertBatchAsync(List<S3ObjectRecord> records, CancellationToken ct)
	{
		await using DbTransaction transaction = await _connection.BeginTransactionAsync(ct);
		using SqliteCommand command = _connection.CreateCommand();
		command.Transaction = (SqliteTransaction)transaction;
		command.CommandText = "\r\n            INSERT INTO s3_objects (key, extension, download_url)\r\n            VALUES ($key, $extension, $download_url);\r\n        ";
		SqliteParameter keyParam = command.Parameters.Add("$key", SqliteType.Text);
		SqliteParameter extensionParam = command.Parameters.Add("$extension", SqliteType.Text);
		SqliteParameter downloadUrlParam = command.Parameters.Add("$download_url", SqliteType.Text);
		foreach (S3ObjectRecord record in records)
		{
			keyParam.Value = record.Key;
			extensionParam.Value = (string.IsNullOrEmpty(record.Extension) ? ((IConvertible)DBNull.Value) : ((IConvertible)record.Extension));
			downloadUrlParam.Value = record.DownloadUrl;
			await command.ExecuteNonQueryAsync(ct);
		}
		await transaction.CommitAsync(ct);
	}

	private async Task CreateIndexesAsync(CancellationToken ct)
	{
		string[] lookupTable = new string[5] { "CREATE INDEX idx_key ON s3_objects(key);", "CREATE INDEX idx_extension ON s3_objects(extension);", "CREATE INDEX idx_extension_key ON s3_objects(extension, key);", "CREATE VIRTUAL TABLE s3_objects_fts USING fts5(\r\n                key,\r\n                extension,\r\n                content='s3_objects',\r\n                content_rowid='id'\r\n            );", "INSERT INTO s3_objects_fts(rowid, key, extension)\r\n              SELECT id, key, extension FROM s3_objects;" };
		string[] lookupTableArray = lookupTable;
		foreach (string commandText in lookupTableArray)
		{
			using SqliteCommand command = _connection.CreateCommand();
			command.CommandText = commandText;
			await command.ExecuteNonQueryAsync(ct);
		}
	}

	private async Task OptimizeDatabaseAsync(CancellationToken ct)
	{
		string[] lookupTable = new string[2] { "ANALYZE;", "VACUUM;" };
		string[] lookupTableArray = lookupTable;
		foreach (string commandText in lookupTableArray)
		{
			using SqliteCommand command = _connection.CreateCommand();
			command.CommandText = commandText;
			await command.ExecuteNonQueryAsync(ct);
		}
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_connection?.Dispose();
			_disposed = true;
		}
	}
}
