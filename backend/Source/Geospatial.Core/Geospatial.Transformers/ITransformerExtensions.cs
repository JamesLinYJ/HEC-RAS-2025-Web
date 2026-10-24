using System;
using Geospatial.Vectors;

namespace Geospatial.Transformers;

public static class ITransformerExtensions
{
	public static ISpatialTransformer GetInvertedTransformer(this ISpatialTransformer xf)
	{
		return new ActionTransformer((Memory<Point> input, Memory<Point> output) =>
		{
			xf.ReverseTransform(input, output);
		}, (Memory<Point> input, Memory<Point> output) =>
		{
			xf.Transform(input, output);
		});
	}
}
