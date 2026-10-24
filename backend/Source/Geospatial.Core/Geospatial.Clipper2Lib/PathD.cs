using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial.Clipper2Lib;

public class PathD : List<Point>
{
	public PathD()
	{
	}

	public PathD(int capacity = 0)
		: base(capacity)
	{
	}

	public PathD(IEnumerable<Point> path)
		: base(path)
	{
	}

	public string ToString(int precision = 2)
	{
		string text = "";
		using (Enumerator enumerator = GetEnumerator())
		{
			while (enumerator.MoveNext())
			{
				text = text + enumerator.Current.ToString(precision) + ", ";
			}
		}
		if (text != "")
		{
			text = text.Remove(text.Length - 2);
		}
		return text;
	}
}
