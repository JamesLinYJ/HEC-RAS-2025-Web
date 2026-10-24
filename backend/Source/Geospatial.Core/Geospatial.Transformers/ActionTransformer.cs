using System;
using Geospatial.Rasters;
using Geospatial.Vectors;

namespace Geospatial.Transformers;

public class ActionTransformer : ISpatialTransformer
{
	private readonly ConverterDelegates.Convert<Point> _transformAction;

	private readonly ConverterDelegates.Convert<Point> _reverseAction;

	public bool IsReversible => _reverseAction != null;

	public ActionTransformer(ConverterDelegates.Convert<Point> transformAction)
	{
		_transformAction = transformAction;
	}

	public ActionTransformer(ConverterDelegates.Convert<Point> transformAction, ConverterDelegates.Convert<Point> reverseAction)
	{
		_transformAction = transformAction;
		_reverseAction = reverseAction;
	}

	public void ReverseTransform(Memory<Point> points, Memory<Point> transformed)
	{
		if (_reverseAction == null)
		{
			throw new Exception("Inverse transform not provided. Check IsReversible before invoking this method.");
		}
		_reverseAction(points, transformed);
	}

	public void Transform(Memory<Point> points, Memory<Point> transformed)
	{
		_transformAction(points, transformed);
	}

	public static ActionTransformer IdentityTransform()
	{
		return new ActionTransformer((Memory<Point> a, Memory<Point> b) =>
		{
			a.CopyTo(b);
		}, (Memory<Point> a, Memory<Point> b) =>
		{
			a.CopyTo(b);
		});
	}
}
