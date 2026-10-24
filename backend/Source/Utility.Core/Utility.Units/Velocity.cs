using System;
using System.Collections.Generic;
using System.Linq;

namespace Utility.Units;

public class Velocity : BaseUnitType
{
	public const string DefaultDataTypeName = "Velocity";

	public static readonly Velocity MetersPerSecond;

	public static readonly Velocity FeetPerSecond;

	public Length LengthUnits { get; }

	public Time TimeUnits { get; }

	static Velocity()
	{
		MetersPerSecond = new Velocity("m/s", Length.Meters, Time.Seconds, new string[2] { "metersPerSecond", "meters/second" }, null, null);
		FeetPerSecond = new Velocity("ft/s", Length.Feet, Time.Seconds, new string[2] { "feetPerSecond", "feet/second" }, (double mps) => mps * 0.3048, (double fps) => fps / 0.3048);
	}

	private Velocity(string name, Length lenUnits, Time timeUnits, string[] variations, Func<double, double> convertToBase, Func<double, double> convertFromBase)
		: base(name, variations, convertToBase, convertFromBase)
	{
		LengthUnits = lenUnits;
		TimeUnits = timeUnits;
	}

	public double ConvertTo(Velocity t, double doubleValue)
	{
		return BaseConvertTo(t, doubleValue);
	}

	public static List<Velocity> GetAvailableTypes()
	{
		return BaseUnitType.AvailableTypes.OfType<Velocity>().ToList();
	}

	public static List<string> GetAvailableTypeNames()
	{
		return (from t in BaseUnitType.AvailableTypes.OfType<Velocity>()
			select t.Name).ToList();
	}

	public static Dictionary<string, List<BaseUnitType>> GetUnitDictionary()
	{
		Dictionary<string, List<BaseUnitType>> unitDictionary = new Dictionary<string, List<BaseUnitType>>();
		unitDictionary["Velocity"] = new List<BaseUnitType>(new Velocity[2] { FeetPerSecond, MetersPerSecond });
		return unitDictionary;
	}

	public static Velocity TryParse(string units)
	{
		return BaseUnitType.BaseParse<Velocity>(units);
	}
}
