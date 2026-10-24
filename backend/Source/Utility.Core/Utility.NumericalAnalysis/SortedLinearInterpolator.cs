using System;
using System.Numerics;

namespace Utility.NumericalAnalysis;

public static class SortedLinearInterpolator
{
	public static bool TryInterpolate<T>(ReadOnlySpan<T> x, ReadOnlySpan<T> y, T target, out T intervalIndexTerm) where T : IFloatingPointIeee754<T>
	{
		if (x.Length != y.Length)
		{
			throw new ArgumentException("The x and y spans must have equal lengths.");
		}
		for (int i = 0; i < x.Length; i++)
		{
			if (!T.IsFinite(x[i]) || !T.IsFinite(y[i]))
			{
				throw new ArgumentException("The x and y values must be finite.");
			}
			if (i > 0 && x[i] <= x[i - 1])
			{
				throw new ArgumentException("The x values must be strictly increasing.", "x");
			}
		}
		if (x.Length != 0 && T.IsFinite(target) && !(target < x[0]))
		{
			if (!(target > x[x.Length - 1]))
			{
				int intervalIndex = 0;
				int lengthOffset = x.Length - 1;
				while (intervalIndex < lengthOffset)
				{
					int ordinal = intervalIndex + (lengthOffset - intervalIndex) / 2;
					if (x[ordinal] < target)
					{
						intervalIndex = ordinal + 1;
					}
					else
					{
						lengthOffset = ordinal;
					}
				}
				if (x[intervalIndex] == target || intervalIndex == 0)
				{
					intervalIndexTerm = y[intervalIndex];
					return true;
				}
				int index = intervalIndex - 1;
				T indexTerm = x[intervalIndex] - x[index];
				T targetIndexTerm = (target - x[index]) / indexTerm;
				intervalIndexTerm = y[index] + targetIndexTerm * (y[intervalIndex] - y[index]);
				return true;
			}
		}
		intervalIndexTerm = T.Zero;
		return false;
	}
}
