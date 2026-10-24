using System.Collections.Generic;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;

namespace Geospatial.Rendering.Editing;

public class ReverseArcDirectionOperation : TopologyEditOperation
{
	private readonly int _arcIndex;

	public override string Description => "Reverse Arc";

	public ReverseArcDirectionOperation(TopologyEditContext context, int arcIndex)
		: base(context, null)
	{
		_arcIndex = arcIndex;
	}

	protected override bool ApplyInternal()
	{
		ReverseArcDirection();
		return true;
	}

	protected override bool UndoInternal()
	{
		ReverseArcDirection();
		UndoSelectionState();
		return true;
	}

	private void ReverseArcDirection()
	{
		Topology topology = _context.Topology;
		Arc arc = topology.Arcs[_arcIndex];
		int startNode = arc.StartNode;
		topology.ReconnectArc(newNodeId: arc.EndNode, arcId: _arcIndex, isStart: true);
		topology.ReconnectArc(_arcIndex, startNode, isStart: false);
		Arc arc2 = arc;
		Arc arc3 = arc;
		int regionB = arc.RegionB;
		int regionA = arc.RegionA;
		arc2.RegionA = regionB;
		arc3.RegionB = regionA;
		ReverseInternalPointsInPlace(arc.InternalPoints);
	}

	private static void ReverseInternalPointsInPlace(PointCollection pts)
	{
		if (pts != null && pts.Count >= 2)
		{
			List<Point> points = new List<Point>(pts.Count);
			for (int i = 0; i < pts.Count; i++)
			{
				points.Add(pts[i]);
			}
			points.Reverse();
			for (int j = 0; j < points.Count; j++)
			{
				pts[j] = points[j];
			}
		}
	}
}
