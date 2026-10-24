using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

[StandardModule]
internal static class DictionaryExtensionMethods
{
	public static void AddRange<T, K>(this Dictionary<T, K> dic, IList<T> keys, IList<K> values)
	{
		if (keys.Count != values.Count)
		{
			throw new ArgumentException("Keys and values must be the same length");
		}
		checked
		{
			int count = keys.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				dic.Add(keys[i], values[i]);
			}
		}
	}

	public static void DisposeAll<T, K>(this Dictionary<T, K> dic) where K : IDisposable
	{
		if (dic == null)
		{
			return;
		}
		foreach (K localValue in dic.Values)
		{
			((IDisposable)localValue)?.Dispose();
		}
	}

	public static Dictionary<K, V> Merge<K, V>(this Dictionary<K, V> dic, IList<Dictionary<K, V>> others)
	{
		if (dic == null)
		{
			throw new ArgumentNullException("me");
		}
		Dictionary<K, V> entries = new Dictionary<K, V>();
		AddRange(entries, dic.Keys.ToList(), dic.Values.ToList());
		foreach (Dictionary<K, V> other in others)
		{
			AddRange(entries, other.Keys.ToList(), other.Values.ToList());
		}
		return entries;
	}

	public static bool ContainsAndMatchesValue<T, K>(this Dictionary<T, K> dic, T key, K localValue)
	{
		if (dic == null)
		{
			throw new ArgumentNullException("me");
		}
		if (dic.ContainsKey(key))
		{
			return dic[key].Equals(localValue);
		}
		return false;
	}
}
