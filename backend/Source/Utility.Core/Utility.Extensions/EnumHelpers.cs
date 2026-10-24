using System;
using System.Collections.Generic;

namespace Utility.Extensions;

public static class EnumHelpers
{
	public static IEnumerable<T> Enumerate<T>() where T : struct, Enum
	{
		T[] getValues = (T[])Enum.GetValues(typeof(T));
		for (int i = 0; i < getValues.Length; i++)
		{
			yield return getValues[i];
		}
	}
}
