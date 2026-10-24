using System;
using System.Collections;
using System.IO;
using System.Linq;
using Geospatial.GDALAssist;
using Geospatial.Terrain;
using Geospatial.Vectors;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Utilities;
using TiffAssist;

namespace RasMapperLib.Terrain;

public class RasterFileInfo
{
	public string Filename;

	public string BaseName;

	public string FileLastWrite;

	public string Groupname;

	public int Priority;

	public int Cols;

	public int Rows;

	public Extent Extent;

	public string Rounding;

	public int Levels;

	public BitArray[] TileHasAnyData;

	public double[] CellSize;

	public int[] TileSize;

	public RasterM[] TileRasters;

	public RasterM[] RenderBlockTiles;

	public RasterFileInfo(H5Reader hr, string directory, string baseTerrain, string groupname)
	{
		Filename = "";
		BaseName = "";
		FileLastWrite = "";
		Groupname = "";
		Priority = -1;
		Extent = null;
		Rounding = null;
		CellSize = null;
		TileSize = null;
		TileRasters = null;
		RenderBlockTiles = null;
		Groupname = "/Terrain/" + groupname;
		string textValue = "";
		hr.ReadAttributeScalar(Groupname, "File", ref textValue);
		Filename = directory + Conversions.ToString(System.IO.Path.DirectorySeparatorChar) + textValue;
		BaseName = groupname;
		hr.ReadAttributeScalar(Groupname, "File Last-Write", ref FileLastWrite);
		if (hr.ExistsAsAttribute(Groupname, "Priority"))
		{
			hr.ReadAttributeScalar(Groupname, "Priority", ref Priority);
		}
		if (!File.Exists(Filename))
		{
			return;
		}
		Geospatial.Vectors.Extent fullNativeExtents = GDALRaster.GetFullNativeExtents(Filename);
		Extent = Converter.Convert(fullNativeExtents);
		checked
		{
			using FloatTiffReader floatTiffReader = new FloatTiffReader(Filename);
			Cols = floatTiffReader.GetImageWidth(0);
			Rows = floatTiffReader.GetImageHeight(0);
			TiffMetadata<float> tiffMetadata = floatTiffReader.GetTiffMetadata(0);
			Rounding = TiffBase.GetDescription(tiffMetadata.Rounding);
			Levels = floatTiffReader.GetNumDirectories();
			CellSize = new double[Levels - 1 + 1];
			TileSize = new int[Levels - 1 + 1];
			TileRasters = new RasterM[Levels - 1 + 1];
			RenderBlockTiles = new RasterM[Levels - 1 + 1];
			double deltaX = Extent.DeltaX;
			double deltaY = Extent.DeltaY;
			int count = Levels - 1;
			double cellSize = default;
			for (int i = 0; i <= count; i++)
			{
				if (i == 0)
				{
					double deltaXOverImageWidth = deltaX / (double)floatTiffReader.GetImageWidth(i);
					double deltaYOverImageHeight = deltaY / (double)floatTiffReader.GetImageHeight(i);
					cellSize = System.Math.Max(deltaXOverImageWidth, deltaYOverImageHeight);
				}
				else
				{
					cellSize *= 2.0;
				}
				CellSize[i] = cellSize;
				int numTilesWide = floatTiffReader.GetNumTilesWide(i);
				int numTilesTall = floatTiffReader.GetNumTilesTall(i);
				TileSize[i] = floatTiffReader.GetTileWidth(i);
				double tileSizeCellSizeTerm = (double)TileSize[i] * cellSize;
				double x = fullNativeExtents.UpperLeft.X;
				double y = fullNativeExtents.UpperLeft.Y;
				TileRasters[i] = new RasterM(numTilesTall, numTilesWide, x + (double)numTilesWide * tileSizeCellSizeTerm, x, y, y - (double)numTilesTall * tileSizeCellSizeTerm);
				x += cellSize * 0.5;
				y -= cellSize * 0.5;
				RenderBlockTiles[i] = new RasterM(numTilesTall, numTilesWide, x + (double)numTilesWide * tileSizeCellSizeTerm, x, y, y - (double)numTilesTall * tileSizeCellSizeTerm);
			}
			SetAllRasterNoData(hr);
		}
	}

	public int ComputeLevel(double desiredCellSize)
	{
		checked
		{
			int count = Levels - 1;
			for (int i = 0; i <= count; i++)
			{
				if (CellSize[i] >= desiredCellSize)
				{
					return i;
				}
			}
			return CellSize.Count() - 1;
		}
	}

	private void SetAllRasterNoData(H5Reader hr)
	{
		checked
		{
			TileHasAnyData = new BitArray[Levels - 1 + 1];
			float[,] values = new float[1, 1];
			int count = Levels - 1;
			for (int i = 0; i <= count; i++)
			{
				hr.ReadDataset(Geospatial.Terrain.Terrain.GetMinMaxName(Filename, i), ref values);
				TileHasAnyData[i] = new BitArray(values.GetLength(0));
				int count2 = values.GetLength(0) - 1;
				for (int j = 0; j <= count2; j++)
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

	public SpatialIndex<int> CreateTileSpatialIndex()
	{
		return new SpatialIndex<int>(TileRasters[0]);
	}

	public SpatialIndex<int> CreateRenderBlockSpatialIndex()
	{
		return new SpatialIndex<int>(RenderBlockTiles[0]);
	}
}
