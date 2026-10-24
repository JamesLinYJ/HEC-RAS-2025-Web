using System;
using Geospatial.Transformers;

namespace Geospatial.Vectors.MeshTopology;

public class CoonsPatch : CoonsBase, ISpatialTransformer
{
	public bool IsReversible => false;

	public CoonsPatch(Polyline south, Polyline east, Polyline north, Polyline west)
		: base(south, east, north, west)
	{
	}

	private Point CoordToPoint(Polyline p, double coord)
	{
		int coordSegmentsTerm = Math.Max(0, Math.Min((int)coord, p.Segments.Count - 1));
		double t = coord - (double)coordSegmentsTerm;
		return p.Segments[coordSegmentsTerm].PointAtFraction(t);
	}

	public Point Forward(Point point)
	{
		double x = point.X;
		double c0Term = x / (double)(c0.Count - 1);
		double y = point.Y;
		double d0Term = y / (double)(d0.Count - 1);
		Point point2 = (1.0 - d0Term) * CoordToPoint(c0, x) + d0Term * CoordToPoint(c1, x);
		Point point3 = (1.0 - c0Term) * CoordToPoint(d0, y) + c0Term * CoordToPoint(d1, y);
		Point point4 = (1.0 - c0Term) * (1.0 - d0Term) * c0.FirstPointOnPolyline().Point + c0Term * (1.0 - d0Term) * c0.LastPointOnPolyline().Point + (1.0 - c0Term) * d0Term * c1.FirstPointOnPolyline().Point + c0Term * d0Term * c1.LastPointOnPolyline().Point;
		return point2 + (point3 - point4);
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
		throw new NotImplementedException();
	}
}
