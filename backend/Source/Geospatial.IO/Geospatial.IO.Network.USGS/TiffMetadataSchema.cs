using Microsoft.Data.Sqlite;

namespace Geospatial.IO.Network.USGS;

public static class TiffMetadataSchema
{
	public static void CreateSchema(SqliteConnection conn)
	{
		using SqliteCommand sqliteCommand = conn.CreateCommand();
		sqliteCommand.CommandText = "\r\n            -- Deduplicated WKT projection strings\r\n            CREATE TABLE IF NOT EXISTS projections (\r\n                id INTEGER PRIMARY KEY,\r\n                wkt TEXT NOT NULL UNIQUE\r\n            );\r\n\r\n            -- TIFF metadata, joins to files.id\r\n            CREATE TABLE IF NOT EXISTS tiff_metadata (\r\n                file_id INTEGER PRIMARY KEY,\r\n                size INTEGER NOT NULL,\r\n                projection_id INTEGER,\r\n                cell_size REAL NOT NULL,\r\n                left REAL NOT NULL,\r\n                top REAL NOT NULL,\r\n                right REAL NOT NULL,\r\n                bottom REAL NOT NULL,\r\n                FOREIGN KEY (file_id) REFERENCES files(id),\r\n                FOREIGN KEY (projection_id) REFERENCES projections(id)\r\n            );\r\n\r\n            -- Indexes for common queries\r\n            CREATE INDEX IF NOT EXISTS idx_tiff_metadata_projection ON tiff_metadata(projection_id);\r\n            CREATE INDEX IF NOT EXISTS idx_tiff_metadata_extent ON tiff_metadata(left, right, bottom, top);\r\n        ";
		sqliteCommand.ExecuteNonQuery();
	}
}
