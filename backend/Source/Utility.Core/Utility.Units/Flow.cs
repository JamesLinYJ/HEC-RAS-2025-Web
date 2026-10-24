using System;
using System.Collections.Generic;
using System.Linq;

namespace Utility.Units;

public class Flow : BaseUnitType
{
	public static readonly string[] UnitTypeNames;

	public static readonly Flow CubicFeetPerSecond;

	public static readonly Flow CubicMetersPerSecond;

	static Flow()
	{
		UnitTypeNames = new string[1] { "flow" };
		CubicFeetPerSecond = new Flow("cfs", new string[2] { "ft3/s", "ft^3/s" }, null, null);
		CubicMetersPerSecond = new Flow("cms", new string[2] { "m3/s", "m^3/s" }, (double cms) => cms * 35.3147, (double cfs) => cfs / 35.3147);
	}

	public static Flow TryParse(string unitName)
	{
		return BaseUnitType.BaseParse<Flow>(unitName);
	}

	private Flow(string name, string[] variations, Func<double, double> convertToBase, Func<double, double> convertFromBase)
		: base(name, variations, convertToBase, convertFromBase)
	{
	}

	public double ConvertTo(Flow targetUnits, double sourceValue)
	{
		return BaseConvertTo(targetUnits, sourceValue);
	}

	public static List<Flow> GetAvailableTypes()
	{
		return BaseUnitType.AvailableTypes.OfType<Flow>().ToList();
	}
}
