using System;
using System.Collections.Generic;
using System.Linq;

namespace Utility.Units;

public class WindDirection : BaseUnitType
{
	public const string DefaultDataTypeName = "Wind Direction";

	public static readonly WindDirection Bearing;

	static WindDirection()
	{
		Bearing = new WindDirection("Bearing (CW from North)", new string[3] { "Bearing", "Degrees", "Deg" }, null, null);
	}

	private WindDirection(string name, string[] variations, Func<double, double> convertToBase, Func<double, double> convertFromBase)
		: base(name, variations, convertToBase, convertFromBase)
	{
	}

	public double ConvertTo(WindDirection t, double doubleValue)
	{
		return BaseConvertTo(t, doubleValue);
	}

	public static WindDirection TryParse(string unitName)
	{
		return BaseUnitType.BaseParse<WindDirection>(unitName);
	}

	public static List<WindDirection> GetAvailableTypes()
	{
		return BaseUnitType.AvailableTypes.OfType<WindDirection>().ToList();
	}

	public static List<string> GetAvailableTypeNames()
	{
		return (from t in BaseUnitType.AvailableTypes.OfType<WindDirection>()
			select t.Name).ToList();
	}

	public static Dictionary<string, List<BaseUnitType>> GetUnitDictionary()
	{
		Dictionary<string, List<BaseUnitType>> unitDictionary = new Dictionary<string, List<BaseUnitType>>();
		unitDictionary["Wind Direction"] = new List<BaseUnitType>(new WindDirection[1] { Bearing });
		return unitDictionary;
	}
}
