using System;
using System.Collections.Generic;
using System.Linq;

namespace Utility.Units;

public class Direction : BaseUnitType
{
	public static readonly Direction Bearing;

	static Direction()
	{
		Bearing = new Direction("Bearing (CW from North)", new string[3] { "Bearing", "Degrees", "Deg" }, null, null);
	}

	private Direction(string name, string[] variations, Func<double, double> convertToBase, Func<double, double> convertFromBase)
		: base(name, variations, convertToBase, convertFromBase)
	{
	}

	public double ConvertTo(Direction t, double doubleValue)
	{
		return BaseConvertTo(t, doubleValue);
	}

	public static Direction TryParse(string unitName)
	{
		return BaseUnitType.BaseParse<Direction>(unitName);
	}

	public static List<Direction> GetAvailableTypes()
	{
		return BaseUnitType.AvailableTypes.OfType<Direction>().ToList();
	}

	public static List<string> GetAvailableTypeNames()
	{
		return (from t in BaseUnitType.AvailableTypes.OfType<Direction>()
			select t.Name).ToList();
	}
}
