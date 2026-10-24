using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;

namespace Utility;

public class AllocationTracker
{
	private static ConcurrentBag<StackTrace> _stackTraces = new ConcurrentBag<StackTrace>();

	public void LogAllocation()
	{
		if (_stackTraces == null)
		{
			_stackTraces = new ConcurrentBag<StackTrace>();
		}
		_stackTraces.Add(new StackTrace());
	}

	public static void ComputeStackTraceTrace()
	{
		Dictionary<string, int> intByString = new Dictionary<string, int>();
		foreach (StackTrace stackTrace in _stackTraces)
		{
			string key = stackTrace.ToString();
			int ordinal = 0;
			intByString.TryGetValue(key, out ordinal);
			intByString[key] = ordinal + 1;
		}
		SortedDictionary<int, string> sortedDictionary = new SortedDictionary<int, string>();
		foreach (KeyValuePair<string, int> localValue in intByString)
		{
			sortedDictionary[localValue.Value] = localValue.Key;
		}
	}
}
