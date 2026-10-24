using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Utility.Memory;

public static class StaticCache
{
	private static ConcurrentDictionary<object, ConcurrentDictionary<string, object>> Cache = new ConcurrentDictionary<object, ConcurrentDictionary<string, object>>();

	public static object TryGet(object keyObj, Func<object> generator, [CallerMemberName] string key = "")
	{
		object keyObjTerm;
		if (!Cache.TryGetValue(keyObj, out var localValue))
		{
			keyObjTerm = keyObj;
			bool lockTaken = false;
			try
			{
				Monitor.Enter(keyObjTerm, ref lockTaken);
				if (!Cache.TryGetValue(keyObj, out localValue))
				{
					localValue = new ConcurrentDictionary<string, object>();
					if (!Cache.TryAdd(keyObj, localValue))
					{
						throw new Exception("Pretty sure this should never fail. Race condition?");
					}
				}
			}
			finally
			{
				if (lockTaken)
				{
					Monitor.Exit(keyObjTerm);
				}
			}
		}
		if (localValue.TryGetValue(key, out var generator2))
		{
			return generator2;
		}
		lock (localValue)
		{
			if (localValue.TryGetValue(key, out generator2))
			{
				keyObjTerm = generator2;
			}
			else
			{
				generator2 = generator();
				localValue.TryAdd(key, generator2);
				keyObjTerm = generator2;
			}
		}
		return keyObjTerm;
	}
}
