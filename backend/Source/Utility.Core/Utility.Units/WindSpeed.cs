using System;
using System.Collections.Generic;
using System.Linq;

namespace Utility.Units;

public class WindSpeed : BaseUnitType
{
	public const string DefaultDataTypeName = "Wind Speed";

	public static readonly WindSpeed MetersPerSecond;

	public static readonly WindSpeed FeetPerSecond;

	public Length LengthUnits { get; }

	static WindSpeed()
	{
		MetersPerSecond = new WindSpeed("m/s", Length.Meters, new string[2] { "metersPerSecond", "meters/second" }, null, null);
		FeetPerSecond = new WindSpeed("ft/s", Length.Feet, new string[3] { "feetPerSecond", "feet/second", "fps" }, (double mps) => mps * 0.3048, (double fps) => fps / 0.3048);
	}

	private WindSpeed(string name, Length lenUnits, string[] variations, Func<double, double> convertToBase, Func<double, double> convertFromBase)
		: base(name, variations, convertToBase, convertFromBase)
	{
		LengthUnits = lenUnits;
	}

	public double ConvertTo(WindSpeed t, double doubleValue)
	{
		return BaseConvertTo(t, doubleValue);
	}

	public static List<WindSpeed> GetAvailableTypes()
	{
		return BaseUnitType.AvailableTypes.OfType<WindSpeed>().ToList();
	}

	public static List<string> GetAvailableTypeNames()
	{
		return (from t in BaseUnitType.AvailableTypes.OfType<WindSpeed>()
			select t.Name).ToList();
	}

	public static Dictionary<string, List<BaseUnitType>> GetUnitDictionary()
	{
		Dictionary<string, List<BaseUnitType>> unitDictionary = new Dictionary<string, List<BaseUnitType>>();
		unitDictionary["Wind Speed"] = new List<BaseUnitType>(new WindSpeed[2] { FeetPerSecond, MetersPerSecond });
		return unitDictionary;
	}

	public static WindSpeed TryParse(string units)
	{
		return BaseUnitType.BaseParse<WindSpeed>(units);
	}
}
