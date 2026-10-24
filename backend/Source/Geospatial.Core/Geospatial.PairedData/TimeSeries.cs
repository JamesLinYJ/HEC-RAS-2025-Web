using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Geospatial.Transformers;
using Utility;
using Utility.DataStructures;
using Utility.Unsafe;

namespace Geospatial.PairedData;

public class TimeSeries : IDiscreteTimeSeries, ITimeSeries, ISpatialVersion
{
	protected List<long> _dtTicks;

	protected List<double> _values;

	protected long _xyVersion = 1L;

	private Freezer _freezer;

	private readonly Versioned<TimeSeriesDataBehavior> _behavior = new Versioned<TimeSeriesDataBehavior>();

	public long VersionXY => _xyVersion;

	public TimeSeriesDataBehavior Behavior => _behavior.Get(VersionXY, this, (TimeSeries ts) => ts.CheckDataBehavior());

	public IReadOnlyList<DateTime> Times { get; }

	public IReadOnlyList<double> Values { get; }

	public int Count => _dtTicks.Count;

	public DateTime StartTime => new DateTime(_dtTicks.FirstOrDefault());

	public DateTime EndTime => new DateTime(_dtTicks.LastOrDefault());

	public ProfileInterpolationMethod InterpolationMethod { get; set; }

	public TimeSeries()
	{
		_dtTicks = new List<long>();
		_values = new List<double>();
		Times = new TranslatedList<long, DateTime>(_dtTicks, (long lng) => new DateTime(lng));
		Values = _values.AsReadOnly();
	}

	public TimeSeries(int capacity)
	{
		_dtTicks = new List<long>(capacity);
		_values = new List<double>(capacity);
		Times = new TranslatedList<long, DateTime>(_dtTicks, (long lng) => new DateTime(lng));
		Values = _values.AsReadOnly();
	}

	public TimeSeries(IEnumerable<DateTime> times, IEnumerable<double> values)
	{
		_dtTicks = times.Select((DateTime dt) => dt.Ticks).ToList();
		_values = values.ToList();
		Times = new TranslatedList<long, DateTime>(_dtTicks, (long lng) => new DateTime(lng));
		Values = _values.AsReadOnly();
	}

	public TimeSeries(IEnumerable<DateTime> times, IEnumerable<float> values)
	{
		_dtTicks = times.Select((DateTime dt) => dt.Ticks).ToList();
		_values = values.Select((Func<float, double>)((float f) => f)).ToList();
		Times = new TranslatedList<long, DateTime>(_dtTicks, (long lng) => new DateTime(lng));
		Values = _values.AsReadOnly();
	}

	public TimeSeries(IList<long> timeInTicks, IList<double> values)
	{
		if (timeInTicks.Count != values.Count)
		{
			throw new ArgumentException("Times/Values don't have the same length.");
		}
		_dtTicks = timeInTicks.ToList();
		_values = values.ToList();
		Times = new TranslatedList<long, DateTime>(_dtTicks, (long lng) => new DateTime(lng));
		Values = _values.AsReadOnly();
	}

	protected TimeSeries(bool fakeArg, List<long> dtTicks, List<double> values)
	{
		_values = values;
		_dtTicks = dtTicks;
		Times = new TranslatedList<long, DateTime>(_dtTicks, (long lng) => new DateTime(lng));
		Values = _values.AsReadOnly();
	}

	public static TimeSeries CreateFromBuffers(List<long> dtTicks, List<double> values)
	{
		return new TimeSeries(fakeArg: false, dtTicks, values);
	}

	public static TimeSeries NoDataTimeSeries(IList<DateTime> times, double noDataValue)
	{
		List<long> dtTicks = times.Select((DateTime dt) => dt.Ticks).ToList();
		List<double> values = Enumerable.Repeat(noDataValue, times.Count).ToList();
		return CreateFromBuffers(dtTicks, values);
	}

	public static TimeSeries NoDataTimeSeries(IReadOnlyList<DateTime> times, double noDataValue)
	{
		List<long> dtTicks = times.Select((DateTime dt) => dt.Ticks).ToList();
		List<double> values = Enumerable.Repeat(noDataValue, times.Count).ToList();
		return CreateFromBuffers(dtTicks, values);
	}

	public static TimeSeries ConstantValue(double doubleValue)
	{
		return new TimeSeries(new long[2]
		{
			DateTime.MinValue.Ticks,
			DateTime.MaxValue.Ticks
		}, new double[2] { doubleValue, doubleValue });
	}

	public bool IsConstantValueTimeSeries()
	{
		if (_dtTicks.Count == 2 && _dtTicks[0] == DateTime.MinValue.Ticks)
		{
			return _dtTicks[1] == DateTime.MaxValue.Ticks;
		}
		return false;
	}

	public static TimeSeries FromRTS(IList<double> values, DateTime startTime, TimeSpan dt)
	{
		DateTime[] dateTimes = new DateTime[values.Count];
		for (int i = 0; i < values.Count; i++)
		{
			dateTimes[i] = startTime.AddDays(dt.TotalDays * (double)i);
		}
		return new TimeSeries(dateTimes, values);
	}

	public void Add(DateTime dt, double doubleValue)
	{
		Add(dt.Ticks, doubleValue);
	}

	public void Add(long dtTickCount, double doubleValue)
	{
		if (_dtTicks.Count > 0 && dtTickCount <= _dtTicks.Last())
		{
			throw new ArgumentException("DateTime not added in-order");
		}
		_dtTicks.Add(dtTickCount);
		_values.Add(doubleValue);
		OnDataChanged();
	}

	public void AddRange(IList<DateTime> times, IList<double> values)
	{
		if (times == null || values == null || times.Count != values.Count)
		{
			throw new ArgumentException("Bad time/value information for time-series.");
		}
		for (int i = 0; i < times.Count; i++)
		{
			long ticks = times[i].Ticks;
			double computed = values[i];
			_dtTicks.Add(ticks);
			_values.Add(computed);
		}
		OnDataChanged();
	}

	public void Insert(DateTime dt, double doubleValue)
	{
		long ticks = dt.Ticks;
		int binarySearch = MathHelpers.BinarySearch(_dtTicks, ticks);
		if (binarySearch >= 0)
		{
			throw new ArgumentException("DateTime key already added to this collection.");
		}
		binarySearch = ~binarySearch - 1;
		_dtTicks.Insert(binarySearch, ticks);
		_values.Insert(binarySearch, doubleValue);
	}

	public void Shift(TimeSpan ts)
	{
		long ticks = ts.Ticks;
		for (int i = 0; i < _dtTicks.Count; i++)
		{
			_dtTicks[i] += ticks;
		}
		OnDataChanged();
	}

	public void Shift(Func<DateTime, DateTime> modifier)
	{
		for (int i = 0; i < Times.Count; i++)
		{
			_dtTicks[i] = modifier(Times[i]).Ticks;
		}
		OnDataChanged();
	}

	public void Scale(double scaleFactor)
	{
		for (int i = 0; i < _values.Count; i++)
		{
			_values[i] *= scaleFactor;
		}
		OnDataChanged();
	}

	protected void OnDataChanged()
	{
		_xyVersion++;
	}

	public double Sample(DateTime dt)
	{
		return Sample(dt, ExtrapolationBehavior.ReturnNoData);
	}

	public double Sample(long ticks)
	{
		return Sample(ticks, ExtrapolationBehavior.ReturnNoData);
	}

	public double[] Sample(IList<DateTime> times)
	{
		double[] doubleValues = new double[times.Count];
		for (int i = 0; i < doubleValues.Length; i++)
		{
			doubleValues[i] = Sample(times[i]);
		}
		return doubleValues;
	}

	protected int BinarySearchTime(long ticks)
	{
		return MathHelpers.BinarySearch(_dtTicks, ticks);
	}

	public double Sample(DateTime dt, ExtrapolationBehavior extrapolate)
	{
		return Sample(dt.Ticks, extrapolate);
	}

	public double Sample(long ticks, ExtrapolationBehavior extrapolate)
	{
		if (_dtTicks.Count == 0)
		{
			return -9999.0;
		}
		if (ticks < _dtTicks.First())
		{
			return extrapolate switch
			{
				ExtrapolationBehavior.ReturnNoData => -9999.0, 
				ExtrapolationBehavior.ReturnZero => 0.0, 
				ExtrapolationBehavior.NearestValue => _values.First(), 
				ExtrapolationBehavior.ExtrapolateNearestSegment => throw new NotImplementedException(), 
				ExtrapolationBehavior.ThrowException => throw new ArgumentException("DateTime was out of range."), 
				_ => throw new NotImplementedException(), 
			};
		}
		int binarySearchTime = BinarySearchTime(ticks);
		if (binarySearchTime >= 0)
		{
			return _values[binarySearchTime];
		}
		binarySearchTime = ~binarySearchTime;
		if (binarySearchTime == _dtTicks.Count)
		{
			return extrapolate switch
			{
				ExtrapolationBehavior.ReturnNoData => -9999.0, 
				ExtrapolationBehavior.ReturnZero => 0.0, 
				ExtrapolationBehavior.NearestValue => _values.Last(), 
				ExtrapolationBehavior.ExtrapolateNearestSegment => throw new NotImplementedException(), 
				ExtrapolationBehavior.ThrowException => throw new ArgumentException("DateTime was out of range."), 
				_ => throw new NotImplementedException(), 
			};
		}
		return QuerySegmentInternal(ticks, binarySearchTime);
	}

	protected double QuerySegmentInternal(long ticks, int nextPtIdx)
	{
		int index = nextPtIdx - 1;
		double indexTerm = _values[index];
		double nextPtIdxTerm = _values[nextPtIdx];
		double interpolationFraction = (double)(ticks - _dtTicks[index]) / (double)(_dtTicks[nextPtIdx] - _dtTicks[index]);
		return interpolationFraction * nextPtIdxTerm + (1.0 - interpolationFraction) * indexTerm;
	}

	public static double Sample(Span<long> sourceTimes, Span<double> sourceValues, long ticks, ExtrapolationBehavior extrapolate)
	{
		if (sourceTimes.Length != sourceValues.Length)
		{
			throw new ArgumentException();
		}
		if (sourceTimes.Length == 0)
		{
			return -9999.0;
		}
		if (ticks < sourceTimes[0])
		{
			return extrapolate switch
			{
				ExtrapolationBehavior.ReturnNoData => -9999.0, 
				ExtrapolationBehavior.ReturnZero => 0.0, 
				ExtrapolationBehavior.NearestValue => sourceValues[0], 
				ExtrapolationBehavior.ExtrapolateNearestSegment => throw new NotImplementedException(), 
				ExtrapolationBehavior.ThrowException => throw new ArgumentException("DateTime was out of range."), 
				_ => throw new NotImplementedException(), 
			};
		}
		int binarySearch = MathHelpers.BinarySearch(sourceTimes, ticks);
		if (binarySearch >= 0)
		{
			return sourceValues[binarySearch];
		}
		binarySearch = ~binarySearch;
		if (binarySearch == sourceTimes.Length)
		{
			switch (extrapolate)
			{
			case ExtrapolationBehavior.ReturnNoData:
				return -9999.0;
			case ExtrapolationBehavior.ReturnZero:
				return 0.0;
			case ExtrapolationBehavior.NearestValue:
				return sourceValues[sourceValues.Length - 1];
			case ExtrapolationBehavior.ExtrapolateNearestSegment:
				throw new NotImplementedException();
			case ExtrapolationBehavior.ThrowException:
				throw new ArgumentException("DateTime was out of range.");
			default:
				throw new NotImplementedException();
			}
		}
		return QuerySegmentInternal(sourceTimes, sourceValues, ticks, binarySearch);
	}

	protected static double QuerySegmentInternal(Span<long> sourceTicks, Span<double> sourceValues, long ticks, int nextPtIdx)
	{
		int index = nextPtIdx - 1;
		double sourceValue = sourceValues[index];
		double sourceValue2 = sourceValues[nextPtIdx];
		double interpolationFraction = (double)(ticks - sourceTicks[index]) / (double)(sourceTicks[nextPtIdx] - sourceTicks[index]);
		return interpolationFraction * sourceValue2 + (1.0 - interpolationFraction) * sourceValue;
	}

	private TimeSeriesDataBehavior CheckDataBehavior()
	{
		TimeSeriesDataBehavior result = default;
		for (int i = 0; i < Count; i++)
		{
			double computed = Values[i];
			if (computed == -9999.0)
			{
				result.AnyNoData = true;
			}
			else if (computed == 0.0)
			{
				result.AnyZero = true;
			}
			else if (computed > 0.0)
			{
				result.AnyPositive = true;
			}
			else
			{
				result.AnyNegative = true;
			}
		}
		return result;
	}

	protected DateTime TimeToAccumulate_Internal(double yValue, IReadOnlyList<double> accumulatedValues, TimeQueryTiebreaker tiebreaker = TimeQueryTiebreaker.First)
	{
		if (!Behavior.AnyNegative)
		{
			return TimeToAccumulate_NonDecreasing(yValue, accumulatedValues, tiebreaker);
		}
		return TimeToAccumulate_General(yValue, accumulatedValues, tiebreaker);
	}

	private DateTime TimeToAccumulate_NonDecreasing(double yValue, IReadOnlyList<double> accumulatedValues, TimeQueryTiebreaker tiebreaker)
	{
		double accumulatedValuesTerm = accumulatedValues.First();
		if (yValue < accumulatedValuesTerm)
		{
			return DateTime.MinValue;
		}
		double accumulatedValuesTerm2 = accumulatedValues.Last();
		if (yValue > accumulatedValuesTerm2)
		{
			return DateTime.MaxValue;
		}
		int idx = MathHelpers.BinarySearch(accumulatedValues, yValue);
		if (idx >= 0)
		{
			bool condition = false;
			if (idx > 0 && accumulatedValues[idx - 1] == accumulatedValues[idx])
			{
				condition = true;
			}
			else if (idx < Count - 1 && accumulatedValues[idx] == accumulatedValues[idx + 1])
			{
				condition = true;
			}
			if (condition)
			{
				Func<long> func = () =>
				{
					for (int idxOffset = idx - 1; idxOffset >= 0; idxOffset--)
					{
						if (accumulatedValues[idxOffset] != yValue)
						{
							return _dtTicks[idxOffset + 1];
						}
					}
					return _dtTicks[0];
				};
				Func<long> func2 = () =>
				{
					for (int i = idx + 1; i < Count; i++)
					{
						if (accumulatedValues[i] != yValue)
						{
							return _dtTicks[i - 1];
						}
					}
					return _dtTicks.Last();
				};
				return tiebreaker switch
				{
					TimeQueryTiebreaker.First => new DateTime(func()), 
					TimeQueryTiebreaker.Last => new DateTime(func2()), 
					_ => new DateTime((func() + func2()) / 2), 
				};
			}
			return new DateTime(_dtTicks[idx]);
		}
		int segIdx = ~idx - 1;
		return new DateTime(InterpolateSegmentTime(accumulatedValues, segIdx, yValue));
	}

	private DateTime TimeToAccumulate_General(double yValue, IReadOnlyList<double> accumulatedValues, TimeQueryTiebreaker tieBreaker)
	{
		_ = DateTime.MinValue;
		_ = DateTime.MaxValue;
		if (tieBreaker != TimeQueryTiebreaker.First && tieBreaker != TimeQueryTiebreaker.Last)
		{
			throw new NotSupportedException();
		}
		for (int i = 1; i < Count; i++)
		{
			double computed = Values[i - 1];
			double computed2 = Values[i];
			if (computed == -9999.0 || computed2 == -9999.0 || computed < yValue)
			{
			}
		}
		throw new NotImplementedException();
	}

	protected virtual long InterpolateSegmentTime(IReadOnlyList<double> accumulatedValues, int segIdx, double yValue)
	{
		int index = segIdx + 1;
		return InterpolateTime(_dtTicks[segIdx], _dtTicks[index], accumulatedValues[segIdx], accumulatedValues[index], yValue);
	}

	protected static long InterpolateTime(long t1, long t2, double doubleValue, double doubleValue2, double yValue)
	{
		double submergenceRatio = (yValue - doubleValue) / (doubleValue2 - doubleValue);
		long t2AboveT1 = t2 - t1;
		return t1 + (long)((double)t2AboveT1 * submergenceRatio);
	}

	public virtual TimeSeries Clone()
	{
		return new TimeSeries(_dtTicks, _values);
	}

	public virtual TimeSeries Clone(IValueTransformer<double> valueTransformer)
	{
		List<double> doubleList = _values.ToList();
		Memory<double> listBackingBuffer = UnsafeHelpers.GetListBackingBuffer(doubleList);
		valueTransformer.Transform(listBackingBuffer, -9999.0);
		return new TimeSeries(fakeArg: false, _dtTicks.ToList(), doubleList);
	}

	public double[] ComputeSummation()
	{
		double[] summation = new double[Count];
		double summation2 = 0.0;
		for (int i = 0; i < Count; i++)
		{
			double computed = Values[i];
			if (computed != -9999.0)
			{
				summation2 += computed;
			}
			summation[i] = summation2;
		}
		return summation;
	}

	public Memory<double> GetValuesUnsafe()
	{
		return UnsafeHelpers.GetListBackingBuffer(_values);
	}

	public Memory<long> GetTimesUnsafe()
	{
		return UnsafeHelpers.GetListBackingBuffer(_dtTicks);
	}

	public void FreezeOne()
	{
		_freezer.FreezeOne(this);
	}

	public void ThawOne()
	{
		_freezer.ThawOne();
	}

	public string DebugPrint(int rowCh = 40, int colCh = 180)
	{
		Buffers.MaxMin(CollectionsMarshal.AsSpan(_values), out var max, out var min);
		string min2 = min.ToString("G6");
		string max2 = max.ToString("G6");
		long dtTick = _dtTicks[0];
		List<long> dtTicks = _dtTicks;
		long dtTick2 = dtTicks[dtTicks.Count - 1];
		string tickString = GetTickString(dtTick);
		string tickString2 = GetTickString(dtTick2);
		int min2Max2Term = Math.Max(min2.Length, max2.Length) + 1;
		char[,] charValues = new char[rowCh, colCh];
		for (int i = 0; i < rowCh; i++)
		{
			for (int j = 0; j < colCh; j++)
			{
				charValues[i, j] = ' ';
			}
		}
		int ordinal = min2Max2Term + 1;
		int count = rowCh - 2;
		int countOffset = count + 1;
		int ordinal2 = ordinal;
		for (int k = ordinal; k < colCh; k++)
		{
			charValues[count, k] = '-';
		}
		for (int l = 0; l < count; l++)
		{
			charValues[l, ordinal] = '|';
		}
		for (int m = 0; m < min2.Length; m++)
		{
			charValues[count, m] = min2[m];
		}
		for (int n = 0; n < max2.Length; n++)
		{
			charValues[0, n] = max2[n];
		}
		for (int intervalIndex = 0; intervalIndex < tickString.Length; intervalIndex++)
		{
			charValues[countOffset, intervalIndex + ordinal2] = tickString[intervalIndex];
		}
		for (int intervalIndex2 = 0; intervalIndex2 < tickString2.Length; intervalIndex2++)
		{
			charValues[countOffset, intervalIndex2 + (colCh - tickString2.Length)] = tickString2[intervalIndex2];
		}
		int ordinal3 = ordinal + 1;
		int count2 = colCh - 1;
		int countOffset2 = count - 1;
		for (int ordinal4 = ordinal3; ordinal4 <= count2; ordinal4++)
		{
			double submergenceRatio = (double)(ordinal4 - ordinal3) / (double)(count2 - ordinal3);
			long ticks = dtTick + (long)(submergenceRatio * (double)(dtTick2 - dtTick));
			double submergenceRatio2 = (Sample(ticks) - min) / (max - min);
			int ordinal5 = (int)((1.0 - submergenceRatio2) * (double)countOffset2);
			charValues[ordinal5, ordinal4] = 'o';
		}
		StringBuilder stringBuilder = new StringBuilder();
		for (int intervalIndex3 = 0; intervalIndex3 < rowCh; intervalIndex3++)
		{
			for (int intervalIndex4 = 0; intervalIndex4 < colCh; intervalIndex4++)
			{
				stringBuilder.Append(charValues[intervalIndex3, intervalIndex4]);
			}
			stringBuilder.AppendLine();
		}
		return stringBuilder.ToString();
		static string GetTickString(long ticks2)
		{
			return new DateTime(ticks2).ToString("yyyy-MMM-dd HH:mm:ss");
		}
	}
}
