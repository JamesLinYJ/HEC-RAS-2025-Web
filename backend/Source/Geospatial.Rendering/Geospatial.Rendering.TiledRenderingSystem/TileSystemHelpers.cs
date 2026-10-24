using System;
using System.Collections.Generic;
using Geospatial.Rasters;

namespace Geospatial.Rendering.TiledRenderingSystem;

public class TileSystemHelpers
{
	public delegate PixelRange GetPixelRangeForScreenAtPyramidLevel<Model>(Model model, int pyramidLevel);

	public delegate bool RemoveTile<Model>(Model model, TileKey key);

	public delegate IList<TileKey> GetTilesAtPyramidLevel<Model>(Model model, int pyramidLevel);

	public static bool DoesTileSpaceHaveQualityData<Model>(Model model, TileKey tile, ICollection<TileKey> tileLookup, GetPixelRangeForScreenAtPyramidLevel<Model> getOnScreenTilesForPyramidLevel, int pyramidLevelCount, TilingScheme tileScheme = TilingScheme.XYZ)
	{
		return DoesTileSpaceHaveQualityData(model, tile, tileLookup, getOnScreenTilesForPyramidLevel, pyramidLevelCount, tileScheme, true);
	}

	private static bool DoesTileSpaceHaveQualityData<Model>(Model model, TileKey tile, ICollection<TileKey> tileLookup, GetPixelRangeForScreenAtPyramidLevel<Model> getOnScreenTilesForPyramidLevel, int pyramidLevelCount, TilingScheme tileScheme = TilingScheme.XYZ, bool isBaseTile = true)
	{
		Pixel tileIndex = tile.TileIndex;
		int level = tile.Level;
		if (!isBaseTile && tileLookup.Contains(tile))
		{
			return true;
		}
		if (level == pyramidLevelCount)
		{
			return false;
		}
		PixelRange pixelRange = getOnScreenTilesForPyramidLevel(model, level + 1);
		if (pixelRange.IsNull())
		{
			return false;
		}
		PixelRange pixelRange2;
		if (tileScheme == TilingScheme.XYZ)
		{
			int scaledRow = tileIndex.Row * 2;
			int rowTileIndexTerm = tileIndex.Row * 2 + 1;
			int scaledColumn = tileIndex.Column * 2;
			int columnTileIndexTerm = tileIndex.Column * 2 + 1;
			pixelRange2 = new PixelRange(Math.Max(scaledRow, pixelRange.MinRow), Math.Min(rowTileIndexTerm, pixelRange.MaxRow), Math.Max(scaledColumn, pixelRange.MinCol), Math.Min(columnTileIndexTerm, pixelRange.MaxCol));
		}
		else
		{
			int scaledRow2 = tileIndex.Row * 2;
			int rowTileIndexTerm2 = tileIndex.Row * 2 + 1;
			int scaledColumn2 = tileIndex.Column * 2;
			int columnTileIndexTerm2 = tileIndex.Column * 2 + 1;
			pixelRange2 = new PixelRange(Math.Min(rowTileIndexTerm2, pixelRange.MaxRow), Math.Max(scaledRow2, pixelRange.MinRow), Math.Max(scaledColumn2, pixelRange.MinCol), Math.Min(columnTileIndexTerm2, pixelRange.MaxCol));
		}
		bool condition = true;
		for (int i = 0; i < 2; i++)
		{
			for (int j = 0; j < 2; j++)
			{
				Pixel pixel = new Pixel(tileIndex.Row * 2 + j, tileIndex.Column * 2 + i);
				if (pixelRange2.InRange(pixel))
				{
					condition &= DoesTileSpaceHaveQualityData(model, new TileKey(level + 1, pixel), tileLookup, getOnScreenTilesForPyramidLevel, pyramidLevelCount, tileScheme, isBaseTile: false);
					if (!condition)
					{
						break;
					}
				}
			}
			if (!condition)
			{
				break;
			}
		}
		return condition;
	}

	public static void TryClearHigherQualityTiles<Model>(Model model, TileKey tile, ICollection<TileKey> tileLookup, GetTilesAtPyramidLevel<Model> getTilesAtPyramidLevel, RemoveTile<Model> removeTile, int pyramidLevelCount)
	{
		Pixel tileIndex = tile.TileIndex;
		int level = tile.Level;
		for (int i = level + 1; i <= pyramidLevelCount; i++)
		{
			IList<TileKey> getTilesAtPyramidLevelList = getTilesAtPyramidLevel(model, i);
			if (getTilesAtPyramidLevelList.Count == 0)
			{
				continue;
			}
			int scale = GetScale(i, level);
			long scale2 = scale;
			if (getTilesAtPyramidLevelList.Count < scale2 * scale2)
			{
				for (int j = 0; j < getTilesAtPyramidLevelList.Count; j++)
				{
					TileKey key = getTilesAtPyramidLevelList[j];
					if (TileContains(tileIndex, level, key.TileIndex, key.Level) && removeTile(model, key))
					{
						j--;
					}
				}
				continue;
			}
			for (int k = 0; k < scale; k++)
			{
				for (int l = 0; l < scale; l++)
				{
					Pixel tileIndex2 = new Pixel(tileIndex.Row * scale + l, tileIndex.Column * scale + k);
					TileKey tileKey = new TileKey(i, tileIndex2);
					if (tileLookup.Contains(tileKey))
					{
						removeTile(model, tileKey);
					}
				}
			}
		}
	}

	public static void TryClearLowerQualityTilesInternal<Model>(Model model, TileKey tile, ICollection<TileKey> tileLookup, GetTilesAtPyramidLevel<Model> getTilesAtPyramidLevel, RemoveTile<Model> removeTile, GetPixelRangeForScreenAtPyramidLevel<Model> getPixelRangeForScreenAtPyramidLevel, int pyramidLevelCount, TilingScheme tileScheme = TilingScheme.XYZ, bool isBaseTile = true)
	{
		int level = tile.Level;
		Pixel tileIndex = tile.TileIndex;
		if (level >= 0)
		{
			if (!isBaseTile && getTilesAtPyramidLevel(model, level).Count != 0 && tileLookup.Contains(tile) && DoesTileSpaceHaveQualityData(model, tile, tileLookup, getPixelRangeForScreenAtPyramidLevel, pyramidLevelCount, tileScheme))
			{
				removeTile(model, tile);
			}
			double computed = 2.0;
			level--;
			Pixel tileIndex2 = new Pixel((int)Math.Floor((double)(float)tileIndex.Row / computed), (int)Math.Floor((double)(float)tileIndex.Column / computed));
			TileKey tile2 = new TileKey(level, tileIndex2);
			TryClearLowerQualityTilesInternal(model, tile2, tileLookup, getTilesAtPyramidLevel, removeTile, getPixelRangeForScreenAtPyramidLevel, pyramidLevelCount, tileScheme, isBaseTile: false);
		}
	}

	private static int GetScale(int activeTilePyramidLevelIndex, int tileIndexPyramidLevelIndex)
	{
		return 1 << activeTilePyramidLevelIndex - tileIndexPyramidLevelIndex;
	}

	private static bool TileContains(Pixel aTileIndex, int aPyramidLevel, Pixel bTileIndex, int bPyramidLevel)
	{
		int scale = GetScale(bPyramidLevel, aPyramidLevel);
		int aTileIndexScaleTerm = aTileIndex.X * scale;
		int aTileIndexScaleTerm2 = aTileIndex.Y * scale;
		int x = bTileIndex.X;
		int y = bTileIndex.Y;
		if (x >= aTileIndexScaleTerm && x < aTileIndexScaleTerm + scale)
		{
			if (y >= aTileIndexScaleTerm2)
			{
				return y < aTileIndexScaleTerm2 + scale;
			}
			return false;
		}
		return false;
	}
}
