using System;
using Geospatial.Vectors;

namespace Ras.Optimization;

public sealed class ManningZone
{
	public string Name { get; }

	public Polygon Boundary { get; }

	public ManningZone(string name, Polygon boundary)
	{
		if (boundary == null)
		{
			throw new ArgumentNullException("boundary");
		}
		Name = name;
		Boundary = boundary;
	}
}
