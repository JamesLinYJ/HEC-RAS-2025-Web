using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Geospatial.IO.Network.USGS;

public class TiffMetadataInserter : IDisposable
{
	private readonly SqliteConnection _conn;

	private readonly SqliteTransaction _transaction;

	private readonly Dictionary<string, long> _projectionCache = new Dictionary<string, long>();

	private long _nextProjectionId = 1L;

	private readonly SqliteCommand _insertProjectionCmd;

	private readonly SqliteParameter _pProjId;

	private readonly SqliteParameter _pProjWkt;

	private readonly SqliteCommand _insertMetadataCmd;

	private readonly SqliteParameter _pFileId;

	private readonly SqliteParameter _pSize;

	private readonly SqliteParameter _pProjectionId;

	private readonly SqliteParameter _pCellSize;

	private readonly SqliteParameter _pLeft;

	private readonly SqliteParameter _pTop;

	private readonly SqliteParameter _pRight;

	private readonly SqliteParameter _pBottom;

	public int ProjectionCount => _projectionCache.Count;

	public TiffMetadataInserter(SqliteConnection conn, SqliteTransaction transaction = null)
	{
		_conn = conn;
		_transaction = transaction ?? conn.BeginTransaction();
		using (SqliteCommand sqliteCommand = conn.CreateCommand())
		{
			sqliteCommand.Transaction = _transaction;
			sqliteCommand.CommandText = "SELECT COALESCE(MAX(id), 0) + 1 FROM projections";
			_nextProjectionId = (long)sqliteCommand.ExecuteScalar();
		}
		using (SqliteCommand sqliteCommand2 = conn.CreateCommand())
		{
			sqliteCommand2.Transaction = _transaction;
			sqliteCommand2.CommandText = "SELECT id, wkt FROM projections";
			using SqliteDataReader sqliteDataReader = sqliteCommand2.ExecuteReader();
			while (sqliteDataReader.Read())
			{
				_projectionCache[sqliteDataReader.GetString(1)] = sqliteDataReader.GetInt64(0);
			}
		}
		_insertProjectionCmd = conn.CreateCommand();
		_insertProjectionCmd.Transaction = _transaction;
		_insertProjectionCmd.CommandText = "INSERT INTO projections (id, wkt) VALUES ($id, $wkt)";
		_pProjId = _insertProjectionCmd.Parameters.Add("$id", SqliteType.Integer);
		_pProjWkt = _insertProjectionCmd.Parameters.Add("$wkt", SqliteType.Text);
		_insertMetadataCmd = conn.CreateCommand();
		_insertMetadataCmd.Transaction = _transaction;
		_insertMetadataCmd.CommandText = "\r\n            INSERT INTO tiff_metadata (file_id, size, projection_id, cell_size, left, top, right, bottom)\r\n            VALUES ($fileId, $size, $projId, $cellSize, $left, $top, $right, $bottom)";
		_pFileId = _insertMetadataCmd.Parameters.Add("$fileId", SqliteType.Integer);
		_pSize = _insertMetadataCmd.Parameters.Add("$size", SqliteType.Integer);
		_pProjectionId = _insertMetadataCmd.Parameters.Add("$projId", SqliteType.Integer);
		_pCellSize = _insertMetadataCmd.Parameters.Add("$cellSize", SqliteType.Real);
		_pLeft = _insertMetadataCmd.Parameters.Add("$left", SqliteType.Real);
		_pTop = _insertMetadataCmd.Parameters.Add("$top", SqliteType.Real);
		_pRight = _insertMetadataCmd.Parameters.Add("$right", SqliteType.Real);
		_pBottom = _insertMetadataCmd.Parameters.Add("$bottom", SqliteType.Real);
	}

	public long? GetOrCreateProjection(string wkt)
	{
		if (string.IsNullOrEmpty(wkt))
		{
			return null;
		}
		if (_projectionCache.TryGetValue(wkt, out var localValue))
		{
			return localValue;
		}
		long projectionCache = _nextProjectionId++;
		_pProjId.Value = projectionCache;
		_pProjWkt.Value = wkt;
		_insertProjectionCmd.ExecuteNonQuery();
		_projectionCache[wkt] = projectionCache;
		return projectionCache;
	}

	public void Insert(long fileId, long size, string projectionWkt, float cellSize, float left, float top, float right, float bottom)
	{
		long? orCreateProjection = GetOrCreateProjection(projectionWkt);
		_pFileId.Value = fileId;
		_pSize.Value = size;
		_pProjectionId.Value = (orCreateProjection.HasValue ? ((object)orCreateProjection.Value) : DBNull.Value);
		_pCellSize.Value = cellSize;
		_pLeft.Value = left;
		_pTop.Value = top;
		_pRight.Value = right;
		_pBottom.Value = bottom;
		_insertMetadataCmd.ExecuteNonQuery();
	}

	public void Commit()
	{
		_transaction.Commit();
	}

	public void Dispose()
	{
		_insertProjectionCmd?.Dispose();
		_insertMetadataCmd?.Dispose();
		_transaction?.Dispose();
	}
}
