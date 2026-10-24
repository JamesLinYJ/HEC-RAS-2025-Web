using System;
using System.Collections.Generic;

namespace Utility.Memory;

public class TimedCacheItemComparer<TKey> : IComparer<TKey> where TKey : ITimedCacheItem
{
	public int Compare(TKey x, TKey y)
	{
		if (x == null)
		{
			throw new ArgumentException("x must be a ITimedCacheItem!");
		}
		if (y == null)
		{
			throw new ArgumentException("y must be a ITimedCacheItem!");
		}
		int lastAccessedTimeTerm = x.LastAccessedTime.CompareTo(y.LastAccessedTime);
		if (lastAccessedTimeTerm == 0)
		{
			return -1;
		}
		return lastAccessedTimeTerm;
	}
}
