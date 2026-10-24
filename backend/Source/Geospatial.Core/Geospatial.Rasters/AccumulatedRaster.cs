using System;
using System.Buffers;
using System.Collections.Generic;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Utility;
using Utility.DataStructures;
using Utility.Memory;
using Utility.Units;

namespace Geospatial.Rasters;

public class AccumulatedRaster : RasterTimeSeries, IAccumulatedRasterizer<float>, IAccumulatedResampler<float>, ISurface<float>, IDiscreteTimesteps, IRasterDefinition
{
	public Time TimeDenominator { get; }

	public bool HasDiscreteTimesteps => true;

	public TimeSeriesDataType NativeDataType => TimeSeriesDataType.Rate;

	public AccumulatedRaster(RasterDefinition raster, IList<DateTime> timesteps, IList<Any<float[]>> rates, Time rateTimeDenominator)
		: base(raster, timesteps, rates)
	{
		TimeDenominator = rateTimeDenominator;
	}

	public static AccumulatedRaster FromPeriodCumulativeData(RasterDefinition raster, IList<DateTime> timesteps, IList<Any<float[]>> perCumData, Time desiredTimeUnits)
	{
		Lazy<float[]> lazyNoData = new Lazy<float[]>(() =>
		{
			float[] floatValues = new float[raster.CellCount];
			floatValues.AsSpan().Fill(-9999f);
			return floatValues;
		});
		JITList<Any<float[]>> rates = new JITList<Any<float[]>>(perCumData.Count, shouldCache: true, (int i) =>
		{
			if (i < 0)
			{
				throw new ArgumentOutOfRangeException();
			}
			if (i == 0)
			{
				return lazyNoData.Value;
			}
			float[] buf = perCumData[i].Value;
			double ticks = desiredTimeUnits.Ticks;
			DateTime dateTime = timesteps[i - 1];
			DateTime dateTime2 = timesteps[i];
			double deltaTime = (double)(dateTime2 - dateTime).Ticks / ticks;
			float[] retn = new float[buf.Length];
			Parallel.SmartFor(buf.Length, (int start, int end) =>
			{
				for (int j = start; j <= end; j++)
				{
					float buf2 = buf[j];
					if (buf2 == -9999f)
					{
						retn[j] = buf2;
					}
					else
					{
						retn[j] = (float)((double)buf2 / deltaTime);
					}
				}
			});
			return retn;
		});
		return new AccumulatedRaster(raster, timesteps, rates, desiredTimeUnits);
	}

	public void ResampleAccumulated(ResampleArgs args, DateTime start, DateTime end, Memory<float> outData)
	{
		if (!Clip(ref start, ref end))
		{
			args.EnsureBuffer(outData, -9999f);
			return;
		}
		using IMemoryOwner<float> memoryOwner = SharedMemoryPool<float>.Rent(RasterDefinition.CellCount, 0f);
		Memory<float> slice = memoryOwner.Memory.Slice(0, RasterDefinition.CellCount);
		TimestepRatios(start, out var aIdx, out var aRatio, out var bIdx, out var _);
		TimestepRatios(end, out var aIdx2, out var _, out var bIdx2, out var bRatio2);
		int bIdxTerm;
		float aRatioTerm;
		if (bIdx != -1)
		{
			bIdxTerm = bIdx;
			aRatioTerm = aRatio;
		}
		else
		{
			bIdxTerm = aIdx + 1;
			aRatioTerm = 1f;
		}
		int count;
		float bRatio2Term;
		if (bIdx2 != -1)
		{
			count = bIdx2;
			bRatio2Term = bRatio2;
		}
		else
		{
			count = aIdx2;
			bRatio2Term = 1f;
		}
		if (bIdxTerm == count)
		{
			TimeSpan timeSpan = Times[bIdxTerm] - Times[bIdxTerm - 1];
			aRatioTerm = (float)((end - start).TotalHours / timeSpan.TotalHours);
			bRatio2Term = aRatioTerm;
		}
		double ticks = TimeDenominator.Ticks;
		for (int i = bIdxTerm; i <= count; i++)
		{
			float[] band = _data[i].Value;
			DateTime dateTime = Times[i - 1];
			DateTime dateTime2 = Times[i];
			double deltaTime = (double)(dateTime2 - dateTime).Ticks / ticks;
			if (i == bIdxTerm)
			{
				deltaTime *= aRatioTerm;
			}
			else if (i == count)
			{
				deltaTime *= bRatio2Term;
			}
			Parallel.SmartFor(RasterDefinition.CellCount, (int pxStart, int pxEnd) =>
			{
				Span<float> sliceSpan = slice.Span;
				for (int j = pxStart; j <= pxEnd; j++)
				{
					float band2 = band[j];
					if (band2 != -9999f)
					{
						sliceSpan[j] += (float)((double)band2 * deltaTime);
					}
				}
			});
		}
		ResampleArgs args2 = args.CloneWithoutBufferClear();
		RasterOperations.Resample(RasterDefinition, slice, args2, outData);
	}

	public void ResampleRates(ResampleArgs args, DateTime dt, Memory<float> outData)
	{
		Resample(args, dt, outData);
	}

	public RateTimeSeries SampleRateTimeSeries(Point pt)
	{
		float[] samplePoint = this.SamplePoint(pt);
		double[] applyPointToPreviousSegmentIds = new double[Times.Count];
		for (int i = 0; i < Times.Count; i++)
		{
			applyPointToPreviousSegmentIds[i] = samplePoint[i];
		}
		return new RateTimeSeries(Times, applyPointToPreviousSegmentIds, TimeDenominator, ProfileInterpolationMethod.ApplyPointToPreviousSegment);
	}
}
