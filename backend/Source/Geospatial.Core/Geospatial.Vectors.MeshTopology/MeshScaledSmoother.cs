using System;
using System.Collections.Generic;

namespace Geospatial.Vectors.MeshTopology;

public class MeshScaledSmoother(SlidingMesh mesh, IList<int> nodes = null) : OptimizeConstructed(mesh, nodes)
{
	public bool pushBoundaryNodes = true;

	public bool weightByArea = true;

	public double MoveTolerance = 0.0001;

	public override bool ConstructedPoint(int nIdx, out double x, out double y)
	{
		x = 0.0;
		y = 0.0;
		double unity = 0.0;
		Point point = mesh.NodePoints[nIdx];
		List<HalfEdge> nodeToHalfEdges = mesh.NodeToHalfEdges(nIdx);
		float faceTargetLengthSum = 0f;
		int ordinal = 0;
		foreach (HalfEdge nodeToHalfEdge in nodeToHalfEdges)
		{
			float faceTargetLength = mesh.FaceTargetLength[nodeToHalfEdge.fIdx];
			if (faceTargetLength > 0f)
			{
				faceTargetLengthSum += faceTargetLength;
				ordinal++;
			}
		}
		if (ordinal == 0)
		{
			return false;
		}
		faceTargetLengthSum /= (float)ordinal;
		foreach (HalfEdge nodeToHalfEdge2 in nodeToHalfEdges)
		{
			Point point2 = mesh.NodePoints[mesh.HalfEdgeNodeForward(nodeToHalfEdge2)];
			float faceTargetLength2 = mesh.FaceTargetLength[nodeToHalfEdge2.fIdx];
			if (faceTargetLength2 < 0f)
			{
				faceTargetLength2 = faceTargetLengthSum;
			}
			if ((mesh.FaceCells[nodeToHalfEdge2.fIdx].A < 0 || mesh.CellTypes[mesh.FaceCells[nodeToHalfEdge2.fIdx].A] != SlidingMesh.CellType.CELL_2D) && (mesh.FaceCells[nodeToHalfEdge2.fIdx].B < 0 || mesh.CellTypes[mesh.FaceCells[nodeToHalfEdge2.fIdx].B] != SlidingMesh.CellType.CELL_2D))
			{
				continue;
			}
			Vector vector = point2 - point;
			double length = vector.Length;
			if (pushBoundaryNodes && mesh.FaceConstraints[nodeToHalfEdge2.fIdx] >= (ConstraintState)0)
			{
				vector = (double)(0f - faceTargetLength2) / (length * length) * vector;
			}
			else
			{
				vector /= faceTargetLength2;
			}
			if (weightByArea)
			{
				double faceCellCellSimpleAreaTerm = 0.0;
				int fIdx = nodeToHalfEdge2.fIdx;
				if (mesh.FaceConstraints[nodeToHalfEdge2.fIdx] >= (ConstraintState)0)
				{
					fIdx = -1;
					foreach (HalfEdge nodeToHalfEdge3 in nodeToHalfEdges)
					{
						if (mesh.FaceConstraints[nodeToHalfEdge2.fIdx] == mesh.FaceConstraints[nodeToHalfEdge3.fIdx])
						{
							fIdx = nodeToHalfEdge3.fIdx;
							break;
						}
					}
					if (fIdx < 0)
					{
						return false;
					}
				}
				int faceCell = mesh.FaceCells[fIdx][0];
				int faceCell2 = mesh.FaceCells[fIdx][1];
				faceCellCellSimpleAreaTerm = ((faceCell < 0) ? (faceCellCellSimpleAreaTerm + mesh.CellSimpleArea(faceCell2)) : (faceCellCellSimpleAreaTerm + mesh.CellSimpleArea(faceCell)));
				faceCellCellSimpleAreaTerm = ((faceCell2 < 0) ? (faceCellCellSimpleAreaTerm + mesh.CellSimpleArea(faceCell)) : (faceCellCellSimpleAreaTerm + mesh.CellSimpleArea(faceCell2)));
				faceCellCellSimpleAreaTerm /= length;
				faceCellCellSimpleAreaTerm = Math.Max(faceCellCellSimpleAreaTerm, 0.01 * (double)faceTargetLength2);
				vector *= faceCellCellSimpleAreaTerm;
				unity += faceCellCellSimpleAreaTerm;
			}
			else
			{
				unity = 1.0;
			}
			x += vector.DeltaX;
			y += vector.DeltaY;
		}
		x = point.X + x * 0.25 * (double)faceTargetLengthSum / unity;
		y = point.Y + y * 0.25 * (double)faceTargetLengthSum / unity;
		if (point.DistanceTo(new Point(x, y)) < MoveTolerance * (double)faceTargetLengthSum)
		{
			x = point.X;
			y = point.Y;
			return false;
		}
		return true;
	}
}
