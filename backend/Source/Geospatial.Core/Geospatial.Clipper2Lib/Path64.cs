using System.Collections.Generic;

namespace Geospatial.Clipper2Lib;

public class Path64 : List<Point64>
{
	public Path64()
	{
	}

	public Path64(int capacity = 0)
		: base(capacity)
	{
	}

	public Path64(IEnumerable<Point64> path)
		: base(path)
	{
	}

	public override string ToString()
	{
		string text = "";
		using (Enumerator enumerator = GetEnumerator())
		{
			while (enumerator.MoveNext())
			{
				text = text + enumerator.Current.ToString() + ", ";
			}
		}
		if (text != "")
		{
			text = text.Remove(text.Length - 2);
		}
		return text;
	}
}
