using System;
using System.Buffers;
using System.Collections.Generic;
using Utility.Memory;

namespace Geospatial.Rasters;

public static class IMultiBandRasterExtensions
{
	public static void GetBand<T>(this IMultiBandRaster<T> raster, int bandIndex, out T[] outData) where T : unmanaged, IEquatable<T>
	{
		RasterDefinition rasterDefinition = raster.RasterDefinition;
		outData = new T[rasterDefinition.CellCount];
		raster.GetRange(rasterDefinition.FullRange(), bandIndex, outData);
	}

	public static T[] GetBand<T>(this IMultiBandRaster<T> raster, int bandIndex) where T : unmanaged, IEquatable<T>
	{
		RasterDefinition rasterDefinition = raster.RasterDefinition;
		T[] band = new T[rasterDefinition.CellCount];
		raster.GetRange(rasterDefinition.FullRange(), bandIndex, band);
		return band;
	}

	public static void GetBand<T>(this IMultiBandRaster<T> raster, int bandIndex, Memory<T> outData) where T : unmanaged, IEquatable<T>
	{
		raster.GetRange(raster.RasterDefinition.FullRange(), bandIndex, outData);
	}

	public static T GetPixel<T>(this IMultiBandRaster<T> pf, int bandIndex, int pixel) where T : unmanaged, IEquatable<T>
	{
		Pixel pixel2 = pf.RasterDefinition.CellIndex2D(pixel);
		return pf.GetPixel(bandIndex, pixel2);
	}

	public static T GetPixel<T>(this IMultiBandRaster<T> pf, int bandIndex, Pixel pixel) where T : unmanaged, IEquatable<T>
	{
		PixelRange range = new PixelRange(pixel, pixel);
		T[] values = new T[1];
		pf.GetRange(range, bandIndex, values);
		return values[0];
	}

	public static double[] PiecewiseSum(this IMultiBandRaster<float> pf)
	{
		double[] buf = new double[pf.RasterDefinition.CellCount];
		pf.ForEachBand((Memory<float> mem) =>
		{
			Span<float> memSpan = mem.Span;
			for (int i = 0; i < memSpan.Length; i++)
			{
				float mem2 = memSpan[i];
				if (mem2 != -9999f)
				{
					buf[i] += mem2;
				}
			}
		});
		return buf;
	}

	public static void ForEachBand<T>(this IMultiBandRaster<T> ipf, Action<Memory<T>> bandAction) where T : unmanaged, IEquatable<T>
	{
		ResampleArgs args = new ResampleArgs(ipf.RasterDefinition);
		int cellCount = ipf.RasterDefinition.CellCount;
		using IMemoryOwner<T> memoryOwner = SharedMemoryPool<T>.Rent(cellCount);
		Memory<T> slice = memoryOwner.Memory.Slice(0, cellCount);
		for (int i = 0; i < ipf.ProfileCount; i++)
		{
			ipf.Resample(args, i, slice);
			bandAction(slice);
		}
	}

	public static IMultiBandRaster<TOut> WithTranslator<TIn, TOut>(this IMultiBandRaster<TIn> src, ConverterDelegates.ConvertTo<TIn, TOut> translator) where TIn : unmanaged, IEquatable<TIn> where TOut : unmanaged, IEquatable<TOut>
	{
		return new MultiBandRasterModifier<TIn, TOut>(src, translator);
	}

	public static ITimeSeriesRaster<T> AsTimeSeriesRaster<T>(this IMultiBandRaster<T> resampler, IList<DateTime> timesteps) where T : unmanaged, IEquatable<T>
	{
		return new VirtualTimeSeriesRaster<T>(resampler, timesteps);
	}
}
