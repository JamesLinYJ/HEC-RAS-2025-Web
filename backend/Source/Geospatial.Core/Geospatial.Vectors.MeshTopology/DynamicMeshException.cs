using System;

namespace Geospatial.Vectors.MeshTopology;

public class DynamicMeshException : Exception
{
	public DynamicMeshException(string message)
		: base(message)
	{
	}
}
