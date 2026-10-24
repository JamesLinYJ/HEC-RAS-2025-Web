using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using Geospatial.GDALAssist;
using Geospatial.Rasters;
using Geospatial.Terrain;
using Geospatial.Vectors;
using H5Assist;
using Microsoft.Extensions.Logging;
using TiffAssist;
using Utility.Logging;

namespace Ras.Layers;

[DebuggerDisplay("{BaseName} : [{CellSize[0]}]")]
public class RasterFileInfo
{
	private static readonly ILogger _logger = LogHelper.GetLogger("RasterFileInfo");

	public string Filename = "";

	public string BaseName = "";

	public string FileLastWrite = "";

	public string Groupname = "";

	public int Priority;

	public int Cols;

	public int Rows;

	public Extent Extent;

	public string Rounding;

	public int Levels;

	public BitArray[] TileHasAnyData;

	public double[] CellSize;

	public int[] TileSize;

	public RasterDefinition[] TileRasters;

	public RasterDefinition[] RenderBlockTiles;

	public RasterFileInfo(H5Reader hr, string directory, string baseTerrain, string grpName)
	{
		Groupname = "/Terrain/" + grpName;
		hr.ReadAttributeScalar(Groupname, "File", out var localValue);
		Filename = directory + Path.DirectorySeparatorChar + localValue;
		BaseName = grpName;
		hr.TryReadAttributeScalar(Groupname, "File Last-Write", out FileLastWrite);
		if (hr.ExistsAsAttribute(Groupname, "Priority"))
		{
			hr.ReadAttributeScalar(Groupname, "Priority", out int priority);
			Priority = priority;
		}
		else
		{
			Priority = -1;
		}
		if (!File.Exists(Filename))
		{
			return;
		}
		Extent = GDALRaster.GetFullNativeExtents(Filename);
		using FloatTiffReader floatTiffReader = new FloatTiffReader(Filename);
		Cols = floatTiffReader.GetImageWidth(0);
		Rows = floatTiffReader.GetImageHeight(0);
		TiffMetadata<float> tiffMetadata = floatTiffReader.GetTiffMetadata(0);
		Rounding = TiffBase.GetDescription(tiffMetadata.Rounding);
		Levels = floatTiffReader.GetNumDirectories();
		CellSize = new double[Levels];
		TileSize = new int[Levels];
		TileRasters = new RasterDefinition[Levels];
		RenderBlockTiles = new RasterDefinition[Levels];
		double deltaX = Extent.DeltaX;
		double deltaY = Extent.DeltaY;
		double cellSize2 = 0.0;
		for (int i = 0; i < Levels; i++)
		{
			if (i == 0)
			{
				double deltaXOverImageWidth = deltaX / (double)floatTiffReader.GetImageWidth(i);
				double deltaYOverImageHeight = deltaY / (double)floatTiffReader.GetImageHeight(i);
				cellSize2 = Math.Max(deltaXOverImageWidth, deltaYOverImageHeight);
			}
			else
			{
				cellSize2 *= 2.0;
			}
			CellSize[i] = cellSize2;
			int numTilesWide = floatTiffReader.GetNumTilesWide(i);
			int numTilesTall = floatTiffReader.GetNumTilesTall(i);
			TileSize[i] = floatTiffReader.GetTileWidth(i);
			double cellSize = (double)TileSize[i] * cellSize2;
			double x = Extent.UpperLeft.X;
			double y = Extent.UpperLeft.Y;
			TileRasters[i] = new RasterDefinition(new Point(x, y), cellSize, numTilesTall, numTilesWide);
			x += cellSize2 * 0.5;
			y -= cellSize2 * 0.5;
			RenderBlockTiles[i] = new RasterDefinition(new Point(x, y), cellSize, numTilesTall, numTilesWide);
		}
	}

	private void SetAllRasterNoData(H5Reader hr)
	{
		TileHasAnyData = new BitArray[Levels];
		for (int i = 0; i < Levels; i++)
		{
			hr.ReadDataset(Geospatial.Terrain.Terrain.GetMinMaxName(Filename, i), out float[,] values);
			TileHasAnyData[i] = new BitArray(values.GetLength(0));
			for (int j = 0; j < values.GetLength(0); j++)
			{
				if (values[j, 2] == 1f)
				{
					TileHasAnyData[i][j] = false;
				}
				else
				{
					TileHasAnyData[i][j] = true;
				}
			}
		}
	}
}
