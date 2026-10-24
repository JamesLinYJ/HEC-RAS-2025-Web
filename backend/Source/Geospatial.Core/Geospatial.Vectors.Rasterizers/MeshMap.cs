using System.Collections.Generic;
using Geospatial.Vectors.Meshes;
using Utility;

namespace Geospatial.Vectors.Rasterizers;

public abstract class MeshMap
{
	private readonly object _lock = new object();

	private MeshSubset _subset;

	public Mesh Mesh { get; set; }

	public MeshPropertyTables Tables { get; set; }

	public HydraulicMeshComputer HydraulicMesh => Mesh.MetadataCache;

	public int MeshIndex { get; }

	public List<int> UniqueFaces { get; }

	public List<int> UniqueFacePoints { get; }

	public MeshMap(Mesh mesh, int meshIdx, MeshPropertyTables tables, List<int> uniqueFaces, List<int> uniqueFacepoints)
	{
		Mesh = mesh;
		MeshIndex = meshIdx;
		Tables = tables;
		UniqueFaces = uniqueFaces;
		UniqueFacePoints = uniqueFacepoints;
	}

	public MeshSubset GetMeshSubset()
	{
		return LazyThreadsafe<MeshSubset>.AllocateOnce(ref _subset, _lock, () => new MeshSubset
		{
			Mesh = Mesh,
			Cells = UniqueCells(),
			Faces = UniqueFaces,
			Facepoints = UniqueFacePoints
		});
	}

	public abstract List<int> UniqueCells();
}
