using System;
using System.Collections.Generic;
using System.Linq;

namespace Utility.Units;

public class AirPressure : BaseUnitType
{
	public static readonly AirPressure Pascals;

	public static readonly AirPressure KiloPascals;

	public static readonly AirPressure MillimeterMercury;

	public static readonly AirPressure MilliBars;

	static AirPressure()
	{
		Pascals = new AirPressure("Pa", new string[2] { "Pa", "Pascal" }, null, null);
		KiloPascals = new AirPressure("kPa", new string[2] { "kPa", "kPascal" }, (double Pa) => Pa * 1000.0, (double kPa) => kPa / 1000.0);
		MillimeterMercury = new AirPressure("mmHg", new string[2] { "mm Hg", "mmHg" }, (double mmHg) => mmHg * 133.322, (double Pa) => Pa / 133.322);
		MilliBars = new AirPressure("mb", new string[5] { "mb", "millibar", "mbar", "hPa", "hectopascal" }, (double mb) => mb * 100.0, (double Pa) => Pa / 100.0);
	}

	public static AirPressure TryParse(string unitName)
	{
		return BaseUnitType.BaseParse<AirPressure>(unitName);
	}

	private AirPressure(string name, string[] variations, Func<double, double> convertToBase, Func<double, double> convertFromBase)
		: base(name, variations, convertToBase, convertFromBase)
	{
	}

	public double ConvertTo(AirPressure targetUnits, double sourceValue)
	{
		return BaseConvertTo(targetUnits, sourceValue);
	}

	public static List<AirPressure> GetAvailableTypes()
	{
		return BaseUnitType.AvailableTypes.OfType<AirPressure>().ToList();
	}

	public static List<string> GetAvailableTypeNames()
	{
		return (from t in GetAvailableTypes()
			select t.Name).ToList();
	}
}
