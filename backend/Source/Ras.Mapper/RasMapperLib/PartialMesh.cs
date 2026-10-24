using System;

namespace RasMapperLib;

public class PartialMesh
{
	public MeshFV2D Mesh;

	public Extent ValidExtent;

	public DateTime Timestamp;

	public PartialMesh(MeshFV2D mesh, Extent ext, DateTime time)
	{
		Mesh = mesh;
		ValidExtent = ext;
		Timestamp = time;
	}
}
