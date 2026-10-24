using System;
using System.IO;
using System.Linq;
using Geospatial.GDALAssist;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using TiffBinaryReader;
using Utility.Logging;

namespace Geospatial.IO;

public static class RasterReader
{
	private static readonly ILogger _logger = LogHelper.GetLogger("RasterReader");

	public static RasterData<float> TryReadTiff(string filename)
	{
		if (!File.Exists(filename))
		{
			return null;
		}
		using TiffFile tiffFile = new TiffFile(filename);
		TiffDirectory tiffDirectory = tiffFile.Directories[0];
		RasterDefinition rasterDefinition = TryReadRasterInfo(tiffDirectory);
		if (rasterDefinition.IsNull())
		{
			return null;
		}
		int rows = rasterDefinition.Rows;
		int cols = rasterDefinition.Cols;
		double cellSize = rasterDefinition.CellSize;
		float[] floatValues = new float[rows * cols];
		floatValues.AsSpan().Fill(-9999f);
		int numberCellsPerTile = tiffDirectory.Metadata.NumberCellsPerTile;
		float[] floatValuesArray = new float[numberCellsPerTile];
		int sqrtNumberCellsPerTile = (int)Math.Sqrt(numberCellsPerTile);
		int tilesTall = tiffDirectory.Metadata.TilesTall;
		int tilesWide = tiffDirectory.Metadata.TilesWide;
		RasterDefinition rasterDefinition2 = new RasterDefinition(rasterDefinition.Extent.UpperLeft, cellSize * (double)sqrtNumberCellsPerTile, tilesTall, tilesWide);
		for (int i = 0; i < tiffDirectory.Metadata.TilesTall; i++)
		{
			for (int j = 0; j < tiffDirectory.Metadata.TilesWide; j++)
			{
				int tilesWideMetadataTerm = i * tiffDirectory.Metadata.TilesWide + j;
				tiffDirectory.GetTile<float>(tilesWideMetadataTerm, floatValuesArray);
				RasterOperations.CopyTo<float>(rasterDefinition2.GetCellRasterDefinition(tilesWideMetadataTerm, sqrtNumberCellsPerTile), floatValuesArray, rasterDefinition, floatValues);
			}
		}
		return new RasterData<float>(rasterDefinition, floatValues);
	}

	public static IRaster<T> TryGetBestRasterizer<T>(string sourceFilename, out bool supportsMT, out IDisposable cleanup, string suggestedDriver = "") where T : unmanaged, IEquatable<T>
	{
		IRaster<T> raster = null;
		cleanup = null;
		try
		{
			supportsMT = false;
			if (suggestedDriver != "GDAL")
			{
				TiffDataSource<T> tiffDataSource = TiffDataSource<T>.TryLoad(sourceFilename);
				if (tiffDataSource != null)
				{
					cleanup = tiffDataSource;
					raster = tiffDataSource.AsRasterizer();
					supportsMT = true;
				}
			}
			if (raster == null)
			{
				raster = new GdalBandedRaster<T>((GDALRaster)(cleanup = new GDALRaster(sourceFilename)))
				{
					AllowParallelResampling = false
				}.AsSingleProfile(0);
				supportsMT = false;
			}
			return raster;
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "TryGetBestRasterizer");
			cleanup?.Dispose();
			cleanup = null;
			supportsMT = false;
			return null;
		}
	}

	public static RasterDefinition TryReadRasterInfo(string filename)
	{
		using TiffFile tiffFile = new TiffFile(filename);
		return TryReadRasterInfo(tiffFile.Directories[0]);
	}

	public static RasterDefinition TryReadRasterInfo(TiffDirectory dir)
	{
		Tag tag = dir.Metadata.Tags.FirstOrDefault((Tag t) => t.TagType.Name == "ModelTiePoint");
		if (tag == null)
		{
			return RasterDefinition.NullRaster;
		}
		Span<double> valuesAs = tag.ValuesAs<double>();
		double x = valuesAs[3];
		double y = valuesAs[4];
		Tag tag2 = dir.Metadata.Tags.FirstOrDefault((Tag t) => t.TagType.Name == "ModelPixelScale");
		if (tag2 == null)
		{
			return RasterDefinition.NullRaster;
		}
		double cellSize = tag2.ValuesAs<double>()[0];
		int rows = (int)dir.Metadata.ImageHeight;
		int cols = (int)dir.Metadata.ImageWidth;
		return new RasterDefinition(new Point(x, y), cellSize, rows, cols);
	}

	public static PrioritizedRasterCollection TryReadTiffFiles(params string[] files)
	{
		PrioritizedRasterCollection prioritizedRasterCollection = new PrioritizedRasterCollection();
		for (int i = 0; i < files.Length; i++)
		{
			RasterData<float> rasterData = TryReadTiff(files[i]);
			if (rasterData != null)
			{
				prioritizedRasterCollection.Add(rasterData);
			}
		}
		return prioritizedRasterCollection;
	}

	public static PrioritizedRasterCollection TryReadTiffsFromVRT(string vrtFilename)
	{
		if (!File.Exists(vrtFilename))
		{
			return null;
		}
		new PrioritizedRasterCollection();
		throw new NotImplementedException();
	}
}
