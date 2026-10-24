using System;
using System.Collections.Generic;
using Geospatial.PairedData;
using Geospatial.Vectors;

namespace Geospatial.Rasterizers;

public class SimpleTimeSeriesResampler : GaugedTimeSeriesResampler
{
	public SimpleTimeSeriesResampler(IList<TimeSeriesGauge> gauges, ISpatialInterpolator interpolator)
		: base(gauges, interpolator)
	{
	}

	public override TimeSeries SamplePointTimeSeries(Point pt)
	{
		SpatialInterpolator.Query(pt, out var indexes, out var weights);
		if (indexes.Length == 0)
		{
			return NoDataTimeSeries();
		}
		List<double> doubleList = new List<double>(Times.Count);
		foreach (DateTime time in Times)
		{
			double sampleTimesWeightSum = 0.0;
			for (int i = 0; i < indexes.Length; i++)
			{
				double weight = weights[i];
				if (weight != 0.0)
				{
					int index = indexes[i];
					double sample = Gauges[index].TimeSeries.Sample(time);
					if (sample != -9999.0)
					{
						sampleTimesWeightSum += sample * weight;
					}
				}
			}
			doubleList.Add(sampleTimesWeightSum);
		}
		return new TimeSeries(Times, doubleList);
	}
}
