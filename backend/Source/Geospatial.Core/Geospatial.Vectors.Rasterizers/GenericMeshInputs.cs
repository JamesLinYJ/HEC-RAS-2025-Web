using System;
using Geospatial.Vectors.Meshes;

namespace Geospatial.Vectors.Rasterizers;

public class GenericMeshInputs
{
	public GenericMapType MapType { get; set; }

	public Mesh Mesh { get; set; }

	public MeshPropertyTables PropertyTables { get; set; }

	public Memory<float> Values { get; set; }

	public GenericMeshInputs(GenericMapType mt, Mesh mesh, MeshPropertyTables props)
	{
		MapType = mt;
		Mesh = mesh;
		PropertyTables = props;
		if (props == null || props.CellMinimumElevations == null || props.FaceMinimumElevations == null)
		{
			throw new ArgumentNullException("props or some necessary internal buffer.");
		}
		if (mt.MeshElementType == MeshElementType.Cell)
		{
			Values = new float[mesh.CellCount];
			return;
		}
		if (mt.MeshElementType == MeshElementType.Face)
		{
			Values = new float[mesh.FaceCount];
			return;
		}
		throw new NotSupportedException();
	}
}
