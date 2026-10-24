using System;
using System.Collections.Generic;
using Geospatial.PairedData;
using Geospatial.Vectors;

namespace Geospatial.Rasterizers;

public class SimpleAccumulatedResampler : GaugedAccumulatedResampler
{
	public SimpleAccumulatedResampler(IList<AccumulatedGauge> gauges, ISpatialInterpolator interpolator)
		: base(gauges, interpolator)
	{
	}

	public override RateTimeSeries SampleRateTimeSeries(Point pt)
	{
		SpatialInterpolator.Query(pt, out var indexes, out var weights);
		if (indexes.Length == 0)
		{
			return NoDataTimeSeries();
		}
		List<double> applyPointToPreviousSegmentIds = new List<double>(Times.Count);
		foreach (DateTime time in Times)
		{
			double sampleRateTimesWeightSum = 0.0;
			for (int i = 0; i < indexes.Length; i++)
			{
				double weight = weights[i];
				if (weight != 0.0)
				{
					int index = indexes[i];
					double sampleRate = Gauges[index].TimeSeries.SampleRate(time, TimeDenominator);
					if (sampleRate != -9999.0)
					{
						sampleRateTimesWeightSum += sampleRate * weight;
					}
				}
			}
			applyPointToPreviousSegmentIds.Add(sampleRateTimesWeightSum);
		}
		return new RateTimeSeries(Times, applyPointToPreviousSegmentIds, TimeDenominator, ProfileInterpolationMethod.ApplyPointToPreviousSegment);
	}
}
