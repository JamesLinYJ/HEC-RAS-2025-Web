namespace Geospatial.Clipper2Lib;

internal readonly struct LocalMinima(Vertex vertex, PathType polytype, bool isOpen = false)
{
	public readonly Vertex vertex = vertex;

	public readonly PathType polytype = polytype;

	public readonly bool isOpen = isOpen;

	public static bool operator ==(LocalMinima lm1, LocalMinima lm2)
	{
		return lm1.vertex == lm2.vertex;
	}

	public static bool operator !=(LocalMinima lm1, LocalMinima lm2)
	{
		return !(lm1 == lm2);
	}

	public override bool Equals(object? obj)
	{
		if (obj is LocalMinima localMinima)
		{
			return this == localMinima;
		}
		return false;
	}

	public override int GetHashCode()
	{
		return vertex.GetHashCode();
	}
}
