using System;
using System.Collections.Generic;
using Utility.Progress;

namespace Geospatial.Vectors.MeshTopology;

public class OptimizeConstructed(SlidingMesh mesh, IList<int> nodes = null) : MeshOptimizer(mesh, nodes)
{
	public double nudgeConvex = -1.0;

	public bool checkMoves = true;

	public override bool OptimizeOnce(ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		if (nodes == null)
		{
			nodes = DefaultNodes();
		}
		int ordinal = 0;
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
			if (ConstructedPoint(node, out var x, out var y))
			{
				if (!double.IsFinite(x) || !double.IsFinite(y))
				{
					x = mesh.NodePoints[node].X;
					y = mesh.NodePoints[node].Y;
					throw new OptimizeException($"Non-finite constructed point at {x} {y}");
				}
				if (nudgeConvex >= 0.0)
				{
					mesh.NudgePoint(node, nudgeConvex, ref x, ref y);
				}
				Point p = new Point(x, y);
				if (mesh.MoveOrSlideNode(node, p, checkMoves))
				{
					ordinal++;
				}
			}
		}
		return ordinal > 0;
	}

	public virtual bool ConstructedPoint(int nIdx, out double x, out double y)
	{
		throw new NotImplementedException();
	}
}
