using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Utility.DataStructures;
using Utility.Extensions;
using Utility.Progress;

namespace Geospatial.Vectors.MeshTopology;

public class PostMergeFaces : MeshOptimizer
{
	public bool enableMerge = true;

	public bool enableCollapse;

	public bool enablePerimeter = true;

	public double deltaCostThreshold = 0.2;

	public double minCostThreshold = 0.4;

	public int maxSidesAllowed = 4;

	public double skewFactor = 2.0;

	public double cellScaleFactor;

	public double faceSmoothFactor = 30.0;

	public double faceScaleFactor = 0.1;

	public double localIntersectionFactor;

	public ISizeFunction sizeFunction;

	public PostMergeFaces(SlidingMesh mesh, ISizeFunction sizeFunction)
		: base(mesh)
	{
		this.sizeFunction = sizeFunction;
	}

	public override bool OptimizeOnce(ProgressReporter reporter = null)
	{
		if (nodes == null)
		{
			nodes = DefaultNodes();
		}
		int scaledCount = (int)((double)nodes.Count / 100.0);
		if (scaledCount == 0)
		{
			scaledCount = 1;
		}
		bool result = false;
		for (int i = 0; i < nodes.Count; i++)
		{
			if (reporter != null)
			{
				if (i % scaledCount == 0)
				{
					reporter.ReportProgressFraction((float)i / (float)nodes.Count);
				}
				if (reporter.CancelRequested)
				{
					return false;
				}
			}
			int nIdx = nodes[i];
			if (MaybeCollapse(nIdx))
			{
				result = true;
			}
		}
		return result;
	}

	private double HalfEdgeCost(HalfEdge he)
	{
		SlidingMesh.CollapseFacePreflightInfo preflight;
		int mergeFace;
		return HalfEdgeCost(he, out preflight, out mergeFace);
	}

	private double HalfEdgeCost(HalfEdge he, out SlidingMesh.CollapseFacePreflightInfo preflight, out int mergeFace)
	{
		double scale = sizeFunction.Query(mesh.FaceMidPoint(he.fIdx));
		preflight = null;
		mergeFace = -1;
		double costBeforeCollapseHalfEdgeNodeBackTerm;
		double costCollapsedHeTerm;
		if (he.IsDirected())
		{
			costBeforeCollapseHalfEdgeNodeBackTerm = CostBeforeCollapse(mesh.HalfEdgeNodeBack(he), scale);
			costCollapsedHeTerm = CostCollapsed(he, scale, ref preflight);
		}
		else
		{
			mergeFace = he.fIdx;
			if (mesh.FaceConstraints[mergeFace] >= (ConstraintState)0)
			{
				return double.MaxValue;
			}
			costBeforeCollapseHalfEdgeNodeBackTerm = CostBeforeMerge(he.fIdx, scale);
			costCollapsedHeTerm = CostMerged(he.fIdx, scale);
		}
		return costCollapsedHeTerm - costBeforeCollapseHalfEdgeNodeBackTerm;
	}

	public bool OptimizeAdaptive(int maxOperationCount, ProgressReporter reporter = null)
	{
		if (maxOperationCount == 0)
		{
			return false;
		}
		bool result = false;
		bool allowCollapseOntoNode = true;
		int scaledMaxOperationCount = 20 * maxOperationCount;
		List<int> intList = new List<int>();
		PriorityDictionary<HalfEdge, double> pqueue = new PriorityDictionary<HalfEdge, double>(HalfEdge.NullHalfEdge, double.MaxValue);
		if (nodes == null)
		{
			nodes = DefaultNodes();
		}
		HashSet<int> nodeSet = new HashSet<int>(nodes);
		foreach (int node in nodes)
		{
			UpdateMoves(node, update: false);
		}
		while (scaledMaxOperationCount > 0 && pqueue.Count != 0)
		{
			HalfEdge he = pqueue.Dequeue(out var _);
			if (!mesh.FaceDeleted[he.fIdx] && ValidOperation(he))
			{
				if (HalfEdgeCost(he, out var preflight, out var mergeFace) >= 0.0)
				{
					break;
				}
				intList.Clear();
				if (preflight != null)
				{
					int halfEdgeNodeBack = mesh.HalfEdgeNodeBack(preflight.spokes[0]);
					List<int> intListList = new List<int>();
					foreach (HalfEdge spoke in preflight.spokes)
					{
						int halfEdgeToCell = mesh.HalfEdgeToCell(spoke);
						if (halfEdgeToCell < 0)
						{
							continue;
						}
						intListList.Add(halfEdgeToCell);
						if (mesh.CellNumSides(halfEdgeToCell) == 3)
						{
							int halfEdgeToOppositeCell = mesh.HalfEdgeToOppositeCell(mesh.HalfEdgeNext(spoke));
							if (halfEdgeToOppositeCell >= 0)
							{
								intListList.Add(halfEdgeToOppositeCell);
							}
						}
					}
					foreach (int ordinal in intListList)
					{
						foreach (HalfEdge cellFace in mesh.CellFaces[ordinal])
						{
							int halfEdgeNodeForward = mesh.HalfEdgeNodeForward(cellFace);
							if (halfEdgeNodeForward != halfEdgeNodeBack && !intList.Contains(halfEdgeNodeForward))
							{
								intList.Add(halfEdgeNodeForward);
							}
						}
					}
					mesh.DoCollapseFace(preflight);
				}
				else
				{
					int cIdx = mesh.MergeCells(mergeFace);
					mesh.CellToNodes(cIdx, intList);
				}
				result = true;
				foreach (int ordinal2 in intList)
				{
					UpdateMoves(ordinal2, update: true);
				}
			}
			scaledMaxOperationCount--;
		}
		return result;
		void UpdateMoves(int nIdx, bool update)
		{
			foreach (HalfEdge nodeToHalfEdge in mesh.NodeToHalfEdges(nIdx))
			{
				if (enableCollapse)
				{
					if (ValidOperation(nodeToHalfEdge) && (update || !pqueue.Contains(nodeToHalfEdge)))
					{
						pqueue.Add(nodeToHalfEdge, HalfEdgeCost(nodeToHalfEdge));
					}
					if (allowCollapseOntoNode)
					{
						HalfEdge halfEdge = nodeToHalfEdge.Flipped();
						if (ValidOperation(halfEdge) && (update || !pqueue.Contains(halfEdge)))
						{
							double newPriority = HalfEdgeCost(halfEdge);
							pqueue.Add(halfEdge, newPriority);
						}
					}
				}
				if (enableMerge)
				{
					HalfEdge halfEdge2 = nodeToHalfEdge.Undirected();
					if (ValidOperation(halfEdge2) && (update || !pqueue.Contains(halfEdge2)))
					{
						pqueue.Add(halfEdge2, HalfEdgeCost(halfEdge2));
					}
				}
			}
		}
		bool ValidOperation(HalfEdge he2)
		{
			if (!he2.IsDirected())
			{
				if (!nodeSet.Contains(mesh.FaceNodes[he2.fIdx].A))
				{
					return nodeSet.Contains(mesh.FaceNodes[he2.fIdx].B);
				}
				return true;
			}
			if (nodeSet.Contains(mesh.HalfEdgeNodeBack(he2)))
			{
				return true;
			}
			if (allowCollapseOntoNode && nodeSet.Contains(mesh.HalfEdgeNodeForward(he2)))
			{
				return true;
			}
			return false;
		}
	}

	public double CostBeforeCollapse(int nIdx, double scale)
	{
		return CostBeforeCollapse(mesh.NodeToSortedHalfEdges(nIdx), scale);
	}

	public double CostBeforeCollapse(List<HalfEdge> spokes, double scale)
	{
		double cellSkewnessMeshMetricsTerm = 0.0;
		double maxValue = double.MaxValue;
		double absoluteCellSimpleAreaTerm = 0.0;
		double cellFaceLengthMeshMetricsTerm = 0.0;
		double cellLocalIntersectionCostSum = 0.0;
		foreach (HalfEdge spoke in spokes)
		{
			int halfEdgeToCell = mesh.HalfEdgeToCell(spoke);
			if (halfEdgeToCell >= 0)
			{
				cellSkewnessMeshMetricsTerm = Math.Max(cellSkewnessMeshMetricsTerm, MeshMetrics.CellSkewness(mesh, halfEdgeToCell));
				double absoluteCellSimpleArea = Math.Abs(mesh.CellSimpleArea(halfEdgeToCell));
				maxValue = Math.Min(maxValue, absoluteCellSimpleArea);
				absoluteCellSimpleAreaTerm = Math.Max(absoluteCellSimpleAreaTerm, absoluteCellSimpleArea);
				cellFaceLengthMeshMetricsTerm = Math.Max(cellFaceLengthMeshMetricsTerm, MeshMetrics.CellFaceLength(mesh, halfEdgeToCell, (float)scale));
				cellLocalIntersectionCostSum += CellLocalIntersectionCost(halfEdgeToCell);
			}
		}
		return NetCost(cellSkewnessMeshMetricsTerm, maxValue, absoluteCellSimpleAreaTerm, cellFaceLengthMeshMetricsTerm, 0.0, cellLocalIntersectionCostSum, scale);
	}

	public double CostCollapsed(HalfEdge he, double scale, ref SlidingMesh.CollapseFacePreflightInfo preflight, List<HalfEdge> spokes = null)
	{
		HalfEdge addTo = HalfEdge.NullHalfEdge;
		int halfEdgeNodeBack = mesh.HalfEdgeNodeBack(he);
		if (mesh.NodeIsFixed(halfEdgeNodeBack))
		{
			return double.MaxValue;
		}
		if (spokes == null)
		{
			spokes = mesh.NodeToHalfEdges(halfEdgeNodeBack);
		}
		ConstraintState constraintState = mesh.FaceConstraints[he.fIdx];
		if (mesh.NodeConstraints[halfEdgeNodeBack] >= (ConstraintState)0 && constraintState < (ConstraintState)0)
		{
			return double.MaxValue;
		}
		if (constraintState >= (ConstraintState)0)
		{
			for (int i = 0; i < spokes.Count; i++)
			{
				if (!(spokes[i] == he) && mesh.FaceConstraints[spokes[i].fIdx] == constraintState)
				{
					addTo = spokes[i];
					break;
				}
			}
		}
		return CostCollapsed(he, addTo, scale, ref preflight);
	}

	private static double SmoothRatio(Polyline pline)
	{
		if (pline == null)
		{
			return 0.0;
		}
		return Math.Abs(Polygon.ComputeArea(pline, pline.PointCount, (Polyline pl, int i) => pl.Points[i])) / Math.Pow(pline.Length, 2.0);
	}

	public double NetCost(double maxSkew, double minArea, double maxArea, double maxLengthCost, double smoothRatio, double localIntersections, double scale)
	{
		double scalePolynomial = scale * scale;
		double minAreaMaxAreaTerm = 0.0;
		if (minArea <= maxArea)
		{
			minAreaMaxAreaTerm = Math.Max(1.0, scalePolynomial / minArea) + Math.Max(1.0, maxArea / scalePolynomial) - 2.0;
		}
		maxSkew += Math.Max(0.0, 100.0 * (maxSkew - 1.0));
		return skewFactor * maxSkew + cellScaleFactor * minAreaMaxAreaTerm + faceSmoothFactor * smoothRatio + faceScaleFactor * maxLengthCost + localIntersectionFactor * localIntersections;
	}

	public double CostCollapsed(HalfEdge he, HalfEdge addTo, double scale, ref SlidingMesh.CollapseFacePreflightInfo preflight)
	{
		preflight = null;
		if (!mesh.CollapseFacePreflight(in he, addTo, ref preflight))
		{
			preflight = null;
			return double.MaxValue;
		}
		double cellSkewnessMeshMetricsTerm = 0.0;
		double maxValue = double.MaxValue;
		double absoluteAreaTerm = 0.0;
		double cellFaceLengthMeshMetricsTerm = 0.0;
		double cellLocalIntersectionCostSum = 0.0;
		for (int i = 0; i < preflight.newSimpleCells.Count; i++)
		{
			Polygon polygon = preflight.newSimpleCells[i];
			_ = preflight.newSmoothCells[i];
			cellSkewnessMeshMetricsTerm = Math.Max(cellSkewnessMeshMetricsTerm, MeshMetrics.CellSkewness(polygon));
			double absoluteArea = Math.Abs(polygon.Area);
			maxValue = Math.Min(maxValue, absoluteArea);
			absoluteAreaTerm = Math.Max(absoluteAreaTerm, absoluteArea);
			cellFaceLengthMeshMetricsTerm = Math.Max(cellFaceLengthMeshMetricsTerm, MeshMetrics.CellFaceLength(polygon, (float)scale));
			cellLocalIntersectionCostSum += CellLocalIntersectionCost(preflight.newSmoothCells[i]);
		}
		return NetCost(cellSkewnessMeshMetricsTerm, maxValue, absoluteAreaTerm, cellFaceLengthMeshMetricsTerm, SmoothRatio(preflight.plineAE), cellLocalIntersectionCostSum, scale);
	}

	public double CostBeforeMerge(int fIdx, double scale)
	{
		double cellSkewnessMeshMetricsTerm = 0.0;
		double maxValue = double.MaxValue;
		double absoluteCellSimpleAreaTerm = 0.0;
		double cellFaceLengthMeshMetricsTerm = 0.0;
		double cellLocalIntersectionCostSum = 0.0;
		foreach (int faceCell in mesh.FaceCells[fIdx])
		{
			if (faceCell >= 0)
			{
				double absoluteCellSimpleArea = Math.Abs(mesh.CellSimpleArea(faceCell));
				cellSkewnessMeshMetricsTerm = Math.Max(cellSkewnessMeshMetricsTerm, MeshMetrics.CellSkewness(mesh, faceCell));
				maxValue = Math.Min(maxValue, absoluteCellSimpleArea);
				absoluteCellSimpleAreaTerm = Math.Max(absoluteCellSimpleAreaTerm, absoluteCellSimpleArea);
				cellFaceLengthMeshMetricsTerm = Math.Max(cellFaceLengthMeshMetricsTerm, MeshMetrics.CellFaceLength(mesh, faceCell, (float)scale));
				cellLocalIntersectionCostSum += CellLocalIntersectionCost(faceCell);
			}
		}
		return NetCost(cellSkewnessMeshMetricsTerm, maxValue, absoluteCellSimpleAreaTerm, cellFaceLengthMeshMetricsTerm, 0.0, cellLocalIntersectionCostSum, scale);
	}

	public double CostMerged(int fIdx, double scale)
	{
		if (mesh.FaceCells[fIdx].A < 0 || mesh.FaceCells[fIdx].B < 0)
		{
			return double.MaxValue;
		}
		if (mesh.FaceConstraints[fIdx] >= (ConstraintState)0)
		{
			return double.MaxValue;
		}
		if (mesh.CellNumSides(mesh.FaceCells[fIdx].A) + mesh.CellNumSides(mesh.FaceCells[fIdx].B) - 2 > maxSidesAllowed)
		{
			return double.MaxValue;
		}
		Polygon polygon = new Polygon();
		List<Polyline> polylines = null;
		if (localIntersectionFactor > 0.0)
		{
			polylines = new List<Polyline>(6);
		}
		int unity = -1;
		List<HalfEdge> cellFace = mesh.CellFaces[mesh.FaceCells[fIdx].B];
		for (unity = 0; unity < cellFace.Count && cellFace[unity].fIdx != fIdx; unity++)
		{
		}
		for (int i = 1; i < cellFace.Count; i++)
		{
			polygon.Add(mesh.NodePoints[mesh.HalfEdgeNodeForward(cellFace[unity])]);
			unity = (unity + 1) % cellFace.Count;
			polylines?.Add(mesh.GetFaceSmoothGeometry(cellFace[unity].fIdx));
		}
		cellFace = mesh.CellFaces[mesh.FaceCells[fIdx].A];
		for (unity = 0; unity < cellFace.Count && cellFace[unity].fIdx != fIdx; unity++)
		{
		}
		for (int j = 1; j < cellFace.Count; j++)
		{
			polygon.Add(mesh.NodePoints[mesh.HalfEdgeNodeForward(cellFace[unity])]);
			unity = (unity + 1) % cellFace.Count;
			polylines?.Add(mesh.GetFaceSmoothGeometry(cellFace[unity].fIdx));
		}
		Math.Abs(mesh.CellSmoothArea(mesh.FaceCells[fIdx].A));
		Math.Abs(mesh.CellSmoothArea(mesh.FaceCells[fIdx].B));
		float cellSkewness = MeshMetrics.CellSkewness(polygon);
		double absoluteArea = Math.Abs(polygon.Area);
		float cellFaceLength = MeshMetrics.CellFaceLength(polygon, (float)scale);
		double localIntersections = ((localIntersectionFactor > 0.0) ? CellLocalIntersectionCost(polylines) : 0.0);
		double maxSkew = cellSkewness;
		double maxArea = absoluteArea;
		return NetCost(maxSkew, absoluteArea, maxArea, cellFaceLength, 0.0, localIntersections, scale);
	}

	private double CellLocalIntersectionCost(int cIdx)
	{
		if (localIntersectionFactor == 0.0)
		{
			return 0.0;
		}
		List<Polyline> polylines = new List<Polyline>(mesh.CellFaces[cIdx].Count);
		foreach (HalfEdge cellFace in mesh.CellFaces[cIdx])
		{
			polylines.Add(mesh.GetFaceSmoothGeometry(cellFace.fIdx));
		}
		return CellLocalIntersectionCost(polylines);
	}

	private double CellLocalIntersectionCost(List<Polyline> faceSmooths)
	{
		for (int i = 0; i < faceSmooths.Count - 1; i++)
		{
			for (int j = i + 1; j < faceSmooths.Count; j++)
			{
				if (Polyline.Conflict(faceSmooths[i], faceSmooths[j]))
				{
					return 1.0;
				}
			}
		}
		return 0.0;
	}

	public double CellLocalIntersectionCost(Polygon smoothCell)
	{
		if (smoothCell.SelfIntersects())
		{
			return 1.0;
		}
		return 0.0;
	}

	private bool MaybeCollapse(int nIdx)
	{
		if (mesh.NodeConstraints[nIdx] == ConstraintState.FIXED)
		{
			return false;
		}
		double scale = sizeFunction.Query(mesh.NodePoints[nIdx]);
		List<HalfEdge> nodeToSortedHalfEdges = mesh.NodeToSortedHalfEdges(nIdx);
		double costBeforeCollapse = CostBeforeCollapse(nodeToSortedHalfEdges, scale);
		if (costBeforeCollapse < minCostThreshold)
		{
			return false;
		}
		int ordinal = -1;
		double costBeforeCollapseAboveDeltaCostThreshold = costBeforeCollapse - deltaCostThreshold;
		SlidingMesh.CollapseFacePreflightInfo preflight = null;
		for (int i = 0; i < nodeToSortedHalfEdges.Count; i++)
		{
			SlidingMesh.CollapseFacePreflightInfo preflight2 = null;
			double costCollapsed = CostCollapsed(nodeToSortedHalfEdges[i], scale, ref preflight2, nodeToSortedHalfEdges);
			if (costCollapsed < costBeforeCollapseAboveDeltaCostThreshold)
			{
				costBeforeCollapseAboveDeltaCostThreshold = costCollapsed;
				ordinal = i;
				preflight = preflight2;
			}
		}
		if (ordinal < 0)
		{
			return false;
		}
		nodeToSortedHalfEdges.RotateBackward(ordinal);
		mesh.DoCollapseFace(preflight);
		return true;
	}

	public void DumpToJson(Dictionary<string, object> meshBefore, SlidingMesh meshAfter, List<int> spokeNodes, int centerNode, bool success)
	{
		using FileStream utf8Json = File.Create("C:\\Temp\\cmd.json");
		JsonSerializerOptions options = new JsonSerializerOptions
		{
			WriteIndented = true,
			NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
			Converters = { (JsonConverter)new TinJsonConverter() }
		};
		List<object> localValue = new List<object>
		{
			new List<object> { "SET", "g_pre_raw", meshBefore },
			new List<object> { "SET", "g_post_raw", meshAfter },
			new List<object> { "SET", "spokes_raw", spokeNodes },
			new List<object> { "SET", "center", centerNode },
			new List<object> { "SET", "success", success },
			new List<object> { "PYTHON", "\r\ng_pre =json_to_mesh(g_pre_raw)\r\ng_post=json_to_mesh(g_post_raw)\r\nspokes=np.array(spokes_raw)\r\nfig,ax=plt.subplots(num=1,clear=1)\r\nax.set_adjustable('datalim')\r\ng_pre.plot_nodes(ax = ax, mask=spokes, color='green', labeler='id')\r\n#g_pre.plot_nodes(ax = ax, mask=spokes[:1], color='r')\r\nax.annotate('collapse',xy=g_pre.nodes['x'][spokes[0]],xytext=g_pre.nodes['x'][center],\r\n    arrowprops=dict(arrowstyle='->'))\r\ng_post.plot_edges(ax = ax, color = 'b', subedges='smoothStringXY')\r\ng_pre.plot_edges(ax = ax, color = 'm', ls='--', subedges='smoothStringXY')\r\nprint('Success:',success)\r\n" }
		};
		JsonSerializer.Serialize(utf8Json, localValue, options);
	}

	protected override IList<int> DefaultNodes()
	{
		if (enablePerimeter)
		{
			return mesh.MovableNodes();
		}
		return mesh.InternalNodes();
	}
}
