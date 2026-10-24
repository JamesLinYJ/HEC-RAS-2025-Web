using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.PairedData;
using Geospatial.Vectors;

namespace Geospatial.Rasterizers;

public class McCormackAccumulatedResampler : GaugedAccumulatedResampler
{
	private List<RateTimeSeries> _alignedGauges;

	private List<TimeSpan> _alignedTimeshifts;

	private List<DateTime> _alignedInputTimes;

	private List<DateTime> _gaugeCenterOfMass;

	private DateTime _baselineCoM;

	private List<Tuple<TimeSpan, TimeSpan>> _gaugeStdDevs;

	public TimeShiftMethod TimeShiftMethod { get; set; }

	public McCormackAccumulatedResampler(IList<AccumulatedGauge> gauges, ISpatialInterpolator interpolator, TimeShiftMethod tsMethod = TimeShiftMethod.McCormack, Extent? limitExtent = null)
		: base(gauges, interpolator)
	{
		TimeShiftMethod = tsMethod;
		if (limitExtent.HasValue)
		{
			ValidExtent = limitExtent.Value;
		}
		else
		{
			ValidExtent = Extent.Infinite;
		}
		if (interpolator != null)
		{
			SpatialInterpolator = interpolator;
		}
		else
		{
			SpatialInterpolator = new InverseDistanceInterpolator();
		}
		InitializeAlignedGauges();
	}

	private void InitializeAlignedGauges()
	{
		_alignedGauges = new List<RateTimeSeries>(Gauges.Count);
		_alignedTimeshifts = new List<TimeSpan>();
		_gaugeCenterOfMass = new List<DateTime>();
		_gaugeStdDevs = new List<Tuple<TimeSpan, TimeSpan>>();
		List<IAccumulatedTimeSeries> iAccumulatedTimeSeries = new List<IAccumulatedTimeSeries>(Gauges.Count);
		_baselineCoM = Gauges.First().TimeSeries.CenterOfMass();
		for (int i = 0; i < Gauges.Count; i++)
		{
			IAccumulatedTimeSeries timeSeries = Gauges[i].TimeSeries;
			DateTime dateTime = timeSeries.CenterOfMass();
			_gaugeCenterOfMass.Add(dateTime);
			TimeSpan timeSpan = _baselineCoM - dateTime;
			_alignedTimeshifts.Add(timeSpan);
			IAccumulatedTimeSeries accumulatedTimeSeries = timeSeries.Clone();
			accumulatedTimeSeries.Shift(timeSpan);
			iAccumulatedTimeSeries.Add(accumulatedTimeSeries);
			timeSeries.ComputeStandardDeviation(out var leftStdDev, out var rightStdDev);
			_gaugeStdDevs.Add(new Tuple<TimeSpan, TimeSpan>(leftStdDev, rightStdDev));
		}
		_alignedInputTimes = (from dt in iAccumulatedTimeSeries.SelectMany((IAccumulatedTimeSeries g) => g.Times)
			orderby dt
			select dt).Distinct().ToList();
		_alignedInputTimes = TimeSeriesResampleMethods.Filter(_alignedInputTimes, TimeSpanFilterTolerance);
		for (int intervalIndex = 0; intervalIndex < iAccumulatedTimeSeries.Count; intervalIndex++)
		{
			_alignedGauges.Add(iAccumulatedTimeSeries[intervalIndex].ResampleToPeriodAverage(_alignedInputTimes, TimeDenominator));
		}
	}

	public override RateTimeSeries SampleRateTimeSeries(Point pt)
	{
		if (TimeShiftMethod == TimeShiftMethod.McCormack)
		{
			return SamplePoint_McCormackShift(pt);
		}
		if (TimeShiftMethod == TimeShiftMethod.AdvancedMcCormack)
		{
			return SamplePoint_AdvancedMcCormackShift(pt);
		}
		throw new NotImplementedException();
	}

	protected virtual RateTimeSeries SamplePoint_McCormackShift(Point pt)
	{
		SpatialInterpolator.Query(pt, out var indexes, out var weights);
		if (indexes.Length == 0)
		{
			return NoDataTimeSeries();
		}
		RateTimeSeries rateTimeSeries = new RateTimeSeries(TimeDenominator, ProfileInterpolationMethod.ApplyPointToPreviousSegment);
		for (int i = 0; i < _alignedInputTimes.Count; i++)
		{
			double roundSamplesToTerm = 0.0;
			for (int j = 0; j < indexes.Length; j++)
			{
				double weight = weights[j];
				if (weight != 0.0)
				{
					int index = indexes[j];
					roundSamplesToTerm += _alignedGauges[index].Values[i] * weight;
				}
			}
			if (RoundSamplesTo >= 0)
			{
				roundSamplesToTerm = Math.Round(roundSamplesToTerm, RoundSamplesTo);
			}
			rateTimeSeries.Add(_alignedInputTimes[i], roundSamplesToTerm);
		}
		double computed = 0.0;
		RebalanceWeightsForTemporal(ref indexes, ref weights);
		for (int k = 0; k < indexes.Length; k++)
		{
			int index2 = indexes[k];
			computed += (_gaugeCenterOfMass[index2] - _baselineCoM).TotalHours * weights[k];
		}
		DateTime dt = _baselineCoM + TimeSpan.FromHours(computed);
		rateTimeSeries.ShiftCenterOfMassTo(dt);
		return rateTimeSeries;
	}

	protected virtual RateTimeSeries SamplePoint_AdvancedMcCormackShift(Point pt)
	{
		SpatialInterpolator.Query(pt, out var indexes, out var weights);
		if (indexes.Length == 0)
		{
			return NoDataTimeSeries();
		}
		RateTimeSeries rateTimeSeries = new RateTimeSeries(TimeDenominator, ProfileInterpolationMethod.ApplyPointToPreviousSegment);
		for (int i = 0; i < _alignedInputTimes.Count; i++)
		{
			double roundSamplesToTerm = 0.0;
			for (int j = 0; j < indexes.Length; j++)
			{
				double weight = weights[j];
				if (weight != 0.0)
				{
					int index = indexes[j];
					roundSamplesToTerm += _alignedGauges[index].Values[i] * weight;
				}
			}
			if (RoundSamplesTo >= 0)
			{
				roundSamplesToTerm = Math.Round(roundSamplesToTerm, RoundSamplesTo);
			}
			rateTimeSeries.Add(_alignedInputTimes[i], roundSamplesToTerm);
		}
		RebalanceWeightsForTemporal(ref indexes, ref weights);
		if (indexes == null || indexes.Length == 0)
		{
			return rateTimeSeries;
		}
		double computed = 0.0;
		double leftTargetHours = 0.0;
		double rightTargetHours = 0.0;
		for (int k = 0; k < indexes.Length; k++)
		{
			int index2 = indexes[k];
			computed += (_gaugeCenterOfMass[index2] - _baselineCoM).TotalHours * weights[k];
			leftTargetHours += _gaugeStdDevs[index2].Item1.TotalHours * weights[k];
			rightTargetHours += _gaugeStdDevs[index2].Item2.TotalHours * weights[k];
		}
		DateTime newCoM = _baselineCoM + TimeSpan.FromHours(computed);
		rateTimeSeries.ShiftCenterOfMassTo(newCoM);
		rateTimeSeries.ComputeStandardDeviation(out var leftStdDev, out var rightStdDev);
		double leftBaseHours = Math.Abs(leftStdDev.TotalHours);
		double rightBaseHours = Math.Abs(rightStdDev.TotalHours);
		if (leftBaseHours == 0.0 || rightBaseHours == 0.0)
		{
			return rateTimeSeries;
		}
		rateTimeSeries.Shift((DateTime baseDt) =>
		{
			if (baseDt == newCoM)
			{
				return baseDt;
			}
			double totalHours = (baseDt - newCoM).TotalHours;
			if (totalHours < 0.0)
			{
				double totalHoursLeftBaseHoursTerm = (0.0 - totalHours) / leftBaseHours * leftTargetHours;
				return newCoM - TimeSpan.FromHours(totalHoursLeftBaseHoursTerm);
			}
			double totalHoursRightBaseHoursTerm = totalHours / rightBaseHours * rightTargetHours;
			return newCoM + TimeSpan.FromHours(totalHoursRightBaseHoursTerm);
		});
		return rateTimeSeries;
	}

	private void RebalanceWeightsForTemporal(ref int[] indexes, ref double[] weights)
	{
		double weightSum = 0.0;
		int count = 0;
		for (int i = 0; i < indexes.Length; i++)
		{
			int index = indexes[i];
			if (!(_gaugeCenterOfMass[index] == Gauges[index].TimeSeries.StartTime))
			{
				weightSum += weights[i];
				count++;
			}
		}
		if (count == indexes.Length)
		{
			return;
		}
		if (count == 0 || weightSum == 0.0)
		{
			indexes = Array.Empty<int>();
			weights = Array.Empty<double>();
			return;
		}
		int[] indexesArray = new int[count];
		double[] weightsArray = new double[count];
		int ordinal = 0;
		for (int j = 0; j < indexes.Length; j++)
		{
			int index2 = indexes[j];
			if (!(_gaugeCenterOfMass[index2] == Gauges[index2].TimeSeries.StartTime))
			{
				indexesArray[ordinal] = indexes[j];
				weightsArray[ordinal] = weights[j] / weightSum;
				ordinal++;
			}
		}
		indexes = indexesArray;
		weights = weightsArray;
	}
}
