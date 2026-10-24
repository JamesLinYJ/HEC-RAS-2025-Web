using System;
using System.Collections.Generic;
using System.Linq;

namespace Utility.Units;

public class Area : BaseUnitType
{
	public static readonly Area SquaredFeet;

	public static readonly Area SquaredMeters;

	static Area()
	{
		SquaredFeet = new Area("ft^2", new string[1] { "cf" }, null, null);
		SquaredMeters = new Area("m^2", new string[1] { "cm" }, (double sm) => sm * 10.764, (double sf) => sf / 10.764);
	}

	public static Area TryParse(string unitName)
	{
		return BaseUnitType.BaseParse<Area>(unitName);
	}

	private Area(string name, string[] variations, Func<double, double> convertToBase, Func<double, double> convertFromBase)
		: base(name, variations, convertToBase, convertFromBase)
	{
	}

	public double ConvertTo(Area targetUnits, double sourceValue)
	{
		return BaseConvertTo(targetUnits, sourceValue);
	}

	public static List<Area> GetAvailableTypes()
	{
		return BaseUnitType.AvailableTypes.OfType<Area>().ToList();
	}

	public static List<string> GetAvailableTypeNames()
	{
		return (from t in GetAvailableTypes()
			select t.Name).ToList();
	}
}
