using System;

namespace Geospatial.Vectors.MeshTopology;

public class OptimizeException : Exception
{
	public OptimizeException(string message)
		: base(message)
	{
	}
}
