using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Data.Sqlite;

namespace Geospatial.IO.Network.S3;

public class S3TrieMigrator
{
	public TrieMigrationResult Migrate(string sourceDb, string destDb, IProgress<TrieMigrationProgress> progress = null, int progressInterval = 100000)
	{
		if (File.Exists(destDb))
		{
			File.Delete(destDb);
		}
		using SqliteConnection sqliteConnection = new SqliteConnection("Data Source=" + sourceDb + ";Mode=ReadOnly");
		sqliteConnection.Open();
		using SqliteConnection sqliteConnection2 = new SqliteConnection("Data Source=" + destDb);
		sqliteConnection2.Open();
		CreateTrieSchema(sqliteConnection2);
		Dictionary<(long?, string), long> segmentCache = new Dictionary<(long?, string), long>();
		long nextSegmentId = 1L;
		SqliteCommand insertSegmentCmd;
		SqliteParameter pSegId;
		SqliteParameter pSegParent;
		SqliteParameter pSegSegment;
		using (SqliteTransaction sqliteTransaction = sqliteConnection2.BeginTransaction())
		{
			insertSegmentCmd = sqliteConnection2.CreateCommand();
			insertSegmentCmd.Transaction = sqliteTransaction;
			insertSegmentCmd.CommandText = "INSERT INTO path_segments (id, parent_id, segment) VALUES ($id, $parent, $segment)";
			pSegId = insertSegmentCmd.Parameters.Add("$id", SqliteType.Integer);
			pSegParent = insertSegmentCmd.Parameters.Add("$parent", SqliteType.Integer);
			pSegSegment = insertSegmentCmd.Parameters.Add("$segment", SqliteType.Text);
			SqliteCommand sqliteCommand = sqliteConnection2.CreateCommand();
			sqliteCommand.Transaction = sqliteTransaction;
			sqliteCommand.CommandText = "INSERT INTO files (path_id, filename, extension, download_url) VALUES ($pathId, $filename, $ext, $url)";
			SqliteParameter sqliteParameter = sqliteCommand.Parameters.Add("$pathId", SqliteType.Integer);
			SqliteParameter sqliteParameter2 = sqliteCommand.Parameters.Add("$filename", SqliteType.Text);
			SqliteParameter sqliteParameter3 = sqliteCommand.Parameters.Add("$ext", SqliteType.Text);
			SqliteParameter sqliteParameter4 = sqliteCommand.Parameters.Add("$url", SqliteType.Text);
			using SqliteCommand sqliteCommand2 = sqliteConnection.CreateCommand();
			sqliteCommand2.CommandText = "SELECT key, extension, download_url FROM s3_objects";
			Stopwatch stopwatch = Stopwatch.StartNew();
			long ordinal = 0L;
			long ordinal2 = 0L;
			using SqliteDataReader sqliteDataReader = sqliteCommand2.ExecuteReader();
			while (sqliteDataReader.Read())
			{
				string getString = sqliteDataReader.GetString(0);
				string getString2 = (sqliteDataReader.IsDBNull(1) ? null : sqliteDataReader.GetString(1));
				string getString3 = sqliteDataReader.GetString(2);
				int lastIndexOf = getString.LastIndexOf('/');
				string substringGetStringTerm;
				string substringGetStringTerm2;
				if (lastIndexOf >= 0)
				{
					substringGetStringTerm = getString.Substring(0, lastIndexOf);
					substringGetStringTerm2 = getString.Substring(lastIndexOf + 1);
				}
				else
				{
					substringGetStringTerm = "";
					substringGetStringTerm2 = getString;
				}
				string[] parts = (string.IsNullOrEmpty(substringGetStringTerm) ? Array.Empty<string>() : substringGetStringTerm.Split('/'));
				long orCreatePath = ((parts.Length != 0) ? GetOrCreatePath(parts) : GetOrCreateSegment(null, ""));
				sqliteParameter.Value = orCreatePath;
				sqliteParameter2.Value = substringGetStringTerm2;
				sqliteParameter3.Value = ((getString2 != null) ? ((IConvertible)getString2) : ((IConvertible)DBNull.Value));
				sqliteParameter4.Value = getString3;
				sqliteCommand.ExecuteNonQuery();
				ordinal++;
				if (ordinal - ordinal2 >= progressInterval)
				{
					double totalSeconds = stopwatch.Elapsed.TotalSeconds;
					double rowsPerSecond = (double)ordinal / totalSeconds;
					progress?.Report(new TrieMigrationProgress(ordinal, segmentCache.Count, rowsPerSecond));
					ordinal2 = ordinal;
				}
			}
			sqliteTransaction.Commit();
			CreateFtsIndex(sqliteConnection2);
			using (SqliteCommand sqliteCommand3 = sqliteConnection2.CreateCommand())
			{
				sqliteCommand3.CommandText = "VACUUM";
				sqliteCommand3.ExecuteNonQuery();
			}
			stopwatch.Stop();
			long length = new FileInfo(sourceDb).Length;
			long length2 = new FileInfo(destDb).Length;
			return new TrieMigrationResult(ordinal, segmentCache.Count, stopwatch.Elapsed, length, length2);
		}
		long GetOrCreatePath(string[] segments)
		{
			long? parentId = null;
			foreach (string segment in segments)
			{
				parentId = GetOrCreateSegment(parentId, segment);
			}
			return parentId.Value;
		}
		long GetOrCreateSegment(long? parentId, string segment)
		{
			(long?, string) key = (parentId, segment);
			if (segmentCache.TryGetValue(key, out var localValue))
			{
				return localValue;
			}
			long segmentCache2 = nextSegmentId++;
			pSegId.Value = segmentCache2;
			pSegParent.Value = (parentId.HasValue ? ((object)parentId.Value) : DBNull.Value);
			pSegSegment.Value = segment;
			insertSegmentCmd.ExecuteNonQuery();
			segmentCache[key] = segmentCache2;
			return segmentCache2;
		}
	}

	private static void CreateTrieSchema(SqliteConnection conn)
	{
		using SqliteCommand sqliteCommand = conn.CreateCommand();
		sqliteCommand.CommandText = "\r\n            CREATE TABLE path_segments (\r\n                id INTEGER PRIMARY KEY,\r\n                parent_id INTEGER,\r\n                segment TEXT NOT NULL,\r\n                FOREIGN KEY (parent_id) REFERENCES path_segments(id),\r\n                UNIQUE(parent_id, segment)\r\n            );\r\n\r\n            CREATE TABLE files (\r\n                id INTEGER PRIMARY KEY,\r\n                path_id INTEGER NOT NULL,\r\n                filename TEXT NOT NULL,\r\n                extension TEXT,\r\n                download_url TEXT NOT NULL,\r\n                FOREIGN KEY (path_id) REFERENCES path_segments(id)\r\n            );\r\n\r\n            CREATE INDEX idx_path_segments_parent ON path_segments(parent_id);\r\n            CREATE INDEX idx_files_path ON files(path_id);\r\n            CREATE INDEX idx_files_extension ON files(extension);\r\n        ";
		sqliteCommand.ExecuteNonQuery();
	}

	private static void CreateFtsIndex(SqliteConnection conn)
	{
		using SqliteCommand sqliteCommand = conn.CreateCommand();
		sqliteCommand.CommandText = "\r\n            CREATE VIRTUAL TABLE files_fts USING fts5(\r\n                filename,\r\n                content='files',\r\n                content_rowid='id'\r\n            );\r\n            INSERT INTO files_fts(files_fts) VALUES('rebuild');\r\n        ";
		sqliteCommand.ExecuteNonQuery();
	}

	public static string GetFullPath(SqliteConnection conn, long fileId)
	{
		string getString;
		long @int;
		using (SqliteCommand sqliteCommand = conn.CreateCommand())
		{
			sqliteCommand.CommandText = "SELECT filename, path_id FROM files WHERE id = $id";
			sqliteCommand.Parameters.AddWithValue("$id", fileId);
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			if (!sqliteDataReader.Read())
			{
				return null;
			}
			getString = sqliteDataReader.GetString(0);
			@int = sqliteDataReader.GetInt64(1);
		}
		List<string> stringList = new List<string>();
		long? isDBNullSqliteDataReader2Term = @int;
		using (SqliteCommand sqliteCommand2 = conn.CreateCommand())
		{
			sqliteCommand2.CommandText = "SELECT segment, parent_id FROM path_segments WHERE id = $id";
			SqliteParameter sqliteParameter = sqliteCommand2.Parameters.Add("$id", SqliteType.Integer);
			while (isDBNullSqliteDataReader2Term.HasValue)
			{
				sqliteParameter.Value = isDBNullSqliteDataReader2Term.Value;
				using SqliteDataReader sqliteDataReader2 = sqliteCommand2.ExecuteReader();
				if (!sqliteDataReader2.Read())
				{
					break;
				}
				string getString2 = sqliteDataReader2.GetString(0);
				if (!string.IsNullOrEmpty(getString2))
				{
					stringList.Add(getString2);
				}
				isDBNullSqliteDataReader2Term = (sqliteDataReader2.IsDBNull(1) ? ((long?)null) : new long?(sqliteDataReader2.GetInt64(1)));
				continue;
			}
		}
		stringList.Reverse();
		stringList.Add(getString);
		return string.Join("/", stringList);
	}
}
