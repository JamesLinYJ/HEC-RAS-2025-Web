using System;
using System.Collections.Generic;
using System.Linq;
using Utility.Extensions;

namespace Utility.Units;

public class BaseUnitType
{
	private const double NoData = -9999.0;

	public static List<BaseUnitType> AvailableTypes = new List<BaseUnitType>();

	protected Func<double, double> ConvertToBaseUnits;

	protected Func<double, double> ConvertFromBaseUnits;

	private static readonly Func<double, double> _identityConverter = (double a) => a;

	public string Name { get; }

	public string[] ParseableStrings { get; }

	public static Type TryParseUnitTypeOf(string dataTypeName)
	{
		if (dataTypeName.RoughlyEquals("Length") || dataTypeName.RoughlyEquals("len"))
		{
			return typeof(Length);
		}
		if (dataTypeName.RoughlyEquals("flow"))
		{
			return typeof(Flow);
		}
		return null;
	}

	public static BaseUnitType TryParseUnitType(string dataTypeName, string unitName)
	{
		if (TryMatchAnyTypeName(Length.UnitTypeNames, dataTypeName))
		{
			return Length.TryParse(unitName);
		}
		if (TryMatchAnyTypeName(Flow.UnitTypeNames, dataTypeName))
		{
			return Flow.TryParse(unitName);
		}
		return null;
	}

	private static bool TryMatchAnyTypeName(IList<string> typeNames, string candidate)
	{
		foreach (string typeName in typeNames)
		{
			if (typeName.RoughlyEquals(candidate))
			{
				return true;
			}
		}
		return false;
	}

	protected BaseUnitType(string name, string[] parseableStrings, Func<double, double> convertToBase = null, Func<double, double> convertFromBase = null)
	{
		Name = name;
		ParseableStrings = parseableStrings;
		ConvertToBaseUnits = convertToBase ?? _identityConverter;
		ConvertFromBaseUnits = convertFromBase ?? _identityConverter;
		AvailableTypes.Add(this);
	}

	public static List<BaseUnitType> GetAvailableTypesBase(Type type)
	{
		return AvailableTypes.Where((BaseUnitType baseUnitType) => baseUnitType.GetType().Equals(type)).ToList();
	}

	protected static List<T> GetAvailableTypesBase<T>() where T : BaseUnitType
	{
		return AvailableTypes.OfType<T>().ToList();
	}

	protected static T BaseParse<T>(string stringValue) where T : BaseUnitType
	{
		if (string.IsNullOrEmpty(stringValue))
		{
			return null;
		}
		IEnumerable<T> enumerable = AvailableTypes.OfType<T>();
		foreach (T enumerable2 in enumerable)
		{
			if (stringValue.RoughlyEquals(enumerable2.Name))
			{
				return enumerable2;
			}
			string[] parseableStrings = enumerable2.ParseableStrings;
			foreach (string other in parseableStrings)
			{
				if (stringValue.RoughlyEquals(other))
				{
					return enumerable2;
				}
			}
		}
		string stripWhitespace = stringValue.StripWhitespace();
		if (stripWhitespace == stringValue)
		{
			return null;
		}
		foreach (T enumerable3 in enumerable)
		{
			if (stripWhitespace.RoughlyEquals(enumerable3.Name))
			{
				return enumerable3;
			}
			string[] parseableStrings = enumerable3.ParseableStrings;
			foreach (string other2 in parseableStrings)
			{
				if (stripWhitespace.RoughlyEquals(other2))
				{
					return enumerable3;
				}
			}
		}
		return null;
	}

	public bool Matches(string stringValue)
	{
		if (string.IsNullOrEmpty(stringValue))
		{
			return false;
		}
		if (stringValue.RoughlyEquals(Name))
		{
			return true;
		}
		string[] parseableStrings = ParseableStrings;
		foreach (string other in parseableStrings)
		{
			if (stringValue.RoughlyEquals(other))
			{
				return true;
			}
		}
		return false;
	}

	protected double BaseConvertTo(BaseUnitType target, double sourceValue)
	{
		if (target.Equals(this))
		{
			return sourceValue;
		}
		double convertToBaseUnits = ConvertToBaseUnits(sourceValue);
		return target.ConvertFromBaseUnits(convertToBaseUnits);
	}

	public List<double> TryConvertTo(BaseUnitType bt, IList<double> values)
	{
		if (GetType() == bt.GetType())
		{
			return BaseConvertTo(bt, values);
		}
		return null;
	}

	public List<float> TryConvertTo(BaseUnitType bt, IList<float> values)
	{
		if (GetType() == bt.GetType())
		{
			return BaseConvertTo(bt, values);
		}
		return null;
	}

	public bool TryConvertTo(BaseUnitType bt, double doubleValue, out double result)
	{
		if (GetType() == bt.GetType())
		{
			result = BaseConvertTo(bt, doubleValue);
			return true;
		}
		result = double.NaN;
		return false;
	}

	protected List<double> BaseConvertTo(BaseUnitType t, IList<double> doubleList)
	{
		if (t.Equals(this))
		{
			return doubleList.ToList();
		}
		List<double> doubleListList = new List<double>();
		for (int i = 0; i < doubleList.Count; i++)
		{
			double computed = doubleList[i];
			if (computed == -9999.0)
			{
				doubleListList.Add(computed);
			}
			else
			{
				doubleListList.Add(t.ConvertFromBaseUnits(ConvertToBaseUnits(doubleList[i])));
			}
		}
		return doubleListList;
	}

	protected List<float> BaseConvertTo(BaseUnitType t, IList<float> floatList)
	{
		if (t.Equals(this))
		{
			return floatList.ToList();
		}
		List<float> floatListList = new List<float>();
		for (int i = 0; i < floatList.Count; i++)
		{
			double computed = floatList[i];
			if (computed == -9999.0)
			{
				floatListList.Add((float)computed);
			}
			else
			{
				floatListList.Add((float)t.ConvertFromBaseUnits(ConvertToBaseUnits(floatList[i])));
			}
		}
		return floatListList;
	}
}
