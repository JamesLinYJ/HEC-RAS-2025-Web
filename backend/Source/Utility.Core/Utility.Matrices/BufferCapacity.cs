using System;

namespace Utility.Matrices;

internal static class BufferCapacity
{
	public static bool EnsureArrayCapacity<T>(ref T[] buf, int required, int cap)
	{
		int bufTerm = buf?.Length ?? 0;
		bool condition = bufTerm < required;
		bool condition2 = required > 0 && required * 5 < bufTerm * 2;
		if (!condition && !condition2)
		{
			return false;
		}
		int count;
		if (condition)
		{
			count = ((bufTerm <= 0) ? required : Math.Max(bufTerm * 2, required));
			if (cap > 0)
			{
				count = Math.Min(count, cap);
			}
			if (count < required)
			{
				count = required;
			}
		}
		else
		{
			count = required;
		}
		T[] bufArray = new T[count];
		if (buf != null)
		{
			Array.Copy(buf, bufArray, Math.Min(buf.Length, count));
		}
		buf = bufArray;
		return true;
	}
}
