using System;

namespace Utility;

public static class Types
{
	public static bool Matches<T>(Type other)
	{
		return other is T;
	}

	public static string ToFriendlyName(Type type)
	{
		if (type == typeof(int) || type == typeof(int))
		{
			return "Integer";
		}
		if (type == typeof(long) || type == typeof(long))
		{
			return "Long Integer";
		}
		if (type == typeof(short) || type == typeof(short))
		{
			return "Short Integer";
		}
		if (type == typeof(byte))
		{
			return "Byte";
		}
		if (type == typeof(sbyte))
		{
			return "Signed Byte";
		}
		if (type == typeof(uint) || type == typeof(uint))
		{
			return "Unsigned Integer";
		}
		if (type == typeof(ulong) || type == typeof(ulong))
		{
			return "Unsigned Long Integer";
		}
		if (type == typeof(ushort) || type == typeof(ushort))
		{
			return "Unsigned Short Integer";
		}
		if (type == typeof(double) || type == typeof(double))
		{
			return "Number";
		}
		if (type == typeof(float) || type == typeof(float))
		{
			return "Decimal";
		}
		if (type == typeof(decimal) || type == typeof(decimal))
		{
			return "Precise Decimal";
		}
		if (type == typeof(bool) || type == typeof(bool))
		{
			return "Boolean";
		}
		if (type == typeof(char) || type == typeof(char))
		{
			return "Character";
		}
		if (type == typeof(string) || type == typeof(string))
		{
			return "Text";
		}
		if (type == typeof(DateTime))
		{
			return "Date/Time";
		}
		if (type == typeof(TimeSpan))
		{
			return "Duration";
		}
		if (type == typeof(Guid))
		{
			return "Unique Identifier";
		}
		if (type == typeof(object))
		{
			return "Object";
		}
		Type underlyingType = Nullable.GetUnderlyingType(type);
		if (underlyingType != null)
		{
			return ToFriendlyName(underlyingType) + " (Optional)";
		}
		return type.Name;
	}

	public static string ToFriendlyName<T>()
	{
		return ToFriendlyName(typeof(T));
	}
}
