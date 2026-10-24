using Geospatial.Vectors;

namespace Ras.Layers.Helpers;

public class MeshInfo
{
	public int CellCount { get; set; }

	public int FaceCount { get; set; }

	public double Area { get; set; }

	public MeshInfo()
	{
	}

	public MeshInfo(Mesh m)
	{
		CellCount = m.CellCount;
		FaceCount = m.FaceCount;
		Area = m.TotalCellArea();
	}
}
