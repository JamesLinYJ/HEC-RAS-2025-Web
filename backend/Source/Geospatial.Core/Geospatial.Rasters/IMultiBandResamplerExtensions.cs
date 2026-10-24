using System;
using System.Collections.Generic;
using Geospatial.Transformers;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

public static class IMultiBandResamplerExtensions
{
	public static T[] Resample<T>(this IMultiBandResampler<T> resampler, ResampleArgs args, int profileIdx) where T : unmanaged, IEquatable<T>
	{
		T[] values = new T[args.Raster.CellCount];
		resampler.Resample(args, profileIdx, values);
		return values;
	}

	public static void SamplePoints_NaiiveLoop<T>(this IMultiBandResampler<T> res, Memory<Point> points, int start, int count, Memory<T>[] outData) where T : unmanaged, IEquatable<T>
	{
		for (int i = 0; i < count; i++)
		{
			res.SamplePoints(points, i + start, outData[i]);
		}
	}

	public static void Resample_NaiiveLoop<T>(this IMultiBandResampler<T> res, ResampleArgs args, int start, int count, Memory<T>[] outData) where T : unmanaged, IEquatable<T>
	{
		using ResampleCache cache = new ResampleCache();
		for (int i = 0; i < count; i++)
		{
			res.Resample(args, i + start, outData[i], cache);
		}
	}

	public static T[] SamplePoint<T>(this IMultiBandResampler<T> ts, Point point) where T : unmanaged, IEquatable<T>
	{
		T[] values = new T[ts.ProfileCount];
		Memory<T> valuesSpan = new Memory<T>(values);
		Memory<T>[] bufferArray = new Memory<T>[ts.ProfileCount];
		for (int i = 0; i < ts.ProfileCount; i++)
		{
			bufferArray[i] = valuesSpan.Slice(i, 1);
		}
		ts.SamplePoints(new Point[1] { point }, 0, ts.ProfileCount, bufferArray);
		return values;
	}

	public static T[] SamplePoints<T>(this IMultiBandResampler<T> ts, Memory<Point> points, int bandIndex) where T : unmanaged, IEquatable<T>
	{
		T[] values = new T[points.Length];
		ts.SamplePoints(points, bandIndex, values);
		return values;
	}

	public static T SamplePoint<T>(this IMultiBandResampler<T> ts, Point point, int profileIdx) where T : unmanaged, IEquatable<T>
	{
		T[] values = new T[1];
		ts.SamplePoints(new Point[1] { point }, profileIdx, values);
		return values[0];
	}

	public static IResample<T> AsSingleProfile<T>(this IMultiBandResampler<T> rts, int profileIdx) where T : unmanaged, IEquatable<T>
	{
		return new PinnedProfileResampler<T>(rts, profileIdx);
	}

	public static IRaster<T> AsSingleProfile<T>(this IMultiBandRaster<T> rts, int profileIdx) where T : unmanaged, IEquatable<T>
	{
		return new PinnedProfileRasterizer<T>(rts, profileIdx);
	}

	public static IMultiBandRaster<T> AsBandedRaster<T>(this IMultiBandResampler<T> resample, RasterDefinition rasterDef) where T : unmanaged, IEquatable<T>
	{
		return new VirtualBandedRaster<T>(resample, rasterDef);
	}

	public static IMultiBandRaster<T> AsBandedRaster<T>(this IMultiBandResampler<T> resample, double cellSize) where T : unmanaged, IEquatable<T>
	{
		RasterDefinition rdef = new RasterDefinition(resample.ValidExtent, cellSize);
		return new VirtualBandedRaster<T>(resample, rdef);
	}

	public static ITimeSeriesResampler<T> AsTimeSeriesResampler<T>(this IMultiBandResampler<T> resampler, IList<DateTime> timesteps) where T : unmanaged, IEquatable<T>
	{
		return new VirtualTimeSeriesResampler<T>(resampler, timesteps);
	}

	public static ITimeSeriesRaster<T> AsTimeSeriesRaster<T>(this IMultiBandResampler<T> resampler, IList<DateTime> timesteps, RasterDefinition raster) where T : unmanaged, IEquatable<T>
	{
		return new TimeSeriesRasterizer<T>(new VirtualTimeSeriesResampler<T>(resampler, timesteps), raster);
	}

	public static IMultiBandResampler<T> WithTransformer<T>(this IMultiBandResampler<T> baseRes, ISpatialTransformer transformer) where T : unmanaged, IEquatable<T>
	{
		return new TransformedBandedResampler<T>(baseRes, transformer);
	}

	public static IMultiBandRaster<T> WithRasterTransformer<T>(this IMultiBandRaster<T> baseRes, ISpatialTransformer transformer) where T : unmanaged, IEquatable<T>
	{
		return new TransformedBandedRasterizer<T>(baseRes, transformer);
	}

	public static IMultiBandRaster<T> AsClipped<T>(this IMultiBandRaster<T> baseRes, Extent ext) where T : unmanaged, IEquatable<T>
	{
		return new ClippedBandedRaster<T>(baseRes, baseRes.RasterDefinition.GetPixelRange(ext, PixelRangeOptions.CenterPixelCovered));
	}

	public static IMultiBandRaster<T> AsClipped<T>(this IMultiBandRaster<T> baseRes, PixelRange pr) where T : unmanaged, IEquatable<T>
	{
		return new ClippedBandedRaster<T>(baseRes, pr);
	}

	public static IMultiBandRaster<T> AsClippedToPolygon<T>(this IMultiBandRaster<T> baseRes, Polygon clipPolygon) where T : unmanaged, IEquatable<T>
	{
		return new PolygonClippedBandedRaster<T>(baseRes, clipPolygon);
	}

	public static IMultiBandResampler<T> ComposeWith<T>(this IMultiBandResampler<T> baseRes, IResample<T> res, ConverterDelegates.Convert<T> composeInPlace) where T : unmanaged, IEquatable<T>
	{
		return new ComposedMultiBandResampler<T>(baseRes, res, composeInPlace);
	}

	public static IMultiBandResampler<T> ComposeWith<T>(this IMultiBandResampler<T> baseRes, IMultiBandResampler<T> altResampler, ConverterDelegates.Convert<T> composeInPlace) where T : unmanaged, IEquatable<T>
	{
		return new ComposedMultiBandResampler<T>(baseRes, altResampler, composeInPlace);
	}

	public static ITimeSeriesResampler<T> ComposeWith<T>(this ITimeSeriesResampler<T> baseRes, ITimeSeriesResampler<T> altResampler, ConverterDelegates.Convert<T> composeInPlace) where T : unmanaged, IEquatable<T>
	{
		return new ComposedTimeSeriesResampler<T>(baseRes, altResampler, composeInPlace);
	}
}
