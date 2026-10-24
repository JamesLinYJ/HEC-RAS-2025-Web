using System;
using System.Collections.Generic;
using System.Linq;

namespace Utility.Units;

public class Temperature : BaseUnitType
{
	public static readonly Temperature Celsius;

	public static readonly Temperature Fahrenheit;

	static Temperature()
	{
		Celsius = new Temperature("°C", new string[4] { "C", "DegC", "Celsius", "Centigrade" }, null, null);
		Fahrenheit = new Temperature("°F", new string[3] { "F", "DegF", "Fahrenheit" }, (double f) => (f - 32.0) * 5.0 / 9.0, (double celsius) => celsius * 9.0 / 5.0 + 32.0);
	}

	private Temperature(string name, string[] variations, Func<double, double> convertToBase, Func<double, double> convertFromBase)
		: base(name, variations, convertToBase, convertFromBase)
	{
	}

	public double ConvertTo(Temperature t, double doubleValue)
	{
		return BaseConvertTo(t, doubleValue);
	}

	public static Temperature TryParse(string unitName)
	{
		return BaseUnitType.BaseParse<Temperature>(unitName);
	}

	public static List<Temperature> GetAvailableTypes()
	{
		return BaseUnitType.AvailableTypes.OfType<Temperature>().ToList();
	}

	public static List<string> GetAvailableTypeNames()
	{
		return (from t in BaseUnitType.AvailableTypes.OfType<Temperature>()
			select t.Name).ToList();
	}
}
