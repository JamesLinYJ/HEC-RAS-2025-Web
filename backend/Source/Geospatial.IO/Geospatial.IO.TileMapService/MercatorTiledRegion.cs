using System;
using Geospatial.Rasters;
using Geospatial.Vectors;

namespace Geospatial.IO.TileMapService;

public static class MercatorTiledRegion
{
	public const double MERCATOR_EDGE = Math.PI * 6378137.0;

	public const int TileWidth = 256;

	public const int TileHeight = 256;

	public static RasterDefinition GetTileDefinitionForPyramidLevel(int pLevel)
	{
		int numTilesWide = NumTilesWide(pLevel);
		Point upperLeftCorner = new Point(Math.PI * -6378137.0, Math.PI * 6378137.0);
		double tileSize = GetTileSize(pLevel);
		return new RasterDefinition(upperLeftCorner, tileSize, numTilesWide, numTilesWide);
	}

	public static RasterDefinition GetRasterDefinitionForPyramidLevel(int pLevel)
	{
		int numTilesWidePLevelTerm = NumTilesWide(pLevel) * 256;
		Point upperLeftCorner = new Point(Math.PI * -6378137.0, Math.PI * 6378137.0);
		double cellSize = GetCellSize(pLevel);
		return new RasterDefinition(upperLeftCorner, cellSize, numTilesWidePLevelTerm, numTilesWidePLevelTerm);
	}

	public static Extent FullExtent()
	{
		return new Extent(Math.PI * -6378137.0, Math.PI * 6378137.0, Math.PI * -6378137.0, Math.PI * 6378137.0);
	}

	public static int GetLeftTileIdx(int zoom, Extent mercatorBBox)
	{
		int numTilesWide = NumTilesWide(zoom);
		return (int)Math.Floor((mercatorBBox.UpperLeft.X + Math.PI * 6378137.0) / (Math.PI * 12756274.0) * (double)numTilesWide);
	}

	public static int GetRightTileIdx(int zoom, Extent mercatorBBox)
	{
		int numTilesWide = NumTilesWide(zoom);
		return (int)Math.Floor((mercatorBBox.UpperRight.X + Math.PI * 6378137.0) / (Math.PI * 12756274.0) * (double)numTilesWide);
	}

	public static int GetTopTileIdx(int zoom, Extent mercatorBBox)
	{
		int numTilesWide = NumTilesWide(zoom);
		double y = mercatorBBox.UpperRight.Y;
		return (int)Math.Floor((Math.PI * 6378137.0 - y) / (Math.PI * 12756274.0) * (double)numTilesWide);
	}

	public static int GetBotTileIdx(int zoom, Extent mercatorBBox)
	{
		int numTilesWide = NumTilesWide(zoom);
		double y = mercatorBBox.LowerRight.Y;
		return (int)Math.Floor((Math.PI * 6378137.0 - y) / (Math.PI * 12756274.0) * (double)numTilesWide);
	}

	public static int NumTilesWide(int zoom)
	{
		return (int)Math.Round(Math.Pow(2.0, zoom));
	}

	public static Extent GetTileMercatorBBox(int zoom, int tileX, int tileY)
	{
		int tilesPerEdge = NumTilesWide(zoom);
		double leftMercCoord = GetLeftMercCoord(tilesPerEdge, tileX);
		double rightMercCoord = GetRightMercCoord(tilesPerEdge, tileX);
		double topMercCoord = GetTopMercCoord(tilesPerEdge, tileY);
		double botMercCoord = GetBotMercCoord(tilesPerEdge, tileY);
		return new Extent(leftMercCoord, rightMercCoord, botMercCoord, topMercCoord);
	}

	private static double GetLeftMercCoord(int tilesPerEdge, int tileX)
	{
		double tileXOverTilesPerEdge = (double)tileX / (double)tilesPerEdge;
		return Math.PI * -6378137.0 + Math.PI * 12756274.0 * tileXOverTilesPerEdge;
	}

	private static double GetRightMercCoord(int tilesPerEdge, int tileX)
	{
		return GetLeftMercCoord(tilesPerEdge, tileX + 1);
	}

	private static double GetTopMercCoord(int tilesPerEdge, int tileY)
	{
		double tileYOverTilesPerEdge = (double)tileY / (double)tilesPerEdge;
		return Math.PI * 6378137.0 - Math.PI * 12756274.0 * tileYOverTilesPerEdge;
	}

	private static double GetBotMercCoord(int tilesPerEdge, int tileY)
	{
		return GetTopMercCoord(tilesPerEdge, tileY + 1);
	}

	public static double GetTileSize(int zoom)
	{
		int numTilesWide = NumTilesWide(zoom);
		return Math.PI * 6378137.0 / ((double)numTilesWide / 2.0);
	}

	public static double GetCellSize(int zoom)
	{
		return GetTileSize(zoom) / 256.0;
	}

	public static int GetZoomLevel(double cellSize)
	{
		for (int i = 0; i <= 20; i++)
		{
			if (GetCellSize(i) < cellSize)
			{
				return i;
			}
		}
		return 20;
	}
}
