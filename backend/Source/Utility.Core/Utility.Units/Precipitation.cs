using System;
using System.Collections.Generic;
using System.Linq;
using Utility.Extensions;

namespace Utility.Units;

public abstract class Precipitation : BaseUnitType
{
	public abstract PrecipitationDataType DataType { get; }

	public abstract Length VerticalUnits { get; }

	public string DataTypeName
	{
		get
		{
			if (DataType != PrecipitationDataType.PeriodCumulative)
			{
				return "per-avg";
			}
			return "per-cum";
		}
	}

	static Precipitation()
	{
		_ = PeriodAveragePrecipitation.InchesPerHour;
		_ = PeriodCumulativePrecipitation.Inches;
	}

	public static List<Precipitation> GetAvailableTypes()
	{
		return BaseUnitType.AvailableTypes.OfType<Precipitation>().ToList();
	}

	public static List<string> GetAvailableTypeNames()
	{
		return (from t in BaseUnitType.AvailableTypes.OfType<Precipitation>()
			select t.Name).ToList();
	}

	public static PrecipitationDataType ParseDataType(string stringValue)
	{
		if (stringValue.RoughlyEquals("per-avg") || stringValue.RoughlyEquals("per-aver"))
		{
			return PrecipitationDataType.PeriodAverageRate;
		}
		if (stringValue.RoughlyEquals("per-cum"))
		{
			return PrecipitationDataType.PeriodCumulative;
		}
		if (stringValue.RoughlyEquals("inst-val"))
		{
			return PrecipitationDataType.InstantaneousRate;
		}
		if (stringValue.RoughlyEquals("cumulative"))
		{
			return PrecipitationDataType.Cumulative;
		}
		return PrecipitationDataType.Unknown;
	}

	protected Precipitation(string name, string[] variations, Func<double, double> convertToBase, Func<double, double> convertFromBase)
		: base(name, variations, convertToBase, convertFromBase)
	{
	}

	public static Precipitation TryParse(string precipUnits)
	{
		return BaseUnitType.BaseParse<Precipitation>(precipUnits);
	}

	public static Dictionary<string, List<BaseUnitType>> GetPrecipUnitDictionary()
	{
		return new Dictionary<string, List<BaseUnitType>>
		{
			["per-cum"] = PeriodCumulativePrecipitation.GetAvailableTypes().Cast<BaseUnitType>().ToList(),
			["per-avg"] = PeriodAveragePrecipitation.GetAvailableTypes().Cast<BaseUnitType>().ToList()
		};
	}
}
