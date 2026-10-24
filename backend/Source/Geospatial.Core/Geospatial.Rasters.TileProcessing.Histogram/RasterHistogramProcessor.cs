using System;
using Utility.Progress;

namespace Geospatial.Rasters.TileProcessing.Histogram;

public sealed class RasterHistogramProcessor : ITileProcessor<float, HistogramTileState, RasterHistogram>
{
	private readonly float _min;

	private readonly float _binSize;

	private readonly int _binCount;

	private readonly float _noDataValue;

	public ITileStateSerializer<HistogramTileState> Serializer { get; }

	public RasterHistogramProcessor(float min, float binSize, int binCount, float noDataValue = -9999f)
	{
		if (binSize <= 0f)
		{
			throw new ArgumentOutOfRangeException("binSize");
		}
		if (binCount < 1)
		{
			throw new ArgumentOutOfRangeException("binCount");
		}
		_min = min;
		_binSize = binSize;
		_binCount = binCount;
		_noDataValue = noDataValue;
		Serializer = new HistogramTileStateSerializer(binCount);
	}

	public HistogramTileState CreateState(long tileIndex, RasterDefinition tileDefinition)
	{
		return new HistogramTileState(_binCount);
	}

	public void Accumulate(in TileAccumulateContext<float, HistogramTileState> context)
	{
		HistogramTileState state = context.State;
		ReadOnlySpan<float> floatSpan = context.Center.Data.Span;
		long ordinal = 0L;
		float observedMin = state.ObservedMin;
		float observedMax = state.ObservedMax;
		ReadOnlySpan<float> floatSpan2 = floatSpan;
		for (int i = 0; i < floatSpan2.Length; i++)
		{
			float computed = floatSpan2[i];
			if (computed == _noDataValue || float.IsNaN(computed))
			{
				ordinal++;
				continue;
			}
			int binCountTerm = (int)MathF.Floor((computed - _min) / _binSize);
			binCountTerm = Math.Clamp(binCountTerm, 0, _binCount - 1);
			state.Counts[binCountTerm]++;
			if (computed < observedMin)
			{
				observedMin = computed;
			}
			if (computed > observedMax)
			{
				observedMax = computed;
			}
		}
		state.NoDataCount += ordinal;
		state.ObservedMin = observedMin;
		state.ObservedMax = observedMax;
	}

	public RasterHistogram Reduce(ITileStateStore<HistogramTileState> states, ITiledRasterDefinition tiling)
	{
		long[] longs = new long[_binCount];
		long noDataCountSum = 0L;
		float positiveInfinity = float.PositiveInfinity;
		float negativeInfinity = float.NegativeInfinity;
		foreach (long tileIndex in states.TileIndices)
		{
			if (states.TryPeek(tileIndex, out var state))
			{
				for (int i = 0; i < _binCount; i++)
				{
					longs[i] += state.Counts[i];
				}
				noDataCountSum += state.NoDataCount;
				if (state.ObservedMin < positiveInfinity)
				{
					positiveInfinity = state.ObservedMin;
				}
				if (state.ObservedMax > negativeInfinity)
				{
					negativeInfinity = state.ObservedMax;
				}
			}
		}
		bool notIsPositiveInfinity = !float.IsPositiveInfinity(positiveInfinity);
		return new RasterHistogram(_min, _binSize, longs, noDataCountSum, notIsPositiveInfinity ? positiveInfinity : float.NaN, notIsPositiveInfinity ? negativeInfinity : float.NaN);
	}

	public static RasterHistogram Compute(ITileProvider<float> source, float min, float binSize, int binCount, float noDataValue = -9999f, TileProcessingOptions options = null, ProgressReporter progress = null)
	{
		RasterHistogramProcessor processor = new RasterHistogramProcessor(min, binSize, binCount, noDataValue);
		return TileProcessingPipeline.Run(source, processor, options, progress);
	}

	void ITileProcessor<float, HistogramTileState, RasterHistogram>.Accumulate(in TileAccumulateContext<float, HistogramTileState> context)
	{
		Accumulate(in context);
	}
}
