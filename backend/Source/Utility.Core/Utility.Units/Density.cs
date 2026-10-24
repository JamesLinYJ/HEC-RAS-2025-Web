using System;
using System.Collections.Generic;
using System.Linq;

namespace Utility.Units;

public class Density : BaseUnitType
{
	public const string DefaultDataTypeName = "Density";

	public static readonly Density PoundPerCubicFoot;

	public static readonly Density KilogramPerCubicMeter;

	public Weight WeightUnits { get; set; }

	public Volume VolumeUnits { get; set; }

	static Density()
	{
		PoundPerCubicFoot = new Density("lb/ft^3", Weight.Pounds, Volume.CubicFeet, new string[8] { "lb/ft3", "poundpercubicfoot", "lb/cf", "lbs/ft^3", "lbs/cf", "lbs/ft3", "pcf", "p/cf" }, null, null);
		KilogramPerCubicMeter = new Density("kg/m^3", Weight.Kilograms, Volume.CubicMeters, new string[6] { "kilogram/meter^3", "kilogrampercubicmeter", "kilo/m^3", "kg/m3", "kgcm", "kg/cm" }, (double kgmc) => kgmc / 16.01846, (double pcf) => pcf * 16.01846);
	}

	private Density(string name, Weight weightUnits, Volume volumeUnits, string[] variations, Func<double, double> convertToBase, Func<double, double> convertFromBase)
		: base(name, variations, convertToBase, convertFromBase)
	{
		WeightUnits = weightUnits;
		VolumeUnits = volumeUnits;
	}

	public double ConvertTo(Density t, double doubleValue)
	{
		return BaseConvertTo(t, doubleValue);
	}

	public static List<Density> GetAvailableTypes()
	{
		return BaseUnitType.AvailableTypes.OfType<Density>().ToList();
	}

	public static List<string> GetAvailableTypeNames()
	{
		return (from t in BaseUnitType.AvailableTypes.OfType<Density>()
			select t.Name).ToList();
	}

	public static Dictionary<string, List<BaseUnitType>> GetUnitDictionary()
	{
		Dictionary<string, List<BaseUnitType>> unitDictionary = new Dictionary<string, List<BaseUnitType>>();
		unitDictionary["Density"] = new List<BaseUnitType>(new Density[2] { PoundPerCubicFoot, KilogramPerCubicMeter });
		return unitDictionary;
	}

	public static Density TryParse(string units)
	{
		return BaseUnitType.BaseParse<Density>(units);
	}
}
