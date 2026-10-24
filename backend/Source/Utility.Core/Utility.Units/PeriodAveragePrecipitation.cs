using System;
using System.Collections.Generic;
using System.Linq;

namespace Utility.Units;

public class PeriodAveragePrecipitation : Precipitation
{
	public const string DefaultDataTypeName = "per-avg";

	public static readonly PeriodAveragePrecipitation MillimeterPerHour;

	public static readonly PeriodAveragePrecipitation MillimeterPerSecond;

	public static readonly PeriodAveragePrecipitation InchesPerHour;

	public override PrecipitationDataType DataType => PrecipitationDataType.PeriodAverageRate;

	public override Length VerticalUnits { get; }

	public Time TimeUnits { get; }

	public static double ConvertInToBase(double input)
	{
		return input / 3600.0;
	}

	static PeriodAveragePrecipitation()
	{
		MillimeterPerHour = new PeriodAveragePrecipitation(Length.Millimeters, Time.Hours, "mm/hr", new string[3] { "millimeterPerHour", "mmph", "[mm]" }, null, null);
		MillimeterPerSecond = new PeriodAveragePrecipitation(Length.Millimeters, Time.Seconds, "mm/s", new string[2] { "millimeterPerSecond", "mmps" }, (double mms) => mms * 3600.0, (double mmh) => mmh / 3600.0);
		InchesPerHour = new PeriodAveragePrecipitation(Length.Inches, Time.Hours, "in/hr", new string[3] { "inchesPerHour", "iph", "inches/hour" }, (double inph) => inph * 25.4, (double mmph) => mmph / 25.4);
	}

	private PeriodAveragePrecipitation(Length len, Time t, string name, string[] variations, Func<double, double> convertToBase, Func<double, double> convertFromBase)
		: base(name, variations, convertToBase, convertFromBase)
	{
		VerticalUnits = len;
		TimeUnits = t;
	}

	public double ConvertTo(PeriodAveragePrecipitation t, double doubleValue)
	{
		return BaseConvertTo(t, doubleValue);
	}

	public List<float> ConvertTo(PeriodAveragePrecipitation t, List<float> floatList)
	{
		return BaseConvertTo(t, floatList);
	}

	public List<double> ConvertTo(PeriodAveragePrecipitation t, List<double> doubleList)
	{
		return BaseConvertTo(t, doubleList);
	}

	public new static List<PeriodAveragePrecipitation> GetAvailableTypes()
	{
		return BaseUnitType.AvailableTypes.OfType<PeriodAveragePrecipitation>().ToList();
	}

	public new static List<string> GetAvailableTypeNames()
	{
		return (from t in BaseUnitType.AvailableTypes.OfType<PeriodAveragePrecipitation>()
			select t.Name).ToList();
	}

	public new static PeriodAveragePrecipitation TryParse(string precipUnits)
	{
		return BaseUnitType.BaseParse<PeriodAveragePrecipitation>(precipUnits);
	}
}
