using System.Collections.Generic;

namespace Utility.DataStructures;

public static class IArrayExtensions
{
	public static IArray<T> Wrapped<T>(this IList<T> lst)
	{
		return new ArrayWrapper<T>(lst);
	}

	public static IArray<T> Wrapped<T>(this T[,] arr)
	{
		return new ArrayWrapper2D<T>(arr);
	}

	public static void CopyTo<T>(this IArray<T> from, IArray<T> to, int startIndex, int count)
	{
		for (int i = startIndex; i < startIndex + count; i++)
		{
			to[i] = from[i];
		}
	}
}
