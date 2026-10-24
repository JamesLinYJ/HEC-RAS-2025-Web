using System;
using Geospatial.Vectors;

namespace Geospatial.Transformers;

public class ScaleTransformer : ISpatialTransformer
{
	private readonly Point _origin;

	private readonly double _vert;

	private readonly double _hz;

	public bool IsReversible => true;

	public ScaleTransformer(Point origin, double vert, double hz)
	{
		_vert = vert;
		_hz = hz;
		_origin = origin;
	}

	public void Transform(Memory<Point> points, Memory<Point> transformed)
	{
		Span<Point> pointsSpan = points.Span;
		Span<Point> transformedSpan = transformed.Span;
		for (int i = 0; i < pointsSpan.Length; i++)
		{
			Vector vector = pointsSpan[i] - _origin;
			Vector vector2 = new Vector(vector.DeltaX * _hz, vector.DeltaY * _vert);
			transformedSpan[i] = _origin + vector2;
		}
	}

	public void ReverseTransform(Memory<Point> points, Memory<Point> transformed)
	{
		Span<Point> pointsSpan = points.Span;
		Span<Point> transformedSpan = transformed.Span;
		for (int i = 0; i < pointsSpan.Length; i++)
		{
			Vector vector = pointsSpan[i] - _origin;
			Vector vector2 = new Vector(vector.DeltaX / _hz, vector.DeltaY / _vert);
			transformedSpan[i] = _origin + vector2;
		}
	}
}
