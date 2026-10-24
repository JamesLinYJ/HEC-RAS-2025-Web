using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.PairedData;
using Geospatial.Vectors;

namespace Geospatial.Rasterizers;

public class HydrographMorphologyAccumulatedResampler : GaugedAccumulatedResampler
{
	public List<List<DateTime>> MappedGaugeInflectionPoints { get; private set; }

	public int InterpolationPointCount => (MappedGaugeInflectionPoints?.FirstOrDefault()?.Count).GetValueOrDefault();

	public HydrographMorphologyAccumulatedResampler(List<AccumulatedGauge> gauges, List<List<DateTime>> gaugeInflectionPoints, ISpatialInterpolator interpolator)
		: base(gauges, interpolator)
	{
		Initialize(gaugeInflectionPoints);
	}

	public HydrographMorphologyAccumulatedResampler(List<AccumulatedGauge> gauges, List<double> massInflectionFractions, ISpatialInterpolator interpolator)
		: base(gauges, interpolator)
	{
		Initialize(massInflectionFractions);
	}

	private void Initialize(List<double> massInflectionFractions)
	{
		List<List<DateTime>> dateTimeListList = new List<List<DateTime>>();
		if (massInflectionFractions != null && massInflectionFractions.Count != 0)
		{
			massInflectionFractions.Sort();
			for (int i = 0; i < GaugeCount; i++)
			{
				List<DateTime> dateTimes = new List<DateTime>();
				dateTimeListList.Add(dateTimes);
				foreach (double massInflectionFraction in massInflectionFractions)
				{
					if (massInflectionFraction < 0.0 || massInflectionFraction > 1.0)
					{
						throw new ArgumentOutOfRangeException("Mass Inflection Ratio can't be less than zero or greater than one.");
					}
					dateTimes.Add(Gauges[i].TimeSeries.TimeToAccumulateRatio(massInflectionFraction));
				}
			}
		}
		Initialize(dateTimeListList);
	}

	private void Initialize(List<List<DateTime>> gaugeInflectionPoints)
	{
		MappedGaugeInflectionPoints = new List<List<DateTime>>(Gauges.Count);
		for (int i = 0; i < Gauges.Count; i++)
		{
			List<DateTime> dateTimes = new List<DateTime>();
			dateTimes.Add(Gauges[i].TimeSeries.StartTime);
			MappedGaugeInflectionPoints.Add(dateTimes);
		}
		if (gaugeInflectionPoints != null && gaugeInflectionPoints.Count != 0)
		{
			if (gaugeInflectionPoints.Count != Gauges.Count)
			{
				throw new ArgumentException("Input gauge inflection point list needs to map 1:1 with input gauges.");
			}
			int count = gaugeInflectionPoints.First().Count;
			for (int j = 0; j < gaugeInflectionPoints.Count; j++)
			{
				List<DateTime> gaugeInflectionPoint = gaugeInflectionPoints[j];
				if (count != gaugeInflectionPoint.Count)
				{
					throw new ArgumentException("Each gauge needs the same number of inflection points, so we can map them to eachother.");
				}
				MappedGaugeInflectionPoints[j].AddRange(gaugeInflectionPoint);
			}
		}
		for (int k = 0; k < Gauges.Count; k++)
		{
			MappedGaugeInflectionPoints[k].Add(Gauges[k].TimeSeries.EndTime);
		}
		ValidExtent = SpatialInterpolator.ValidExtent;
	}

	public override RateTimeSeries SampleRateTimeSeries(Point pt)
	{
		SpatialInterpolator.Query(pt, out var indexes, out var weights);
		if (indexes.Length == 0)
		{
			return NoDataTimeSeries();
		}
		RebalanceWeightsForTemporal(indexes, weights, out var temporalWeights);
		RateTimeSeries rateTimeSeries = new RateTimeSeries(TimeDenominator, ProfileInterpolationMethod.ApplyPointToPreviousSegment);
		for (int rgnIdx = 0; rgnIdx < InterpolationPointCount - 1; rgnIdx++)
		{
			List<Tuple<DateTime, double>> finalValues = new List<Tuple<DateTime, double>>();
			for (int i = 0; i < indexes.Length; i++)
			{
				int baseIdx = indexes[i];
				double baseWeight = weights[i];
				AccumulatedGauge accumulatedGauge = Gauges[baseIdx];
				if (accumulatedGauge.TimeSeries.Behavior.AllZero)
				{
					continue;
				}
				DateTime baseRgnStart = MappedGaugeInflectionPoints[baseIdx][rgnIdx];
				DateTime dateTime = MappedGaugeInflectionPoints[baseIdx][rgnIdx + 1];
				double baseRgnHours = (dateTime - baseRgnStart).TotalHours;
				Action<DateTime, double> action = (DateTime baseTime, double baseRate) =>
				{
					double totalHoursOverBaseRgnHours = (baseTime - baseRgnStart).TotalHours / baseRgnHours;
					double baseRateTimesBaseWeight = baseRate * baseWeight;
					double totalHoursTimesTemporalWeightSum = 0.0;
					for (int j = 0; j < indexes.Length; j++)
					{
						int index = indexes[j];
						double weight = weights[j];
						double temporalWeight = temporalWeights[j];
						if (index != baseIdx && temporalWeight > 0.0)
						{
							AccumulatedGauge accumulatedGauge2 = Gauges[index];
							DateTime dateTime3 = MappedGaugeInflectionPoints[index][rgnIdx];
							DateTime dateTime4 = MappedGaugeInflectionPoints[index][rgnIdx + 1];
							DateTime minValue = DateTime.MinValue;
							if (totalHoursOverBaseRgnHours == 0.0)
							{
								minValue = dateTime3;
							}
							else if (totalHoursOverBaseRgnHours == 1.0)
							{
								minValue = dateTime4;
							}
							else
							{
								double totalHoursDateTime4Term = (dateTime4 - dateTime3).TotalHours * totalHoursOverBaseRgnHours;
								minValue = dateTime3.AddHours(totalHoursDateTime4Term);
							}
							totalHoursTimesTemporalWeightSum += (minValue - baseTime).TotalHours * temporalWeight;
							double sampleRate = accumulatedGauge2.TimeSeries.SampleRate(minValue, TimeDenominator);
							if (sampleRate != -9999.0)
							{
								baseRateTimesBaseWeight += sampleRate * weight;
							}
						}
					}
					finalValues.Add(new Tuple<DateTime, double>(baseTime.AddHours(totalHoursTimesTemporalWeightSum), baseRateTimesBaseWeight));
				};
				double sampleRate2 = accumulatedGauge.TimeSeries.SampleRate(baseRgnStart, TimeDenominator);
				double sampleRate3 = accumulatedGauge.TimeSeries.SampleRate(dateTime, TimeDenominator);
				action(baseRgnStart, sampleRate2);
				action(dateTime, sampleRate3);
				int binarySearchNextLargerIndex = TimeSeriesResampleMethods.BinarySearchNextLargerIndex(accumulatedGauge.TimeSeries.Times, baseRgnStart);
				int binarySearchNextSmallerIndex = TimeSeriesResampleMethods.BinarySearchNextSmallerIndex(accumulatedGauge.TimeSeries.Times, dateTime);
				for (int binarySearchNextLargerIndex2 = binarySearchNextLargerIndex; binarySearchNextLargerIndex2 <= binarySearchNextSmallerIndex; binarySearchNextLargerIndex2++)
				{
					DateTime time = accumulatedGauge.TimeSeries.Times[binarySearchNextLargerIndex2];
					double timeSeriesAccumulatedGaugeTerm = accumulatedGauge.TimeSeries.Values[binarySearchNextLargerIndex2];
					action(time, timeSeriesAccumulatedGaugeTerm);
				}
			}
			DateTime dateTime2 = DateTime.MinValue;
			if (rateTimeSeries.Times.Count > 0)
			{
				dateTime2 = rateTimeSeries.Times.Last();
			}
			foreach (Tuple<DateTime, double> orderBy in finalValues.OrderBy((Tuple<DateTime, double> t) => t.Item1))
			{
				DateTime dateTime22 = orderBy.Item1;
				if (!(dateTime22 - dateTime2 < TimeSpanFilterTolerance))
				{
					rateTimeSeries.Add(orderBy.Item1, orderBy.Item2);
					dateTime2 = dateTime22;
				}
			}
		}
		return rateTimeSeries;
	}

	private void RebalanceWeightsForTemporal(int[] indexes, double[] weights, out double[] newWeights)
	{
		double weightSum = 0.0;
		int ordinal = 0;
		for (int i = 0; i < indexes.Length; i++)
		{
			int index = indexes[i];
			if (!Gauges[index].TimeSeries.Behavior.AllZero)
			{
				weightSum += weights[i];
				ordinal++;
			}
		}
		if (ordinal == indexes.Length)
		{
			newWeights = weights;
			return;
		}
		if (ordinal == 0 || weightSum == 0.0)
		{
			newWeights = new double[indexes.Length];
			return;
		}
		newWeights = new double[indexes.Length];
		for (int j = 0; j < indexes.Length; j++)
		{
			int index2 = indexes[j];
			if (!Gauges[index2].TimeSeries.Behavior.AllZero)
			{
				newWeights[j] = weights[j] / weightSum;
			}
		}
	}
}
