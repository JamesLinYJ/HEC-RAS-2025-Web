using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using Geospatial.GDALAssist;
using Geospatial.Vectors;

namespace Geospatial.IO.TileMapService;

public class SQLiteImageTMS : SQLiteTiledRegion
{
	private class MetadataInfo
	{
		public string Name;

		public int MaxZoom;

		public float CenterX;

		public float CenterY;

		public float CenterZ;

		public float Bottom;

		public float Top;

		public float Left;

		public float Right;

		public MetadataInfo(string tableName)
		{
			Name = tableName;
			MaxZoom = 1;
			CenterX = float.NaN;
			CenterY = float.NaN;
			CenterZ = float.NaN;
			ResetBounds();
		}

		public void ResetBounds()
		{
			Bottom = float.MaxValue;
			Top = float.MinValue;
			Left = float.MaxValue;
			Right = float.MinValue;
		}
	}

	private struct ByteTile(string tableName, string time, int zoom, int tileX, int tileY, byte[] imageBytes)
	{
		public string TableName = tableName;

		public string Time = time;

		public int Zoom = zoom;

		public int TileX = tileX;

		public int TileY = tileY;

		public byte[] ImageBytes = imageBytes;
	}

	public const string TimeColumnName = "time";

	public const string ZoomColumnName = "zoom_level";

	public const string TileColumnName = "tile_column";

	public const string TileRowName = "tile_row";

	public const string DataColumnName = "tile_data";

	public const string MBTilesTable = "tiles";

	public const string MetadataMaxZoomName = "maxzoom";

	public const string MetadataProjectName = "project_name";

	public const string MetadataPlanName = "plan_name";

	public const string MetadataMapType = "map_type";

	public const string MetadataLegendValues = "legend_values";

	public const string MetadataLegendColors = "legend_rgba";

	public static int MAX_TILES_CACHED = 50;

	private List<ByteTile> _tiles;

	private List<MetadataInfo> _metadata;

	private Dictionary<string, Tuple<string, string, string, string, string>> _rasMD = new Dictionary<string, Tuple<string, string, string, string, string>>();

	public SQLiteImageTMS(string filename)
		: base(filename)
	{
		CreateMetadataTableIfDNE();
		CreateUniqueIDXIfDNE(SQLiteTiledRegion.MetadataTable + "_index", SQLiteTiledRegion.MetadataTable, "name");
		_metadata = ParseMetadata();
		_tiles = new List<ByteTile>();
	}

	private List<MetadataInfo> ParseMetadata()
	{
		List<MetadataInfo> metadataInfos = new List<MetadataInfo>();
		List<string> allTables = GetAllTables();
		Dictionary<string, string> allMetadata = GetAllMetadata();
		foreach (string allTable in allTables)
		{
			if (allTable != SQLiteTiledRegion.MetadataTable)
			{
				MetadataInfo metadataInfo = new MetadataInfo(allTable);
				if (allMetadata.TryGetValue(allTable + "_maxzoom", out var localValue))
				{
					int.TryParse(localValue, out metadataInfo.MaxZoom);
				}
				if (allMetadata.TryGetValue(allTable + "_centerx", out localValue))
				{
					float.TryParse(localValue, out metadataInfo.CenterX);
				}
				if (allMetadata.TryGetValue(allTable + "_centery", out localValue))
				{
					float.TryParse(localValue, out metadataInfo.CenterY);
				}
				if (allMetadata.TryGetValue(allTable + "_centerz", out localValue))
				{
					float.TryParse(localValue, out metadataInfo.CenterZ);
				}
				if (allMetadata.TryGetValue(allTable + "_bottom", out localValue))
				{
					float.TryParse(localValue, out metadataInfo.Bottom);
				}
				if (allMetadata.TryGetValue(allTable + "_top", out localValue))
				{
					float.TryParse(localValue, out metadataInfo.Top);
				}
				if (allMetadata.TryGetValue(allTable + "_left", out localValue))
				{
					float.TryParse(localValue, out metadataInfo.Left);
				}
				if (allMetadata.TryGetValue(allTable + "_right", out localValue))
				{
					float.TryParse(localValue, out metadataInfo.Right);
				}
				metadataInfos.Add(metadataInfo);
			}
		}
		return metadataInfos;
	}

	public void AddIndexedRasterTable(string tableName)
	{
		CreateRasterTableIfDNE(tableName);
		CreateUniqueIDXIfDNE(tableName + "_index", tableName, "time", "zoom_level", "tile_column", "tile_row");
	}

	private void CreateRasterTableIfDNE(string TableName)
	{
		using SQLiteTransaction sQLiteTransaction = _con.BeginTransaction();
		using (SQLiteCommand sQLiteCommand = new SQLiteCommand("CREATE TABLE IF NOT EXISTS " + TableName + " (time TEXT, zoom_level INT, tile_column INT, tile_row INT, tile_data BLOB)", _con))
		{
			sQLiteCommand.ExecuteNonQuery();
		}
		sQLiteTransaction.Commit();
	}

	public void WriteTile(string tableName, string time, int zoom, int tileX, int tileY, byte[] imageBytes)
	{
		UpdateMetadata(tableName, zoom, tileX, tileY);
		using SQLiteTransaction sQLiteTransaction = _con.BeginTransaction();
		using (SQLiteCommand sQLiteCommand = new SQLiteCommand("INSERT OR REPLACE INTO " + tableName + " (time, zoom_level, tile_column, tile_row, tile_data) values (\"" + time + "\", " + zoom + ", " + tileX + ", " + tileY + ", @img)", _con))
		{
			sQLiteCommand.Parameters.Add("@img", DbType.Binary).Value = imageBytes;
			sQLiteCommand.ExecuteNonQuery();
		}
		sQLiteTransaction.Commit();
	}

	public List<GoogleCacheTile> GetAllTileDescriptions(string tablename)
	{
		List<GoogleCacheTile> googleCacheTiles = new List<GoogleCacheTile>();
		using SQLiteTransaction sQLiteTransaction = _con.BeginTransaction();
		using (SQLiteCommand sQLiteCommand = new SQLiteCommand("select time,zoom_level,tile_column,tile_row from " + tablename, _con))
		{
			using SQLiteDataReader sQLiteDataReader = sQLiteCommand.ExecuteReader();
			while (sQLiteDataReader.Read())
			{
				googleCacheTiles.Add(new GoogleCacheTile
				{
					Tablename = tablename,
					TimeName = (string)sQLiteDataReader["time"],
					ZoomLevel = (int)sQLiteDataReader["zoom_level"],
					Row = (int)sQLiteDataReader["tile_row"],
					Column = (int)sQLiteDataReader["tile_column"],
					ImageData = null
				});
			}
		}
		sQLiteTransaction.Commit();
		return googleCacheTiles;
	}

	public void DeleteTableAndMetadata(string tableName)
	{
		SQLiteTiledRegion.DropTable(_con, tableName);
		MetadataInfo metadataInfo = _metadata.FirstOrDefault((MetadataInfo m) => m.Name == tableName);
		if (metadataInfo != null)
		{
			_metadata.Remove(metadataInfo);
		}
		DeleteMetadata(tableName + "_maxzoom");
		DeleteMetadata(tableName + "_centerx");
		DeleteMetadata(tableName + "_centery");
		DeleteMetadata(tableName + "_centerz");
		DeleteMetadata(tableName + "_bottom");
		DeleteMetadata(tableName + "_top");
		DeleteMetadata(tableName + "_left");
		DeleteMetadata(tableName + "_right");
	}

	public void StreamWriteTile(string tableName, string time, int zoom, int tileX, int tileY, byte[] imageBytes)
	{
		UpdateMetadata(tableName, zoom, tileX, tileY);
		_tiles.Add(new ByteTile(tableName, time, zoom, tileX, tileY, imageBytes));
		if (_tiles.Count >= MAX_TILES_CACHED)
		{
			WriteCachedTiles();
		}
	}

	private void WriteCachedTiles()
	{
		foreach (string tableName in _tiles.Select((ByteTile tile) => tile.TableName).Distinct())
		{
			using SQLiteCommand sQLiteCommand = new SQLiteCommand("INSERT OR REPLACE INTO " + tableName + " (time, zoom_level, tile_column, tile_row, tile_data) values (@time, @zoom, @col, @row, @img)", _con);
			using SQLiteTransaction sQLiteTransaction = _con.BeginTransaction();
			sQLiteCommand.Parameters.AddWithValue("@time", "");
			sQLiteCommand.Parameters.AddWithValue("@zoom", "");
			sQLiteCommand.Parameters.AddWithValue("@col", "");
			sQLiteCommand.Parameters.AddWithValue("@row", "");
			sQLiteCommand.Parameters.AddWithValue("@img", "");
			sQLiteCommand.Parameters["@img"].DbType = DbType.Binary;
			foreach (ByteTile byteTile in _tiles.Where((ByteTile tile) => tile.TableName == tableName))
			{
				sQLiteCommand.Parameters["@time"].Value = byteTile.Time;
				sQLiteCommand.Parameters["@zoom"].Value = byteTile.Zoom;
				sQLiteCommand.Parameters["@col"].Value = byteTile.TileX;
				sQLiteCommand.Parameters["@row"].Value = byteTile.TileY;
				sQLiteCommand.Parameters["@img"].Value = byteTile.ImageBytes;
				sQLiteCommand.ExecuteNonQuery();
			}
			sQLiteTransaction.Commit();
		}
		_tiles.Clear();
	}

	private void UpdateMetadata(string tableName, int zoom, int tileX, int tileY)
	{
		MetadataInfo metadataInfo = _metadata.FirstOrDefault((MetadataInfo metadataInfo2) => metadataInfo2.Name == tableName);
		if (metadataInfo == null)
		{
			metadataInfo = new MetadataInfo(tableName);
			_metadata.Add(metadataInfo);
		}
		if (zoom > metadataInfo.MaxZoom)
		{
			metadataInfo.MaxZoom = zoom;
			metadataInfo.ResetBounds();
		}
		if (zoom == metadataInfo.MaxZoom)
		{
			Extent boundingBox = Projector.GetBoundingBox(MercatorTiledRegion.GetTileMercatorBBox(zoom, tileX, tileY), new EPSGProjection(900913), new EPSGProjection(4326));
			metadataInfo.Bottom = (float)Math.Min(metadataInfo.Bottom, boundingBox.LowerLeft.Y);
			metadataInfo.Top = (float)Math.Max(metadataInfo.Top, boundingBox.UpperLeft.Y);
			metadataInfo.Left = (float)Math.Min(metadataInfo.Left, boundingBox.LowerLeft.X);
			metadataInfo.Right = (float)Math.Max(metadataInfo.Right, boundingBox.LowerRight.X);
		}
	}

	protected static void CreateTilesView(SQLiteConnection con, string RasterName, string Time)
	{
		using SQLiteTransaction sQLiteTransaction = con.BeginTransaction();
		SQLiteCommand sQLiteCommand = new SQLiteCommand("CREATE VIEW IF NOT EXISTS tiles as select zoom_level, tile_column, tile_row, tile_data from " + RasterName + " where time=\"" + Time + "\"", con);
		sQLiteCommand.ExecuteNonQuery();
		sQLiteCommand.Dispose();
		sQLiteTransaction.Commit();
	}

	public void AddRASMetadata(string table, string pjctName, string planName, string mapType, string legendValsCSV, string legendColsCSV)
	{
		if (_rasMD.ContainsKey(table))
		{
			_rasMD.Remove(table);
		}
		_rasMD.Add(table, new Tuple<string, string, string, string, string>(pjctName, planName, mapType, legendValsCSV, legendColsCSV));
	}

	public override void Dispose()
	{
		WriteCachedTiles();
		foreach (MetadataInfo metadata in _metadata)
		{
			metadata.CenterX = (metadata.Left + metadata.Right) / 2f;
			metadata.CenterY = (metadata.Bottom + metadata.Top) / 2f;
			metadata.CenterZ = 11f;
			WriteMetadata(metadata.Name + "_maxzoom", metadata.MaxZoom.ToString());
			WriteMetadata(metadata.Name + "_centerx", metadata.CenterX.ToString());
			WriteMetadata(metadata.Name + "_centery", metadata.CenterY.ToString());
			WriteMetadata(metadata.Name + "_centerz", metadata.CenterZ.ToString());
			WriteMetadata(metadata.Name + "_bottom", metadata.Bottom.ToString());
			WriteMetadata(metadata.Name + "_top", metadata.Top.ToString());
			WriteMetadata(metadata.Name + "_left", metadata.Left.ToString());
			WriteMetadata(metadata.Name + "_right", metadata.Right.ToString());
			if (_rasMD.ContainsKey(metadata.Name))
			{
				Tuple<string, string, string, string, string> tuple = _rasMD[metadata.Name];
				WriteMetadata(metadata.Name + "_project_name", tuple.Item1);
				WriteMetadata(metadata.Name + "_plan_name", tuple.Item2);
				WriteMetadata(metadata.Name + "_map_type", tuple.Item3);
				WriteMetadata(metadata.Name + "_legend_values", tuple.Item4);
				WriteMetadata(metadata.Name + "_legend_rgba", tuple.Item5);
			}
		}
		base.Dispose();
	}

	public static void ExportToDirectory(string SQLiteFilename, string RasterTableName, string DestinationFolder)
	{
		RasterTableName = SQLiteTiledRegion.FormatTablename(RasterTableName);
		if (!File.Exists(SQLiteFilename))
		{
			throw new Exception(SQLiteFilename + " does not exist.");
		}
		if (DestinationFolder.Last() != '\\' || DestinationFolder.Last() != '/')
		{
			DestinationFolder += Path.DirectorySeparatorChar;
		}
		if (!Directory.Exists(DestinationFolder))
		{
			Directory.CreateDirectory(DestinationFolder);
		}
		using SQLiteConnection sQLiteConnection = new SQLiteConnection("Data Source=" + SQLiteFilename);
		sQLiteConnection.Open();
		using SQLiteTransaction sQLiteTransaction = sQLiteConnection.BeginTransaction();
		SQLiteCommand sQLiteCommand = new SQLiteCommand("select * from " + RasterTableName, sQLiteConnection);
		SQLiteDataReader sQLiteDataReader = sQLiteCommand.ExecuteReader();
		while (sQLiteDataReader.Read())
		{
			int sQLiteDataReader2 = (int)sQLiteDataReader["tile_row"];
			int sQLiteDataReader3 = (int)sQLiteDataReader["tile_column"];
			int sQLiteDataReader4 = (int)sQLiteDataReader["zoom_level"];
			string sQLiteDataReader5 = (string)sQLiteDataReader["time"];
			byte[] bytes = (byte[])sQLiteDataReader["tile_data"];
			string path = DestinationFolder + sQLiteDataReader5 + Path.DirectorySeparatorChar + sQLiteDataReader4 + Path.DirectorySeparatorChar + sQLiteDataReader3 + Path.DirectorySeparatorChar + sQLiteDataReader2 + ".png";
			if (!Directory.Exists(Path.GetDirectoryName(path)))
			{
				Directory.CreateDirectory(Path.GetDirectoryName(path));
			}
			if (!File.Exists(path))
			{
				File.WriteAllBytes(path, bytes);
			}
		}
		sQLiteDataReader.Close();
		sQLiteCommand.Dispose();
		sQLiteTransaction.Commit();
	}
}
