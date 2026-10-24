using System;
using System.Buffers;
using System.Collections.Generic;
using Geospatial.PairedData;
using Geospatial.Transformers;
using Geospatial.Vectors;
using Utility.Memory;
using Utility.Units;

namespace Geospatial.Rasters;

internal class TransformedIAccumulatedResampler : IAccumulatedResampler<float>, ISurface<float>, IDiscreteTimesteps
{
	private readonly IAccumulatedResampler<float> _resampler;

	protected readonly ISpatialTransformer _transformer;

	protected readonly ISpatialTransformer _reversed;

	public Extent ValidExtent => _resampler.ValidExtent;

	public float? NoDataValue => _resampler.NoDataValue;

	public IReadOnlyList<DateTime> Times => _resampler.Times;

	public bool HasDiscreteTimesteps => _resampler.HasDiscreteTimesteps;

	public TimeSeriesDataType NativeDataType => _resampler.NativeDataType;

	public Time TimeDenominator => _resampler.TimeDenominator;

	public TransformedIAccumulatedResampler(IAccumulatedResampler<float> resampler, ISpatialTransformer transformer)
	{
		_resampler = resampler;
		_transformer = transformer;
		_reversed = transformer.GetInvertedTransformer();
	}

	public void ResampleAccumulated(ResampleArgs args, DateTime start, DateTime end, Memory<float> outData)
	{
		ResampleArgs resampleArgs = args.Transform(_reversed);
		int cellCount = resampleArgs.Raster.CellCount;
		using IMemoryOwner<float> memoryOwner = SharedMemoryPool<float>.Rent(cellCount);
		Memory<float> slice = memoryOwner.Memory.Slice(0, cellCount);
		_resampler.ResampleAccumulated(resampleArgs, start, end, slice);
		RasterOperations.Warp(resampleArgs.Raster, slice, args.Raster, outData, _reversed);
	}

	public void ResampleRates(ResampleArgs args, DateTime time, Memory<float> outData)
	{
		ResampleArgs resampleArgs = args.Transform(_reversed);
		int cellCount = resampleArgs.Raster.CellCount;
		using IMemoryOwner<float> memoryOwner = SharedMemoryPool<float>.Rent(cellCount);
		Memory<float> slice = memoryOwner.Memory.Slice(0, cellCount);
		_resampler.ResampleRates(resampleArgs, time, slice);
		RasterOperations.Warp(resampleArgs.Raster, slice, args.Raster, outData, _reversed);
	}

	public RateTimeSeries SampleRateTimeSeries(Point pt)
	{
		pt = _reversed.Transform(pt);
		return _resampler.SampleRateTimeSeries(pt);
	}
}
