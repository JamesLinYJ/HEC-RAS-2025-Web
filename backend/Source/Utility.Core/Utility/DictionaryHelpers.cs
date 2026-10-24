using System.Collections.Generic;

namespace Utility;

public static class DictionaryHelpers
{
	public static void AppendRange<T, D>(this Dictionary<T, D> toAppendTo, Dictionary<T, D> toAppend)
	{
		foreach (KeyValuePair<T, D> toAppend2 in toAppend)
		{
			toAppendTo.Add(toAppend2.Key, toAppend2.Value);
		}
	}
}
