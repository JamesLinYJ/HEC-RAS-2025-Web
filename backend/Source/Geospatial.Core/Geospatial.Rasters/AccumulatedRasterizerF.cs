using System;
using System.Collections.Generic;
using System.Threading;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Utility;
using Utility.Units;

namespace Geospatial.Rasters;

internal class AccumulatedRasterizerF : IAccumulatedRasterizer<float>, IAccumulatedResampler<float>, ISurface<float>, IDiscreteTimesteps, IRasterDefinition
{
	protected const int ParallelLoopSize = 256;

	protected ThreadLocal<float[]> _rasterSizedBuffer;

	public RasterDefinition RasterDefinition { get; }

	public Extent ValidExtent => RasterDefinition.Extent;

	public IAccumulatedResampler<float> Accumulator { get; }

	public RateTimeSeries[] CellTimeSeriesRates { get; private set; }

	public float? NoDataValue => -9999f;

	public IReadOnlyList<DateTime> Times => Accumulator.Times;

	public bool HasDiscreteTimesteps => true;

	public TimeSeriesDataType NativeDataType => Accumulator.NativeDataType;

	public Time TimeDenominator => Accumulator.TimeDenominator;

	public AccumulatedRasterizerF(IAccumulatedResampler<float> accum, RasterDefinition def)
	{
		RasterDefinition = def;
		Accumulator = accum;
		_rasterSizedBuffer = new ThreadLocal<float[]>(() => new float[RasterDefinition.CellCount]);
		InitializePixelTimeSeries();
	}

	private void InitializePixelTimeSeries()
	{
		CellTimeSeriesRates = new RateTimeSeries[RasterDefinition.CellCount];
		Parallel.SmartFor(RasterDefinition.CellCount, (int start, int end) =>
		{
			for (int i = start; i <= end; i++)
			{
				Point pt = RasterDefinition.CellCenter(i);
				CellTimeSeriesRates[i] = Accumulator.SampleRateTimeSeries(pt);
			}
		}, 256);
	}

	public void ResampleAccumulated(ResampleArgs args, DateTime start, DateTime end, Memory<float> outData)
	{
		args.EnsureBuffer(outData, NoDataValue);
		ResampleAccumulatedInternal(args, start, end, outData);
	}

	protected void ResampleAccumulatedInternal(ResampleArgs args, DateTime startDt, DateTime endDt, Memory<float> outData)
	{
		float[] buf = _rasterSizedBuffer.Value;
		Parallel.SmartFor(RasterDefinition.CellCount, (int start, int end) =>
		{
			for (int i = start; i <= end; i++)
			{
				RateTimeSeries rateTimeSeries = CellTimeSeriesRates[i];
				buf[i] = (float)rateTimeSeries.SampleAccumulatedDuring(startDt, endDt);
			}
		}, 256);
		ResampleArgs args2 = args.CloneWithoutBufferClear();
		RasterOperations.Resample(RasterDefinition, buf, args2, outData);
	}

	public RateTimeSeries SampleRateTimeSeries(Point pt)
	{
		return Accumulator.SampleRateTimeSeries(pt);
	}

	public void ResampleRates(ResampleArgs args, DateTime dt, Memory<float> outData)
	{
		float[] buf = _rasterSizedBuffer.Value;
		Parallel.SmartFor(RasterDefinition.CellCount, (int start, int end) =>
		{
			for (int i = start; i <= end; i++)
			{
				RateTimeSeries rateTimeSeries = CellTimeSeriesRates[i];
				buf[i] = (float)rateTimeSeries.SampleRate(dt, Time.Hours);
			}
		}, 256);
		ResampleArgs args2 = args.CloneWithoutBufferClear();
		RasterOperations.Resample(RasterDefinition, buf, args2, outData);
	}
}
