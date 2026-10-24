using System;
using System.Collections.Generic;
using System.Linq;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib.Functional;

public class RegularTimeSeries : DiscreteTimeSeries
{
	public const string sH5TypeName = "Regular Time Series";

	public const string sH5StartDateTime = "Start Date";

	public const string sH5DTSeconds = "Timestep (seconds)";

	private TimeSpan _timeStep;

	private DateTime _startTime;

	private List<double> _data;

	public override int Count => _data.Count;

	// C# has no syntax for parameterized property 'Value'.
	public override double get_Value(int idx)
	{
		return _data[idx];
	}

	public override void set_Value(int idx, double Value)
	{
		_data[idx] = Value;
	}

	// C# has no syntax for parameterized property 'DateTime'.
	public override DateTime get_DateTime(int idx)
	{
		return _startTime + TimeSpan.FromSeconds(Timestep.TotalSeconds * (double)idx);
	}

	public override DateTime StartTime => _startTime;

	public override DateTime EndTime => _startTime.AddSeconds(Timestep.TotalSeconds * (double)_data.Count);

	public TimeSpan Timestep => _timeStep;

	public override TimeSpan Duration => TimeSpan.FromSeconds(Timestep.TotalSeconds * (double)_data.Count);

	public RegularTimeSeries(DateTime start, IList<double> data, TimeSpan timestep)
		: this(start, data, timestep, "", "")
	{
	}

	public RegularTimeSeries(DateTime start, IList<double> data, TimeSpan timestep, string variableName, string units)
		: base(variableName, units)
	{
		_startTime = start;
		_data = data.ToList();
		_timeStep = timestep;
	}

	public RegularTimeSeries(DateTime start, IList<double> data, double timestepSeconds)
		: this(start, data, TimeSpan.FromSeconds(timestepSeconds), "", "")
	{
	}

	protected override void SaveInternal(H5Writer h5w, string groupName)
	{
		h5w.WriteAttribute(groupName, "Type", "Regular Time Series");
		h5w.WriteAttribute(groupName, "Start Date", StartTime.ToString());
		h5w.WriteAttribute(groupName, "Timestep (seconds)", Timestep.TotalSeconds);
		h5w.WriteArray(groupName + "/Data", _data.ToArray());
	}

	public static RegularTimeSeries TryLoad(H5Reader hr, string groupName)
	{
		RegularTimeSeries result;
		try
		{
			if (!hr.ExistsAsGroup(groupName))
			{
				result = null;
			}
			else
			{
				string textValue = "";
				DateTime result2;
				if (!hr.TryReadAttributeScalar(groupName, "Start Date", ref textValue))
				{
					result = null;
				}
				else if (!DateTime.TryParse(textValue, out result2))
				{
					result = null;
				}
				else
				{
					double computed = 0.0;
					if (!hr.TryReadAttributeScalar(groupName, "Timestep (seconds)", ref computed))
					{
						result = null;
					}
					else
					{
						TimeSpan timestep = TimeSpan.FromSeconds(computed);
						if (!hr.ExistsAsDataset(groupName + "/Data"))
						{
							result = null;
						}
						else
						{
							double[] values = null;
							hr.ReadDataset(groupName + "/Data", ref values);
							string var = "";
							string units = "";
							TimeSeries.LoadVariableUnits(hr, groupName, ref var, ref units);
							result = new RegularTimeSeries(result2, values, timestep, var, units);
						}
					}
				}
			}
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			result = null;
			ProjectData.ClearProjectError();
		}
		return result;
	}

	public void Add(double v)
	{
		_data.Add(v);
	}

	public override List<double> CopyData()
	{
		return _data.ToList();
	}

	public override List<DateTime> CopyDates()
	{
		List<DateTime> dateTimes = new List<DateTime>();
		DateTime startTime = StartTime;
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				dateTimes.Add(startTime.AddSeconds(Timestep.TotalSeconds * (double)i));
			}
			return dateTimes;
		}
	}

	public override double Sample(DateTime dt, bool canExtrapolate = false)
	{
		if (Count == 0)
		{
			return double.NaN;
		}
		if (DateTime.Compare(dt, StartTime) == 0)
		{
			return _data.First();
		}
		if (DateTime.Compare(dt, EndTime) == 0)
		{
			return _data.Last();
		}
		if (DateTime.Compare(dt, StartTime) < 0)
		{
			if (canExtrapolate)
			{
				return _data.First();
			}
			return double.NaN;
		}
		if (DateTime.Compare(dt, EndTime) > 0)
		{
			if (canExtrapolate)
			{
				return _data.Last();
			}
			return double.NaN;
		}
		double totalSecondsDtTerm = (dt - StartTime).TotalSeconds / Duration.TotalSeconds * (double)_data.Count;
		checked
		{
			int ordinal = (int)totalSecondsDtTerm;
			int index = ordinal + 1;
			double computed = totalSecondsDtTerm - (double)ordinal;
			double computed2 = _data[ordinal];
			double indexTerm = _data[index];
			return computed * indexTerm + (1.0 - computed) * computed2;
		}
	}

	public IrregularTimeSeries ToIrregularTimeSeries()
	{
		return new IrregularTimeSeries(CopyData(), CopyDates());
	}
}
