using System;
using Geospatial.Transformers;
using Geospatial.Vectors;
using Geospatial.Vectors.Rasterizers;
using Utility.Units;

namespace Geospatial.Rasters;

public static class IAccumulatedResamplerExtensions
{
	public static T[] ResampleTotalAccumulated<T>(this IAccumulatedResampler<T> ts, ResampleArgs args) where T : unmanaged, IEquatable<T>
	{
		return ts.ResampleAccumulated(args, DateTime.MinValue, DateTime.MaxValue);
	}

	public static T[] ResampleAccumulated<T>(this IAccumulatedResampler<T> ts, ResampleArgs args, DateTime start, DateTime end) where T : unmanaged, IEquatable<T>
	{
		T[] values = new T[args.Raster.CellCount];
		ts.ResampleAccumulated(args, start, end, values);
		return values;
	}

	public static T[] ResampleAccumulated<T>(this IAccumulatedResampler<T> ts, ResampleArgs args, int startProfile, int endProfile) where T : unmanaged, IEquatable<T>
	{
		T[] values = new T[args.Raster.CellCount];
		ts.ResampleAccumulated(args, startProfile, endProfile, values);
		return values;
	}

	public static void ResampleAccumulated<T>(this IAccumulatedResampler<T> ts, ResampleArgs args, int startProfile, int endProfile, Memory<T> outData) where T : unmanaged, IEquatable<T>
	{
		if (!ts.HasDiscreteTimesteps)
		{
			throw new ArgumentException("Provided IAccumulatedResampler does not have discrete timesteps.");
		}
		ts.ResampleAccumulated(args, ts.Times[startProfile], ts.Times[endProfile], outData);
	}

	public static double Aggregate(this IAccumulatedResampler<float> ts, RasterDefinition resampleRaster, DateTime start, DateTime end)
	{
		ResampleArgs args = new ResampleArgs(resampleRaster);
		float[] resampleAccumulated = ts.ResampleAccumulated(args, start, end);
		double cellArea = resampleRaster.CellArea;
		double computed = 0.0;
		for (int i = 0; i < resampleAccumulated.Length; i++)
		{
			computed += (double)resampleAccumulated[i] * cellArea;
		}
		return computed;
	}

	public static double Aggregate(this IAccumulatedResampler<float> ts, Polygon pgon, double resampleCellSize, DateTime start, DateTime end)
	{
		Extent extent = pgon.Extent;
		RasterDefinition rasterDefinition = new RasterDefinition(extent, resampleCellSize);
		ResampleArgs args = new ResampleArgs(rasterDefinition);
		float[] buf = ts.ResampleAccumulated(args, start, end);
		double area = rasterDefinition.CellArea;
		double sum = 0.0;
		new PolygonRasterizer(rasterDefinition).Rasterize(pgon, (int i) =>
		{
			sum += (double)buf[i] * area;
		});
		return sum;
	}

	public static IAccumulatedResampler<float> WithTransformer(this IAccumulatedResampler<float> ts, ISpatialTransformer transformer)
	{
		return new TransformedIAccumulatedResampler(ts, transformer);
	}

	public static IAccumulatedRasterizer<float> AsAccumulatedRasterizer(this IAccumulatedResampler<float> ts, RasterDefinition def)
	{
		return new AccumulatedRasterizerF(ts, def);
	}

	public static IAccumulatedResampler<float> WithValueTransformer(this IAccumulatedResampler<float> ts, IValueTransformer<float> transformer, Time newTimeUnits)
	{
		return new AccumulatedResamplerValueTransformed(ts, transformer.Transform, newTimeUnits);
	}
}
