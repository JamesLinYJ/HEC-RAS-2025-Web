using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace Geospatial.IO.TileMapService;

public abstract class SQLiteTiledRegion : IDisposable
{
	protected SQLiteConnection _con;

	public static readonly string MetadataTable = "metadata";

	public SQLiteTiledRegion(string filename)
	{
		string connectionString = "Data Source=" + filename;
		_con = new SQLiteConnection(connectionString);
		_con.Open();
	}

	protected void WriteMetadata(string name, string stringValue)
	{
		using SQLiteTransaction sQLiteTransaction = _con.BeginTransaction();
		using (SQLiteCommand sQLiteCommand = new SQLiteCommand("INSERT OR REPLACE INTO " + MetadataTable + " (name, value) values (\"" + name + "\", \"" + stringValue + "\")", _con))
		{
			sQLiteCommand.ExecuteNonQuery();
		}
		sQLiteTransaction.Commit();
	}

	protected void DeleteMetadata(string name)
	{
		using SQLiteTransaction sQLiteTransaction = _con.BeginTransaction();
		using (SQLiteCommand sQLiteCommand = new SQLiteCommand("DELETE FROM " + MetadataTable + " WHERE NAME = '" + name + "' COLLATE NOCASE", _con))
		{
			sQLiteCommand.ExecuteNonQuery();
		}
		sQLiteTransaction.Commit();
	}

	protected void CreateMetadataTableIfDNE()
	{
		using SQLiteTransaction sQLiteTransaction = _con.BeginTransaction();
		using (SQLiteCommand sQLiteCommand = new SQLiteCommand("CREATE TABLE IF NOT EXISTS " + MetadataTable + " (name TEXT, value TEXT)", _con))
		{
			sQLiteCommand.ExecuteNonQuery();
		}
		sQLiteTransaction.Commit();
	}

	protected void CreateUniqueIDXIfDNE(string IDXName, string TableName, params string[] IndexedColumns)
	{
		using SQLiteTransaction sQLiteTransaction = _con.BeginTransaction();
		using (SQLiteCommand sQLiteCommand = new SQLiteCommand("CREATE UNIQUE INDEX IF NOT EXISTS " + IDXName + " ON " + TableName + " " + FormatSQLiteIndexList(IndexedColumns), _con))
		{
			sQLiteCommand.ExecuteNonQuery();
		}
		sQLiteTransaction.Commit();
	}

	protected void CreateIDX(string IDXName, string TableName, params string[] IndexedColumns)
	{
		using SQLiteTransaction sQLiteTransaction = _con.BeginTransaction();
		using (SQLiteCommand sQLiteCommand = new SQLiteCommand("CREATE INDEX IF NOT EXISTS " + IDXName + " ON " + TableName + " " + FormatSQLiteIndexList(IndexedColumns), _con))
		{
			sQLiteCommand.ExecuteNonQuery();
		}
		sQLiteTransaction.Commit();
	}

	private string FormatSQLiteIndexList(string[] Names)
	{
		string textValue = "(";
		for (int i = 0; i < Names.Length; i++)
		{
			textValue += Names[i];
			if (i != Names.Length - 1)
			{
				textValue += ", ";
			}
		}
		return textValue + ")";
	}

	private void DropView(string viewName)
	{
		using SQLiteTransaction sQLiteTransaction = _con.BeginTransaction();
		using (SQLiteCommand sQLiteCommand = new SQLiteCommand("DROP VIEW IF EXISTS " + viewName, _con))
		{
			sQLiteCommand.ExecuteNonQuery();
		}
		sQLiteTransaction.Commit();
	}

	protected static void DropTable(SQLiteConnection con, string tableName)
	{
		using SQLiteTransaction sQLiteTransaction = con.BeginTransaction();
		using (SQLiteCommand sQLiteCommand = new SQLiteCommand("DROP TABLE IF EXISTS " + tableName, con))
		{
			sQLiteCommand.ExecuteNonQuery();
		}
		sQLiteTransaction.Commit();
	}

	protected string SearchMetadata(string name)
	{
		string result = "";
		using SQLiteTransaction sQLiteTransaction = _con.BeginTransaction();
		using (SQLiteCommand sQLiteCommand = new SQLiteCommand("select value from " + MetadataTable + " where name = '" + name + "'", _con))
		{
			using SQLiteDataReader sQLiteDataReader = sQLiteCommand.ExecuteReader();
			sQLiteDataReader.Read();
			result = (string)sQLiteDataReader["value"];
		}
		sQLiteTransaction.Commit();
		return result;
	}

	protected Dictionary<string, string> GetAllMetadata()
	{
		Dictionary<string, string> allMetadata = new Dictionary<string, string>();
		using SQLiteTransaction sQLiteTransaction = _con.BeginTransaction();
		using (SQLiteCommand sQLiteCommand = new SQLiteCommand("select * from " + MetadataTable, _con))
		{
			using SQLiteDataReader sQLiteDataReader = sQLiteCommand.ExecuteReader();
			while (sQLiteDataReader.Read())
			{
				allMetadata.Add((string)sQLiteDataReader["name"], (string)sQLiteDataReader["value"]);
			}
		}
		sQLiteTransaction.Commit();
		return allMetadata;
	}

	public List<string> GetAllTables()
	{
		List<string> allTables = new List<string>();
		using SQLiteTransaction sQLiteTransaction = _con.BeginTransaction();
		using (SQLiteCommand sQLiteCommand = new SQLiteCommand("SELECT name FROM sqlite_master WHERE type='table'", _con))
		{
			using SQLiteDataReader sQLiteDataReader = sQLiteCommand.ExecuteReader();
			while (sQLiteDataReader.Read())
			{
				allTables.Add((string)sQLiteDataReader["name"]);
			}
		}
		sQLiteTransaction.Commit();
		return allTables;
	}

	private static int GetMax(SQLiteConnection con, string tableName, string columnName, string whereClause)
	{
		int result = 0;
		using SQLiteTransaction sQLiteTransaction = con.BeginTransaction();
		string whereClauseTerm = "select max (" + columnName + ") from " + tableName;
		if (!string.IsNullOrWhiteSpace(whereClause))
		{
			whereClauseTerm = whereClauseTerm + " where " + whereClause;
		}
		using (SQLiteCommand sQLiteCommand = new SQLiteCommand(whereClauseTerm, con))
		{
			using SQLiteDataReader sQLiteDataReader = sQLiteCommand.ExecuteReader();
			if (sQLiteDataReader.Read())
			{
				result = Convert.ToInt32(sQLiteDataReader[0]);
			}
		}
		sQLiteTransaction.Commit();
		return result;
	}

	private static int GetMin(SQLiteConnection con, string tableName, string columnName, string whereClause)
	{
		int result = 0;
		using SQLiteTransaction sQLiteTransaction = con.BeginTransaction();
		string whereClauseTerm = "select min (" + columnName + ") from " + tableName;
		if (!string.IsNullOrWhiteSpace(whereClause))
		{
			whereClauseTerm = whereClauseTerm + " where " + whereClause;
		}
		using (SQLiteCommand sQLiteCommand = new SQLiteCommand(whereClauseTerm, con))
		{
			using SQLiteDataReader sQLiteDataReader = sQLiteCommand.ExecuteReader();
			if (sQLiteDataReader.Read())
			{
				result = Convert.ToInt32(sQLiteDataReader[0]);
			}
		}
		sQLiteTransaction.Commit();
		return result;
	}

	protected static string FormatTablename(string TableName)
	{
		return TableName.Replace(' ', '_');
	}

	public virtual void Dispose()
	{
		_con.Close();
	}
}
