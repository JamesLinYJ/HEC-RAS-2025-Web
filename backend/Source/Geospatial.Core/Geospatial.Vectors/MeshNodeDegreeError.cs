using System.Collections.Generic;

namespace Geospatial.Vectors;

public class MeshNodeDegreeError
{
	public int MeshIndex { get; }

	public int NodeIndex { get; }

	public Point NodePoint { get; }

	public int FaceCount { get; }

	public IReadOnlyList<int> Cells { get; }

	public MeshNodeDegreeError(int meshIndex, int nodeIndex, Point nodePoint, int faceCount, IReadOnlyList<int> cells)
	{
		MeshIndex = meshIndex;
		NodeIndex = nodeIndex;
		NodePoint = nodePoint;
		FaceCount = faceCount;
		Cells = cells;
	}

	public Extent CellsExtent(Mesh mesh)
	{
		Extent extent = Extent.NullExtent;
		if (mesh == null)
		{
			return extent;
		}
		foreach (int cell in Cells)
		{
			if (cell >= 0 && cell < mesh.CellCount)
			{
				extent = Extent.Union(extent, mesh.CellExtent(cell));
			}
		}
		return extent;
	}
}
