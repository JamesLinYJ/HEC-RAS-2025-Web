using System;
using System.Collections.Generic;
using System.Diagnostics;
using Geospatial.Vectors.Topology;
using Utility.Systems;

namespace Geospatial.Vectors.Graph;

[DebuggerDisplay("{DebugDisplay()}")]
public class GraphArc : BaseArc, ISpatialVersion, IStreamableObject, IEquatable<GraphArc>
{
	public GraphArc()
	{
	}

	public GraphArc(int startNode, int endNode, List<Point> internalPoints = null)
		: base(startNode, endNode, internalPoints)
	{
	}

	public bool Equals(GraphArc other)
	{
		if (StartNode != other.StartNode)
		{
			return false;
		}
		if (EndNode != other.EndNode)
		{
			return false;
		}
		if (InternalPointCount != other.InternalPointCount)
		{
			return false;
		}
		if (InternalPointCount != 0 && !InternalPoints.EqualsXY(other.InternalPoints))
		{
			return false;
		}
		return true;
	}

	public GraphArc Clone()
	{
		List<Point> internalPoints = ((InternalPoints == null) ? null : new List<Point>(InternalPoints));
		return new GraphArc(StartNode, EndNode, internalPoints);
	}
}
