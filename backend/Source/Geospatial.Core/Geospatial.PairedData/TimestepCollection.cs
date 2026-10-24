using System;
using System.Collections.Generic;
using System.Linq;
using Utility;
using Utility.DataStructures;
using Utility.Units;

namespace Geospatial.PairedData;

public class TimestepCollection
{
	private readonly IList<long> _dtTicks;

	public IReadOnlyList<DateTime> Times { get; }

	public int Count => _dtTicks.Count;

	public DateTime StartTime => new DateTime(_dtTicks.FirstOrDefault());

	public DateTime EndTime => new DateTime(_dtTicks.LastOrDefault());

	public ProfileInterpolationMethod InterpolationMethod { get; set; } = ProfileInterpolationMethod.ApplyPointToPreviousSegment;

	public ExtrapolationBehavior ExtrapolationBehavior { get; private set; }

	public TimeSeriesDataType TimeSeriesDataType { get; set; }

	public Time RateTimeDenominator { get; set; } = Time.Hours;

	public TimestepCollection(IEnumerable<DateTime> times, TimeSeriesDataType dt)
		: this(times.Select((DateTime t) => t.Ticks).ToList(), dt)
	{
	}

	public TimestepCollection(IList<long> ticks, TimeSeriesDataType dt)
	{
		_dtTicks = ticks;
		TimeSeriesDataType = dt;
		Times = new TranslatedList<long, DateTime>(_dtTicks, (long lng) => new DateTime(lng));
	}

	public void GetRateWeights(DateTime dt, out TsWeight weight1, out TsWeight weight2)
	{
		long ticks = dt.Ticks;
		if (ticks < _dtTicks.First() || ticks > _dtTicks.Last())
		{
			weight1 = TsWeight.NullWeight;
			weight2 = TsWeight.NullWeight;
			if (ExtrapolationBehavior == ExtrapolationBehavior.ReturnNoData || ExtrapolationBehavior == ExtrapolationBehavior.ReturnZero)
			{
				return;
			}
			if (ExtrapolationBehavior == ExtrapolationBehavior.NearestValue)
			{
				if (ticks < _dtTicks.First())
				{
					weight1 = new TsWeight(0, 1.0);
				}
				else
				{
					weight1 = new TsWeight(Count - 1, 1.0);
				}
				throw new NotImplementedException();
			}
			if (ExtrapolationBehavior == ExtrapolationBehavior.ExtrapolateNearestSegment)
			{
				throw new NotImplementedException();
			}
			if (ExtrapolationBehavior == ExtrapolationBehavior.ThrowException)
			{
				throw new ArgumentException("DateTime was out of range.");
			}
			throw new NotImplementedException();
		}
		int binarySearchTime = BinarySearchTime(ticks);
		long ticks2 = RateTimeDenominator.Ticks;
		if (TimeSeriesDataType == TimeSeriesDataType.Amount)
		{
			if (binarySearchTime == 0)
			{
				weight1 = TsWeight.NullWeight;
				weight2 = TsWeight.NullWeight;
				return;
			}
			if (binarySearchTime < 0)
			{
				binarySearchTime = ~binarySearchTime;
			}
			double dtTickStepOverTicks2 = (double)(_dtTicks[binarySearchTime] - _dtTicks[binarySearchTime - 1]) / (double)ticks2;
			weight1 = new TsWeight(binarySearchTime - 1, -1.0 / dtTickStepOverTicks2);
			weight2 = new TsWeight(binarySearchTime, 1.0 / dtTickStepOverTicks2);
			return;
		}
		if (TimeSeriesDataType == TimeSeriesDataType.Rate)
		{
			if (InterpolationMethod != ProfileInterpolationMethod.ApplyPointToPreviousSegment)
			{
				throw new NotImplementedException();
			}
			if (binarySearchTime < 0)
			{
				binarySearchTime = ~binarySearchTime;
			}
			weight1 = new TsWeight(binarySearchTime, 1.0);
			weight2 = TsWeight.NullWeight;
			return;
		}
		if (TimeSeriesDataType == TimeSeriesDataType.Delta)
		{
			if (binarySearchTime == 0)
			{
				weight1 = TsWeight.NullWeight;
				weight2 = TsWeight.NullWeight;
				return;
			}
			if (binarySearchTime < 0)
			{
				binarySearchTime = ~binarySearchTime;
			}
			double dtTickStepOverTicks22 = (double)(_dtTicks[binarySearchTime] - _dtTicks[binarySearchTime - 1]) / (double)ticks2;
			weight1 = new TsWeight(binarySearchTime, 1.0 / dtTickStepOverTicks22);
			weight2 = TsWeight.NullWeight;
			return;
		}
		throw new NotImplementedException();
	}

	private static void AddCombineWeights(List<TsWeight> weights, TsWeight newWeight)
	{
		if (weights.Count == 0)
		{
			weights.Add(newWeight);
			return;
		}
		TsWeight tsWeight = weights[weights.Count - 1];
		if (tsWeight.Index == newWeight.Index)
		{
			double weightDifference = tsWeight.Weight + newWeight.Weight;
			if (weightDifference == 0.0)
			{
				weights.RemoveAt(weights.Count - 1);
			}
			else
			{
				weights[weights.Count - 1] = new TsWeight(tsWeight.Index, weightDifference);
			}
		}
		else
		{
			weights.Add(newWeight);
		}
	}

	public List<TsWeight> GetAccumulationWeights(DateTime startDt, DateTime endDt)
	{
		List<TsWeight> tsWeights = new List<TsWeight>();
		long ticks2 = startDt.Ticks;
		long ticks3 = endDt.Ticks;
		long ticks22 = _dtTicks.First();
		long ticks32 = _dtTicks.Last();
		if (ticks2 < ticks22)
		{
			ticks2 = ticks22;
		}
		if (ticks3 > ticks32)
		{
			ticks3 = ticks32;
		}
		if (ticks3 <= ticks22 || ticks2 >= ticks32)
		{
			if (ExtrapolationBehavior != ExtrapolationBehavior.ReturnNoData && ExtrapolationBehavior != ExtrapolationBehavior.ReturnZero)
			{
				if (ExtrapolationBehavior == ExtrapolationBehavior.NearestValue)
				{
					throw new NotImplementedException();
				}
				if (ExtrapolationBehavior == ExtrapolationBehavior.ExtrapolateNearestSegment)
				{
					throw new NotImplementedException();
				}
				if (ExtrapolationBehavior == ExtrapolationBehavior.ThrowException)
				{
					throw new ArgumentException("DateTime was out of range.");
				}
				throw new NotImplementedException();
			}
			return tsWeights;
		}
		int binarySearchTime = BinarySearchTime(ticks2);
		int binarySearchTime2 = BinarySearchTime(ticks3);
		int binarySearchTime3 = binarySearchTime;
		if (binarySearchTime3 < 0)
		{
			binarySearchTime3 = ~binarySearchTime;
		}
		int binarySearchTime4 = binarySearchTime2;
		if (binarySearchTime4 < 0)
		{
			binarySearchTime4 = ~binarySearchTime2 - 1;
		}
		long ticks = RateTimeDenominator.Ticks;
		if (TimeSeriesDataType == TimeSeriesDataType.Amount)
		{
			if (binarySearchTime < 0)
			{
				long dtTickStep = _dtTicks[binarySearchTime3] - _dtTicks[binarySearchTime3 - 1];
				double ticks2DtTicksTerm = ((double)ticks2 - (double)_dtTicks[binarySearchTime3 - 1]) / (double)dtTickStep;
				double computed = 1.0 - ticks2DtTicksTerm;
				tsWeights.Add(new TsWeight(binarySearchTime3 - 1, -1.0 * computed));
				tsWeights.Add(new TsWeight(binarySearchTime3, computed));
			}
			AddCombineWeights(tsWeights, new TsWeight(binarySearchTime3, -1.0));
			AddCombineWeights(tsWeights, new TsWeight(binarySearchTime4, 1.0));
			if (binarySearchTime2 < 0)
			{
				long dtTickStep2 = _dtTicks[binarySearchTime4 + 1] - _dtTicks[binarySearchTime4];
				double ticks3DtTicksTerm = ((double)ticks3 - (double)_dtTicks[binarySearchTime4]) / (double)dtTickStep2;
				AddCombineWeights(tsWeights, new TsWeight(binarySearchTime4, 0.0 - ticks3DtTicksTerm));
				AddCombineWeights(tsWeights, new TsWeight(binarySearchTime4 + 1, ticks3DtTicksTerm));
			}
		}
		else if (TimeSeriesDataType == TimeSeriesDataType.Rate)
		{
			if (InterpolationMethod != ProfileInterpolationMethod.ApplyPointToPreviousSegment)
			{
				throw new NotImplementedException();
			}
			if (binarySearchTime < 0)
			{
				long dtTicksBinarySearchTime3Term = _dtTicks[binarySearchTime3] - ticks2;
				tsWeights.Add(new TsWeight(binarySearchTime3, (double)dtTicksBinarySearchTime3Term / (double)ticks));
			}
			for (int i = binarySearchTime3 + 1; i <= binarySearchTime4; i++)
			{
				long dtTickStep3 = _dtTicks[i] - _dtTicks[i - 1];
				tsWeights.Add(new TsWeight(i, (double)dtTickStep3 / (double)ticks));
			}
			if (binarySearchTime2 < 0)
			{
				long ticks3DtTicksTerm2 = ticks3 - _dtTicks[binarySearchTime4];
				AddCombineWeights(tsWeights, new TsWeight(binarySearchTime4 + 1, (double)ticks3DtTicksTerm2 / (double)ticks));
			}
		}
		else
		{
			if (TimeSeriesDataType != TimeSeriesDataType.Delta)
			{
				throw new NotImplementedException();
			}
			if (binarySearchTime < 0)
			{
				long dtTicksBinarySearchTime3Term2 = _dtTicks[binarySearchTime3] - ticks2;
				long dtTickStep4 = _dtTicks[binarySearchTime3] - _dtTicks[binarySearchTime3 - 1];
				tsWeights.Add(new TsWeight(binarySearchTime3, (double)dtTicksBinarySearchTime3Term2 / (double)dtTickStep4));
			}
			for (int j = binarySearchTime3 + 1; j <= binarySearchTime4; j++)
			{
				tsWeights.Add(new TsWeight(j, 1.0));
			}
			if (binarySearchTime2 < 0)
			{
				long ticks3DtTicksTerm3 = ticks3 - _dtTicks[binarySearchTime4];
				long dtTickStep5 = _dtTicks[binarySearchTime4 + 1] - _dtTicks[binarySearchTime4];
				tsWeights.Add(new TsWeight(binarySearchTime4 + 1, (double)ticks3DtTicksTerm3 / (double)dtTickStep5));
			}
		}
		return tsWeights;
	}

	protected int BinarySearchTime(DateTime dt)
	{
		return MathHelpers.BinarySearch(_dtTicks, dt.Ticks);
	}

	protected int BinarySearchTime(long ticks)
	{
		return MathHelpers.BinarySearch(_dtTicks, ticks);
	}
}
