using System;
using System.Collections.Generic;
using System.Linq;

namespace Utility.Helpers;

public static class MiscHelpers
{
	public static IEnumerable<double> TrimNaNsFromEnd(this double[] dblEnum, out int trimmed)
	{
		if (dblEnum == null)
		{
			trimmed = 0;
			return Array.Empty<double>();
		}
		if (!double.IsNaN(dblEnum[^1]))
		{
			trimmed = 0;
			return dblEnum;
		}
		int lengthOffset = dblEnum.Length - 1;
		while (lengthOffset >= 0 && double.IsNaN(dblEnum[lengthOffset]))
		{
			lengthOffset--;
		}
		trimmed = dblEnum.Length - lengthOffset - 1;
		if (trimmed == dblEnum.Length)
		{
			return Array.Empty<double>();
		}
		return dblEnum.SkipLast(trimmed);
	}
}
