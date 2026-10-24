using System;
using System.Collections.Generic;
using System.Linq;

namespace Utility.Units;

public class AirDensity : BaseUnitType
{
	public static readonly AirDensity kilogramPerCubicMeter;

	static AirDensity()
	{
		kilogramPerCubicMeter = new AirDensity("kg/m3", new string[1] { "kg/m3" }, null, null);
	}

	private AirDensity(string name, string[] variations, Func<double, double> convertToBase, Func<double, double> convertFromBase)
		: base(name, variations, convertToBase, convertFromBase)
	{
	}

	public static AirDensity TryParse(string unitName)
	{
		return BaseUnitType.BaseParse<AirDensity>(unitName);
	}

	public double ConvertTo(AirDensity t, double doubleValue)
	{
		return BaseConvertTo(t, doubleValue);
	}

	public static List<AirDensity> GetAvailableTypes()
	{
		return BaseUnitType.AvailableTypes.OfType<AirDensity>().ToList();
	}

	public static List<string> GetAvailableTypeNames()
	{
		return (from t in BaseUnitType.AvailableTypes.OfType<AirDensity>()
			select t.Name).ToList();
	}
}
