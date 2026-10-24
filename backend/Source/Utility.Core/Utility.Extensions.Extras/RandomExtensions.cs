using System;
using System.Collections.Generic;

namespace Utility.Extensions.Extras;

public static class RandomExtensions
{
	private static Random _random = new Random(0);

	public static T PickRandom<T>(this IList<T> lst)
	{
		lock (_random)
		{
			return lst[_random.Next(lst.Count)];
		}
	}
}
