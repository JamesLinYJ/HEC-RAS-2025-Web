using System.Collections.Generic;

namespace Geospatial.Clipper2Lib;

public class Paths64 : List<Path64>
{
	public Paths64()
	{
	}

	public Paths64(int capacity = 0)
		: base(capacity)
	{
	}

	public Paths64(IEnumerable<Path64> paths)
		: base(paths)
	{
	}

	public override string ToString()
	{
		string text = "";
		using List<Path64>.Enumerator enumerator = GetEnumerator();
		while (enumerator.MoveNext())
		{
			text = text + enumerator.Current?.ToString() + "\n";
		}
		return text;
	}
}
