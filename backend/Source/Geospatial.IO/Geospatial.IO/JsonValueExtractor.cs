using System;
using System.Text.Json;

namespace Geospatial.IO;

public static class JsonValueExtractor
{
	public static object ToTypedValue(JsonElement element, Type targetType)
	{
		JsonValueKind valueKind = element.ValueKind;
		if ((valueKind == JsonValueKind.Undefined || valueKind == JsonValueKind.Null) ? true : false)
		{
			return DefaultFor(targetType);
		}
		if (targetType == typeof(string))
		{
			if (element.ValueKind != JsonValueKind.String)
			{
				return element.GetRawText();
			}
			return element.GetString() ?? "";
		}
		if (targetType == typeof(bool))
		{
			return element.ValueKind == JsonValueKind.True;
		}
		short ordinal;
		if (targetType == typeof(short))
		{
			return (short)((element.ValueKind == JsonValueKind.Number && element.TryGetInt16(out ordinal)) ? ordinal : 0);
		}
		int ordinal2;
		if (targetType == typeof(int))
		{
			return (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out ordinal2)) ? ordinal2 : 0;
		}
		long ordinal3;
		if (targetType == typeof(long))
		{
			return (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out ordinal3)) ? ordinal3 : 0;
		}
		float computed;
		if (targetType == typeof(float))
		{
			return (element.ValueKind == JsonValueKind.Number && element.TryGetSingle(out computed)) ? computed : 0f;
		}
		double computed2;
		if (targetType == typeof(double))
		{
			return (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out computed2)) ? computed2 : 0.0;
		}
		throw new NotSupportedException($"{"JsonValueExtractor"} does not support target type {targetType}.");
	}

	public static object TryParseToTypedValue_OrDefault(object raw, Type targetType)
	{
		if (raw == null)
		{
			return DefaultFor(targetType);
		}
		if (raw is JsonElement element)
		{
			return ToTypedValue(element, targetType);
		}
		return raw;
	}

	public static object DefaultFor(Type type)
	{
		if (type == typeof(string))
		{
			return "";
		}
		if (type == typeof(bool))
		{
			return false;
		}
		if (type == typeof(short))
		{
			return (short)0;
		}
		if (type == typeof(int))
		{
			return 0;
		}
		if (type == typeof(long))
		{
			return 0L;
		}
		if (type == typeof(float))
		{
			return 0f;
		}
		if (type == typeof(double))
		{
			return 0.0;
		}
		throw new NotSupportedException($"{"JsonValueExtractor"} does not support target type {type}.");
	}
}
