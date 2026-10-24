using System;
using System.Collections.Generic;
using System.Linq;

namespace Utility.Units;

public class PeriodCumulativePrecipitation : Precipitation
{
	public const string DefaultDataTypeName = "per-cum";

	public static readonly PeriodCumulativePrecipitation Millimeters;

	public static readonly PeriodCumulativePrecipitation Inches;

	public override PrecipitationDataType DataType => PrecipitationDataType.PeriodCumulative;

	public override Length VerticalUnits { get; }

	static PeriodCumulativePrecipitation()
	{
		Inches = new PeriodCumulativePrecipitation(Length.Inches, Length.Inches.ParseableStrings, (double mm) => mm * 25.4, (double inch) => inch / 25.4);
		List<string> millimetersIds = Length.Millimeters.ParseableStrings.ToList();
		millimetersIds.Add("[kg/m^2]");
		millimetersIds.Add("[kg/(m^2)]");
		millimetersIds.Add("kg/m^2");
		Millimeters = new PeriodCumulativePrecipitation(Length.Millimeters, millimetersIds.ToArray(), null, null);
	}

	private PeriodCumulativePrecipitation(Length len, string[] variations, Func<double, double> convertToBase, Func<double, double> convertFromBase)
		: base(len.Name, variations, convertToBase, convertFromBase)
	{
		VerticalUnits = len;
	}

	public double ConvertTo(PeriodCumulativePrecipitation t, double doubleValue)
	{
		return BaseConvertTo(t, doubleValue);
	}

	public List<float> ConvertTo(PeriodCumulativePrecipitation t, List<float> floatList)
	{
		return BaseConvertTo(t, floatList);
	}

	public List<double> ConvertTo(PeriodCumulativePrecipitation t, List<double> doubleList)
	{
		return BaseConvertTo(t, doubleList);
	}

	public new static List<PeriodCumulativePrecipitation> GetAvailableTypes()
	{
		return BaseUnitType.AvailableTypes.OfType<PeriodCumulativePrecipitation>().ToList();
	}

	public new static List<string> GetAvailableTypeNames()
	{
		return (from t in BaseUnitType.AvailableTypes.OfType<PeriodCumulativePrecipitation>()
			select t.Name).ToList();
	}

	public new static PeriodCumulativePrecipitation TryParse(string precipUnits)
	{
		return BaseUnitType.BaseParse<PeriodCumulativePrecipitation>(precipUnits);
	}
}
