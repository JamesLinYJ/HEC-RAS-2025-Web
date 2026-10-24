using System;
using System.Numerics;
using Geospatial.Transformers;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

public static class IResampleExtensionMethods
{
	public static IRaster<T> AsRaster<T>(this IResample<T> resample, RasterDefinition rasterDef) where T : unmanaged, IEquatable<T>
	{
		return new VirtualRaster<T>(resample, rasterDef);
	}

	public static IRaster<T> AsRaster<T>(this IResample<T> resample, double cellSize) where T : unmanaged, IEquatable<T>
	{
		RasterDefinition rdef = new RasterDefinition(resample.ValidExtent, cellSize);
		return new VirtualRaster<T>(resample, rdef);
	}

	public static ITileProvider<T> AsTileProvider<T>(this IResample<T> resampler, double cellSize, int tileSize = 256, SamplingParams? sampleParams = null) where T : unmanaged, IEquatable<T>
	{
		return new VirtualTileProvider<T>(resampler, cellSize, tileSize, sampleParams);
	}

	public static ITileProvider<T> AsTileProvider<T>(this IRaster<T> raster, int tileSize = 256, SamplingParams? sampleParams = null) where T : unmanaged, IEquatable<T>
	{
		return new VirtualTileProvider<T>(raster, raster.RasterDefinition.CellSize, tileSize, sampleParams);
	}

	public static T SamplePoint<T>(this IResample<T> res, Point p) where T : unmanaged, IEquatable<T>
	{
		if (res == null)
		{
			throw new ArgumentNullException("res");
		}
		Point[] points = new Point[1] { p };
		T[] values = new T[1];
		res.SamplePoints(points, values, SamplingParams.Default);
		return values[0];
	}

	public static T SamplePoint<T>(this IResample<T> res, Point p, SamplingParams sampleParams) where T : unmanaged, IEquatable<T>
	{
		if (res == null)
		{
			throw new ArgumentNullException("res");
		}
		Point[] points = new Point[1] { p };
		T[] values = new T[1];
		res.SamplePoints(points, values, sampleParams);
		return values[0];
	}

	public static T SamplePoint<T>(this IResample<T> res, double x, double y) where T : unmanaged, IEquatable<T>
	{
		Point[] points = new Point[1]
		{
			new Point(x, y)
		};
		T[] values = new T[1];
		res.SamplePoints(points, values, SamplingParams.Default);
		return values[0];
	}

	public static T[] SamplePoints<T>(this IResample<T> res, Memory<Point> points) where T : unmanaged, IEquatable<T>
	{
		T[] values = new T[points.Length];
		res.SamplePoints(points, values, SamplingParams.Default);
		return values;
	}

	public static T[] Resample<T>(this IResample<T> resampler, ResampleArgs args) where T : unmanaged, IEquatable<T>
	{
		T[] values = new T[args.Raster.CellCount];
		resampler.Resample(args, values);
		return values;
	}

	public static T[] Resample<T>(this IResample<T> resampler, RasterDefinition raster) where T : unmanaged, IEquatable<T>
	{
		return resampler.Resample(new ResampleArgs(raster));
	}

	public static IResample<T> WithTransformer<T>(this IResample<T> resampler, ISpatialTransformer transformer) where T : unmanaged, IEquatable<T>
	{
		return new TransformedResampler<T>(resampler, transformer);
	}

	public static IResample<T> ReplaceNodata<T>(this IResample<T> resampler, T newNoDataValue) where T : unmanaged, IEquatable<T>
	{
		if (resampler.NoDataValue.HasValue && resampler.NoDataValue.Equals(newNoDataValue))
		{
			return resampler;
		}
		return new ReplaceNoDataResampler<T>(resampler, newNoDataValue);
	}

	public static IRaster<T> ReplaceNodataRaster<T>(this IRaster<T> resampler, T newNoDataValue) where T : unmanaged, IEquatable<T>
	{
		if (resampler.NoDataValue.HasValue && resampler.NoDataValue.Equals(newNoDataValue))
		{
			return resampler;
		}
		return new ReplaceNoDataRaster<T>(resampler, newNoDataValue);
	}

	public static IResample<T> ClipTo<T>(this IResample<T> resampler, Extent ext) where T : unmanaged, IEquatable<T>
	{
		return new ClippedResampler<T>(resampler, ext);
	}

	public static IResample<T> WithTransformer<T>(this IResample<T> resampler, IValueTransformer<T> transformer) where T : unmanaged, IEquatable<T>
	{
		return new ValueTransformedResampler<T>(resampler, transformer);
	}

	public static IRaster<T> WithValueTransformer<T>(this IRaster<T> raster, IValueTransformer<T> transformer) where T : unmanaged, IEquatable<T>
	{
		return new ValueTransformedRasterizer<T>(raster, transformer);
	}

	public static IRaster<T> WithValueTransformer<T>(this IRaster<T> raster, RoundTo rounding) where T : unmanaged, IBinaryFloatingPointIeee754<T>
	{
		if (rounding == RoundTo.None)
		{
			return raster;
		}
		return new ValueTransformedRasterizer<T>(raster, RoundValueTransformer<T>.FromRoundTo(rounding));
	}

	public static IResample<TOut> ConvertType<TIn, TOut>(this IResample<TIn> resampler, Action<Memory<TIn>, Memory<TOut>> converterAction, TOut? noDataValue) where TIn : unmanaged, INumber<TIn> where TOut : unmanaged, INumber<TOut>
	{
		return new ConvertTypeResampler<TIn, TOut>(resampler, converterAction, noDataValue);
	}

	public static IRaster<TOut> ConvertType<TIn, TOut>(this IRaster<TIn> resampler, Action<Memory<TIn>, Memory<TOut>> converterAction, TOut? noDataValue) where TIn : unmanaged, INumber<TIn> where TOut : unmanaged, INumber<TOut>
	{
		return new ConvertTypeRasterizer<TIn, TOut>(resampler, converterAction, noDataValue);
	}

	public static IResample<float> InterpolateWith(this IResample<float> ts, IResample<float> other, float ratioFromThisToThat)
	{
		return new InterpolatedResampler(ts, other, ratioFromThisToThat);
	}
}
