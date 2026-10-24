using System;
using System.Buffers;
using Geospatial.Rasters;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.Logging;
using Utility.Memory;

namespace Geospatial.Rendering.Layers;

public static class RasterRenderHelper
{
	private static readonly ILogger _logger = LogHelper.GetLogger("RasterRenderHelper");

	public static IShader DebugShader = null;

	public static bool DefaultRenderImplementation(IRenderLayer instance, PlotRequest plot, IResample<float> resampler, RasterSymbology symbology, ContourLayerStats contourStats = default(ContourLayerStats))
	{
		if (resampler == null || plot == null || symbology == null)
		{
			return false;
		}
		if (!plot.RasterQuery.Overlaps(resampler.ValidExtent))
		{
			return false;
		}
		RasterDefinition rasterQuery = plot.RasterQuery;
		IMemoryOwner<float> memoryOwner = SharedMemoryPool<float>.RentNoFill(rasterQuery.CellCount, out var mem);
		ContourValueContext contourContext = default;
		IMemoryOwner<float> memoryOwner2 = null;
		if (symbology.ContourProperties.IsEnabled)
		{
			RasterDefinition rasterDefinition = rasterQuery.ExtendRaster(ContourRenderHelper.GutterCells);
			memoryOwner2 = SharedMemoryPool<float>.RentNoFill(rasterDefinition.CellCount, out var mem2);
			resampler.Resample(symbology.AsResampleArgs(rasterDefinition), mem2);
			RasterOperations.CopyTo(rasterDefinition, mem2, rasterQuery, mem);
			contourContext = new ContourValueContext(rasterDefinition, mem2, contourStats);
		}
		else
		{
			resampler.Resample(symbology.AsResampleArgs(rasterQuery), mem);
		}
		if (plot.Reporter.CancelRequested)
		{
			memoryOwner2?.Dispose();
			memoryOwner.Dispose();
			return false;
		}
		plot.SetRasterData_TransferOwnership(instance, memoryOwner);
		try
		{
			Render(mem, plot, symbology, resampler.NoDataValue.GetValueOrDefault(), contourContext);
		}
		finally
		{
			memoryOwner2?.Dispose();
		}
		return true;
	}

	public unsafe static void Render(Memory<float> values, PlotRequest plot, RasterSymbology symbology, float NDV = -9999f, ContourValueContext contourContext = default(ContourValueContext))
	{
		RasterDefinition rasterQuery = plot.RasterQuery;
		int cellCount = rasterQuery.CellCount;
		HillshadeProperties hillshadeProperties = symbology.HillshadeProperties;
		if (symbology.IsSurfaceFillEnabled)
		{
			if (plot.SplatCount == 0)
			{
				symbology.PaintSurface(values, plot.ColorBufferARGB, NDV);
				plot.AlertDirectSplat();
			}
			else
			{
				Memory<int> mem;
				using (SharedMemoryPool<int>.RentNoFill(cellCount, out mem))
				{
					symbology.PaintSurface(values, mem, NDV);
					plot.Splat(mem);
				}
			}
		}
		else if (hillshadeProperties.IsEnabled)
		{
			Memory<int> mem2;
			using (SharedMemoryPool<int>.Rent(cellCount, out mem2))
			{
				sbyte* alphaSymbologyTerm = stackalloc sbyte[4]
				{
					-1,
					-1,
					-1,
					(sbyte)symbology.Alpha
				};
				int outValue = *(int*)alphaSymbologyTerm;
				Buffers.ArraySetIfNotEqual(values, NDV, mem2, outValue);
				plot.Splat(mem2);
			}
		}
		else
		{
			_logger.LogDebug("Render method with no surface fill or hillshade enabled - error?");
		}
		if (hillshadeProperties.IsEnabled)
		{
			Memory<int> mem3;
			using (SharedMemoryPool<int>.RentNoFill(cellCount, out mem3))
			{
				if (HillshadeOperations.UseOptimizedHillshade)
				{
					HillshadeOperations.ComputeHillShadeUnsafe_Optimized_SmartFor(rasterQuery, values, (byte)(128.0 * ((double)(int)symbology.Alpha / 255.0)), hillshadeProperties.Zenith, hillshadeProperties.Azimuth, hillshadeProperties.ZFactor, NDV, mem3);
				}
				else
				{
					HillshadeOperations.ComputeHillShadeUnsafe_SmartFor(rasterQuery, values, (byte)(128.0 * ((double)(int)symbology.Alpha / 255.0)), hillshadeProperties.Zenith, hillshadeProperties.Azimuth, hillshadeProperties.ZFactor, NDV, mem3);
				}
				plot.Splat(mem3);
			}
		}
		if (symbology.ContourProperties.IsEnabled)
		{
			ContourRenderHelper.Render(values, plot, symbology, NDV, in contourContext);
		}
	}
}
