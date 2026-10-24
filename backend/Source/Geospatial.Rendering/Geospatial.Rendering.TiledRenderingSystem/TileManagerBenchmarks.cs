using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Geospatial.Rasters;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Progress;

namespace Geospatial.Rendering.TiledRenderingSystem;

public static class TileManagerBenchmarks
{
	private static readonly ILogger _logger = LogHelper.GetLogger("TileManagerBenchmarks");

	public static async Task BenchmarkAllSpace(PyramidedTileManager tm, ProgressReporter prog = null)
	{
		await BenchmarkSpace(tm, tm.RenderContext.ValidExtent, 1000, 25, prog);
	}

	public static async Task BenchmarkSpace(PyramidedTileManager tm, Extent fullExtent, int nFrames, int nSubsteps, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		if (tm.RenderContext == null)
		{
			throw new Exception("No render context - turn a map on.");
		}
		RasterDefinition rdef = RasterDefinition.ComputeBoundingRaster(fullExtent, nFrames);
		await tm.SetWindowAsync(rdef.CellExtent(0));
		await tm.WaitForRenderCompleteAsync();
		double pxSize = tm.LiveScreenRaster.CellSize;
		Point point = rdef.Extent.UpperLeft;
		Stopwatch frameSw = new Stopwatch();
		for (int r = 0; r < rdef.Rows; r++)
		{
			for (int c = 0; c < rdef.Cols; c++)
			{
				if (prog.CancelRequested)
				{
					return;
				}
				prog.ReportProgress(100 * (r * rdef.Cols + c) / rdef.CellCount);
				int actualC = c;
				if (r % 2 == 1)
				{
					actualC = rdef.Cols - 1 - c;
				}
				Point targetPoint = rdef.CellCenter(r, actualC);
				double targetPointPointTerm = targetPoint.X - point.X;
				double targetPointPointTerm2 = targetPoint.Y - point.Y;
				double pxSizeNSubstepsTerm = (0.0 - targetPointPointTerm) / (pxSize * (double)nSubsteps);
				double pxSizeNSubstepsTerm2 = targetPointPointTerm2 / (pxSize * (double)nSubsteps);
				for (int i = 0; i < nSubsteps; i++)
				{
					tm.PartialPan(pxSizeNSubstepsTerm * (double)(i + 1), pxSizeNSubstepsTerm2 * (double)(i + 1));
				}
				tm.FinishPan();
				frameSw.Restart();
				tm.QueueRenderTiles();
				await tm.WaitForRenderCompleteAsync();
				frameSw.Stop();
				_logger.LogTrace("Benchmark frame [{Row},{Col}] rendered in {Elapsed}ms", r, actualC, frameSw.ElapsedMilliseconds);
				point = targetPoint;
			}
		}
	}

	public static async Task BenchmarkAllTime(PyramidedTileManager tm, IBandedRenderLayer layer, int nRepeats = 100, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		if (tm.RenderContext == null)
		{
			throw new Exception("No render context - turn a map on.");
		}
		await tm.SetWindowAsync(layer.ValidExtent.RatioAroundCenter(1.1));
		int totalSteps = nRepeats * layer.ProfileState.ProfileCount;
		int step = 0;
		for (int n = 0; n < nRepeats; n++)
		{
			for (int i = 0; i < layer.ProfileState.ProfileCount; i++)
			{
				if (prog.CancelRequested)
				{
					return;
				}
				layer.ProfileState.CurrentProfileIndex = i;
				tm.QueueRenderTiles();
				step++;
				prog.ReportProgress(100 * step / totalSteps);
				await Task.Delay(1);
			}
		}
	}
}
