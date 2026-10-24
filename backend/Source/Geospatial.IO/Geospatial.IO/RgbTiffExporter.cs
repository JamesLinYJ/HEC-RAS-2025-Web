using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Geospatial.GDALAssist;
using Geospatial.Rasters;
using Geospatial.Rendering;
using Geospatial.Rendering.Layers;
using TiffBinaryReader;
using Utility.Files;
using Utility.Progress;

namespace Geospatial.IO;

public static class RgbTiffExporter
{
	private const int TileSize = 256;

	private const float RgbCompressedBytesPerPixel = 1f;

	public static long EstimateFileSize(RasterDefinition rdef)
	{
		return EstimateFileSize(rdef.CellCountLong);
	}

	public static long EstimateFileSize(long cellCount)
	{
		return (long)((float)cellCount * 1f);
	}

	public static void ExportRgba(string destFilename, IRenderLayer renderLayer, RasterDefinition exportRdef, Projection? projectionMetadata = null, ProgressReporter pr = null)
	{
		ExportInternal(destFilename, renderLayer, exportRdef, projectionMetadata, pr, withAlpha: true);
	}

	public static void Export(string destFilename, IRenderLayer renderLayer, RasterDefinition exportRdef, Projection? projectionMetadata = null, ProgressReporter pr = null)
	{
		ExportInternal(destFilename, renderLayer, exportRdef, projectionMetadata, pr, withAlpha: false);
	}

	private static void ExportInternal(string destFilename, IRenderLayer renderLayer, RasterDefinition exportRdef, Projection? projectionMetadata, ProgressReporter pr, bool withAlpha)
	{
		if (renderLayer == null)
		{
			throw new ArgumentNullException("renderLayer");
		}
		if (exportRdef.IsNull() || !exportRdef.IsValid())
		{
			throw new ArgumentException("Invalid export raster definition.", "exportRdef");
		}
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		pr.ReportMessage("Exporting " + (withAlpha ? "RGBA" : "RGB") + " TIFF " + Path.GetFileName(destFilename) + "...");
		TagMetadata directoryToCreate = (withAlpha ? TagMetadata.ForImageRGBA(exportRdef.Cols, exportRdef.Rows) : TagMetadata.ForImage(exportRdef.Cols, exportRdef.Rows));
		RasterDefinition tiledRdef = exportRdef.TiledRaster(256);
		int totalTiles = tiledRdef.CellCount;
		TiffFileWriter writer = new TiffFileWriter(destFilename, directoryToCreate);
		try
		{
			int completed = 0;
			ParallelOptions parallelOptions = new ParallelOptions();
			parallelOptions.MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 8);
			Parallel.For(0, totalTiles, parallelOptions, (int tileIdx, ParallelLoopState loopState) =>
			{
				if (pr.CancelRequested)
				{
					loopState.Stop();
					return;
				}
				int row = tileIdx / tiledRdef.Cols;
				int col = tileIdx % tiledRdef.Cols;
				using PlotRequest plotRequest = new PlotRequest(new RasterDefinition(tiledRdef.CellExtent(row, col).UpperLeft, exportRdef.CellSize, 256, 256));
				object cache = null;
				renderLayer.Render(plotRequest, ref cache);
				Memory<byte> data = (withAlpha ? ArgbToRgba(plotRequest.ColorBufferARGB) : ArgbToRgb(plotRequest.ColorBufferARGB));
				writer.WriteTileParallel(0, tileIdx, data);
				int increment = Interlocked.Increment(ref completed);
				pr.ReportProgress((int)((long)increment * 100L / totalTiles));
			});
		}
		finally
		{
			if (writer != null)
			{
				((IDisposable)writer).Dispose();
			}
		}
		if (pr.CancelRequested)
		{
			pr.ReportMessage("Operation Cancelled - Cleaning up.");
			if (FileHelpers.TryDelete(destFilename))
			{
				pr.ReportMessage("Cleanup successful: File deleted.");
			}
			else
			{
				pr.ReportMessage("Could not delete " + destFilename);
			}
			return;
		}
		GDALRaster.SetLocation(destFilename, exportRdef);
		if (projectionMetadata != null)
		{
			GDALRaster.SetProjection(destFilename, projectionMetadata);
		}
		stopwatch.Stop();
		pr.ReportProgress(100);
		pr.ReportMessage("Finished in " + stopwatch.Elapsed);
	}

	private static Memory<byte> ArgbToRgb(Memory<int> argbBuffer)
	{
		Span<int> argbBufferSpan = argbBuffer.Span;
		int length = argbBufferSpan.Length;
		byte[] byteValues = new byte[length * 3];
		for (int i = 0; i < length; i++)
		{
			int argbBuffer2 = argbBufferSpan[i];
			int ordinal = i * 3;
			byteValues[ordinal] = (byte)((argbBuffer2 >> 16) & 0xFF);
			byteValues[ordinal + 1] = (byte)((argbBuffer2 >> 8) & 0xFF);
			byteValues[ordinal + 2] = (byte)(argbBuffer2 & 0xFF);
		}
		return byteValues;
	}

	private static Memory<byte> ArgbToRgba(Memory<int> argbBuffer)
	{
		Span<int> argbBufferSpan = argbBuffer.Span;
		int length = argbBufferSpan.Length;
		byte[] byteValues = new byte[length * 4];
		for (int i = 0; i < length; i++)
		{
			int argbBuffer2 = argbBufferSpan[i];
			int ordinal = i * 4;
			byteValues[ordinal] = (byte)((argbBuffer2 >> 16) & 0xFF);
			byteValues[ordinal + 1] = (byte)((argbBuffer2 >> 8) & 0xFF);
			byteValues[ordinal + 2] = (byte)(argbBuffer2 & 0xFF);
			byteValues[ordinal + 3] = (byte)((argbBuffer2 >> 24) & 0xFF);
		}
		return byteValues;
	}
}
