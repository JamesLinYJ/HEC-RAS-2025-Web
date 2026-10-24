using System;
using System.Numerics;
using Geospatial.PairedData;
using Geospatial.Transformers;
using Utility;
using Utility.Units;

namespace Geospatial.Rasters;

public static class IAccumulatedRasterizerExtensions
{
	public static T[] ResampleTotalAccumulated<T>(this IAccumulatedRasterizer<T> ts) where T : unmanaged, IEquatable<T>
	{
		ResampleArgs args = new ResampleArgs(ts.RasterDefinition);
		return ts.ResampleAccumulated(args, DateTime.MinValue, DateTime.MaxValue);
	}

	public static void ResampleAccumulated<T>(this IAccumulatedRasterizer<T> ts, int startProfile, int endProfile, Memory<T> outData) where T : unmanaged, IEquatable<T>
	{
		ResampleArgs args = new ResampleArgs(ts.RasterDefinition);
		ts.ResampleAccumulated(args, startProfile, endProfile, outData);
	}

	public static void ResampleRates<T>(this IAccumulatedRasterizer<T> ts, int profile, Memory<T> outData) where T : unmanaged, IEquatable<T>
	{
		if (!ts.HasDiscreteTimesteps)
		{
			throw new ArgumentException("Provided IAccumulatedRasterizer does not have discrete timesteps.");
		}
		ResampleArgs args = new ResampleArgs(ts.RasterDefinition);
		ts.ResampleRates(args, ts.Times[profile], outData);
	}

	public static ITimeSeriesRaster<T> AsRateResampler<T>(this IAccumulatedRasterizer<T> accum) where T : unmanaged, IEquatable<T>
	{
		return new AccumulatedAsRateRasterizer<T>(accum);
	}

	public static double Aggregate(this IAccumulatedRasterizer<float> ts, DateTime start, DateTime end)
	{
		return ts.Aggregate(ts.RasterDefinition, start, end);
	}

	public static double Aggregate(this IAccumulatedRasterizer<float> ts)
	{
		return ts.Aggregate(DateTime.MinValue, DateTime.MaxValue);
	}

	public static void SampleAsDataType<T>(this IAccumulatedRasterizer<T> rasterizer, TimeSeriesDataType desiredWriteType, Action<int, Memory<T>> bandAction, ParallelizationIntent intent = ParallelizationIntent.Serial) where T : unmanaged, IEquatable<T>
	{
		if (!rasterizer.HasDiscreteTimesteps)
		{
			throw new NotSupportedException("rasterizer must have discrete timesteps.");
		}
		if (rasterizer.NativeDataType == TimeSeriesDataType.Amount)
		{
			if (desiredWriteType == TimeSeriesDataType.Delta)
			{
				if (typeof(T) == typeof(float))
				{
					SampleAsDataType_AmountToDelta(rasterizer as IAccumulatedRasterizer<float>, (int i, Memory<float> f) =>
					{
						bandAction(i, f as T[]);
					});
					return;
				}
				if (typeof(T) == typeof(double))
				{
					SampleAsDataType_AmountToDelta(rasterizer as IAccumulatedRasterizer<double>, (int i, Memory<double> d) =>
					{
						bandAction(i, d as T[]);
					});
					return;
				}
			}
		}
		else if (rasterizer.NativeDataType == TimeSeriesDataType.Delta && desiredWriteType == TimeSeriesDataType.Amount)
		{
			if (typeof(T) == typeof(float))
			{
				SampleAsDataType_DeltaToAmount(rasterizer as IAccumulatedRasterizer<float>, (int i, Memory<float> f) =>
				{
					bandAction(i, f as T[]);
				});
				return;
			}
			if (typeof(T) == typeof(double))
			{
				SampleAsDataType_DeltaToAmount(rasterizer as IAccumulatedRasterizer<double>, (int i, Memory<double> d) =>
				{
					bandAction(i, d as T[]);
				});
				return;
			}
		}
		int count = rasterizer.Times.Count;
		T[] a = new T[rasterizer.RasterDefinition.CellCount];
		for (int ordinal = 0; ordinal < count; ordinal++)
		{
			if (ordinal == 0 && (desiredWriteType == TimeSeriesDataType.Amount || desiredWriteType == TimeSeriesDataType.Delta))
			{
				Buffers.ArraySetAll(ref a, default, rasterizer.RasterDefinition.CellCount);
				bandAction(0, a);
				continue;
			}
			switch (desiredWriteType)
			{
			case TimeSeriesDataType.Amount:
				rasterizer.ResampleAccumulated(0, ordinal, a);
				break;
			case TimeSeriesDataType.Delta:
				rasterizer.ResampleAccumulated(ordinal - 1, ordinal, a);
				break;
			case TimeSeriesDataType.Rate:
				rasterizer.ResampleRates(ordinal, a);
				break;
			default:
				throw new NotImplementedException();
			}
			bandAction(ordinal, a);
		}
	}

	private static void SampleAsDataType_AmountToDelta<T>(IAccumulatedRasterizer<T> rasterizer, Action<int, Memory<T>> bandAction) where T : unmanaged, INumber<T>
	{
		int count = rasterizer.Times.Count;
		int cellCount = rasterizer.RasterDefinition.CellCount;
		T? noDataValue = rasterizer.NoDataValue;
		T[] values = new T[cellCount];
		T[] valuesArray = new T[cellCount];
		T[] values2 = new T[cellCount];
		for (int i = 0; i < count; i++)
		{
			if (i == 0)
			{
				bandAction(0, values);
				continue;
			}
			rasterizer.ResampleAccumulated(0, i, valuesArray);
			if (noDataValue.HasValue)
			{
				Buffers.SubtractVector(valuesArray, values2, values, noDataValue.Value);
			}
			else
			{
				Buffers.SubtractVector(valuesArray, values2, values);
			}
			bandAction(i, values);
			T[] values3 = valuesArray;
			valuesArray = values2;
			values2 = values3;
		}
	}

	private static void SampleAsDataType_DeltaToAmount<T>(IAccumulatedRasterizer<T> rasterizer, Action<int, Memory<T>> bandAction) where T : unmanaged, INumber<T>
	{
		int count = rasterizer.Times.Count;
		int cellCount = rasterizer.RasterDefinition.CellCount;
		T? noDataValue = rasterizer.NoDataValue;
		T[] values = new T[cellCount];
		T[] valuesArray = new T[cellCount];
		for (int i = 0; i < count; i++)
		{
			if (i == 0)
			{
				bandAction(0, valuesArray);
				continue;
			}
			rasterizer.ResampleAccumulated(i - 1, i, values);
			if (noDataValue.HasValue)
			{
				Buffers.ReplaceValue(values, noDataValue.Value, T.Zero);
			}
			Buffers.AddVector(values, valuesArray, valuesArray);
			bandAction(i, valuesArray);
		}
	}

	public static IAccumulatedRasterizer<float> WithValueTransformer_Raster(this IAccumulatedRasterizer<float> ts, IValueTransformer<float> transformer, Time newTimeUnits)
	{
		return new AccumulatedRasterizerValueTransformed(ts, transformer.Transform, newTimeUnits);
	}
}
