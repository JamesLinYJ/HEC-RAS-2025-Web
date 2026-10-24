using System.Collections.Generic;

namespace Geospatial.Vectors.MeshTopology;

public class MeshSmoother : OptimizeConstructed
{
	public MeshSmoother(SlidingMesh mesh, IList<int> nodes = null)
		: base(mesh, nodes)
	{
	}

	public override bool ConstructedPoint(int nIdx, out double x, out double y)
	{
		x = 0.0;
		y = 0.0;
		int ordinal = 0;
		foreach (HalfEdge nodeToHalfEdge in mesh.NodeToHalfEdges(nIdx))
		{
			if (!(mesh.FaceTargetLength[nodeToHalfEdge.fIdx] < 0f))
			{
				int index = mesh.HalfEdgeNodeForward(nodeToHalfEdge);
				ordinal++;
				x += mesh.NodePoints[index].X;
				y += mesh.NodePoints[index].Y;
			}
		}
		if (ordinal == 0)
		{
			return false;
		}
		x /= ordinal;
		y /= ordinal;
		return true;
	}
}
