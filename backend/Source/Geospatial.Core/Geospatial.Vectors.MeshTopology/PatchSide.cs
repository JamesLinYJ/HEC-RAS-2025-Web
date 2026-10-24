using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Transformers;
using Geospatial.Vectors.Topology;
using Utility.Logging;

namespace Geospatial.Vectors.MeshTopology;

public class PatchSide
{
	public const int SOUTH = 0;

	public const int EAST = 1;

	public const int NORTH = 2;

	public const int WEST = 3;

	public List<HalfEdge> SidePieces;

	public List<HalfEdge> OriginalPieces;

	public Polyline OriginalNodes;

	public Polyline OriginalSmooth;

	public double[] smoothDistances;

	public Polyline Smooth;

	public Polyline Nodes;

	public List<int> ConstraintIndexes;

	public DiscretizeLine SideDiscretization;

	public ConceptualMesh conceptualMesh { get; private set; }

	public PatchSide(ConceptualMesh conceptualMesh)
	{
		this.conceptualMesh = conceptualMesh;
		Smooth = null;
		Nodes = null;
		SidePieces = new List<HalfEdge>();
		ConstraintIndexes = null;
	}

	public PatchSide(PatchSide orig)
	{
		conceptualMesh = orig.conceptualMesh;
		SidePieces = new List<HalfEdge>(orig.SidePieces);
		OriginalPieces = orig.OriginalPieces;
		OriginalNodes = orig.OriginalNodes;
		OriginalSmooth = orig.OriginalSmooth;
		Smooth = new Polyline(orig.Smooth);
		Nodes = new Polyline(orig.Nodes);
		ConstraintIndexes = new List<int>(orig.ConstraintIndexes);
	}

	public static List<PatchSide> FromConceptualMesh(ConceptualMesh mesh, int cIdx, VirtualConstraints constraints = null, bool useSize = true, bool updateCorners = true)
	{
		if (mesh.CellFaces[cIdx].Count < 4)
		{
			throw new StructuredException(ConceptualMeshTopologyExtensions.MessageCodes.GetQuadPatchLessThanFourArcs(mesh.CellRegionMetadata[cIdx].ControlPointIndex, cIdx));
		}
		MeshRegionMetadata cellRegionMetadata = mesh.CellRegionMetadata[cIdx];
		Span<int> intSpan = stackalloc int[4];
		for (int i = 0; i < 4; i++)
		{
			intSpan[i] = cellRegionMetadata.CornerRingNode(i);
		}
		HashSet<int> intSet = new HashSet<int>();
		foreach (int cellToNode in mesh.CellToNodes(cIdx))
		{
			if (intSet.Contains(cellToNode))
			{
				throw new StructuredException(ConceptualMesh.MessageCodes.GetQuadRepeatedNodes(mesh.CellRegionMetadata[cIdx].ControlPointIndex, cIdx, cellToNode));
			}
			intSet.Add(cellToNode);
		}
		int ordinal = 0;
		for (int j = 0; j < intSpan.Length; j++)
		{
			if (intSpan[j] >= 0)
			{
				ordinal++;
			}
		}
		if (ordinal < 4)
		{
			throw new ArgumentException("Expected all corners set for quad patch");
		}
		List<PatchSide> sides = CornersToSides(mesh, mesh.CellFaces[cIdx], intSpan);
		if (ordinal == 0)
		{
			if (mesh.CellRegionMetadata[cIdx].PatchType == PatchType.OneD)
			{
				OneDReach.RotateSides(mesh, cIdx, ref sides, intSpan);
			}
			else
			{
				PatchCommon.RotateSides(mesh, cIdx, ref sides, intSpan);
			}
		}
		for (int k = 0; k < 4; k++)
		{
			PatchSide patchSide = sides[k];
			patchSide.OriginalPieces = patchSide.SidePieces.ToList();
			patchSide.OriginalNodes = patchSide.Nodes;
			patchSide.OriginalSmooth = patchSide.Smooth;
		}
		if (constraints != null)
		{
			foreach (PatchSide side in sides)
			{
				side.AddToConstraints(constraints);
			}
		}
		if (mesh.CellRegionMetadata[cIdx].PatchType == PatchType.OneD)
		{
			MeshRegionMetadata meshRegionMetadata = mesh.CellRegionMetadata[cIdx];
			sides[0].SideDiscretization = DiscretizeLine.BySizes(meshRegionMetadata.ActualPrimaryCellSize, meshRegionMetadata.ActualSecondaryConstantSize, sides[0].Smooth.Length);
			sides[2].SideDiscretization = DiscretizeLine.BySizes(meshRegionMetadata.ActualSecondaryConstantSize, meshRegionMetadata.ActualPrimaryCellSize, sides[0].Smooth.Length);
			sides[1].SideDiscretization = new DiscretizeLine(2);
			sides[3].SideDiscretization = new DiscretizeLine(2);
		}
		if (updateCorners)
		{
			cellRegionMetadata.SetCornerRingNodes(intSpan);
			mesh.CellRegionMetadata[cIdx] = cellRegionMetadata;
		}
		return sides;
	}

	private static List<PatchSide> CornersToSides(ConceptualMesh mesh, List<HalfEdge> faces, Span<int> corners)
	{
		List<PatchSide> patchSides = new List<PatchSide>();
		int sidePieceCountSum = 0;
		for (int i = 0; i < 4; i++)
		{
			PatchSide patchSide = new PatchSide(mesh);
			int cornerStart = corners[i];
			int cornerEnd = corners[(i + 1) % 4];
			if (cornerStart < cornerEnd)
			{
				for (int j = cornerStart; j < cornerEnd; j++)
				{
					AddFace(patchSide, mesh, faces[j]);
				}
			}
			else
			{
				for (int k = cornerStart; k < faces.Count; k++)
				{
					AddFace(patchSide, mesh, faces[k]);
				}
				for (int l = 0; l < cornerEnd; l++)
				{
					AddFace(patchSide, mesh, faces[l]);
				}
			}
			patchSides.Add(patchSide);
			sidePieceCountSum += patchSide.SidePieces.Count;
		}
		if (sidePieceCountSum != faces.Count)
		{
			throw new ArgumentException("Patch corners led to invalid sides");
		}
		return patchSides;
		static void AddFace(PatchSide patchSide2, ConceptualMesh conceptualMesh, HalfEdge he)
		{
			Polyline polyline = conceptualMesh.FaceNodeString[he.fIdx] ?? conceptualMesh.FaceSimpleGeometry(he.fIdx);
			Polyline polyline2 = conceptualMesh.FaceSmoothLine[he.fIdx];
			if (he.orient == 1)
			{
				polyline = polyline.Clone();
				polyline2 = polyline2.Clone();
				polyline.Reverse();
				polyline2.Reverse();
			}
			patchSide2.Append(polyline2, polyline, he);
		}
	}

	public static void RotateSidesOnce(List<PatchSide> Sides, Span<int> corners)
	{
		RotateSidesOnce(Sides);
		int corner = corners[0];
		corners[0] = corners[1];
		corners[1] = corners[2];
		corners[2] = corners[3];
		corners[3] = corner;
	}

	public static void RotateSidesOnce(List<PatchSide> Sides)
	{
		Sides.Add(Sides[0]);
		Sides.RemoveAt(0);
	}

	public static bool CheckRotation(List<PatchSide> Sides, ISpatialTransformer transform)
	{
		if (transform is QuadTransformBase quadTransformBase)
		{
			HalfEdge originalPiece = Sides[0].OriginalPieces[0];
			if (quadTransformBase.Sides[0].OriginalPieces.Contains(originalPiece))
			{
				return true;
			}
		}
		Point other = transform.Transform(new Point(0.0, 0.0));
		return Sides[0].PieceSmooth(0).First().DistanceTo(other) < 0.001;
	}

	public Polyline PieceSmooth(int i)
	{
		HalfEdge halfEdge = SidePieces[i];
		Polyline polyline = conceptualMesh.FaceSmoothLine[halfEdge.fIdx];
		if (halfEdge.orient == 1)
		{
			polyline = polyline.Clone();
			polyline.Reverse();
		}
		return polyline;
	}

	public int PieceSmoothSegmentCount(int i)
	{
		HalfEdge halfEdge = SidePieces[i];
		if (halfEdge.IsNull())
		{
			return 1;
		}
		return conceptualMesh.FaceSmoothLine[halfEdge.fIdx].SegmentCount;
	}

	public Polyline PieceNodes(int i)
	{
		HalfEdge halfEdge = SidePieces[i];
		Polyline polyline = conceptualMesh.FaceNodeString[halfEdge.fIdx] ?? conceptualMesh.FaceSimpleGeometry(halfEdge.fIdx);
		if (halfEdge.orient == 1)
		{
			polyline = polyline.Clone();
			polyline.Reverse();
		}
		return polyline;
	}

	public bool IsDiscretized(HalfEdge he)
	{
		if (he.IsNull())
		{
			return false;
		}
		FaceDiscretizationState faceDiscretizationState = conceptualMesh.FaceNodeState[he.fIdx];
		if (faceDiscretizationState != FaceDiscretizationState.FIXED)
		{
			return faceDiscretizationState == FaceDiscretizationState.DISCRETIZED;
		}
		return true;
	}

	public void ApproximateLocalDiscretization(out double totalLength, out int totalCount)
	{
		totalLength = 0.0;
		totalCount = 0;
		foreach (HalfEdge originalPiece in OriginalPieces)
		{
			Polyline dst = null;
			if (originalPiece.fIdx >= 0)
			{
				if (conceptualMesh.FaceNodeState[originalPiece.fIdx] == FaceDiscretizationState.DISCRETIZED || conceptualMesh.FaceNodeState[originalPiece.fIdx] == FaceDiscretizationState.FIXED)
				{
					dst = conceptualMesh.FaceNodeString[originalPiece.fIdx];
				}
				else
				{
					conceptualMesh.FaceDiscretization[originalPiece.fIdx].Apply(conceptualMesh.FaceSmoothLine[originalPiece.fIdx], ref dst);
				}
				totalLength += dst.Length;
				totalCount += dst.SegmentCount;
			}
		}
	}

	public double SegmentScalePreceding(PointOnPolyline pop, out float rate, int pieceIdx = -1)
	{
		if (SideDiscretization != null)
		{
			return SideDiscretization.GetSegmentScale(Smooth, pop, out rate, -1.0, following: false);
		}
		pieceIdx = FindPiece(pop, out var popPiece, pieceIdx);
		double lpop = -1.0;
		HalfEdge halfEdge = SidePieces[pieceIdx];
		if (halfEdge.IsNull())
		{
			rate = 1f;
			return double.NaN;
		}
		Polyline polyline = conceptualMesh.FaceSmoothLine[halfEdge.fIdx];
		if (halfEdge.orient == 1)
		{
			popPiece.SegmentIndex = polyline.SegmentCount - 1 - popPiece.SegmentIndex;
			popPiece.Fraction = 1.0 - popPiece.Fraction;
			return conceptualMesh.FaceDiscretization[halfEdge.fIdx].GetSegmentScale(polyline, popPiece, out rate, lpop, following: true);
		}
		return conceptualMesh.FaceDiscretization[halfEdge.fIdx].GetSegmentScale(polyline, popPiece, out rate, lpop, following: false);
	}

	public double SegmentScaleFollowing(PointOnPolyline pop, out float rate, int pieceIdx = -1)
	{
		if (SideDiscretization != null)
		{
			return SideDiscretization.GetSegmentScale(Smooth, pop, out rate, -1.0, following: true);
		}
		double lpop = -1.0;
		pieceIdx = FindPiece(pop, out var popPiece, pieceIdx, leftOf: false);
		HalfEdge halfEdge = SidePieces[pieceIdx];
		if (halfEdge.IsNull())
		{
			rate = 1f;
			return double.NaN;
		}
		Polyline polyline = conceptualMesh.FaceSmoothLine[halfEdge.fIdx];
		if (halfEdge.orient == 1)
		{
			popPiece.SegmentIndex = polyline.SegmentCount - 1 - popPiece.SegmentIndex;
			popPiece.Fraction = 1.0 - popPiece.Fraction;
			return conceptualMesh.FaceDiscretization[halfEdge.fIdx].GetSegmentScale(polyline, popPiece, out rate, lpop, following: false);
		}
		return conceptualMesh.FaceDiscretization[halfEdge.fIdx].GetSegmentScale(polyline, popPiece, out rate, lpop, following: true);
	}

	public double ApproxLocalCount()
	{
		ApproximateLocalDiscretization(out var _, out var totalCount);
		return totalCount;
	}

	public void UpdateNodes(ConceptualMesh cmesh, List<Point> nodes, DiscretizeRegion.ReplaceMode replaceMode, FaceDiscretizationState newFaceDiscretizationState = FaceDiscretizationState.DISCRETIZED)
	{
		int i = 0;
		for (int j = 0; j < SidePieces.Count; j++)
		{
			HalfEdge he = SidePieces[j];
			Point point;
			if (he.IsNull())
			{
				if (j + 1 == SidePieces.Count)
				{
					continue;
				}
				HalfEdge he2 = SidePieces[j + 1];
				point = cmesh.NodePoints[cmesh.HalfEdgeNodeBack(he2)];
			}
			else
			{
				point = cmesh.NodePoints[cmesh.HalfEdgeNodeForward(he)];
			}
			List<Point> points = new List<Point>();
			for (; i < nodes.Count && nodes[i] != point; i++)
			{
				points.Add(nodes[i]);
			}
			if (!he.IsNull() && (replaceMode == DiscretizeRegion.ReplaceMode.COARSEN_EXISTING || !IsDiscretized(he)))
			{
				if (i < nodes.Count)
				{
					points.Add(nodes[i]);
				}
				if (he.orient == 1)
				{
					points.Reverse();
				}
				if (cmesh.FaceNodeString[he.fIdx] == null || (replaceMode == DiscretizeRegion.ReplaceMode.REFINE_EXISTING && points.Count > cmesh.FaceNodeString[he.fIdx].Count) || (replaceMode == DiscretizeRegion.ReplaceMode.COARSEN_EXISTING && points.Count < cmesh.FaceNodeString[he.fIdx].Count))
				{
					cmesh.FaceNodeString[he.fIdx] = new Polyline(points);
				}
				cmesh.FaceNodeState[he.fIdx] = (FaceDiscretizationState)Math.Max((int)newFaceDiscretizationState, (int)cmesh.FaceNodeState[he.fIdx]);
			}
		}
	}

	public int FindPiece(PointOnPolyline pop, int startingPiece = -1)
	{
		PointOnPolyline popPiece;
		return FindPiece(pop, out popPiece, startingPiece);
	}

	public int FindPiece(PointOnPolyline pop, out PointOnPolyline popPiece, int startingPiece = -1, bool leftOf = true)
	{
		int segmentIndex = pop.SegmentIndex;
		int pieceSmoothSegmentCountTerm = -1;
		int i;
		for (i = 0; i < SidePieces.Count - 1; i++)
		{
			pieceSmoothSegmentCountTerm = PieceSmoothSegmentCount(i);
			if (segmentIndex < pieceSmoothSegmentCountTerm)
			{
				break;
			}
			segmentIndex -= pieceSmoothSegmentCountTerm;
		}
		if (leftOf && pop.Fraction == 0.0 && segmentIndex == 0 && i > 0)
		{
			i--;
			pop.Fraction = 1.0;
			segmentIndex = PieceSmoothSegmentCount(i) - 1;
		}
		else if (!leftOf && pop.Fraction == 1.0 && segmentIndex == pieceSmoothSegmentCountTerm - 1 && i < SidePieces.Count - 1)
		{
			i++;
			pop.Fraction = 0.0;
			segmentIndex = 0;
		}
		popPiece = new PointOnPolyline(pop.Point, segmentIndex, pop.Fraction);
		return i;
	}

	public static int FindFace(List<PatchSide> sides, int fIdx)
	{
		for (int i = 0; i < sides.Count; i++)
		{
			foreach (HalfEdge sidePiece in sides[i].SidePieces)
			{
				if (sidePiece.fIdx == fIdx)
				{
					return i;
				}
			}
		}
		return -1;
	}

	public PointOnPolyline PieceEnd(PointOnPolyline pop)
	{
		Polyline polyline = null;
		int segmentCountSum = 0;
		PointOnPolyline result = PointOnPolyline.NullPointOnPolyline;
		for (int i = 0; i < SidePieces.Count; i++)
		{
			polyline = PieceSmooth(i);
			if (pop.SegmentIndex + (int)pop.Fraction < segmentCountSum + polyline.Segments.Count)
			{
				result = new PointOnPolyline(polyline.Last(), segmentCountSum + polyline.Segments.Count - 1, 1.0);
				break;
			}
			segmentCountSum += polyline.Segments.Count;
		}
		if (result.IsNull())
		{
			result = Smooth.LastPointOnPolyline();
		}
		return result;
	}

	public PatchSide CloneReversed()
	{
		PatchSide patchSide = new PatchSide(conceptualMesh);
		patchSide.Smooth = Smooth.Clone();
		patchSide.Nodes = Nodes.Clone();
		patchSide.Smooth.Reverse();
		patchSide.Nodes.Reverse();
		for (int countOffset = SidePieces.Count - 1; countOffset >= 0; countOffset--)
		{
			HalfEdge sidePiece = SidePieces[countOffset];
			sidePiece.orient = (byte)(1 - sidePiece.orient);
			patchSide.SidePieces.Add(sidePiece);
		}
		if (SideDiscretization != null)
		{
			patchSide.SideDiscretization = SideDiscretization.CloneReversed();
		}
		return patchSide;
	}

	public void Append(IList<Point> smooth, IList<Point> nodes, HalfEdge he)
	{
		Append(smooth, nodes, new List<HalfEdge> { he });
	}

	public void Append(IList<Point> smooth, IList<Point> nodes, List<HalfEdge> hes)
	{
		if (Smooth == null)
		{
			Smooth = new Polyline(smooth);
		}
		else
		{
			if (Smooth == OriginalSmooth)
			{
				Smooth = Smooth.Clone();
			}
			Smooth.RemoveAt(Smooth.Count - 1);
			Smooth.AddRange(smooth);
		}
		if (Nodes == null)
		{
			Nodes = new Polyline(nodes);
		}
		else
		{
			if (Nodes == OriginalNodes)
			{
				Nodes = Nodes.Clone();
			}
			Nodes.RemoveAt(Nodes.Count - 1);
			Nodes.AddRange(nodes);
		}
		SidePieces.AddRange(hes);
	}

	public void Append(PatchSide other, bool flip)
	{
		if (!flip)
		{
			Append(other.Smooth, other.Nodes, other.SidePieces);
			return;
		}
		Polyline polyline = new Polyline(other.Smooth);
		polyline.Reverse();
		Polyline polyline2 = new Polyline(other.Nodes);
		polyline2.Reverse();
		List<HalfEdge> sidePieces = new List<HalfEdge>(other.SidePieces);
		sidePieces.Reverse();
		Append(polyline, polyline2, sidePieces);
	}

	public ConstraintState GetConstraint(double sUnit, bool IsNode, out Point point)
	{
		if (IsNode)
		{
			return GetNodeConstraint(sUnit, out point);
		}
		point = Point.NullPoint;
		return GetFaceConstraint(sUnit);
	}

	public ConstraintState GetNodeConstraint(double sUnit, out Point point, bool sIsTrimmed = true)
	{
		int sUnitNodesTerm;
		if (sIsTrimmed)
		{
			sUnitNodesTerm = (int)(0.5 + sUnit * (double)(Nodes.Count - 1));
			if (Math.Abs(sUnit * (double)(Nodes.Count - 1) - (double)sUnitNodesTerm) > 0.001)
			{
				throw new DynamicMeshException("Error setting constrained faces on quad or 1D region");
			}
			point = Nodes[sUnitNodesTerm];
		}
		else
		{
			sUnitNodesTerm = (int)(0.5 + sUnit * (double)(OriginalNodes.Count - 1));
			_ = OriginalNodes.Count;
			point = OriginalNodes[sUnitNodesTerm];
		}
		int nodeToPiece = NodeToPiece(sUnitNodesTerm, out var pieceNodeIdx, out var pieceNodeCount, sIsTrimmed);
		if (nodeToPiece < 0)
		{
			return ConstraintState.FREE;
		}
		if (pieceNodeIdx == 0 || pieceNodeIdx + 1 == pieceNodeCount)
		{
			return ConstraintState.FIXED;
		}
		return (ConstraintState)ConstraintIndexes[nodeToPiece];
	}

	private int NodeToPiece(double nodeIndex, out int pieceNodeIdx, out int pieceNodeCount, bool nodeIsTrimmed = true)
	{
		pieceNodeIdx = -1;
		pieceNodeCount = -1;
		if (OriginalNodes == null)
		{
			return -1;
		}
		if (nodeIndex < 0.0 || nodeIndex >= (double)OriginalNodes.Count)
		{
			return -1;
		}
		if (nodeIsTrimmed)
		{
			if (OriginalNodes[(int)nodeIndex] != Nodes[(int)nodeIndex])
			{
				return -1;
			}
			if (nodeIndex != (double)(int)nodeIndex && OriginalNodes[1 + (int)nodeIndex] != Nodes[1 + (int)nodeIndex])
			{
				return -1;
			}
		}
		int ordinal = 0;
		for (int i = 0; i < OriginalPieces.Count; i++)
		{
			HalfEdge halfEdge = OriginalPieces[i];
			int pieceNodeCount2 = conceptualMesh.FaceNodeString[halfEdge.fIdx]?.Count ?? 2;
			int pieceNodeCount2Term = ordinal + pieceNodeCount2 - 1;
			if (nodeIndex <= (double)pieceNodeCount2Term)
			{
				pieceNodeIdx = (int)(nodeIndex - (double)ordinal);
				pieceNodeCount = pieceNodeCount2;
				return i;
			}
			ordinal = pieceNodeCount2Term;
		}
		throw new DynamicMeshException("Failed to match quad node with original arc");
	}

	public ConstraintState GetFaceConstraint(double sUnit, bool sIsTrimmed = true)
	{
		double nodeIndex = ((!sIsTrimmed) ? (sUnit * (double)(OriginalNodes.Count - 1)) : (sUnit * (double)(Nodes.Count - 1)));
		int nodeToPiece = NodeToPiece(nodeIndex, out var _, out var _, sIsTrimmed);
		if (nodeToPiece < 0)
		{
			return ConstraintState.FREE;
		}
		return (ConstraintState)ConstraintIndexes[nodeToPiece];
	}

	public void AddToConstraints(VirtualConstraints Constraints)
	{
		ConstraintIndexes = new List<int>();
		for (int i = 0; i < OriginalPieces.Count; i++)
		{
			int fIdx = SidePieces[i].fIdx;
			int constraintsFaceSmoothLineTerm = Constraints.Add(conceptualMesh.FaceSmoothLine[fIdx], fIdx, conceptualMesh.ArcRigidity(fIdx));
			ConstraintIndexes.Add(constraintsFaceSmoothLineTerm);
		}
	}
}
