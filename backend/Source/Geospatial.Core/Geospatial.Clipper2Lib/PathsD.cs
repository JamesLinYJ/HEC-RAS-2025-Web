using System.Collections.Generic;

namespace Geospatial.Clipper2Lib;

public class PathsD : List<PathD>
{
	public PathsD()
	{
	}

	public PathsD(int capacity = 0)
		: base(capacity)
	{
	}

	public PathsD(IEnumerable<PathD> paths)
		: base(paths)
	{
	}

	public string ToString(int precision = 2)
	{
		string text = "";
		using List<PathD>.Enumerator enumerator = GetEnumerator();
		while (enumerator.MoveNext())
		{
			PathD current = enumerator.Current;
			text = text + current.ToString(precision) + "\n";
		}
		return text;
	}
}
