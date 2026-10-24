using System;
using System.Collections.Generic;

namespace Geospatial.Vectors.MeshTopology;

public class MeshUnsmoother(SlidingMesh mesh, IList<int> nodes = null) : OptimizeConstructed(mesh, nodes)
{
	public double jitter = 0.25;

	private Random random = new Random(37);

	public override bool ConstructedPoint(int nIdx, out double x, out double y)
	{
		x = 0.0;
		y = 0.0;
		List<HalfEdge> nodeToHalfEdges = mesh.NodeToHalfEdges(nIdx);
		int index = random.Next(nodeToHalfEdges.Count);
		HalfEdge he = nodeToHalfEdges[index];
		double nextDoubleRandomTerm = random.NextDouble() * jitter;
		if (mesh.FaceTargetLength[he.fIdx] < 0f)
		{
			return false;
		}
		int index2 = mesh.HalfEdgeNodeForward(he);
		x = nextDoubleRandomTerm * mesh.NodePoints[index2].X + (1.0 - nextDoubleRandomTerm) * mesh.NodePoints[nIdx].X;
		y = nextDoubleRandomTerm * mesh.NodePoints[index2].Y + (1.0 - nextDoubleRandomTerm) * mesh.NodePoints[nIdx].Y;
		return true;
	}
}
