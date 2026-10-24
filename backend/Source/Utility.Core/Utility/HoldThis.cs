using System.Collections.Generic;

namespace Utility;

public static class HoldThis
{
	public static Dictionary<string, object> Items = new Dictionary<string, object>();

	public static T Get<T>(string key) where T : class
	{
		if (Items.TryGetValue(key, out var localValue))
		{
			return localValue as T;
		}
		return null;
	}
}
