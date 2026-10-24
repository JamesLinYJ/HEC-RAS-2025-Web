using System;
using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial.Transformers;

public class MagneticTransformer : ISpatialTransformer
{
	private readonly Point _origin;

	private readonly double _strength;

	public bool IsReversible => true;

	public MagneticTransformer(Point origin, double strength)
	{
		_origin = origin;
		_strength = strength;
	}

	public void Transform(Memory<Point> points, Memory<Point> transformed)
	{
		TransformInternal(points, transformed, _strength);
	}

	public void ReverseTransform(Memory<Point> points, Memory<Point> transformed)
	{
		TransformInternal(points, transformed, 1.0 / _strength);
	}

	private void TransformInternal(Memory<Point> points, Memory<Point> transformed, double stren)
	{
		Span<Point> pointsSpan = points.Span;
		Span<Point> transformedSpan = transformed.Span;
		for (int i = 0; i < pointsSpan.Length; i++)
		{
			Point other = pointsSpan[i];
			Vector vec = _origin.To(other).Ratio(stren);
			transformedSpan[i] = _origin.Translate(vec);
		}
	}

	public void TransformBuffer(IList<Point> points)
	{
		TransformSelfInternal(points, _strength);
	}

	public void ReverseTransformBuffer(IList<Point> points)
	{
		TransformSelfInternal(points, 1.0 / _strength);
	}

	private void TransformSelfInternal(IList<Point> points, double stren)
	{
		for (int i = 0; i < points.Count; i++)
		{
			Point other = points[i];
			Vector vec = _origin.To(other).Ratio(stren);
			points[i] = _origin.Translate(vec);
		}
	}
}
