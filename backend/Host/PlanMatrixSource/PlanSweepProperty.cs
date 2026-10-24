// Renamed source algorithm copied without UI/WinRT dependencies.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Ras.Layers;
using Utility.Attributes;
using Utility.ORM;
using Utility.Units;

namespace Ras.Interface.WinUI.Debug.R2RMatrix;

public sealed class PlanSweepProperty
{
	private readonly bool _isDenominatedInSeconds;

	private static readonly Dictionary<string, string> FilenamePrefixes = new Dictionary<string, string>
	{
		["SolverType"] = "",
		["TimeStepping"] = "",
		["EquationSet"] = "",
		["TurbulenceModel"] = "",
		["ComputeTimestep"] = "Dt",
		["BaseOutputInterval"] = "Out",
		["MinAdvectiveTimeStep"] = "MinAdvDt",
		["UseMinAdvectiveTimeStep"] = "MinAdv",
		["MaxPercentWetAdvTimeStep"] = "MaxWet",
		["TimestepSafetyFactor"] = "Sf",
		["MaxCourant"] = "Cr",
		["CoreCount"] = "Cores",
		["ParallelMethod"] = "Threads",
		["LtsLevels"] = "LTS",
		["ImplicitFactor"] = "ImpF",
		["AdvectionMethod"] = "Adv",
		["AdvectionImplicitFactor"] = "AdvImpF",
		["MaxInnerIterations"] = "VolIter",
		["MaxOuterIterations"] = "WsIter",
		["InternalComputeDurationSeconds"] = "Dur",
		["InternalStartTime"] = "Start",
		["BaseEddyViscosity"] = "EddyVisc"
	};

	private static readonly string[] PreferredOrder = new string[43]
	{
		"SolverType", "TimeStepping", "EquationSet", "ComputeTimestep", "BaseOutputInterval", "TimeWindowMode", "InternalStartTime", "InternalComputeDurationSeconds", "CoreCount", "ParallelMethod",
		"UseGPUAdaptive", "LtsLevels", "TimestepSafetyFactor", "UseMinAdvectiveTimeStep", "MinAdvectiveTimeStep", "MaxPercentWetAdvTimeStep", "UseFBF", "Theta", "ImplicitAdaptiveTimestepping", "MaxCourant",
		"ImplicitFactor", "UsePartialGrid", "ActiveThresholdPartGrid", "BufferCountPartGrid", "ElevationThresholdPartGrid", "MaxInnerIterations", "VolumeTolerance", "VolumeMaxTolerance", "VolumeRMSTolerance", "MaxOuterIterations",
		"WaterSurfaceTolerance", "WaterSurfaceMaxTolerance", "WaterSurfaceRMSTolerance", "UseAdvection", "AdvectionMethod", "AdvectionImplicitFactor", "UseDiffusion", "TurbulenceModel", "BaseEddyViscosity", "LongMixCoeff",
		"TransMixCoeff", "HorizMixCoeff", "Latitude"
	};

	private static IReadOnlyList<PlanSweepProperty> _all;

	public string PropertyName { get; }

	public string DisplayName { get; }

	public string FilenamePrefix { get; }

	public PropertyInfo Info { get; }

	public Type ValueType => Info.PropertyType;

	public IReadOnlyList<object> EnumeratedValues { get; }

	public bool IsClosedSet => EnumeratedValues.Count > 0;

	public string InputHint { get; }

	public static IReadOnlyList<PlanSweepProperty> All => _all ?? (_all = Build());

	private PlanSweepProperty(PropertyInfo info, string displayName, string filenamePrefix, IReadOnlyList<object> enumeratedValues, string inputHint)
	{
		Info = info;
		PropertyName = info.Name;
		DisplayName = displayName;
		FilenamePrefix = filenamePrefix;
		EnumeratedValues = enumeratedValues;
		_isDenominatedInSeconds = info.PropertyType == typeof(double) && (info.Name.Contains("TimeStep", StringComparison.OrdinalIgnoreCase) || info.Name.Contains("Seconds", StringComparison.OrdinalIgnoreCase) || info.Name.Contains("Duration", StringComparison.OrdinalIgnoreCase));
		InputHint = (_isDenominatedInSeconds ? "seconds, e.g. 0.1, 1, 5" : inputHint);
	}

	public object GetValue(Plan plan)
	{
		return Info.GetValue(plan);
	}

	public void SetValue(Plan plan, object assignedValue)
	{
		Info.SetValue(plan, assignedValue);
	}

	public bool TryParse(string stringValue, out object conditionTerm, out string error)
	{
		conditionTerm = null;
		error = null;
		string trimTerm = stringValue?.Trim();
		if (string.IsNullOrEmpty(trimTerm))
		{
			error = "Enter a value.";
			return false;
		}
		Type valueType = ValueType;
		if (valueType == typeof(bool))
		{
			if (TryParseBool(trimTerm, out var condition))
			{
				conditionTerm = condition;
				return true;
			}
			error = "Expected true or false.";
			return false;
		}
		if (valueType.IsEnum)
		{
			foreach (object enumeratedValue in EnumeratedValues)
			{
				if (enumeratedValue.ToString().Equals(trimTerm, StringComparison.OrdinalIgnoreCase) || GetEnumDisplayName(enumeratedValue).Equals(trimTerm, StringComparison.OrdinalIgnoreCase))
				{
					conditionTerm = enumeratedValue;
					return true;
				}
			}
			error = "Allowed values: " + string.Join(", ", EnumeratedValues.Select((object v) => v.ToString())) + ".";
			return false;
		}
		if (valueType == typeof(int))
		{
			if (int.TryParse(trimTerm, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
			{
				conditionTerm = result;
				return true;
			}
			error = "Expected a whole number.";
			return false;
		}
		if (valueType == typeof(double))
		{
			if (TryParseDouble(StripSecondsSuffix(trimTerm), out var computed))
			{
				conditionTerm = computed;
				return true;
			}
			error = "Expected a number.";
			return false;
		}
		if (valueType == typeof(TimeSpan))
		{
			if (Time.TryParseTimestep(trimTerm, out var ts))
			{
				conditionTerm = ts;
				return true;
			}
			if (TimeSpan.TryParse(trimTerm, CultureInfo.InvariantCulture, out ts))
			{
				conditionTerm = ts;
				return true;
			}
			if (TryParseDouble(trimTerm, out var computed2))
			{
				conditionTerm = TimeSpan.FromSeconds(computed2);
				return true;
			}
			error = "Expected a duration, e.g. \"0.1s\", \"5m\" or \"1:30:00\".";
			return false;
		}
		if (valueType == typeof(DateTime))
		{
			if (DateTime.TryParse(trimTerm, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result2))
			{
				conditionTerm = result2;
				return true;
			}
			error = "Expected a date/time, e.g. \"2000-01-01 06:00\".";
			return false;
		}
		if (valueType == typeof(string))
		{
			conditionTerm = trimTerm;
			return true;
		}
		error = "Unsupported property type " + valueType.Name + ".";
		return false;
	}

	public string Describe(object localValue)
	{
		if (localValue == null)
		{
			return "<null>";
		}
		if (localValue is bool)
		{
			if (!(bool)localValue)
			{
				return "False";
			}
			return "True";
		}
		if (ValueType.IsEnum)
		{
			return GetEnumDisplayName(localValue);
		}
		if (localValue is double computed)
		{
			return computed.ToString("0.######", CultureInfo.InvariantCulture);
		}
		if (localValue is TimeSpan ts)
		{
			return DescribeTimeSpan(ts);
		}
		if (localValue is DateTime dateTime)
		{
			return dateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
		}
		return Convert.ToString(localValue, CultureInfo.InvariantCulture);
	}

	public string Token(object localValue)
	{
		string valueToken = ValueToken(localValue);
		if (string.IsNullOrEmpty(FilenamePrefix))
		{
			return valueToken;
		}
		return FilenamePrefix + "-" + valueToken;
	}

	private string ValueToken(object localValue)
	{
		if (localValue == null)
		{
			return "null";
		}
		if (localValue is bool)
		{
			if (!(bool)localValue)
			{
				return "Off";
			}
			return "On";
		}
		if (ValueType.IsEnum)
		{
			return EnumToken(localValue.ToString());
		}
		if (localValue is double computed)
		{
			string invariantCultureCultureInfoTerm = computed.ToString("0.######", CultureInfo.InvariantCulture);
			if (!_isDenominatedInSeconds)
			{
				return invariantCultureCultureInfoTerm;
			}
			return invariantCultureCultureInfoTerm + "s";
		}
		if (localValue is TimeSpan ts)
		{
			return DescribeTimeSpan(ts);
		}
		if (localValue is DateTime dateTime)
		{
			return dateTime.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
		}
		return Sanitize(Convert.ToString(localValue, CultureInfo.InvariantCulture));
	}

	private string EnumToken(string memberName)
	{
		if (memberName.Length > PropertyName.Length && memberName.StartsWith(PropertyName, StringComparison.OrdinalIgnoreCase))
		{
			memberName = memberName.Substring(PropertyName.Length);
		}
		if (!memberName.Contains('_'))
		{
			return memberName;
		}
		StringBuilder stringBuilder = new StringBuilder();
		string[] parts = memberName.Split('_', StringSplitOptions.RemoveEmptyEntries);
		foreach (string part in parts)
		{
			stringBuilder.Append(char.ToUpperInvariant(part[0]));
			stringBuilder.Append(part.Substring(1).ToLowerInvariant());
		}
		return stringBuilder.ToString();
	}

	private static string DescribeTimeSpan(TimeSpan ts)
	{
		double totalSeconds = ts.TotalSeconds;
		if (Math.Abs(totalSeconds) < 60.0)
		{
			return Format(totalSeconds) + "s";
		}
		if (Math.Abs(ts.TotalMinutes) < 60.0)
		{
			return Format(ts.TotalMinutes) + "m";
		}
		if (Math.Abs(ts.TotalHours) < 48.0)
		{
			return Format(ts.TotalHours) + "h";
		}
		return Format(ts.TotalDays) + "d";
		static string Format(double computed)
		{
			return computed.ToString("0.######", CultureInfo.InvariantCulture);
		}
	}

	private static string GetEnumDisplayName(object localValue)
	{
		MemberInfo[] member = localValue.GetType().GetMember(localValue.ToString());
		if (member.Length != 0)
		{
			EnumDisplayName customAttribute = member[0].GetCustomAttribute<EnumDisplayName>();
			if (customAttribute != null)
			{
				return customAttribute.DisplayName;
			}
		}
		return localValue.ToString();
	}

	private static bool TryParseBool(string textValue, out bool condition)
	{
		string toLowerInvariant = textValue.ToLowerInvariant();
		if (toLowerInvariant != null)
		{
			switch (toLowerInvariant.Length)
			{
			case 1:
			{
				char c = toLowerInvariant[0];
				if ((uint)c <= 102u)
				{
					if (c != '0')
					{
						if (c == '1')
						{
							goto IL_00fe;
						}
						if (c != 'f')
						{
							break;
						}
					}
				}
				else if (c != 'n')
				{
					if (c != 't' && c != 'y')
					{
						break;
					}
					goto IL_00fe;
				}
				goto IL_0103;
			}
			case 3:
			{
				char c = toLowerInvariant[0];
				if (c != 'o')
				{
					if (c != 'y' || !(toLowerInvariant == "yes"))
					{
						break;
					}
					goto IL_00fe;
				}
				if (!(toLowerInvariant == "off"))
				{
					break;
				}
				goto IL_0103;
			}
			case 2:
			{
				char c = toLowerInvariant[0];
				if (c != 'n')
				{
					if (c != 'o' || !(toLowerInvariant == "on"))
					{
						break;
					}
					goto IL_00fe;
				}
				if (!(toLowerInvariant == "no"))
				{
					break;
				}
				goto IL_0103;
			}
			case 4:
				if (!(toLowerInvariant == "true"))
				{
					break;
				}
				goto IL_00fe;
			case 5:
				{
					if (!(toLowerInvariant == "false"))
					{
						break;
					}
					goto IL_0103;
				}
				IL_0103:
				condition = false;
				return true;
				IL_00fe:
				condition = true;
				return true;
			}
		}
		condition = false;
		return false;
	}

	private string StripSecondsSuffix(string textValue)
	{
		if (!_isDenominatedInSeconds)
		{
			return textValue;
		}
		string[] lookupTable = new string[4] { "seconds", "secs", "sec", "s" };
		foreach (string lookupTable2 in lookupTable)
		{
			if (textValue.EndsWith(lookupTable2, StringComparison.OrdinalIgnoreCase))
			{
				return textValue.Substring(0, textValue.Length - lookupTable2.Length).Trim();
			}
		}
		return textValue;
	}

	private static bool TryParseDouble(string textValue, out double computed)
	{
		if (double.TryParse(textValue, NumberStyles.Float, CultureInfo.InvariantCulture, out computed))
		{
			return true;
		}
		return double.TryParse(textValue, NumberStyles.Float, CultureInfo.CurrentCulture, out computed);
	}

	public static string Sanitize(string stringValue)
	{
		if (string.IsNullOrEmpty(stringValue))
		{
			return string.Empty;
		}
		return string.Join("", stringValue.Split(Path.GetInvalidFileNameChars())).Replace(' ', '-');
	}

	public static PlanSweepProperty Find(string propertyName)
	{
		return All.FirstOrDefault((PlanSweepProperty p) => p.PropertyName == propertyName);
	}

	private static IReadOnlyList<PlanSweepProperty> Build()
	{
		List<PlanSweepProperty> planSweepProperties = new List<PlanSweepProperty>();
		PropertyInfo[] properties = typeof(Plan).GetProperties(BindingFlags.Instance | BindingFlags.Public);
		foreach (PropertyInfo propertyInfo in properties)
		{
			if (!(propertyInfo.DeclaringType != typeof(Plan)) && propertyInfo.CanRead && propertyInfo.CanWrite && propertyInfo.GetIndexParameters().Length == 0)
			{
				MetadataAttribute customAttribute = propertyInfo.GetCustomAttribute<MetadataAttribute>();
				if (customAttribute != null && TryDescribeType(propertyInfo.PropertyType, out var enumeratedValues, out var inputHint))
				{
					string displayName = (string.IsNullOrEmpty(customAttribute.KeyName) ? propertyInfo.Name : customAttribute.KeyName);
					string filenamePrefix = (FilenamePrefixes.TryGetValue(propertyInfo.Name, out var localValue) ? localValue : propertyInfo.Name);
					planSweepProperties.Add(new PlanSweepProperty(propertyInfo, displayName, filenamePrefix, enumeratedValues, inputHint));
				}
			}
		}
		Dictionary<string, int> order = PreferredOrder.Select((string name, int index) => (name: name, index: index)).ToDictionary(((string name, int index) x) => x.name, ((string name, int index) x) => x.index);
		return planSweepProperties.OrderBy((PlanSweepProperty p) => (!order.TryGetValue(p.PropertyName, out var localValue2)) ? int.MaxValue : localValue2).ThenBy((PlanSweepProperty p) => p.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private static bool TryDescribeType(Type type, out IReadOnlyList<object> enumeratedValues, out string inputHint)
	{
		enumeratedValues = Array.Empty<object>();
		inputHint = string.Empty;
		if (type == typeof(bool))
		{
			enumeratedValues = new object[2] { false, true };
			return true;
		}
		if (type.IsEnum)
		{
			enumeratedValues = Enum.GetValues(type).Cast<object>().ToList();
			return true;
		}
		if (type == typeof(int))
		{
			inputHint = "e.g. 4, 8, 16";
			return true;
		}
		if (type == typeof(double))
		{
			inputHint = "e.g. 0.1, 1, 5";
			return true;
		}
		if (type == typeof(TimeSpan))
		{
			inputHint = "e.g. 0.1s, 5s, 1m";
			return true;
		}
		if (type == typeof(DateTime))
		{
			inputHint = "e.g. 2000-01-01 06:00";
			return true;
		}
		if (type == typeof(string))
		{
			inputHint = "text value";
			return true;
		}
		return false;
	}
}
