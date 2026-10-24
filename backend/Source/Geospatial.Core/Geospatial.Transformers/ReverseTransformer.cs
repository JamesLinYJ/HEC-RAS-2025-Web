using System;
using Geospatial.Vectors;

namespace Geospatial.Transformers;

public class ReverseTransformer : ISpatialTransformer
{
	public bool IsReversible => true;

	public void Transform(Memory<Point> points, Memory<Point> transformed)
	{
		Span<Point> pointsSpan = points.Span;
		Span<Point> transformedSpan = transformed.Span;
		int lengthOffset = pointsSpan.Length - 1;
		for (int i = 0; i < pointsSpan.Length; i++)
		{
			transformedSpan[lengthOffset - i] = pointsSpan[i];
		}
	}

	public void ReverseTransform(Memory<Point> points, Memory<Point> transformed)
	{
		points.CopyTo(transformed);
	}
}
