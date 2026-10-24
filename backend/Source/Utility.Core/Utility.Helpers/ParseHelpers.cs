using System;
using System.Globalization;

namespace Utility.Helpers;

public static class ParseHelpers
{
	public static bool TryUnbox<T>(object localValue2, T fallback, out T localValueTerm, IFormatProvider provider = null) where T : struct
	{
		if (localValue2 is T localValue)
		{
			localValueTerm = localValue;
			return true;
		}
		localValueTerm = fallback;
		if (localValue2 == null)
		{
			return true;
		}
		try
		{
			localValueTerm = (T)Convert.ChangeType(localValue2, typeof(T), provider ?? CultureInfo.CurrentCulture);
			return true;
		}
		catch
		{
			localValueTerm = fallback;
			return false;
		}
	}

	public static T? TryParseStringAs_CurrentCulture<T>(string noDataStr) where T : unmanaged
	{
		return TryParseStringAs<T>(noDataStr, CultureInfo.CurrentCulture);
	}

	public static T? TryParseStringAs_InvariantCulture<T>(string noDataStr) where T : unmanaged
	{
		return TryParseStringAs<T>(noDataStr, CultureInfo.InvariantCulture);
	}

	public static T? TryParseStringAs<T>(string noDataStr, IFormatProvider provider = null) where T : unmanaged
	{
		if (string.IsNullOrEmpty(noDataStr))
		{
			return null;
		}
		if (provider == null)
		{
			provider = CultureInfo.CurrentCulture;
		}
		Type typeFromHandle = typeof(T);
		if (typeFromHandle == typeof(float) && float.TryParse(noDataStr, NumberStyles.Float, provider, out var result))
		{
			return result as T?;
		}
		if (typeFromHandle == typeof(double) && double.TryParse(noDataStr, NumberStyles.Float, provider, out var result2))
		{
			return result2 as T?;
		}
		if (typeFromHandle == typeof(long) && long.TryParse(noDataStr, NumberStyles.Integer, provider, out var result3))
		{
			return result3 as T?;
		}
		if (typeFromHandle == typeof(int) && int.TryParse(noDataStr, NumberStyles.Integer, provider, out var result4))
		{
			return result4 as T?;
		}
		if (typeFromHandle == typeof(short) && short.TryParse(noDataStr, NumberStyles.Integer, provider, out var result5))
		{
			return result5 as T?;
		}
		if (typeFromHandle == typeof(byte) && byte.TryParse(noDataStr, NumberStyles.Integer, provider, out var result6))
		{
			return result6 as T?;
		}
		if (typeFromHandle == typeof(bool) && bool.TryParse(noDataStr, out var result7))
		{
			return result7 as T?;
		}
		if (typeFromHandle.IsEnum && Enum.TryParse(typeof(T), noDataStr, out object result8))
		{
			return (T)result8;
		}
		return null;
	}

	public static object TryParseStringAs(string stringValue, Type tt)
	{
		if (tt == typeof(string))
		{
			return stringValue;
		}
		if (string.IsNullOrEmpty(stringValue))
		{
			return null;
		}
		if (tt.IsEnum)
		{
			if (!Enum.TryParse(tt, stringValue, out object result))
			{
				return null;
			}
			return result;
		}
		if (tt == typeof(float) && float.TryParse(stringValue, out var result2))
		{
			return result2;
		}
		if (tt == typeof(double) && double.TryParse(stringValue, out var result3))
		{
			return result3;
		}
		if (tt == typeof(long) && long.TryParse(stringValue, out var result4))
		{
			return result4;
		}
		if (tt == typeof(int) && int.TryParse(stringValue, out var result5))
		{
			return result5;
		}
		if (tt == typeof(short) && short.TryParse(stringValue, out var result6))
		{
			return result6;
		}
		if (tt == typeof(byte) && byte.TryParse(stringValue, out var result7))
		{
			return result7;
		}
		if (tt == typeof(bool) && bool.TryParse(stringValue, out var result8))
		{
			return result8;
		}
		return null;
	}
}
