using System;
using System.Collections.Generic;
using System.Data;

namespace Hec.Dss;

public class TimeSeries
{
	private bool _compressed;

	public bool Compressed => _compressed;

	public DateTime StartDateTime
	{
		get
		{
			if (Times.Length == 0)
			{
				throw new NullReferenceException("Error StartDateTime is not defined");
			}
			return Times[0];
		}
		set
		{
			if (Times.Length == 0)
			{
				Times = new DateTime[1] { value };
			}
			else if (Times.Length != 0)
			{
				Times[0] = value;
			}
		}
	}

	public double[] Values { get; set; }

	public int[] Qualities { get; set; }

	public DateTime[] Times { get; set; }

	public string Units { get; set; }

	public DssPath Path { get; set; }

	public string DataType { get; set; }

	public int Count => Values.Length;

	public LocationInformation LocationInformation { get; set; }

	public bool HasQuality
	{
		get
		{
			if (Qualities != null)
			{
				return Qualities.Length != 0;
			}
			return false;
		}
	}

	public string ProgramName { get; internal set; }

	public TimeSeriesPoint this[int i]
	{
		get
		{
			return new TimeSeriesPoint(Times[i], Values[i]);
		}
		set
		{
			Values[i] = value.Value;
			Times[i] = value.DateTime;
			if (HasQuality)
			{
				Qualities[i] = value.Quality;
			}
		}
	}

	public override string ToString()
	{
		return Path?.ToString() + " " + Values.Length + " items ";
	}

	public TimeSeries(string fullPath, double[] values, DateTime dateTime, string units, string dataType)
	{
		Init(new DssPath(fullPath), values, dateTime, units, dataType);
	}

	public TimeSeries(DssPath path, double[] values, DateTime dateTime, string units, string dataType)
	{
		Init(path, values, dateTime, units, dataType);
	}

	private void Init(DssPath path, double[] values, DateTime dateTime, string units, string dataType)
	{
		Path = path;
		Values = values;
		Times = new DateTime[0];
		StartDateTime = dateTime;
		Units = units;
		DataType = dataType;
		Qualities = new int[0];
	}

	public TimeSeries()
	{
		Units = "";
		DataType = "";
		Values = new double[0];
		Times = new DateTime[0];
		Qualities = new int[0];
	}

	public int IndexOf(DateTime t)
	{
		for (int i = 0; i < Times.Length; i++)
		{
			if (Times[i].CompareTo(t) == 0)
			{
				return i;
			}
		}
		return -1;
	}

	public void WriteToConsole()
	{
		for (int i = 0; i < Times.Length; i++)
		{
			Console.WriteLine(Times[i].ToString() + " " + Values[i]);
		}
	}

	public bool IsRegular()
	{
		return Path.IsRegular;
	}

	public static bool IsRegular(DateTime[] times)
	{
		if (times == null || times.Length < 2)
		{
			return false;
		}
		TimeSpan timeSpan = times[1] - times[0];
		for (int i = 1; i < times.Length - 1; i++)
		{
			if (times[i + 1] - times[i] != timeSpan)
			{
				return false;
			}
		}
		return true;
	}

	public void SetQuality(int[] quality)
	{
		if (quality.Length != Values.Length)
		{
			throw new Exception("Size of quality array doesn't match value array.");
		}
		Qualities = quality;
	}

	internal void Append(TimeSeries other)
	{
		List<double> lengthDifference = new List<double>(Values.Length + other.Values.Length);
		lengthDifference.AddRange(Values);
		lengthDifference.AddRange(other.Values);
		Values = lengthDifference.ToArray();
		List<DateTime> lengthDifferenceList = new List<DateTime>(Times.Length + other.Times.Length);
		lengthDifferenceList.AddRange(Times);
		lengthDifferenceList.AddRange(other.Times);
		Times = lengthDifferenceList.ToArray();
		if (other.HasQuality)
		{
			List<int> lengthDifference2 = new List<int>(Qualities.Length + other.Qualities.Length);
			lengthDifference2.AddRange(Qualities);
			lengthDifference2.AddRange(other.Qualities);
			Qualities = lengthDifference2.ToArray();
		}
	}

	public DataTable ToDataTable(bool withIndex = true, bool withQuality = true)
	{
		DataTable dataTable = new DataTable();
		bool withQualityAndHasQuality = withQuality && HasQuality;
		List<DataColumn> dataColumns = new List<DataColumn>();
		if (withIndex)
		{
			dataColumns.Add(new DataColumn("Index", typeof(int)));
		}
		dataColumns.Add(new DataColumn("DateTime", typeof(DateTime)));
		dataColumns.Add(new DataColumn("Value", typeof(double)));
		if (withQualityAndHasQuality)
		{
			dataColumns.Add(new DataColumn("Quality", typeof(int)));
		}
		dataTable.Columns.AddRange(dataColumns.ToArray());
		for (int i = 0; i < Count; i++)
		{
			if (withIndex & withQualityAndHasQuality)
			{
				dataTable.Rows.Add(i + 1, Times[i], Values[i], Qualities[i]);
			}
			else if (withIndex && !withQualityAndHasQuality)
			{
				dataTable.Rows.Add(i + 1, Times[i], Values[i]);
			}
			else if (!withIndex & withQualityAndHasQuality)
			{
				dataTable.Rows.Add(Times[i], Values[i], Qualities[i]);
			}
			else
			{
				dataTable.Rows.Add(Times[i], Values[i]);
			}
		}
		return dataTable;
	}

	public List<TimeSeriesPoint> ToTimeSeriesPoints()
	{
		List<TimeSeriesPoint> timeSeriesPoints = new List<TimeSeriesPoint>();
		if (Qualities != null && Qualities.Length != 0)
		{
			for (int i = 0; i < Count; i++)
			{
				timeSeriesPoints.Add(new TimeSeriesPoint(Times[i], Values[i], Qualities[i]));
			}
		}
		else
		{
			for (int j = 0; j < Count; j++)
			{
				timeSeriesPoints.Add(new TimeSeriesPoint(Times[j], Values[j]));
			}
		}
		return timeSeriesPoints;
	}

	private TimeSeries NoDataCompression()
	{
		TimeSeries timeSeries = new TimeSeries();
		timeSeries.Init(Path, new double[0], StartDateTime, Units, DataType);
		timeSeries.LocationInformation = LocationInformation;
		List<double> doubleList = new List<double>();
		List<DateTime> dateTimes = new List<DateTime>();
		int ordinal = 0;
		for (int i = 0; i < Count; i++)
		{
			if (!DssReader.IsValid(Values[i]))
			{
				ordinal++;
			}
			if ((i + 1 != Count && DssReader.IsValid(Values[i])) || (i == Count - 1 && !DssReader.IsValid(Values[i])))
			{
				ordinal = 0;
			}
			if (ordinal < 2)
			{
				doubleList.Add(Values[i]);
				dateTimes.Add(Times[i]);
			}
		}
		timeSeries.Values = doubleList.ToArray();
		timeSeries.Times = dateTimes.ToArray();
		timeSeries._compressed = true;
		return timeSeries;
	}

	private TimeSeries AnyValueCompression()
	{
		TimeSeries timeSeries = new TimeSeries();
		timeSeries.Init(Path, new double[0], StartDateTime, Units, DataType);
		timeSeries.LocationInformation = LocationInformation;
		List<double> doubleList = new List<double>();
		List<DateTime> dateTimes = new List<DateTime>();
		int ordinal = 0;
		double computed = Values[0];
		for (int i = 1; i < Count; i++)
		{
			if (Values[i] == computed)
			{
				ordinal++;
			}
			if ((i + 1 != Count && Values[i + 1] != computed) || (i == Count - 1 && Values[i] == computed))
			{
				ordinal = 0;
				if (i != Count - 1)
				{
					computed = Values[i + 1];
				}
			}
			if (ordinal < 2)
			{
				doubleList.Add(Values[i]);
				dateTimes.Add(Times[i]);
			}
		}
		timeSeries.Values = doubleList.ToArray();
		timeSeries.Times = dateTimes.ToArray();
		timeSeries._compressed = true;
		return timeSeries;
	}

	private TimeSeries ZeroAndNoDataCompression()
	{
		TimeSeries timeSeries = new TimeSeries();
		timeSeries.Init(Path, new double[0], StartDateTime, Units, DataType);
		timeSeries.LocationInformation = LocationInformation;
		List<double> doubleList = new List<double>();
		List<DateTime> dateTimes = new List<DateTime>();
		int ordinal = 0;
		for (int i = 0; i < Count; i++)
		{
			if (Values[i] == 0.0 || !DssReader.IsValid(Values[i]))
			{
				ordinal++;
			}
			if ((i + 1 != Count && Values[i + 1] != 0.0 && DssReader.IsValid(Values[i])) || (i == Count - 1 && (Values[i] == 0.0 || !DssReader.IsValid(Values[i]))))
			{
				ordinal = 0;
			}
			if (ordinal < 2)
			{
				doubleList.Add(Values[i]);
				dateTimes.Add(Times[i]);
			}
		}
		timeSeries.Values = doubleList.ToArray();
		timeSeries.Times = dateTimes.ToArray();
		timeSeries._compressed = true;
		return timeSeries;
	}

	internal TimeSeries Compress(TimeWindow.ConsecutiveValueCompression compressionType = TimeWindow.ConsecutiveValueCompression.None)
	{
		return compressionType switch
		{
			TimeWindow.ConsecutiveValueCompression.ZeroAndNoData => ZeroAndNoDataCompression(), 
			TimeWindow.ConsecutiveValueCompression.NoData => NoDataCompression(), 
			TimeWindow.ConsecutiveValueCompression.AnyValue => AnyValueCompression(), 
			_ => this, 
		};
	}

	public void Trim()
	{
		int index = Array.FindIndex(Values, (double computed) => DssReader.IsValid(computed));
		if (index < 0)
		{
			Clear();
			return;
		}
		int count = Array.FindLastIndex(Values, (double computed2) => DssReader.IsValid(computed2)) - index + 1;
		if (count != Values.Length)
		{
			int qualityWordsPerValue = 0;
			if (HasQuality)
			{
				if (Qualities.Length % Values.Length != 0)
				{
					throw new InvalidOperationException("The DSS quality array does not match its value count.");
				}
				qualityWordsPerValue = Qualities.Length / Values.Length;
			}
			double[] doubleValues = new double[count];
			DateTime[] times = new DateTime[count];
			int[] qualities = new int[count * qualityWordsPerValue];
			Array.Copy(Values, index, doubleValues, 0, count);
			Array.Copy(Times, index, times, 0, count);
			if (HasQuality)
			{
				Array.Copy(Qualities, index * qualityWordsPerValue, qualities, 0, qualities.Length);
			}
			Values = doubleValues;
			Times = times;
			if (HasQuality)
			{
				Qualities = qualities;
			}
		}
	}

	private void Clear()
	{
		Values = new double[0];
		Times = new DateTime[0];
		if (HasQuality)
		{
			Qualities = new int[0];
		}
	}

	public TimeSpan TimeSpanInterval()
	{
		if (IsRegular(Times))
		{
			if (Times.Length < 2)
			{
				return default;
			}
			return Times[1] - Times[0];
		}
		return default;
	}
}
