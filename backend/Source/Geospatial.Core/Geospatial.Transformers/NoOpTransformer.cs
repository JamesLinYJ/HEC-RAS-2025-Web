using System;
using Geospatial.Vectors;

namespace Geospatial.Transformers;

public class NoOpTransformer : ISpatialTransformer
{
	public static readonly NoOpTransformer Instance = new NoOpTransformer();

	public bool IsReversible => true;

	private NoOpTransformer()
	{
	}

	public void ReverseTransform(Memory<Point> points, Memory<Point> transformed)
	{
		points.CopyTo(transformed);
	}

	public void Transform(Memory<Point> points, Memory<Point> transformed)
	{
		points.CopyTo(transformed);
	}
}
