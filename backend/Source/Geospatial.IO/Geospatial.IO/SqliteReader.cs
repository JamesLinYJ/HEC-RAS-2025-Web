using System;
using System.Data.SQLite;

namespace Geospatial.IO;

public class SqliteReader : IDisposable
{
	protected SQLiteConnection _conn;

	public SqliteReader(string filename)
	{
		string connectionString = "Data Source=" + filename;
		_conn = new SQLiteConnection(connectionString);
		_conn.Open();
	}

	public void Dispose()
	{
		_conn.Dispose();
	}

	public void Query(string query)
	{
		using SQLiteCommand sQLiteCommand = new SQLiteCommand(query, _conn);
		using SQLiteDataReader sQLiteDataReader = sQLiteCommand.ExecuteReader();
		while (sQLiteDataReader.Read())
		{
		}
	}
}
