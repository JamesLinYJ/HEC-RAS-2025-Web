using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Utility.Progress;

namespace Utility.Systems;

public static class DeepObjectInspection
{
	private const string SPACE = "  ";

	public static void Print(object localValue, string title, ProgressReporter prog)
	{
		PrintInternal(localValue, title, new HashSet<object>(), prog);
	}

	private static void PrintInternal(object localValue, string title, HashSet<object> hs, ProgressReporter prog)
	{
		if (hs.Contains(localValue))
		{
			prog.ReportMessage(title + " (Already displayed)");
			return;
		}
		hs.Add(localValue);
		Type type = localValue.GetType();
		prog.ReportMessage(title + " (" + type.Name + ")");
		prog.ReportMessage("{");
		PropertyInfo[] properties = type.GetProperties();
		foreach (PropertyInfo prop in properties)
		{
			PrintProp(localValue, prop, hs, TabOnce(prog));
		}
		prog.ReportMessage("}");
	}

	private static void PrintProp(object localValue, PropertyInfo prop, HashSet<object> hs, ProgressReporter prog)
	{
		if (prop.PropertyType.IsPrimitive || prop.PropertyType == typeof(string))
		{
			prog.ReportMessage(prop.Name + " (" + prop.PropertyType.Name + "): " + prop.GetValue(localValue));
		}
		else
		{
			prog.ReportMessage(prop.Name + " (" + prop.PropertyType?.ToString() + ")");
			prog.ReportMessage("{");
			if (MemoryExtensions.Contains(prop.PropertyType.GetInterfaces(), typeof(IEnumerable), null))
			{
				PrintIEnumerableProp(localValue, prop, hs, TabOnce(prog));
			}
			else
			{
				PrintInternal(prop.GetValue(localValue), prop.Name, hs, TabOnce(prog));
			}
			prog.ReportMessage("}");
		}
	}

	private static void PrintIEnumerableProp(object localValue, PropertyInfo prop, HashSet<object> hs, ProgressReporter prog)
	{
		IEnumerable enumerable = (IEnumerable)prop.GetValue(localValue);
		int ordinal = 0;
		foreach (object enumerable2 in enumerable)
		{
			PrintInternal(enumerable2, prop.Name + "[" + ordinal + "]", hs, TabOnce(prog));
			ordinal++;
		}
	}

	private static ProgressReporter TabOnce(ProgressReporter prog)
	{
		return prog.WithMessageModifier("  ", "");
	}
}
