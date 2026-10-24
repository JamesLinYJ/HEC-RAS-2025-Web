using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Geospatial.GDALAssist;
using Geospatial.Vectors;
using Microsoft.Data.Sqlite;
using OSGeo.OSR;

namespace Geospatial.IO;

// The source projection catalogue uses the bundled PROJ database. Microsoft.Data.Sqlite
// and its NuGet runtime replace the Windows System.Data.SQLite interop library here.
public class ProjDBReader : IDisposable
{
    private static readonly Lazy<bool> SqliteInitialized = new(() => { SQLitePCL.Batteries_V2.Init(); return true; });
    public static readonly ProjDBReader Instance = new ProjDBReader();
    protected readonly SqliteConnection _conn;
    public string DatabasePath { get; }
    public string SQLiteVersion => _conn.ServerVersion;

    public ProjDBReader()
    {
        if (!GDALSetup.IsInitialized) throw new InvalidOperationException("GDAL is not initialized.");
        // GDAL's generic data directory and PROJ's database directory differ in
        // the NuGet build layout. Query the active runtime instead of assuming
        // the Windows installation's shared data folder or a machine install.
        string path = Osr.GetPROJSearchPaths().Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select(directory => Path.Combine(directory, "proj.db"))
            .Append(Path.Combine(GDALSetup.GdalInitializedDataDirectory, "proj.db"))
            .FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("The projection database is unavailable.");
        DatabasePath = path;
        _ = SqliteInitialized.Value;
        _conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        _conn.Open();
    }

    public IEnumerable<AliasNameTableStruct> SearchProjectionBySubstring(string substr, int limit = -1)
    {
        string query = (substr ?? "").Trim();
        if (query.Length == 0) yield break;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            WITH crs AS (
                SELECT 'projected_crs' AS table_name, auth_name, code, name FROM projected_crs WHERE deprecated = 0
                UNION ALL
                SELECT 'geodetic_crs', auth_name, code, name FROM geodetic_crs WHERE deprecated = 0
            )
            SELECT DISTINCT crs.table_name, crs.auth_name, CAST(crs.code AS INTEGER), crs.name, 'PROJ'
            FROM crs
            WHERE crs.auth_name = 'EPSG' AND (
                CAST(crs.code AS TEXT) = @query OR crs.name LIKE @pattern ESCAPE '!'
                OR EXISTS (SELECT 1 FROM alias_name a WHERE a.table_name = crs.table_name
                    AND a.auth_name = crs.auth_name AND a.code = crs.code AND a.alt_name LIKE @pattern ESCAPE '!'))
            ORDER BY CASE WHEN CAST(crs.code AS TEXT) = @query THEN 0 ELSE 1 END, crs.name, CAST(crs.code AS INTEGER)
            LIMIT @limit
            """;
        cmd.Parameters.AddWithValue("@query", query);
        cmd.Parameters.AddWithValue("@pattern", "%" + query.Replace("!", "!!").Replace("%", "!%").Replace("_", "!_") + "%");
        cmd.Parameters.AddWithValue("@limit", limit > 0 ? limit : -1);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) yield return new AliasNameTableStruct
        {
            table_name = reader.GetString(0), auth_name = reader.GetString(1), code = reader.GetInt32(2), alt_name = reader.GetString(3), source = reader.GetString(4)
        };
    }

    public IEnumerable<AreaTableStruct> SearchProjectionByLocation(Point ptLatLon, int limit = -1)
    {
        if (!double.IsFinite(ptLatLon.X) || !double.IsFinite(ptLatLon.Y) || ptLatLon.X < -180 || ptLatLon.X > 180 || ptLatLon.Y < -90 || ptLatLon.Y > 90)
            throw new ArgumentOutOfRangeException(nameof(ptLatLon), "Enter valid longitude and latitude.");
        using var cmd = _conn.CreateCommand();
        // An extent's code identifies an AREA, not a CRS. Resolve usage -> projected_crs
        // before presenting an EPSG choice; otherwise the source UI selected unrelated codes.
        cmd.CommandText = """
            WITH matches AS (
                SELECT c.auth_name, CAST(c.code AS INTEGER) AS code, c.name, e.description,
                    e.west_lon, e.east_lon, e.south_lat, e.north_lat,
                    (CASE WHEN e.east_lon >= e.west_lon THEN e.east_lon - e.west_lon ELSE 360 + e.east_lon - e.west_lon END) * (e.north_lat - e.south_lat) AS area,
                    ROW_NUMBER() OVER (PARTITION BY c.auth_name, c.code ORDER BY
                        (CASE WHEN e.east_lon >= e.west_lon THEN e.east_lon - e.west_lon ELSE 360 + e.east_lon - e.west_lon END) * (e.north_lat - e.south_lat), e.code) AS ordinal
                FROM projected_crs c
                JOIN usage u ON u.object_table_name = 'projected_crs' AND u.object_auth_name = c.auth_name AND u.object_code = c.code
                JOIN extent e ON e.auth_name = u.extent_auth_name AND e.code = u.extent_code
                WHERE c.auth_name = 'EPSG' AND c.deprecated = 0 AND e.deprecated = 0 AND e.name NOT LIKE 'World%'
                    AND e.south_lat <= @lat AND e.north_lat >= @lat
                    AND ((e.west_lon <= e.east_lon AND @lon BETWEEN e.west_lon AND e.east_lon)
                        OR (e.west_lon > e.east_lon AND (@lon >= e.west_lon OR @lon <= e.east_lon)))
            )
            SELECT auth_name, code, name, description, west_lon, east_lon, south_lat, north_lat
            FROM matches WHERE ordinal = 1 ORDER BY area, name, code LIMIT @limit
            """;
        cmd.Parameters.AddWithValue("@lat", ptLatLon.Y);
        cmd.Parameters.AddWithValue("@lon", ptLatLon.X);
        cmd.Parameters.AddWithValue("@limit", limit > 0 ? limit : -1);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) yield return new AreaTableStruct
        {
            AuthName = reader.GetString(0), Code = reader.GetInt32(1), Name = reader.GetString(2), Description = reader.GetString(3),
            Extent = new Extent(reader.GetDouble(4), reader.GetDouble(5), reader.GetDouble(6), reader.GetDouble(7))
        };
    }

    public void Dispose() => _conn.Dispose();
}
