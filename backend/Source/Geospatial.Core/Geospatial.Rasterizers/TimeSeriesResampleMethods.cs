using System;
using System.Collections.Generic;
using System.Linq;
using Utility;

namespace Geospatial.Rasterizers;

public static class TimeSeriesResampleMethods
{
	public static List<DateTime> Filter(IList<DateTime> dates, TimeSpan minResolution)
	{
		if (dates == null)
		{
			return null;
		}
		if (dates.Count == 0)
		{
			return new List<DateTime>();
		}
		List<DateTime> dateTimes = new List<DateTime>(dates.Count);
		DateTime dateTime = dates.First();
		dateTimes.Add(dateTime);
		for (int i = 1; i < dates.Count; i++)
		{
			DateTime dateTime2 = dates[i];
			if (dateTime2 - dateTime > minResolution)
			{
				dateTimes.Add(dateTime2);
				dateTime = dateTime2;
			}
		}
		return dateTimes;
	}

	public static int BinarySearchNextLargerIndex(IList<DateTime> dates, DateTime search)
	{
		int binarySearch = MathHelpers.BinarySearch(dates, search);
		if (binarySearch >= 0)
		{
			return binarySearch + 1;
		}
		return ~binarySearch;
	}

	public static int BinarySearchNextSmallerIndex(IList<DateTime> dates, DateTime search)
	{
		int binarySearch = MathHelpers.BinarySearch(dates, search);
		if (binarySearch >= 0)
		{
			return binarySearch - 1;
		}
		return ~binarySearch - 1;
	}

	public static int BinarySearchNextLargerIndex(IReadOnlyList<DateTime> dates, DateTime search)
	{
		int binarySearch = MathHelpers.BinarySearch(dates, search);
		if (binarySearch >= 0)
		{
			return binarySearch + 1;
		}
		return ~binarySearch;
	}

	public static int BinarySearchNextSmallerIndex(IReadOnlyList<DateTime> dates, DateTime search)
	{
		int binarySearch = MathHelpers.BinarySearch(dates, search);
		if (binarySearch >= 0)
		{
			return binarySearch - 1;
		}
		return ~binarySearch - 1;
	}
}
