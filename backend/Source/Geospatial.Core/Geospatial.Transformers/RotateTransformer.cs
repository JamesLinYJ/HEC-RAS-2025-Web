using System;
using Geospatial.Vectors;

namespace Geospatial.Transformers;

public class RotateTransformer : ISpatialTransformer
{
	private const double DEG_TO_RAD = Math.PI / 180.0;

	private readonly Point _origin;

	private readonly double _rotationAngle;

	public bool IsReversible => true;

	public RotateTransformer(Point origin, double angle, RotationUnits units, Orientation rotationOrientation)
	{
		_origin = origin;
		if (units == RotationUnits.Degrees)
		{
			_rotationAngle = angle * (Math.PI / 180.0);
		}
		else
		{
			_rotationAngle = angle;
		}
		if (rotationOrientation == Orientation.Clockwise)
		{
			_rotationAngle = 0.0 - _rotationAngle;
		}
	}

	public void Transform(Memory<Point> points, Memory<Point> transformed)
	{
		RotatePoints(points, transformed, _origin, _rotationAngle);
	}

	public void ReverseTransform(Memory<Point> points, Memory<Point> transformed)
	{
		RotatePoints(points, transformed, _origin, 0.0 - _rotationAngle);
	}

	private static void RotatePoints(Memory<Point> points, Memory<Point> transformed, Point origin, double angle)
	{
		double angleTerm = Math.Sin(angle);
		double angleTerm2 = Math.Cos(angle);
		Span<Point> pointsSpan = points.Span;
		Span<Point> transformedSpan = transformed.Span;
		for (int i = 0; i < pointsSpan.Length; i++)
		{
			Point point = pointsSpan[i];
			double pointOriginTerm = point.X - origin.X;
			double pointOriginTerm2 = point.Y - origin.Y;
			double computed = pointOriginTerm * angleTerm2 - pointOriginTerm2 * angleTerm;
			double computed2 = pointOriginTerm * angleTerm + pointOriginTerm2 * angleTerm2;
			transformedSpan[i] = new Point(computed + origin.X, computed2 + origin.Y);
		}
	}
}
