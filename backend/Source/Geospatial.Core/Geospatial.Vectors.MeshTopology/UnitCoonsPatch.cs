using System;
using System.Collections.Generic;
using Geospatial.Transformers;

namespace Geospatial.Vectors.MeshTopology;

public class UnitCoonsPatch : CoonsBase, ISpatialTransformer
{
	public double[] c0Nodes;

	public double[] d1Nodes;

	public double[] c1Nodes;

	public double[] d0Nodes;

	public bool IsReversible => true;

	public UnitCoonsPatch(Polyline south, Polyline east, Polyline north, Polyline west, Polyline southNodes = null, Polyline eastNodes = null, Polyline northNodes = null, Polyline westNodes = null)
		: base(south, east, north, west)
	{
		c0Nodes = PrepNodes(c0, southNodes, reverse: false);
		d1Nodes = PrepNodes(d1, eastNodes, reverse: false);
		c1Nodes = PrepNodes(c1, northNodes, reverse: true);
		d0Nodes = PrepNodes(d0, westNodes, reverse: true);
	}

	public UnitCoonsPatch(List<PatchSide> sides)
		: this(sides[0].Smooth, sides[1].Smooth, sides[2].Smooth, sides[3].Smooth, sides[0].Nodes, sides[1].Nodes, sides[2].Nodes, sides[3].Nodes)
	{
	}

	public double[] PrepNodes(Polyline smooth, Polyline nodes, bool reverse)
	{
		if (nodes == null)
		{
			nodes = new Polyline();
			nodes.Add(smooth.First());
			nodes.Add(smooth.Last());
		}
		else if (reverse)
		{
			nodes = nodes.Clone();
			nodes.Reverse();
		}
		double[] doubleValues = new double[nodes.Count];
		doubleValues[0] = 0.0;
		doubleValues[^1] = smooth.Length;
		for (int i = 1; i < nodes.Count - 1; i++)
		{
			doubleValues[i] = smooth.DistanceAlong(smooth.NearestPointOnPolyline(nodes[i]));
		}
		return doubleValues;
	}

	private Point CoordToPoint(Polyline p, double[] pNodes, double coord)
	{
		if (pNodes.Length == 1)
		{
			return p.Points[0];
		}
		double pNodesCoordTerm = (double)(pNodes.Length - 1) * coord;
		int pNodesTerm = Math.Min((int)pNodesCoordTerm, pNodes.Length - 2);
		pNodesCoordTerm -= (double)pNodesTerm;
		double dist = (1.0 - pNodesCoordTerm) * pNodes[pNodesTerm] + pNodesCoordTerm * pNodes[pNodesTerm + 1];
		return p.PointAtDistance(dist).Point;
	}

	private double PointToCoord(Polyline p, double[] pNodes, Point point)
	{
		return PointToCoord(p, pNodes, p.NearestPointOnPolyline(point));
	}

	private double PointToCoord(Polyline p, double[] pNodes, PointOnPolyline pop)
	{
		double result = 1.0;
		double distanceAlong = p.DistanceAlong(pop);
		for (int i = 0; i < pNodes.Length - 1; i++)
		{
			if (distanceAlong < pNodes[i + 1])
			{
				double interpolationFraction = (distanceAlong - pNodes[i]) / (pNodes[i + 1] - pNodes[i]);
				result = ((double)i + interpolationFraction) / (double)(pNodes.Length - 1);
				break;
			}
		}
		return result;
	}

	public Point Forward(Point point)
	{
		double x = point.X;
		double y = point.Y;
		Point point2 = (1.0 - y) * CoordToPoint(c0, c0Nodes, x) + y * CoordToPoint(c1, c1Nodes, x);
		Point point3 = (1.0 - x) * CoordToPoint(d0, d0Nodes, y) + x * CoordToPoint(d1, d1Nodes, y);
		Point point4 = (1.0 - x) * (1.0 - y) * c0.FirstPointOnPolyline().Point + x * (1.0 - y) * c0.LastPointOnPolyline().Point + (1.0 - x) * y * c1.FirstPointOnPolyline().Point + x * y * c1.LastPointOnPolyline().Point;
		return point2 + point3 + -1.0 * point4;
	}

	public Point Reverse(Point point)
	{
		Polyline[] polylines = new Polyline[4] { c0, c1, d0, d1 };
		List<double[]> doubleValuesList = new List<double[]> { c0Nodes, c1Nodes, d0Nodes, d1Nodes };
		double maxValue = double.MaxValue;
		PointOnPolyline pop = PointOnPolyline.NullPointOnPolyline;
		int yComponent = -1;
		for (int i = 0; i < 4; i++)
		{
			PointOnPolyline pointOnPolyline = polylines[i].NearestPointOnPolyline(point, out var actualDistance);
			if (actualDistance < maxValue)
			{
				maxValue = actualDistance;
				pop = pointOnPolyline;
				yComponent = i;
			}
		}
		double yComponent2 = PointToCoord(polylines[yComponent], doubleValuesList[yComponent], pop);
		new PointMinimizer(xInitial: (yComponent >= 2) ? new Point(yComponent - 2, yComponent2) : new Point(yComponent2, yComponent), Loss: Loss, bounds: new Extent(0.0, 1.0, 0.0, 1.0), absXTolerance: 1E-06).Minimize(out var xOpt);
		return xOpt;
		double Loss(Point test)
		{
			return Forward(test).DistanceTo(point);
		}
	}

	public void Transform(Memory<Point> points, Memory<Point> transformed)
	{
		Span<Point> pointsSpan = points.Span;
		Span<Point> transformedSpan = transformed.Span;
		for (int i = 0; i < pointsSpan.Length; i++)
		{
			transformedSpan[i] = Forward(pointsSpan[i]);
		}
	}

	public void ReverseTransform(Memory<Point> points, Memory<Point> transformed)
	{
		Span<Point> pointsSpan = points.Span;
		Span<Point> transformedSpan = transformed.Span;
		for (int i = 0; i < pointsSpan.Length; i++)
		{
			transformedSpan[i] = Reverse(pointsSpan[i]);
		}
	}

	public double CurvilinearDistance(Point a, Point b, bool unitCoords = false)
	{
		Point point = (unitCoords ? a : Reverse(a));
		Point point2 = (unitCoords ? b : Reverse(b));
		int count = (c0.PointCount + c1.PointCount + d0.PointCount + d1.PointCount) / 2;
		Point point3 = (unitCoords ? Forward(a) : a);
		double distanceToSum = 0.0;
		for (int i = 1; i <= count; i++)
		{
			Point point4 = Forward(point + (point2 - point) * i / count);
			distanceToSum += point3.DistanceTo(point4);
			point3 = point4;
		}
		return distanceToSum;
	}
}
