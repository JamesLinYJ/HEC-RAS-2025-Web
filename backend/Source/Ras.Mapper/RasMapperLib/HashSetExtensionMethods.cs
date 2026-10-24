using System;
using System.Collections.Generic;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

[StandardModule]
internal static class HashSetExtensionMethods
{
	public static bool CollectionEquals<T>(this HashSet<T> hs1, HashSet<T> hs2)
	{
		if (hs1.Count != hs2.Count)
		{
			return false;
		}
		foreach (T hs12 in hs1)
		{
			if (!hs2.Contains(hs12))
			{
				return false;
			}
		}
		return true;
	}

	public static void AddRange<T>(this HashSet<T> hs1, ICollection<T> collection)
	{
		if (hs1 == null)
		{
			throw new ArgumentNullException();
		}
		if (collection == null)
		{
			return;
		}
		foreach (T collection2 in collection)
		{
			hs1.Add(collection2);
		}
	}
}
