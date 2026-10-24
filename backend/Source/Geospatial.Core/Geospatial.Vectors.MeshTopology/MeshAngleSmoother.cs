using System;
using System.Collections.Generic;

namespace Geospatial.Vectors.MeshTopology;

public class MeshAngleSmoother : OptimizeConstructed
{
	public MeshAngleSmoother(SlidingMesh mesh, IList<int> nodes = null)
		: base(mesh, nodes)
	{
	}

	public override bool ConstructedPoint(int nIdx, out double x, out double y)
	{
		Point point = new Point(0.0, 0.0);
		int ordinal = 0;
		List<HalfEdge> nodeToSortedHalfEdges = mesh.NodeToSortedHalfEdges(nIdx);
		for (int i = 0; i < nodeToSortedHalfEdges.Count; i++)
		{
			HalfEdge he = nodeToSortedHalfEdges[i];
			int halfEdgeToOppositeCell = mesh.HalfEdgeToOppositeCell(he);
			int halfEdgeToCell = mesh.HalfEdgeToCell(he);
			if (halfEdgeToCell < 0)
			{
				continue;
			}
			int halfEdgeNodeForward = mesh.HalfEdgeNodeForward(he);
			int cellNumSides = mesh.CellNumSides(halfEdgeToCell);
			HalfEdge he2 = mesh.HalfEdgeNextByCell(he);
			int halfEdgeNodeForward2 = mesh.HalfEdgeNodeForward(he2);
			if (cellNumSides == 4)
			{
				HalfEdge he3 = mesh.HalfEdgeNextByCell(he2);
				int c = mesh.HalfEdgeNodeForward(he3);
				HalfEdge halfEdge = nodeToSortedHalfEdges[(i + 1) % nodeToSortedHalfEdges.Count];
				float faceTargetLengthMeshTerm = mesh.FaceTargetLength[he.fIdx] + mesh.FaceTargetLength[he3.fIdx];
				float faceTargetLengthMeshTerm2 = mesh.FaceTargetLength[he2.fIdx] + mesh.FaceTargetLength[halfEdge.fIdx];
				double computed = Math.Atan(faceTargetLengthMeshTerm / faceTargetLengthMeshTerm2);
				ordinal++;
				point += BisectingPoint(nIdx, halfEdgeNodeForward, halfEdgeNodeForward2, c, Math.PI / 2.0 - computed, computed);
			}
			if (halfEdgeToOppositeCell >= 0)
			{
				int cellNumSides2 = mesh.CellNumSides(halfEdgeToOppositeCell);
				if (cellNumSides2 <= 4 && cellNumSides <= 4)
				{
					int a = mesh.HalfEdgeNodeBack(mesh.HalfEdgePrevByCell(mesh.HalfEdgeFlip(he)));
					ordinal++;
					point += BisectingPoint(nIdx, a, halfEdgeNodeForward, halfEdgeNodeForward2, cellNumSides2, cellNumSides);
				}
			}
		}
		if (ordinal == 0)
		{
			x = (y = double.NaN);
			return false;
		}
		x = point.X / (double)ordinal;
		y = point.Y / (double)ordinal;
		return true;
	}

	public Point BisectingPoint(int N, int A, int B, int C, int nSidesCW, int nSidesCCW)
	{
		double angleCW = 180 - 360 / nSidesCW;
		double angleCCW = 180 - 360 / nSidesCCW;
		return BisectingPoint(N, A, B, C, angleCW, angleCCW);
	}

	public Point BisectingPoint(int N, int A, int B, int C, double angleCW, double angleCCW)
	{
		Vector vector = mesh.NodePoints[N] - mesh.NodePoints[B];
		Vector vector2 = vector.UnitVector();
		Vector other = (mesh.NodePoints[C] - mesh.NodePoints[B]).UnitVector();
		Vector other2 = (mesh.NodePoints[A] - mesh.NodePoints[B]).UnitVector();
		double dotVector2Term = Math.Acos(vector2.Dot(other));
		double dotVector2Term2 = Math.Acos(vector2.Dot(other2));
		double angleRads = angleCW / (angleCW + angleCCW) * (dotVector2Term + dotVector2Term2) - dotVector2Term;
		return mesh.NodePoints[B] + vector.Rotate(angleRads);
	}
}
