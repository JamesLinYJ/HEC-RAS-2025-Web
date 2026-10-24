using System;
using System.Collections.Generic;
using System.Linq;

namespace Utility.Units;

public class CumulativePrecipitation : Precipitation
{
	public const string DefaultDataTypeName = "cumulative";

	public static readonly CumulativePrecipitation Millimeters;

	public static readonly CumulativePrecipitation Inches;

	public override PrecipitationDataType DataType => PrecipitationDataType.Cumulative;

	public override Length VerticalUnits { get; }

	static CumulativePrecipitation()
	{
		Millimeters = new CumulativePrecipitation(Length.Millimeters, Length.Millimeters.ParseableStrings, null, null);
		Inches = new CumulativePrecipitation(Length.Inches, Length.Inches.ParseableStrings, (double mm) => mm * 25.4, (double inch) => inch / 25.4);
	}

	private CumulativePrecipitation(Length len, string[] variations, Func<double, double> convertToBase, Func<double, double> convertFromBase)
		: base(len.Name, variations, convertToBase, convertFromBase)
	{
		VerticalUnits = len;
	}

	public double ConvertTo(CumulativePrecipitation t, double doubleValue)
	{
		return BaseConvertTo(t, doubleValue);
	}

	public List<float> ConvertTo(CumulativePrecipitation t, List<float> floatList)
	{
		return BaseConvertTo(t, floatList);
	}

	public List<double> ConvertTo(CumulativePrecipitation t, List<double> doubleList)
	{
		return BaseConvertTo(t, doubleList);
	}

	public new static List<CumulativePrecipitation> GetAvailableTypes()
	{
		return BaseUnitType.AvailableTypes.OfType<CumulativePrecipitation>().ToList();
	}

	public new static List<string> GetAvailableTypeNames()
	{
		return (from t in BaseUnitType.AvailableTypes.OfType<CumulativePrecipitation>()
			select t.Name).ToList();
	}

	public new static CumulativePrecipitation TryParse(string precipUnits)
	{
		return BaseUnitType.BaseParse<CumulativePrecipitation>(precipUnits);
	}
}
