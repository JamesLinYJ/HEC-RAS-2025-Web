using System;
using System.Collections.Generic;
using System.Linq;

namespace Utility;

public class Stats
{
	private double _min;

	private double _max;

	private long _computedAgainstCountValid = -1L;

	private long _computedAgainstCountTotal = -1L;

	private double _mean;

	private double _stdDev;

	private double _fractionNoData;

	private long _countValid;

	private long _countTotal;

	private double _sumNormalized;

	private double _sqSumNormalized;

	private double _sqSum;

	private readonly double _normalizeSubtrahend;

	public double Min => _min;

	public double Max => _max;

	public double Mean
	{
		get
		{
			if (CountValid == 0L)
			{
				return double.NaN;
			}
			ComputeStatsIfNecessary();
			return _mean;
		}
	}

	public double StdDev
	{
		get
		{
			if (CountValid == 0L)
			{
				return double.NaN;
			}
			ComputeStatsIfNecessary();
			return _stdDev;
		}
	}

	public double FractionNoData
	{
		get
		{
			if (CountTotal == 0L)
			{
				return double.NaN;
			}
			ComputeStatsIfNecessary();
			return _fractionNoData;
		}
	}

	public long CountValid => _countValid;

	public long CountTotal => _countTotal;

	public double Sum => _sumNormalized + (double)CountValid * _normalizeSubtrahend;

	public double NoData { get; private set; }

	private Stats(double min, double max, double mean, double stdDev, float fracNoData, long countValid, long countTotal, double sum, double sqSumNormalized, double sqSum, double noData, double normalizeSubtrahend = 0.0)
	{
		NoData = noData;
		_min = min;
		_max = max;
		_mean = mean;
		_stdDev = stdDev;
		_fractionNoData = fracNoData;
		_countValid = countValid;
		_countTotal = countTotal;
		_sumNormalized = sum;
		_sqSumNormalized = sqSumNormalized;
		_sqSum = sqSum;
		_normalizeSubtrahend = normalizeSubtrahend;
	}

	public Stats(Stats other)
	{
		NoData = other.NoData;
		_min = other._min;
		_max = other._max;
		_mean = other._mean;
		_stdDev = other._stdDev;
		_countValid = other._countValid;
		_countTotal = other._countTotal;
		_fractionNoData = other._fractionNoData;
		_sumNormalized = other._sumNormalized;
		_sqSumNormalized = other._sqSumNormalized;
		_sqSum = other._sqSum;
		_normalizeSubtrahend = other._normalizeSubtrahend;
	}

	public Stats(float noData, double normalizeSubtrahend = 0.0)
	{
		NoData = noData;
		_min = double.MaxValue;
		_max = double.MinValue;
		_mean = 0.0;
		_stdDev = 0.0;
		_countValid = 0L;
		_countTotal = 0L;
		_fractionNoData = 0.0;
		_sumNormalized = 0.0;
		_sqSumNormalized = 0.0;
		_normalizeSubtrahend = normalizeSubtrahend;
	}

	public Stats(double noData, double normalizeSubtrahend = 0.0)
	{
		NoData = noData;
		_min = double.MaxValue;
		_max = double.MinValue;
		_mean = 0.0;
		_stdDev = 0.0;
		_countValid = 0L;
		_countTotal = 0L;
		_fractionNoData = 0.0;
		_sumNormalized = 0.0;
		_sqSumNormalized = 0.0;
		_normalizeSubtrahend = normalizeSubtrahend;
	}

	public static Stats Compute(Span<float> data, float noDataValue, int validWidth, int validHeight, int actualWidth, int actualHeight)
	{
		Stats stats = new Stats(noDataValue);
		for (int i = 0; i < validHeight; i++)
		{
			stats.InsertRange(data.Slice(i * actualWidth, validWidth));
		}
		return stats;
	}

	public static Stats Compute(float[,] data, float noDataValue, int validWidth, int validHeight)
	{
		Stats stats = new Stats(noDataValue);
		for (int i = 0; i < validHeight; i++)
		{
			for (int j = 0; j < validWidth; j++)
			{
				stats.Insert(data[i, j]);
			}
		}
		return stats;
	}

	public static Stats Compute(double[,] data, double noDataValue, int validWidth, int validHeight)
	{
		Stats stats = new Stats(noDataValue);
		for (int i = 0; i < validHeight; i++)
		{
			for (int j = 0; j < validWidth; j++)
			{
				stats.Insert(data[i, j]);
			}
		}
		return stats;
	}

	public static Stats Compute(Span<double> data, double noDataValue)
	{
		Stats stats = new Stats(noDataValue);
		for (int i = 0; i < data.Length; i++)
		{
			stats.Insert(data[i]);
		}
		return stats;
	}

	public static Stats Compute(Span<float> data, float noDataValue)
	{
		Stats stats = new Stats(noDataValue);
		for (int i = 0; i < data.Length; i++)
		{
			stats.Insert(data[i]);
		}
		return stats;
	}

	public static Stats Compute(Span<float> data)
	{
		return Compute(data, float.NaN);
	}

	public static Stats Combine(IList<Stats> statistics)
	{
		if (statistics == null)
		{
			throw new NullReferenceException();
		}
		if (statistics.Count() == 0)
		{
			return new Stats(-9999.0);
		}
		double normalizeSubtrahendStatisticTerm = -1.0;
		foreach (Stats statistic in statistics)
		{
			if (normalizeSubtrahendStatisticTerm == -1.0)
			{
				normalizeSubtrahendStatisticTerm = statistic._normalizeSubtrahend;
			}
			else if (statistic._normalizeSubtrahend != normalizeSubtrahendStatisticTerm)
			{
				normalizeSubtrahendStatisticTerm = 0.0;
				break;
			}
		}
		double sumNormalizedStatistic2Term = 0.0;
		double sqSumNormalizedStatistic2Term = 0.0;
		double sumSum = 0.0;
		double sqSum = 0.0;
		double maxValue = double.MaxValue;
		double minValue = double.MinValue;
		long countValidSum = 0L;
		long countTotalSum = 0L;
		foreach (Stats statistic2 in statistics)
		{
			if (statistic2.Sum != statistic2.NoData)
			{
				sumNormalizedStatistic2Term = ((normalizeSubtrahendStatisticTerm != 0.0) ? (sumNormalizedStatistic2Term + statistic2._sumNormalized) : (sumNormalizedStatistic2Term + statistic2.Sum));
				sumSum += statistic2.Sum;
			}
			if (statistic2._sqSum != statistic2.NoData)
			{
				sqSumNormalizedStatistic2Term = ((normalizeSubtrahendStatisticTerm != 0.0) ? (sqSumNormalizedStatistic2Term + statistic2._sqSumNormalized) : (sqSumNormalizedStatistic2Term + statistic2._sqSum));
				sqSum += statistic2._sqSum;
			}
			if (statistic2._min != statistic2.NoData)
			{
				maxValue = Math.Min(maxValue, statistic2._min);
			}
			if (statistic2._max != statistic2.NoData)
			{
				minValue = Math.Max(minValue, statistic2._max);
			}
			countValidSum += statistic2._countValid;
			countTotalSum += statistic2._countTotal;
		}
		double noData = statistics[0].NoData;
		if (countTotalSum == 0L)
		{
			return new Stats(noData, noData, noData, noData, 1f, 0L, 0L, 0.0, 0.0, 0.0, noData);
		}
		if (countTotalSum < countValidSum)
		{
			throw new Exception("More data has been entered than exist in the domain.");
		}
		double countValidSumCountTotalSumTerm = 1f - (float)countValidSum / (float)countTotalSum;
		double mean = sumSum / (double)countValidSum;
		double countValidSumTerm = sumNormalizedStatistic2Term / (double)countValidSum;
		double stdDev = Math.Sqrt(sqSumNormalizedStatistic2Term / (double)countValidSum - countValidSumTerm * countValidSumTerm);
		return new Stats(maxValue, minValue, mean, stdDev, (float)countValidSumCountTotalSumTerm, countValidSum, countTotalSum, sumNormalizedStatistic2Term, sqSumNormalizedStatistic2Term, sqSum, noData, normalizeSubtrahendStatisticTerm);
	}

	public double NormalizeValue(float floatValue)
	{
		return (double)floatValue - _normalizeSubtrahend;
	}

	public double NormalizeValue(double doubleValue)
	{
		return doubleValue - _normalizeSubtrahend;
	}

	public void Remove(float floatValue)
	{
		_computedAgainstCountValid = -1L;
		_computedAgainstCountTotal = -1L;
		if (_countTotal != 0L)
		{
			_countTotal--;
			if ((double)floatValue != NoData)
			{
				_countValid--;
				_min = Math.Min(_min, floatValue);
				_max = Math.Max(_max, floatValue);
				double computed = floatValue;
				_sumNormalized -= NormalizeValue(computed);
				_sqSumNormalized -= NormalizeValue(computed) * NormalizeValue(computed);
				_sqSum -= computed * computed;
			}
		}
	}

	public void Remove(double doubleValue)
	{
		_computedAgainstCountValid = -1L;
		_computedAgainstCountTotal = -1L;
		if (_countTotal != 0L)
		{
			_countTotal--;
			if (doubleValue != NoData)
			{
				_countValid--;
				_min = Math.Min(_min, doubleValue);
				_max = Math.Max(_max, doubleValue);
				_sumNormalized -= NormalizeValue(doubleValue);
				_sqSumNormalized -= NormalizeValue(doubleValue) * NormalizeValue(doubleValue);
				_sqSum -= doubleValue * doubleValue;
			}
		}
	}

	public void Insert(float floatValue)
	{
		_countTotal++;
		if ((double)floatValue != NoData)
		{
			_countValid++;
			_min = Math.Min(_min, floatValue);
			_max = Math.Max(_max, floatValue);
			double computed = floatValue;
			_sumNormalized += NormalizeValue(computed);
			_sqSumNormalized += NormalizeValue(computed) * NormalizeValue(computed);
			_sqSum += computed * computed;
		}
	}

	public void Insert(double doubleValue)
	{
		_countTotal++;
		if (doubleValue != NoData)
		{
			_countValid++;
			_min = Math.Min(_min, doubleValue);
			_max = Math.Max(_max, doubleValue);
			_sumNormalized += NormalizeValue(doubleValue);
			_sqSumNormalized += NormalizeValue(doubleValue) * NormalizeValue(doubleValue);
			_sqSum += doubleValue * doubleValue;
		}
	}

	public void InsertRange(Span<float> values)
	{
		int length = values.Length;
		_countTotal += length;
		for (int i = 0; i < length; i++)
		{
			if ((double)values[i] != NoData)
			{
				_countValid++;
				_min = Math.Min(_min, values[i]);
				_max = Math.Max(_max, values[i]);
				double computed = values[i];
				_sumNormalized += NormalizeValue(computed);
				_sqSumNormalized += NormalizeValue(computed) * NormalizeValue(computed);
				_sqSum += computed * computed;
			}
		}
	}

	public void InsertRange(Span<double> values)
	{
		int length = values.Length;
		_countTotal += length;
		for (int i = 0; i < length; i++)
		{
			if (values[i] != NoData)
			{
				_countValid++;
				_min = Math.Min(_min, values[i]);
				_max = Math.Max(_max, values[i]);
				double computed = values[i];
				_sumNormalized += NormalizeValue(computed);
				_sqSumNormalized += NormalizeValue(computed) * NormalizeValue(computed);
				_sqSum += computed * computed;
			}
		}
	}

	private void ComputeStatsIfNecessary()
	{
		if (_countTotal != _computedAgainstCountTotal || _countValid != _computedAgainstCountValid)
		{
			ComputeMeanAndSD();
		}
	}

	private void ComputeMeanAndSD()
	{
		if (_countTotal < _countValid)
		{
			throw new Exception("Input total count " + _countTotal + " should be at least as large as the internal counter of VALID domain values: " + _countValid);
		}
		_computedAgainstCountValid = _countValid;
		_computedAgainstCountTotal = _countTotal;
		if (_countValid == 0L)
		{
			_fractionNoData = 1.0;
			_mean = double.NaN;
			_stdDev = double.NaN;
		}
		else
		{
			_mean = Sum / (double)_countValid;
			double sumNormalizedOverCountValid = _sumNormalized / (double)_countValid;
			double d = _sqSumNormalized / (double)_countValid - sumNormalizedOverCountValid * sumNormalizedOverCountValid;
			_stdDev = Math.Sqrt(d);
			_fractionNoData = 1f - (float)_countValid / (float)_countTotal;
		}
	}
}
