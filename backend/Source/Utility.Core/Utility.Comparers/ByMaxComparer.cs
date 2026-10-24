using System.Collections.Generic;

namespace Utility.Comparers;

public class ByMaxComparer : IComparer<double>
{
	public static readonly ByMaxComparer Instance = new ByMaxComparer();

	private ByMaxComparer()
	{
	}

	public int Compare(double a, double b)
	{
		return b.CompareTo(a);
	}
}
