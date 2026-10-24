using System;
using Geospatial.PairedData;
using Geospatial.Transformers;
using Utility.Units;

namespace Geospatial.Rasters;

public static class ITimeSeriesExtensions
{
	public static IAccumulatedResampler<float> AsAccumulatedResampler(this ITimeSeriesResampler<float> resampler, TimeSeriesDataType dataType, Time timeUnits)
	{
		return new TimeSeriesAccumulatorF(resampler, dataType, timeUnits);
	}

	public static IAccumulatedRasterizer<float> AsAccumulatedRasterizer(this ITimeSeriesRaster<float> resampler, TimeSeriesDataType dataType, Time timeUnits)
	{
		return new TimeSeriesRasterAccumulatorF(resampler, dataType, timeUnits);
	}

	public static ITimeSeriesResampler<float> WithTransformer(this ITimeSeriesResampler<float> resampler, ISpatialTransformer spatialTransformer, IValueTransformer<float> valueTransformer)
	{
		return new TransformedTimeSeriesResampler(resampler, spatialTransformer, valueTransformer);
	}

	public static ITimeSeriesResampler<float> WithTransformer(this ITimeSeriesResampler<float> resampler, ISpatialTransformer spatialTransformer)
	{
		return new TransformedTimeSeriesResampler(resampler, spatialTransformer, null);
	}

	public static ITimeSeriesRaster<float> WithTransformer_Raster(this ITimeSeriesRaster<float> resampler, [System.Diagnostics.CodeAnalysis.AllowNull] ISpatialTransformer spatialTransformer, IValueTransformer<float> valueTransformer)
	{
		return new TransformedTimeSeriesRasterizer(resampler, spatialTransformer, valueTransformer);
	}

	public static T[] Resample<T>(this ITimeSeriesResampler<T> resampler, ResampleArgs args, DateTime dt) where T : unmanaged, IEquatable<T>
	{
		T[] values = new T[args.Raster.CellCount];
		resampler.Resample(args, dt, values);
		return values;
	}

	public unsafe static long SizeInBytes<T>(this ITimeSeriesRaster<T> raster) where T : unmanaged, IEquatable<T>
	{
		long cellCountLong = raster.RasterDefinition.CellCountLong;
		long timeCount = raster.Times.Count;
		int ordinal = sizeof(T);
		return cellCountLong * timeCount * ordinal;
	}

	public static ITimeSeriesRaster<T> AsTimeSeriesRaster<T>(this ITimeSeriesResampler<T> resampler, RasterDefinition raster) where T : unmanaged, IEquatable<T>
	{
		return new TimeSeriesRasterizer<T>(resampler, raster);
	}
}
