using System;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Helpers;

internal class BlackAndWhiteToBlackAlphaTransformer
{
	public bool IsReversible => false;

	public void Transform(Memory<Point> points, Memory<Point> transformed)
	{
		throw new NotImplementedException();
	}

	public void ReverseTransform(Memory<Point> points, Memory<Point> transformed)
	{
		throw new NotImplementedException();
	}
}
