using System;
using System.Collections.Generic;
using System.Linq;

namespace Utility.Units;

public class Volume : BaseUnitType
{
	public static readonly Volume CubicFeet;

	public static readonly Volume AcreFeet;

	public static readonly Volume CubicMeters;

	static Volume()
	{
		CubicFeet = new Volume("ft^3", new string[4] { "ft3", "cu-ft", "cu ft", "cubic feet" }, null, null);
		AcreFeet = new Volume("acrefeet", new string[4] { "ft3", "cu-ft", "cu ft", "cubic feet" }, null, null);
		CubicMeters = new Volume("m^3", new string[4] { "m3", "cu-m", "cubic meter", "cubic meters" }, (double cuMeters) => cuMeters * 35.3147, (double cuFeet) => cuFeet / 35.3147);
	}

	public static Volume TryParse(string unitName)
	{
		return BaseUnitType.BaseParse<Volume>(unitName);
	}

	private Volume(string name, string[] variations, Func<double, double> convertToBase, Func<double, double> convertFromBase)
		: base(name, variations, convertToBase, convertFromBase)
	{
	}

	public double ConvertTo(Volume targetUnits, double sourceValue)
	{
		return BaseConvertTo(targetUnits, sourceValue);
	}

	public static List<Volume> GetAvailableTypes()
	{
		return BaseUnitType.AvailableTypes.OfType<Volume>().ToList();
	}

	public static List<string> GetAvailableTypeNames()
	{
		return (from t in GetAvailableTypes()
			select t.Name).ToList();
	}
}
