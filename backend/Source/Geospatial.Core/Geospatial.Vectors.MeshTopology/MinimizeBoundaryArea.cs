using System;
using System.Collections.Generic;

namespace Geospatial.Vectors.MeshTopology;

public class MinimizeBoundaryArea : OptimizeNodeMetric
{
	protected int nIdxCached = -1;

	public readonly double epsLength = 1E-10;

	private Polyline constraintCached;

	private List<int> queryNeighborsCached;

	private ConceptualMesh cmesh;

	public double scaleLossFactor;

	public MinimizeBoundaryArea(SlidingMesh mesh, ConceptualMesh cmesh, IList<int> nodes = null)
		: base(mesh, nodes)
	{
		this.cmesh = cmesh;
	}

	protected override IList<int> DefaultNodes()
	{
		return mesh.SlideNodes();
	}

	public override double NodeLoss(int nIdx, Point p)
	{
		CacheNode(nIdx);
		return NodeLoss(nIdx, constraintCached.NearestPointOnPolyline(p));
	}

	public override double NodeLoss(int nIdx, PointOnPolyline pop)
	{
		CacheNode(nIdx);
		double computed = 0.0;
		foreach (int queryNeighborsCached2 in queryNeighborsCached)
		{
			PointOnPolyline pointOnPolyline = ((mesh.NodeConstraints[queryNeighborsCached2] != mesh.NodeConstraints[nIdx]) ? constraintCached.NearestPointOnPolyline(mesh.NodePoints[queryNeighborsCached2]) : mesh.NodePointOnConstraint[queryNeighborsCached2]);
			if (pointOnPolyline.SegmentIndex != pop.SegmentIndex)
			{
				Polyline polyline = ((pointOnPolyline.SegmentIndex >= pop.SegmentIndex) ? constraintCached.Subsection(pop, pointOnPolyline) : constraintCached.Subsection(pointOnPolyline, pop));
				double absoluteArea = Math.Abs(new Polygon(polyline.Points).Area);
				double epsLengthDistanceToTerm = Math.Max(epsLength, pointOnPolyline.Point.DistanceTo(pop.Point));
				computed += absoluteArea / (epsLengthDistanceToTerm * epsLengthDistanceToTerm);
			}
		}
		if (scaleLossFactor > 0.0)
		{
			computed += scaleLossFactor * ScaleLoss(nIdx, pop.Point);
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
		ConstraintState constraintState = mesh.NodeConstraints[nIdx];
		constraintCached = mesh.constraints[(int)constraintState].Polyline;
		queryNeighborsCached = new List<int>();
		foreach (HalfEdge nodeToHalfEdge in mesh.NodeToHalfEdges(nIdx))
		{
			if (mesh.FaceConstraints[nodeToHalfEdge.fIdx] == constraintState)
			{
				queryNeighborsCached.Add(mesh.HalfEdgeNodeForward(nodeToHalfEdge));
			}
		}
	}
}
