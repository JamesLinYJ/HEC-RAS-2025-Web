using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Utility;
using Utility.DataStructures;
using Utility.Memory;

namespace Geospatial.Rasters;

public class RasterTimeSeries : MultiBandRaster<float>, ITimeSeriesRaster<float>, ITimeSeriesResampler<float>, IMultiBandResampler<float>, ISurface<float>, IDiscreteTimesteps, IMultiBandRaster<float>, IRasterDefinition
{
	public IReadOnlyList<DateTime> Times { get; }

	public DateTime Start => Times.FirstOrDefault();

	public DateTime End => Times.LastOrDefault();

	public bool CanInterpolateTimesteps { get; set; } = true;

	public RasterTimeSeries(RasterDefinition raster, IList<DateTime> timesteps, IList<Any<float[]>> data)
		: base(raster, data, (float?)null)
	{
		if (timesteps == null || timesteps.Count != data.Count)
		{
			throw new ArgumentException("Timesteps disagree with data provided.");
		}
		Times = timesteps.AsReadOnly();
	}

	public void Resample(ResampleArgs args, DateTime time, Memory<float> outData, ResampleCache cache = null)
	{
		if (!TimestepRatios(time, out var aIdx, out var aRatio, out var bIdx, out var bRatio))
		{
			args.EnsureBuffer(outData, NoDataValue);
			return;
		}
		if (aIdx == -1)
		{
			float[] bIdxTerm = _data[bIdx].Value;
			RasterOperations.Resample(RasterDefinition, bIdxTerm, args, outData);
			return;
		}
		if (bIdx == -1)
		{
			float[] aIdxTerm = _data[aIdx].Value;
			RasterOperations.Resample(RasterDefinition, aIdxTerm, args, outData);
			return;
		}
		if (!CanInterpolateTimesteps)
		{
			args.EnsureBuffer(outData, NoDataValue);
			return;
		}
		float[] aIdxTermArray = _data[aIdx].Value;
		float[] bIdxTermArray = _data[bIdx].Value;
		ResampleArgs args2 = args.CloneWithoutBufferClear();
		int cellCount = args.Raster.CellCount;
		using IMemoryOwner<float> memoryOwner = SharedMemoryPool<float>.Rent(cellCount);
		Memory<float> slice = memoryOwner.Memory.Slice(0, cellCount);
		using IMemoryOwner<float> memoryOwner2 = SharedMemoryPool<float>.Rent(cellCount);
		Memory<float> sliceSpan = memoryOwner2.Memory.Slice(0, cellCount);
		RasterOperations.Resample(RasterDefinition, aIdxTermArray, args2, slice);
		RasterOperations.Resample(RasterDefinition, bIdxTermArray, args2, sliceSpan);
		args.EnsureBuffer(outData, NoDataValue);
		RasterOperations.Interpolate(args.Raster, slice, aRatio, sliceSpan, bRatio, outData);
	}

	protected bool TimestepRatios(DateTime time, out int aIdx, out float aRatio, out int bIdx, out float bRatio)
	{
		if (time < Start || time > End)
		{
			aIdx = -1;
			aRatio = 0f;
			bIdx = -1;
			bRatio = 0f;
			return false;
		}
		int aIdx2 = MathHelpers.BinarySearch(Times, time);
		if (aIdx2 >= 0)
		{
			aIdx = aIdx2;
			aRatio = 1f;
			bIdx = -1;
			bRatio = 0f;
			return true;
		}
		int bIdx2 = ~aIdx2;
		aIdx = bIdx2 - 1;
		bIdx = bIdx2;
		DateTime dateTime = Times[aIdx];
		DateTime dateTime2 = Times[bIdx];
		long ticksDifference = time.Ticks - dateTime.Ticks;
		long ticksDifference2 = dateTime2.Ticks - dateTime.Ticks;
		aRatio = 1f - (bRatio = (float)((double)ticksDifference / (double)ticksDifference2));
		return true;
	}

	public bool Clip(ref DateTime start, ref DateTime end)
	{
		if (start < Start)
		{
			start = Start;
		}
		if (end > End)
		{
			end = End;
		}
		if (end <= start)
		{
			return false;
		}
		return true;
	}

	public TimeSeries SamplePointTimeSeries(Point pt)
	{
		float[] source = this.SamplePoint(pt);
		return new TimeSeries(Times, ((IEnumerable<float>)source).Select((Func<float, double>)((float f) => f)));
	}
}
