using System.Collections.Generic;
using Geospatial.PairedData;
using Geospatial.Vectors;

namespace Geospatial.Rasterizers;

public class VolumeInterpolationAccumulatedResampler : GaugedAccumulatedResampler
{
	private readonly HashSet<int> _dryGauges;

	public VolumeInterpolationAccumulatedResampler(IList<AccumulatedGauge> gauges, ISpatialInterpolator volumeInterpolator)
		: base(gauges, volumeInterpolator)
	{
		_dryGauges = new HashSet<int>();
		for (int i = 0; i < GaugeCount; i++)
		{
			if (Gauges[i].TimeSeries.Behavior.AllZero)
			{
				_dryGauges.Add(i);
			}
		}
	}

	public override RateTimeSeries SampleRateTimeSeries(Point pt)
	{
		SpatialInterpolator.Query(pt, out var indexes, out var weights);
		if (indexes.Length == 0)
		{
			return NoDataTimeSeries();
		}
		int nearestWetGauge = NearestWetGauge(pt);
		if (nearestWetGauge == -1)
		{
			return NoDataTimeSeries();
		}
		double computed = 0.0;
		for (int i = 0; i < indexes.Length; i++)
		{
			double weight = weights[i];
			if (weight != 0.0)
			{
				computed += Gauges[indexes[i]].TimeSeries.TotalAccumulated() * weight;
			}
		}
		if (computed == 0.0)
		{
			return NoDataTimeSeries();
		}
		PeriodAverageTimeSeries periodAverageTimeSeries = Gauges[nearestWetGauge].TimeSeries.ResampleToPeriodAverage(Times, TimeDenominator);
		double totalAccumulated = periodAverageTimeSeries.TotalAccumulated();
		double scaleFactor = computed / totalAccumulated;
		periodAverageTimeSeries.Scale(scaleFactor);
		return periodAverageTimeSeries;
	}

	private int NearestWetGauge(Point queryPoint)
	{
		double maxValue = double.MaxValue;
		int result = -1;
		for (int i = 0; i < GaugeCount; i++)
		{
			if (!_dryGauges.Contains(i))
			{
				double distanceTo = Gauges[i].Location.DistanceTo(queryPoint);
				if (distanceTo < maxValue)
				{
					maxValue = distanceTo;
					result = i;
				}
			}
		}
		return result;
	}
}
