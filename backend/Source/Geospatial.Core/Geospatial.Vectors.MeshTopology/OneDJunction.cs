using System;
using System.Collections.Generic;
using Geospatial.Vectors.Topology;
using Utility.Logging;
using Utility.Progress;

namespace Geospatial.Vectors.MeshTopology;

public class OneDJunction : PatchCommon, IRegionGenerator
{
	public OneDJunction(ConceptualMesh cmesh, int cIdx)
		: base(cmesh, cIdx)
	{
	}

	public static bool IsJunction(ConceptualMesh cmesh, int regionIdx)
	{
		int ordinal = 0;
		int halfEdgeToOppositeCell = cmesh.HalfEdgeToOppositeCell(cmesh.CellFaces[regionIdx][0]);
		int unity = -1;
		foreach (HalfEdge cellFace in cmesh.CellFaces[regionIdx])
		{
			int halfEdgeToOppositeCell2 = cmesh.HalfEdgeToOppositeCell(cellFace);
			if (halfEdgeToOppositeCell2 >= 0 && cmesh.CellRegionMetadata[halfEdgeToOppositeCell2].PatchType == PatchType.OneD)
			{
				if (halfEdgeToOppositeCell2 != unity)
				{
					ordinal++;
					unity = halfEdgeToOppositeCell2;
				}
			}
			else
			{
				unity = -1;
			}
		}
		if (unity == halfEdgeToOppositeCell)
		{
			ordinal--;
		}
		return ordinal > 2;
	}

	public SlidingMesh Generate(Action<SlidingMesh> Progress = null, ProgressReporter reporter = null, OperationResult res = null)
	{
		result = new SlidingMesh();
		int addCell = result.AddCell();
		List<HalfEdge> halfEdges = new List<HalfEdge>();
		Dictionary<int, int> intByInt = new Dictionary<int, int>();
		for (int i = 0; i < cmesh.CellFaces[regionIdx].Count; i++)
		{
			HalfEdge he = cmesh.CellFaces[regionIdx][i];
			int halfEdgeToOppositeCell = cmesh.HalfEdgeToOppositeCell(he);
			if (halfEdgeToOppositeCell < 0 || cmesh.CellRegionMetadata[halfEdgeToOppositeCell].PatchType == PatchType.Empty || cmesh.CellRegionMetadata[halfEdgeToOppositeCell].PatchType == PatchType.OneD)
			{
				int halfEdgeNodeBack = cmesh.HalfEdgeNodeBack(he);
				int halfEdgeNodeForward = cmesh.HalfEdgeNodeForward(he);
				if (!intByInt.TryGetValue(halfEdgeNodeBack, out var halfEdgeNodeBackFindOrAddFixedNodeTerm))
				{
					halfEdgeNodeBackFindOrAddFixedNodeTerm = (intByInt[halfEdgeNodeBack] = result.FindOrAddFixedNode(cmesh.NodePoints[halfEdgeNodeBack]));
				}
				if (!intByInt.TryGetValue(halfEdgeNodeForward, out var halfEdgeNodeForwardFindOrAddFixedNodeTerm))
				{
					halfEdgeNodeForwardFindOrAddFixedNodeTerm = (intByInt[halfEdgeNodeForward] = result.FindOrAddFixedNode(cmesh.NodePoints[halfEdgeNodeForward]));
				}
				if (he.orient == 1)
				{
					int ordinal = halfEdgeNodeBackFindOrAddFixedNodeTerm;
					halfEdgeNodeBackFindOrAddFixedNodeTerm = halfEdgeNodeForwardFindOrAddFixedNodeTerm;
					halfEdgeNodeForwardFindOrAddFixedNodeTerm = ordinal;
				}
				int addFace = result.AddFace(halfEdgeNodeBackFindOrAddFixedNodeTerm, halfEdgeNodeForwardFindOrAddFixedNodeTerm);
				result.FaceSmoothLine[addFace] = cmesh.FaceSmoothLine[he.fIdx];
				halfEdges.Add(new HalfEdge(addFace, he.orient));
				int faceConstraints = result.constraints.Add(cmesh.FaceSmoothLine[he.fIdx], he.fIdx);
				result.FaceConstraints[addFace] = (ConstraintState)faceConstraints;
			}
			else
			{
				result.AddArcWithConstraint(cmesh, he, intByInt, VirtualConstraints.ConstraintMode.NULL, halfEdges);
			}
		}
		foreach (HalfEdge halfEdge in halfEdges)
		{
			result.CellAddFace(addCell, halfEdge);
		}
		result.CellTypes[addCell] = SlidingMesh.CellType.CELL_1D_JUNCTION;
		return result;
	}
}
