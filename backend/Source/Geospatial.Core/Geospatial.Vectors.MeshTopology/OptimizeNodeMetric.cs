using System;
using System.Collections.Generic;
using Utility.Progress;

namespace Geospatial.Vectors.MeshTopology;

public abstract class OptimizeNodeMetric : MeshOptimizer
{
	public bool snapToVertex = true;

	public double snapTolerance = 1E-08;

	public bool checkMoves = true;

	public int NumFunctionCalls { get; protected set; }

	public int NodesMoved { get; protected set; }

	public OptimizeNodeMetric(SlidingMesh mesh, IList<int> nodes = null)
		: base(mesh, nodes)
	{
	}

	public abstract double NodeLoss(int nIdx, Point p);

	public virtual double NodeLoss(int nIdx, PointOnPolyline pop)
	{
		return NodeLoss(nIdx, pop.Point);
	}

	public override bool OptimizeOnce(ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		NodesMoved = 0;
		NumFunctionCalls = 0;
		if (nodes == null)
		{
			nodes = DefaultNodes();
		}
		int scaledCount = (int)((double)nodes.Count / 100.0);
		if (scaledCount == 0)
		{
			scaledCount = 1;
		}
		for (int i = 0; i < nodes.Count; i++)
		{
			int node = nodes[i];
			if (i % scaledCount == 0)
			{
				reporter.ReportProgressFraction((float)i / (float)nodes.Count);
			}
			if (reporter.CancelRequested)
			{
				return false;
			}
			if (mesh.NodeConstraints[node] >= (ConstraintState)0)
			{
				if (OptimizeOnceSliding(node))
				{
					NodesMoved++;
				}
			}
			else if (mesh.NodeConstraints[node] == ConstraintState.FREE && OptimizeOnceFree(node))
			{
				NodesMoved++;
			}
		}
		return NodesMoved > 0;
	}

	public bool OptimizeOnceSliding(int nIdx)
	{
		double bufferRelative = 0.1;
		int nodeConstraint = (int)mesh.NodeConstraints[nIdx];
		Polyline constraint = mesh.constraints[nodeConstraint].Polyline;
		mesh.SlideNodeBounds(nIdx, out var lowerDist, out var upperDist, bufferRelative);
		if (lowerDist == upperDist)
		{
			return false;
		}
		PointOnPolyline pop = mesh.NodePointOnConstraint[nIdx];
		double[] segLengths = null;
		double distanceAlong = mesh.constraints.DistanceAlong(nodeConstraint, pop, ref segLengths);
		UnivariateMinimizer univariateMinimizer = new UnivariateMinimizer(Loss, distanceAlong, lowerDist, upperDist);
		univariateMinimizer.Minimize(out var xOpt);
		if (snapToVertex && univariateMinimizer.stopCondition == FunctionMinimizer.StopCondition.StopXTolerance)
		{
			PointOnPolyline pointOnPolyline = constraint.PointAtDistanceFast(xOpt, segLengths);
			Segment segment = constraint.Segments[pointOnPolyline.SegmentIndex];
			double xOpt2 = xOpt - pointOnPolyline.Fraction * segment.Length;
			double xOpt3 = xOpt + (1.0 - pointOnPolyline.Fraction) * segment.Length;
			double fOpt = univariateMinimizer.fOpt;
			if (xOpt2 >= univariateMinimizer.xOptLow)
			{
				double loss = Loss(xOpt2);
				if (loss <= fOpt + snapTolerance)
				{
					fOpt = loss;
					xOpt = xOpt2;
				}
			}
			if (xOpt3 <= univariateMinimizer.xOptHigh)
			{
				double loss2 = Loss(xOpt3);
				if (loss2 <= fOpt + snapTolerance)
				{
					fOpt = loss2;
					xOpt = xOpt3;
				}
			}
		}
		if (xOpt != distanceAlong)
		{
			mesh.SlideNode(nIdx, constraint.PointAtDistanceFast(xOpt, segLengths), checkMoves);
			return true;
		}
		return false;
		double Loss(double s)
		{
			PointOnPolyline pop2 = constraint.PointAtDistanceFast(s, segLengths);
			int numFunctionCalls = NumFunctionCalls;
			NumFunctionCalls = numFunctionCalls + 1;
			return NodeLoss(nIdx, pop2);
		}
	}

	public bool OptimizeOnceFree(int nIdx)
	{
		Point xInitial = mesh.NodePoints[nIdx];
		double pointTerm = xInitial.X;
		double pointTerm2 = xInitial.X;
		double pointTerm3 = xInitial.Y;
		double pointTerm4 = xInitial.Y;
		foreach (HalfEdge nodeToHalfEdge in mesh.NodeToHalfEdges(nIdx))
		{
			Point point = mesh.NodePoints[mesh.HalfEdgeNodeForward(nodeToHalfEdge)];
			pointTerm = Math.Min(pointTerm, point.X);
			pointTerm2 = Math.Max(pointTerm2, point.X);
			pointTerm3 = Math.Min(pointTerm3, point.Y);
			pointTerm4 = Math.Max(pointTerm4, point.Y);
		}
		if (new PointMinimizer(bounds: new Extent(pointTerm, pointTerm2, pointTerm3, pointTerm4), Loss: Loss, xInitial: xInitial).Minimize(out var xOpt))
		{
			mesh.MoveNode(nIdx, xOpt, checkMoves);
			return true;
		}
		return false;
		double Loss(Point x)
		{
			int numFunctionCalls = NumFunctionCalls;
			NumFunctionCalls = numFunctionCalls + 1;
			return NodeLoss(nIdx, x);
		}
	}
}
