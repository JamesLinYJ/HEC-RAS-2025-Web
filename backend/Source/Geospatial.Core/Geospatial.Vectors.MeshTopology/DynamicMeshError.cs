using System.Diagnostics;

namespace Geospatial.Vectors.MeshTopology;

[DebuggerDisplay("{errorType}: {element}")]
public struct DynamicMeshError
{
	public int element;

	public sbyte orient;

	public DynamicMeshErrorType errorType;

	public DynamicMeshError(DynamicMeshErrorType errorType, int element)
	{
		this.element = -1;
		orient = -1;
		this.element = element;
		this.errorType = errorType;
	}
}
