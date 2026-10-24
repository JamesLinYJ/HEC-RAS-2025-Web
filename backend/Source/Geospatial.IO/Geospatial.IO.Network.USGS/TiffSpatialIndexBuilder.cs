using System;
using System.Collections.Generic;
using System.Diagnostics;
using Geospatial.DataStructures;
using Geospatial.GDALAssist;
using Geospatial.Vectors;
using Microsoft.Data.Sqlite;

namespace Geospatial.IO.Network.USGS;

public class TiffSpatialIndexBuilder
{
	public SpatialIndex<int> Build(string trieDb, IProgress<SpatialIndexBuildProgress> progress = null, int batchSize = 10000, int maxBinCount = 4096, int progressInterval = 100000)
	{
		using SqliteConnection sqliteConnection = new SqliteConnection("Data Source=" + trieDb + ";Mode=ReadOnly");
		sqliteConnection.Open();
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(WellKnownProjections.CONUSExtentWebMercator, maxBinCount);
		Projection webMercator = WellKnownProjections.WebMercator;
		Dictionary<long, ReprojectionTransformer> reprojectionTransformerByLong = new Dictionary<long, ReprojectionTransformer>();
		Dictionary<long, Projection> projectionByLong = new Dictionary<long, Projection>();
		using (SqliteCommand sqliteCommand = sqliteConnection.CreateCommand())
		{
			sqliteCommand.CommandText = "SELECT id, wkt FROM projections";
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			while (sqliteDataReader.Read())
			{
				long @int = sqliteDataReader.GetInt64(0);
				string wellKnownText = sqliteDataReader.GetString(1);
				try
				{
					projectionByLong[@int] = new WKTProjection(wellKnownText);
				}
				catch
				{
				}
			}
		}
		foreach (KeyValuePair<long, Projection> localValue in projectionByLong)
		{
			try
			{
				reprojectionTransformerByLong[localValue.Key] = new ReprojectionTransformer(localValue.Value, webMercator);
			}
			catch
			{
			}
		}
		long ordinal = 0L;
		long ordinal2 = 0L;
		Stopwatch stopwatch = Stopwatch.StartNew();
		Span<Point> points = stackalloc Point[4];
		Span<Point> transformed = stackalloc Point[4];
		using (SqliteCommand sqliteCommand2 = sqliteConnection.CreateCommand())
		{
			sqliteCommand2.CommandText = "\r\n                SELECT file_id, projection_id, left, top, right, bottom\r\n                FROM tiff_metadata\r\n                WHERE projection_id IS NOT NULL\r\n                ORDER BY file_id\r\n                LIMIT $limit OFFSET $offset";
			sqliteCommand2.Parameters.AddWithValue("$limit", batchSize);
			SqliteParameter sqliteParameter = sqliteCommand2.Parameters.Add("$offset", SqliteType.Integer);
			while (true)
			{
				sqliteParameter.Value = ordinal;
				int ordinal3 = 0;
				using (SqliteDataReader sqliteDataReader2 = sqliteCommand2.ExecuteReader())
				{
					while (sqliteDataReader2.Read())
					{
						int key = (int)sqliteDataReader2.GetInt64(0);
						long int2 = sqliteDataReader2.GetInt64(1);
						double x = sqliteDataReader2.GetDouble(2);
						double y = sqliteDataReader2.GetDouble(3);
						double x2 = sqliteDataReader2.GetDouble(4);
						double y2 = sqliteDataReader2.GetDouble(5);
						if (!reprojectionTransformerByLong.TryGetValue(int2, out var localValue2))
						{
							ordinal3++;
							continue;
						}
						points[0] = new Point(x, y2);
						points[1] = new Point(x2, y2);
						points[2] = new Point(x2, y);
						points[3] = new Point(x, y);
						try
						{
							localValue2.Transform(points, transformed);
							double minX = Math.Min(Math.Min(transformed[0].X, transformed[1].X), Math.Min(transformed[2].X, transformed[3].X));
							double maxX = Math.Max(Math.Max(transformed[0].X, transformed[1].X), Math.Max(transformed[2].X, transformed[3].X));
							double minY = Math.Min(Math.Min(transformed[0].Y, transformed[1].Y), Math.Min(transformed[2].Y, transformed[3].Y));
							double maxY = Math.Max(Math.Max(transformed[0].Y, transformed[1].Y), Math.Max(transformed[2].Y, transformed[3].Y));
							Extent extent = new Extent(minX, maxX, minY, maxY);
							spatialIndex.Add(extent, key);
						}
						catch
						{
						}
						ordinal3++;
					}
				}
				if (ordinal3 == 0)
				{
					break;
				}
				ordinal += ordinal3;
				ordinal2 += ordinal3;
				if (ordinal2 % progressInterval == 0L)
				{
					double recordsPerSecond = (double)ordinal2 / stopwatch.Elapsed.TotalSeconds;
					progress?.Report(new SpatialIndexBuildProgress(ordinal2, recordsPerSecond));
				}
			}
		}
		foreach (ReprojectionTransformer reprojectionTransformer in reprojectionTransformerByLong.Values)
		{
			reprojectionTransformer.Dispose();
		}
		return spatialIndex;
	}
}
