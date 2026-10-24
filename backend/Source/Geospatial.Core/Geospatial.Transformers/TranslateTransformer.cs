using System;
using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial.Transformers;

public class TranslateTransformer : ISpatialTransformer
{
	private Vector _translation;

	public bool IsReversible => true;

	public TranslateTransformer(Vector translation)
	{
		_translation = translation;
	}

	public TranslateTransformer(double dx, double dy)
	{
		_translation = new Vector(dx, dy);
	}

	public void Transform(Memory<Point> points, Memory<Point> transformed)
	{
		Span<Point> pointsSpan = points.Span;
		Span<Point> transformedSpan = transformed.Span;
		for (int i = 0; i < pointsSpan.Length; i++)
		{
			transformedSpan[i] = pointsSpan[i].Translate(_translation);
		}
	}

	public void ReverseTransform(Memory<Point> points, Memory<Point> transformed)
	{
		Vector vec = _translation.Negate();
		Span<Point> pointsSpan = points.Span;
		Span<Point> transformedSpan = transformed.Span;
		for (int i = 0; i < pointsSpan.Length; i++)
		{
			transformedSpan[i] = pointsSpan[i].Translate(vec);
		}
	}

	public void ReverseTransformBuffer(IList<Point> points)
	{
		Vector vec = _translation.Negate();
		for (int i = 0; i < points.Count; i++)
		{
			points[i] = points[i].Translate(vec);
		}
	}
}
