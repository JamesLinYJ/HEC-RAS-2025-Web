using System.Collections.Generic;

namespace Geospatial.Vectors.Rasterizers;

public class MeshSubset
{
	public Mesh Mesh { get; set; }

	public List<int> Cells { get; set; }

	public bool UseAllCells => Cells == null;

	public List<int> Faces { get; set; }

	public bool UseAllFaces => Faces == null;

	public List<int> Facepoints { get; set; }

	public bool UseAllFacepoints => Facepoints == null;
}
