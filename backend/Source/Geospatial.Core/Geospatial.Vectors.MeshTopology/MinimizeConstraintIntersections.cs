using System;
using System.Collections.Generic;
using Utility.Extensions;

namespace Geospatial.Vectors.MeshTopology;

public class MinimizeConstraintIntersections : OptimizeNodeMetric
{
	protected int nIdxCached = -1;

	private Polyline constraintCached;

	private List<int> queryNeighborsCached;

	private double lengthScaleCached = -1.0;

	private ConceptualMesh cmesh;

	public double scaleLossFactor;

	public MinimizeConstraintIntersections(SlidingMesh mesh, ConceptualMesh cmesh, IList<int> nodes = null)
		: base(mesh, nodes)
	{
		this.cmesh = cmesh;
	}

	protected override IList<int> DefaultNodes()
	{
		return HeuristicNodes();
	}

	public List<int> HeuristicNodes()
	{
		MeshIntersectionChecker meshIntersectionChecker = new MeshIntersectionChecker(mesh);
		List<int> intList = new List<int>();
		meshIntersectionChecker.CheckAgainstArcs(cmesh, out var intersections);
		foreach (MeshIntersectionChecker.MeshIntersection intersection in intersections)
		{
			int faceNode = mesh.FaceNodes[intersection.faceA][0];
			int faceNode2 = mesh.FaceNodes[intersection.faceA][1];
			Point point = intersection.point;
			if (!point.IsNull())
			{
				double distanceTo = mesh.NodePoints[faceNode].DistanceTo(intersection.point);
				double distanceTo2 = mesh.NodePoints[faceNode].DistanceTo(intersection.point);
				if (distanceTo < distanceTo2)
				{
					intList.Add(faceNode);
					intList.Add(faceNode2);
				}
				else
				{
					intList.Add(faceNode2);
					intList.Add(faceNode);
				}
			}
			else
			{
				intList.Add(faceNode2);
				intList.Add(faceNode);
			}
		}
		return intList;
	}

	public override double NodeLoss(int nIdx, PointOnPolyline pop)
	{
		return NodeLoss(nIdx, pop.Point);
	}

	public override double NodeLoss(int nIdx, Point point)
	{
		CacheNode(nIdx);
		double computed = 0.0;
		if (scaleLossFactor > 0.0)
		{
			computed += scaleLossFactor * ScaleLoss(nIdx, point);
		}
		if (constraintCached.IsNullOrEmpty())
		{
			return computed;
		}
		foreach (int queryNeighborsCached2 in queryNeighborsCached)
		{
			Segment seg = new Segment(point, mesh.NodePoints[queryNeighborsCached2]);
			foreach (LineIntersection allIntersection in constraintCached.GetAllIntersections(seg))
			{
				double fraction = allIntersection.BIntersection.Fraction;
				double length = seg.Length;
				computed += fraction * (1.0 - fraction) * length * length;
			}
			if (computed > 0.0)
			{
				computed /= lengthScaleCached * lengthScaleCached;
			}
		}
		return computed;
	}

	private double ScaleLoss(int nIdx, Point point)
	{
		double computed = 0.0;
		foreach (HalfEdge nodeToHalfEdge in mesh.NodeToHalfEdges(nIdx))
		{
			double distanceToNodePointsTerm = Math.Log(mesh.NodePoints[mesh.HalfEdgeNodeForward(nodeToHalfEdge)].DistanceTo(point) / (double)mesh.FaceTargetLength[nodeToHalfEdge.fIdx]);
			computed += distanceToNodePointsTerm * distanceToNodePointsTerm;
		}
		return computed;
	}

	protected void CacheNode(int nIdx)
	{
		if (nIdxCached == nIdx)
		{
			return;
		}
		constraintCached = Polyline.NullPolyline();
		ConstraintState constraintState = mesh.NodeConstraints[nIdx];
		if (constraintState >= (ConstraintState)0)
		{
			constraintCached = mesh.constraints[(int)constraintState].Polyline;
		}
		lengthScaleCached = 0.0;
		int ordinal = 0;
		queryNeighborsCached = new List<int>();
		foreach (HalfEdge nodeToHalfEdge in mesh.NodeToHalfEdges(nIdx))
		{
			if (mesh.FaceConstraints[nodeToHalfEdge.fIdx] < (ConstraintState)0)
			{
				int halfEdgeNodeForward = mesh.HalfEdgeNodeForward(nodeToHalfEdge);
				queryNeighborsCached.Add(halfEdgeNodeForward);
				lengthScaleCached += mesh.FaceSegment(nodeToHalfEdge.fIdx).Length;
				if (constraintState < (ConstraintState)0 && mesh.NodeConstraints[halfEdgeNodeForward] >= (ConstraintState)0)
				{
					constraintState = mesh.NodeConstraints[halfEdgeNodeForward];
					constraintCached = mesh.constraints[(int)constraintState].Polyline;
				}
				ordinal++;
			}
		}
		if (ordinal > 0)
		{
			lengthScaleCached /= ordinal;
		}
	}
}
