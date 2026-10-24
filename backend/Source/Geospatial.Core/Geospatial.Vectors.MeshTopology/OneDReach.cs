using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.DataStructures;
using Geospatial.Vectors.Topology;
using Utility.Logging;
using Utility.Progress;

namespace Geospatial.Vectors.MeshTopology;

public class OneDReach : PatchCommon, IRegionGenerator
{
	public OneDReach(ConceptualMesh mesh, int cIdx)
		: base(mesh, cIdx)
	{
	}

	public SlidingMesh Generate(Action<SlidingMesh> Progress = null, ProgressReporter reporter = null, OperationResult res = null)
	{
		SlidingMesh slidingMesh = new HybridQuadPatch(GetLocalConceptualMesh(cmesh, regionIdx, out var _, out var _), 0).Generate(Progress, reporter);
		RemapToOriginal(slidingMesh, cmesh, regionIdx);
		for (int i = 0; i < slidingMesh.CellCount; i++)
		{
			slidingMesh.CellTypes[i] = SlidingMesh.CellType.CELL_1D_REACH;
		}
		return slidingMesh;
	}

	private ConceptualMesh GetLocalConceptualMesh(ConceptualMesh cmesh, int regionIdx, out List<int> faceMap, out List<PatchSide> origSides)
	{
		origSides = cmesh.PatchSides(regionIdx);
		int index = regionIdx;
		ConceptualMesh regionConceptualMesh = cmesh.GetRegionConceptualMesh(ref regionIdx, out faceMap);
		MeshRegionMetadata cellRegionMetadata = cmesh.CellRegionMetadata[index];
		for (int i = 1; i < 4; i += 2)
		{
			int addFace = regionConceptualMesh.AddFace(cmesh.HalfEdgeNodeBack(origSides[i].SidePieces.First()), cmesh.HalfEdgeNodeForward(origSides[i].SidePieces.Last()));
			regionConceptualMesh.FaceRawLine[addFace] = origSides[i].Smooth;
			regionConceptualMesh.FaceDiscretization[addFace] = new DiscretizeLine(2);
			int fcStart = -1;
			for (int j = 0; j < regionConceptualMesh.CellFaces[regionIdx].Count; j++)
			{
				HalfEdge halfEdge = regionConceptualMesh.CellFaces[regionIdx][j];
				if (halfEdge.fIdx < faceMap.Count && new HalfEdge(faceMap[halfEdge.fIdx], halfEdge.orient) == origSides[i].SidePieces[0])
				{
					fcStart = j;
					break;
				}
			}
			int countOffset = origSides[i].SidePieces.Count - 1;
			if (countOffset > 0)
			{
				regionConceptualMesh.CellFacesReplace(regionIdx, fcStart, origSides[i].SidePieces.Count, new HalfEdge(addFace, 0));
				int cornerRingNode = cellRegionMetadata.CornerRingNode(i);
				if (cellRegionMetadata.CornerRingNodeNE > cornerRingNode)
				{
					cellRegionMetadata.CornerRingNodeNE -= countOffset;
				}
				if (cellRegionMetadata.CornerRingNodeNW > cornerRingNode)
				{
					cellRegionMetadata.CornerRingNodeNW -= countOffset;
				}
				if (cellRegionMetadata.CornerRingNodeSE > cornerRingNode)
				{
					cellRegionMetadata.CornerRingNodeSE -= countOffset;
				}
				if (cellRegionMetadata.CornerRingNodeSW > cornerRingNode)
				{
					cellRegionMetadata.CornerRingNodeSW -= countOffset;
				}
			}
		}
		regionConceptualMesh.CellRegionMetadata[regionIdx] = cellRegionMetadata;
		DiscretizeRegion.Apply(regionConceptualMesh, regionIdx, DiscretizeRegion.ReplaceMode.COARSEN_EXISTING);
		return regionConceptualMesh;
	}

	public new static void RotateSides(ConceptualMesh cmesh, int cIdx, ref List<PatchSide> sides, Span<int> corners)
	{
		int ordinal = -1;
		for (int i = 0; i < 4; i++)
		{
			foreach (HalfEdge sidePiece in sides[i].SidePieces)
			{
				int halfEdgeToOppositeCell = cmesh.HalfEdgeToOppositeCell(sidePiece);
				if (halfEdgeToOppositeCell >= 0 && cmesh.CellRegionMetadata[halfEdgeToOppositeCell].PatchType == PatchType.OneD)
				{
					ordinal = i;
					break;
				}
			}
		}
		if (ordinal < 0)
		{
			PatchCommon.RotateSides(cmesh, cIdx, ref sides, corners);
		}
		else if (ordinal % 2 == 0)
		{
			PatchSide.RotateSidesOnce(sides, corners);
		}
	}

	private static void RemapToOriginal(SlidingMesh mesh, ConceptualMesh cmesh, int regionIdx)
	{
		List<Point> points = new List<Point>();
		List<int> intList = new List<int>();
		List<HalfEdge> halfEdges = new List<HalfEdge>(points.Count);
		HashSet<int> intSet = new HashSet<int>();
		SpatialIndex<int> pointIndex = mesh.PointIndex();
		int faceCount = mesh.FaceCount;
		points.Add(cmesh.NodePoints[cmesh.HalfEdgeNodeBack(cmesh.CellFaces[regionIdx][0])]);
		intList.Add(mesh.FindOrAddNode(points.First(), ref pointIndex));
		VirtualConstraints virtualConstraints = new VirtualConstraints();
		for (int i = 0; i < cmesh.CellFaces[regionIdx].Count; i++)
		{
			HalfEdge halfEdge = cmesh.CellFaces[regionIdx][i];
			int faceConstraints = virtualConstraints.Add(cmesh.FaceSmoothLine[halfEdge.fIdx], halfEdge.fIdx);
			Polyline polyline = cmesh.FaceNodeString[halfEdge.fIdx].Clone();
			if (halfEdge.orient == 1)
			{
				polyline.Reverse();
			}
			for (int j = 1; j < polyline.PointCount; j++)
			{
				points.Add(polyline.Points[j]);
				intList.Add(mesh.FindOrAddNode(points.Last(), ref pointIndex));
				int ordinal = intList[intList.Count - 2];
				int ordinal2 = intList[intList.Count - 1];
				HalfEdge nodesToHalfEdge = mesh.NodesToHalfEdge(ordinal, ordinal2);
				if (nodesToHalfEdge.IsNull())
				{
					nodesToHalfEdge = new HalfEdge(mesh.AddFace(ordinal, ordinal2), 0);
				}
				mesh.FaceConstraints[nodesToHalfEdge.fIdx] = (ConstraintState)faceConstraints;
				intSet.Add(nodesToHalfEdge.fIdx);
				halfEdges.Add(nodesToHalfEdge);
			}
		}
		points.RemoveAt(points.Count - 1);
		intList.RemoveAt(intList.Count - 1);
		for (int k = 0; k < faceCount; k++)
		{
			if (!mesh.FaceDeleted[k] && mesh.FaceConstraints[k] >= (ConstraintState)0 && !intSet.Contains(k))
			{
				HalfEdge halfEdge2 = new HalfEdge(k, 0);
				if (mesh.HalfEdgeToCell(halfEdge2) < 0)
				{
					halfEdge2 = halfEdge2.Flipped();
				}
				int halfEdgeNodeBack = mesh.HalfEdgeNodeBack(halfEdge2);
				int halfEdgeNodeForward = mesh.HalfEdgeNodeForward(halfEdge2);
				int indexOf = intList.IndexOf(halfEdgeNodeBack);
				int indexOf2 = intList.IndexOf(halfEdgeNodeForward);
				if (indexOf < 0 || indexOf2 < 0)
				{
					throw new DynamicMeshException("Failed to remap node of 1D reach");
				}
				List<HalfEdge> range = null;
				if (indexOf < indexOf2)
				{
					range = halfEdges.GetRange(indexOf, indexOf2 - indexOf);
				}
				else
				{
					range = halfEdges.GetRange(indexOf, halfEdges.Count - indexOf);
					range.AddRange(halfEdges.GetRange(0, indexOf2));
				}
				int cIdx = mesh.HalfEdgeToCell(halfEdge2);
				mesh.CellFaceReplace(cIdx, halfEdge2, range);
				mesh.DeleteFace(halfEdge2.fIdx);
			}
		}
		mesh.constraints = virtualConstraints;
	}
}
