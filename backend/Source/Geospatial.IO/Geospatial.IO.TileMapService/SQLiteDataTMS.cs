using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Linq;

namespace Geospatial.IO.TileMapService;

public class SQLiteDataTMS : SQLiteTiledRegion
{
	private struct ByteTile(string tableName, int zoom, int tileX, int tileY, byte[] rasterBytes)
	{
		public string TableName = tableName;

		public int Zoom = zoom;

		public int TileX = tileX;

		public int TileY = tileY;

		public byte[] RasterValues = rasterBytes;
	}

	public static readonly string ZoomColumnName = "zoom_level";

	public static readonly string TileColumnName = "tile_column";

	public static readonly string TileRowName = "tile_row";

	public static readonly string DataColumnName = "tile_data";

	public static int MAX_TILES_CACHED = 50;

	private List<ByteTile> _tiles;

	public SQLiteDataTMS(string filename)
		: base(filename)
	{
		CreateMetadataTableIfDNE();
		_tiles = new List<ByteTile>();
	}

	public void AddIndexedRasterTable(string tableName)
	{
		CreateRasterTableIfDNE(tableName);
		CreateUniqueIDXIfDNE(tableName + "_index", tableName, ZoomColumnName, TileColumnName, TileRowName);
	}

	private void CreateRasterTableIfDNE(string tableName)
	{
		using SQLiteTransaction sQLiteTransaction = _con.BeginTransaction();
		SQLiteCommand sQLiteCommand = new SQLiteCommand("CREATE TABLE IF NOT EXISTS " + tableName + " (" + ZoomColumnName + " INT, " + TileColumnName + " INT, " + TileRowName + " INT, " + DataColumnName + " BLOB)", _con);
		sQLiteCommand.ExecuteNonQuery();
		sQLiteCommand.Dispose();
		sQLiteTransaction.Commit();
	}

	public void WriteTile(string tableName, int zoom, int tileColumn, int tileRow, byte[] data)
	{
		using SQLiteTransaction sQLiteTransaction = _con.BeginTransaction();
		using (SQLiteCommand sQLiteCommand = new SQLiteCommand("INSERT OR REPLACE INTO " + tableName + " (" + ZoomColumnName + ", " + TileColumnName + ", " + TileRowName + ", " + DataColumnName + ") values (" + zoom + ", " + tileColumn + ", " + tileRow + ", @img)", _con))
		{
			sQLiteCommand.Parameters.Add("@img", DbType.Binary).Value = data;
			sQLiteCommand.ExecuteNonQuery();
		}
		sQLiteTransaction.Commit();
	}

	public byte[] ReadTile(string tableName, int zoom, int tileColumn, int tileRow)
	{
		string commandText = "SELECT " + DataColumnName + " FROM " + tableName + " WHERE " + ZoomColumnName + "=" + zoom + " AND " + TileColumnName + "=" + tileColumn + " AND " + TileRowName + "=" + tileRow;
		using SQLiteTransaction sQLiteTransaction = _con.BeginTransaction();
		byte[] result;
		using (SQLiteCommand sQLiteCommand = new SQLiteCommand(commandText, _con))
		{
			using SQLiteDataReader sQLiteDataReader = sQLiteCommand.ExecuteReader();
			sQLiteDataReader.Read();
			result = (byte[])sQLiteDataReader[DataColumnName];
		}
		sQLiteTransaction.Commit();
		return result;
	}

	public void StreamWriteTile(string tableName, int zoom, int tileX, int tileY, byte[] rasterVals)
	{
		_tiles.Add(new ByteTile(tableName, zoom, tileX, tileY, rasterVals));
		if (_tiles.Count >= MAX_TILES_CACHED)
		{
			WriteCachedTiles();
		}
	}

	private void WriteCachedTiles()
	{
		foreach (string tableName in _tiles.Select((ByteTile tile) => tile.TableName).Distinct())
		{
			using SQLiteCommand sQLiteCommand = new SQLiteCommand("INSERT OR REPLACE INTO " + tableName + " (" + ZoomColumnName + ", " + TileColumnName + ", " + TileRowName + ", " + DataColumnName + ") values (@zoom, @col, @row, @data)", _con);
			using SQLiteTransaction sQLiteTransaction = _con.BeginTransaction();
			sQLiteCommand.Parameters.AddWithValue("@zoom", "");
			sQLiteCommand.Parameters.AddWithValue("@col", "");
			sQLiteCommand.Parameters.AddWithValue("@row", "");
			sQLiteCommand.Parameters.AddWithValue("@data", "");
			sQLiteCommand.Parameters["@data"].DbType = DbType.Binary;
			foreach (ByteTile byteTile in _tiles.Where((ByteTile tile) => tile.TableName == tableName))
			{
				sQLiteCommand.Parameters["@zoom"].Value = byteTile.Zoom;
				sQLiteCommand.Parameters["@col"].Value = byteTile.TileX;
				sQLiteCommand.Parameters["@row"].Value = byteTile.TileY;
				sQLiteCommand.Parameters["@data"].Value = byteTile.RasterValues;
				sQLiteCommand.ExecuteNonQuery();
			}
			sQLiteTransaction.Commit();
		}
		_tiles.Clear();
	}

	public override void Dispose()
	{
		WriteCachedTiles();
		base.Dispose();
	}
}
