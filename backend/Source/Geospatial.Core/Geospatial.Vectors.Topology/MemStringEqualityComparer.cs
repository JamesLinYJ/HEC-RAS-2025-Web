using System;
using System.Collections.Generic;

namespace Geospatial.Vectors.Topology;

public class MemStringEqualityComparer : IEqualityComparer<ReadOnlyMemory<char>>
{
	public int GetHashCode(ReadOnlyMemory<char> obj)
	{
		return string.GetHashCode(obj.Span, StringComparison.OrdinalIgnoreCase);
	}

	public bool Equals(ReadOnlyMemory<char> x, ReadOnlyMemory<char> y)
	{
		return x.Span.Equals(y.Span, StringComparison.OrdinalIgnoreCase);
	}
}
