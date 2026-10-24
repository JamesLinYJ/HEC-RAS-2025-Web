using System;
using System.Collections.Generic;
using System.Linq;

namespace Utility.Units;

public class Length : BaseUnitType
{
	public const double FeetToMeters = 0.3048;

	public const double MetersToFeet = 1.0 / FeetToMeters;

	public static readonly string[] UnitTypeNames;

	public static readonly Length Inches;

	public static readonly Length Feet;

	public static readonly Length Miles;

	public static readonly Length Meters;

	public static readonly Length Millimeters;

	public static readonly Length Kilometers;

	static Length()
	{
		UnitTypeNames = new string[2] { "length", "len" };
		Inches = new Length("in", new string[2] { "inch", "inches" }, null, null);
		Feet = new Length("ft", new string[3] { "foot", "feet", "us-foot" }, (double ft) => ft * 12.0, (double inches) => inches / 12.0);
		Miles = new Length("mi", new string[2] { "mile", "miles" }, (double mi) => mi * 12.0 * 5280.0, (double inches) => inches / 63360.0);
		Meters = new Length("m", new string[3] { "meters", "meter", "metre" }, (double mt) => mt / 0.0254, (double inches) => inches * 0.0254);
		Millimeters = new Length("mm", new string[2] { "millimeter", "millimeters" }, (double mm) => mm / 25.4, (double inches) => inches * 25.4);
		Kilometers = new Length("km", new string[3] { "km", "kilometer", "kilometre" }, (double km) => km * 1000.0 / 0.0254, (double inches) => inches * 0.0254 / 1000.0);
	}

	public static Length TryParse(string unitName)
	{
		return BaseUnitType.BaseParse<Length>(unitName);
	}

	private Length(string name, string[] variations, Func<double, double> convertToBase, Func<double, double> convertFromBase)
		: base(name, variations, convertToBase, convertFromBase)
	{
	}

	public double ConvertTo(Length targetUnits, double sourceValue)
	{
		return BaseConvertTo(targetUnits, sourceValue);
	}

	public static List<Length> GetAvailableTypes()
	{
		return BaseUnitType.AvailableTypes.OfType<Length>().ToList();
	}
}
