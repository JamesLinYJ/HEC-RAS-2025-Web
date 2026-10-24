using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Utility.Extensions;

namespace RasMapperLib;

public class MetTimeSeries
{
	private readonly List<float> _allValues;

	private readonly List<double> _allSimTimes;

	private List<DateTime> _allDates;

	private TimeDataType _timeType;

	public int TotalCount => _allValues.Count;

	public List<float> AllValues => _allValues;

	public string UnitString { get; }

	public string DataType { get; }

	public MetTimeSeries(IList<float> vals, IList<double> simTimeInDays, string units, string dataType)
	{
		if (vals == null || simTimeInDays == null)
		{
			throw new ArgumentNullException();
		}
		_allValues = vals.ToList();
		_allSimTimes = simTimeInDays.ToList();
		_allDates = null;
		_timeType = TimeDataType.SimulationStart;
		UnitString = units;
		DataType = dataType;
	}

	public MetTimeSeries(IList<float> vals, IList<DateTime> simTimes, string units, string dataType)
	{
		if (vals == null || simTimes == null)
		{
			throw new ArgumentNullException();
		}
		if (vals.Count != simTimes.Count)
		{
			throw new ArgumentException("Provided arrays incorrect length");
		}
		_allValues = vals.ToList();
		_allDates = simTimes.ToList();
		_allSimTimes = null;
		_timeType = TimeDataType.Absolute;
		UnitString = units;
		DataType = dataType;
	}

	public MetTimeSeries(IList<float> vals, IList<DateTime> simTimes, IList<double> simTimeInDays, string units, string dataType)
	{
		if (vals == null || simTimes == null || simTimeInDays == null)
		{
			throw new ArgumentNullException();
		}
		if (vals.Count != simTimes.Count)
		{
			throw new ArgumentException("Provided arrays incorrect length");
		}
		if (vals.Count != simTimeInDays.Count)
		{
			throw new ArgumentException("Provided arrays incorrect length");
		}
		_allValues = vals.ToList();
		_allDates = simTimes.ToList();
		_allSimTimes = simTimeInDays.ToList();
		_timeType = TimeDataType.Processed;
		UnitString = units;
		DataType = dataType;
	}

	public static MetTimeSeries ConstantValue(float floatValue, string units, string dataType)
	{
		return new MetTimeSeries(new float[2] { floatValue, floatValue }, new DateTime[2]
		{
			DateTime.MinValue,
			DateTime.MaxValue
		}, units, dataType);
	}

	public bool IsConstantValueTimeSeries()
	{
		if (_allDates != null && _allDates.Count == 2 && DateTime.Compare(_allDates[0], DateTime.MinValue) == 0)
		{
			return DateTime.Compare(_allDates[1], DateTime.MaxValue) == 0;
		}
		return false;
	}

	public static MetTimeSeries FromRTS(IList<float> values, DateTime startTime, TimeSpan dt, string units, string dataType)
	{
		checked
		{
			DateTime[] dateTimes = new DateTime[values.Count - 1 + 1];
			int count = values.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				dateTimes[i] = startTime.AddDays(dt.TotalDays * (double)i);
			}
			return new MetTimeSeries(values, dateTimes, units, dataType);
		}
	}

	public static MetTimeSeries FromRTS(IList<float> values, TimeSpan dt, string units, string dataType)
	{
		checked
		{
			double[] doubleValues = new double[values.Count - 1 + 1];
			int count = values.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				doubleValues[i] = dt.TotalDays * (double)i;
			}
			return new MetTimeSeries(values, doubleValues, units, dataType);
		}
	}

	public void GetTimeSeries(DateTime dtStart, DateTime dtEnd, ref List<float> values, ref List<DateTime> times)
	{
		if (IsConstantValueTimeSeries())
		{
			values = new List<float>();
			times = new List<DateTime>();
			values.Add(_allValues[0]);
			values.Add(_allValues[0]);
			times.Add(dtStart);
			times.Add(dtEnd);
			return;
		}
		List<DateTime> allTimes = GetAllTimes(dtStart);
		if (allTimes.Count == 0)
		{
			values = new List<float>();
			times = new List<DateTime>();
			return;
		}
		int firstKeptIndex = 0;
		int keepCount = 0;
		allTimes.TrimIndexOf([SpecialName] (DateTime dt) => DateTime.Compare(dt, dtStart) >= 0 && DateTime.Compare(dt, dtEnd) <= 0, out firstKeptIndex, out keepCount);
		if (firstKeptIndex < 0 && DateTime.Compare(dtStart, allTimes.First()) > 0 && DateTime.Compare(dtStart, allTimes.Last()) < 0)
		{
			firstKeptIndex = allTimes.BinarySearch(dtStart);
			if (firstKeptIndex < 0)
			{
				firstKeptIndex = ~firstKeptIndex;
				keepCount = 1;
			}
			else
			{
				firstKeptIndex = -1;
				keepCount = 0;
			}
		}
		if (firstKeptIndex < 0 || keepCount <= 0)
		{
			values = new List<float>();
			times = new List<DateTime>();
			return;
		}
		checked
		{
			if (firstKeptIndex > 0)
			{
				firstKeptIndex--;
				keepCount++;
			}
			if (firstKeptIndex + keepCount < TotalCount)
			{
				keepCount++;
			}
			values = _allValues.GetRange(firstKeptIndex, keepCount).ToList();
			times = allTimes.GetRange(firstKeptIndex, keepCount).ToList();
		}
	}

	public void GetCompleteTimeSeries(DateTime dtOffset, ref List<float> values, ref List<DateTime> times)
	{
		values = new List<float>();
		times = new List<DateTime>();
		checked
		{
			if (_timeType == TimeDataType.Absolute || _timeType == TimeDataType.Processed)
			{
				int count = TotalCount - 1;
				for (int i = 0; i <= count; i++)
				{
					values.Add(_allValues[i]);
					times.Add(_allDates[i]);
				}
			}
			else
			{
				int count2 = TotalCount - 1;
				for (int j = 0; j <= count2; j++)
				{
					values.Add(_allValues[j]);
					times.Add(dtOffset.AddDays(_allSimTimes[j]));
				}
			}
		}
	}

	public List<DateTime> GetAllTimes(DateTime dtOffset)
	{
		List<DateTime> allTimes = new List<DateTime>();
		checked
		{
			if (_timeType == TimeDataType.Absolute || _timeType == TimeDataType.Processed)
			{
				int count = TotalCount - 1;
				for (int i = 0; i <= count; i++)
				{
					allTimes.Add(_allDates[i]);
				}
			}
			else
			{
				int count2 = TotalCount - 1;
				for (int j = 0; j <= count2; j++)
				{
					allTimes.Add(dtOffset.AddDays(_allSimTimes[j]));
				}
			}
			return allTimes;
		}
	}
}
