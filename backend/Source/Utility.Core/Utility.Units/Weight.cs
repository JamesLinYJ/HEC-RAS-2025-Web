using System;
using System.Collections.Generic;
using System.Linq;

namespace Utility.Units;

public class Weight : BaseUnitType
{
	public const string DefaultDataTypeName = "Weight";

	public static readonly Weight Grams;

	public static readonly Weight Kilograms;

	public static readonly Weight Pounds;

	static Weight()
	{
		Grams = new Weight("g", new string[2] { "gram", "gram" }, null, null);
		Kilograms = new Weight("kg", new string[3] { "kilogram", "kilo", "kilos" }, (double kg) => kg * 1000.0, (double g) => g / 1000.0);
		Pounds = new Weight("lb", new string[3] { "lbs", "pounds", "pound" }, (double lb) => lb * 453.592, (double g) => g / 453.592);
	}

	private Weight(string name, string[] variations, Func<double, double> convertToBase, Func<double, double> convertFromBase)
		: base(name, variations, convertToBase, convertFromBase)
	{
	}

	public double ConvertTo(Weight t, double doubleValue)
	{
		return BaseConvertTo(t, doubleValue);
	}

	public static List<Weight> GetAvailableTypes()
	{
		return BaseUnitType.AvailableTypes.OfType<Weight>().ToList();
	}

	public static List<string> GetAvailableTypeNames()
	{
		return (from t in BaseUnitType.AvailableTypes.OfType<Weight>()
			select t.Name).ToList();
	}

	public static Dictionary<string, List<BaseUnitType>> GetUnitDictionary()
	{
		Dictionary<string, List<BaseUnitType>> unitDictionary = new Dictionary<string, List<BaseUnitType>>();
		unitDictionary["Weight"] = new List<BaseUnitType>(new Weight[3] { Grams, Kilograms, Pounds });
		return unitDictionary;
	}

	public static Weight TryParse(string units)
	{
		return BaseUnitType.BaseParse<Weight>(units);
	}
}
