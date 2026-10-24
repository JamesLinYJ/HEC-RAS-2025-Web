using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;
using Utility.Progress;

namespace Geospatial.IO.TileMapService;

public static class TMSWriter
{
	public static void CreateOrAddTileDB(string destDBFilename, Extent mercatorBBox, string datasetID, string pjctName, string planName, string mapType, string legendValsCSV, string legendColsCSV, string time, int maxZoom, Func<int, int, int, byte[]> GetRasterBytesZXY, bool OverwriteExistingTiles = false, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		int ordinal = 0;
		for (int i = 1; i <= maxZoom; i++)
		{
			int leftTileIdx = MercatorTiledRegion.GetLeftTileIdx(i, mercatorBBox);
			int rightTileIdx = MercatorTiledRegion.GetRightTileIdx(i, mercatorBBox);
			int topTileIdx = MercatorTiledRegion.GetTopTileIdx(i, mercatorBBox);
			int botTileIdx = MercatorTiledRegion.GetBotTileIdx(i, mercatorBBox);
			int rightTileIdxLeftTileIdxTerm = rightTileIdx - leftTileIdx + 1;
			int botTileIdxTopTileIdxTerm = botTileIdx - topTileIdx + 1;
			ordinal += rightTileIdxLeftTileIdxTerm * botTileIdxTopTileIdxTerm;
		}
		using SQLiteImageTMS sQLiteImageTMS = new SQLiteImageTMS(destDBFilename);
		sQLiteImageTMS.AddRASMetadata(datasetID, pjctName, planName, mapType, legendValsCSV, legendColsCSV);
		sQLiteImageTMS.AddIndexedRasterTable(datasetID);
		int ordinal2 = 0;
		IEnumerable<GoogleCacheTile> source = from g in sQLiteImageTMS.GetAllTileDescriptions(datasetID)
			where g.TimeName == time
			select g;
		int z;
		for (z = 1; z <= maxZoom; z++)
		{
			IEnumerable<GoogleCacheTile> source2 = source.Where((GoogleCacheTile g) => g.ZoomLevel == z);
			int leftTileIdx2 = MercatorTiledRegion.GetLeftTileIdx(z, mercatorBBox);
			int rightTileIdx2 = MercatorTiledRegion.GetRightTileIdx(z, mercatorBBox);
			int topTileIdx2 = MercatorTiledRegion.GetTopTileIdx(z, mercatorBBox);
			int botTileIdx2 = MercatorTiledRegion.GetBotTileIdx(z, mercatorBBox);
			int i2;
			for (i2 = leftTileIdx2; i2 <= rightTileIdx2; i2++)
			{
				IEnumerable<GoogleCacheTile> source3 = source2.Where((GoogleCacheTile g) => g.Column == i2);
				int j;
				for (j = topTileIdx2; j <= botTileIdx2; j++)
				{
					IEnumerable<GoogleCacheTile> source4 = source3.Where((GoogleCacheTile g) => g.Row == j);
					if (OverwriteExistingTiles || source4.Count() <= 0)
					{
						byte[] rasterBytesZXY = GetRasterBytesZXY(z, i2, j);
						if (rasterBytesZXY != null && rasterBytesZXY.Length != 0)
						{
							sQLiteImageTMS.StreamWriteTile(datasetID, time, z, i2, j, rasterBytesZXY);
						}
						ordinal2++;
						reporter.ReportProgress((int)(100f * ((float)ordinal2 / (float)ordinal)));
						if (reporter.CancelRequested)
						{
							return;
						}
					}
				}
			}
		}
	}
}
