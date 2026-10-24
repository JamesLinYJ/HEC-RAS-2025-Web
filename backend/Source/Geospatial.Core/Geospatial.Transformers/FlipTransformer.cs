using System;
using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial.Transformers;

public class FlipTransformer : ISpatialTransformer
{
	private readonly Axis _axis;

	private readonly double _axisValue;

	public bool IsReversible => false;

	public FlipTransformer(Axis axis)
	{
		_axis = axis;
	}

	public FlipTransformer(Axis axis, double axisValue)
	{
		_axis = axis;
		_axisValue = axisValue;
	}

	public void Transform(Memory<Point> points, Memory<Point> transformed)
	{
		Span<Point> pointsSpan = points.Span;
		Span<Point> transformedSpan = transformed.Span;
		if (_axis == Axis.XAxis)
		{
			if (_axisValue == 0.0)
			{
				for (int i = 0; i < pointsSpan.Length; i++)
				{
					Point point = pointsSpan[i];
					transformedSpan[i] = new Point(point.X, 0.0 - point.Y);
				}
				return;
			}
			double scaledAxisValue = _axisValue * 2.0;
			for (int j = 0; j < pointsSpan.Length; j++)
			{
				Point point2 = pointsSpan[j];
				transformedSpan[j] = new Point(point2.X, scaledAxisValue - point2.Y);
			}
		}
		else
		{
			if (_axis != Axis.YAxis)
			{
				return;
			}
			if (_axisValue == 0.0)
			{
				for (int k = 0; k < pointsSpan.Length; k++)
				{
					Point point3 = pointsSpan[k];
					transformedSpan[k] = new Point(0.0 - point3.X, point3.Y);
				}
				return;
			}
			double scaledAxisValue2 = _axisValue * 2.0;
			for (int l = 0; l < pointsSpan.Length; l++)
			{
				Point point4 = pointsSpan[l];
				transformedSpan[l] = new Point(scaledAxisValue2 - point4.X, point4.Y);
			}
		}
	}

	public void TransformBuffer(IList<Point> points)
	{
		if (_axis == Axis.XAxis)
		{
			if (_axisValue == 0.0)
			{
				for (int i = 0; i < points.Count; i++)
				{
					Point point = points[i];
					points[i] = new Point(point.X, 0.0 - point.Y);
				}
				return;
			}
			double scaledAxisValue = _axisValue * 2.0;
			for (int j = 0; j < points.Count; j++)
			{
				Point point2 = points[j];
				points[j] = new Point(point2.X, scaledAxisValue - point2.Y);
			}
		}
		else
		{
			if (_axis != Axis.YAxis)
			{
				return;
			}
			if (_axisValue == 0.0)
			{
				for (int k = 0; k < points.Count; k++)
				{
					Point point3 = points[k];
					points[k] = new Point(0.0 - point3.X, point3.Y);
				}
				return;
			}
			double scaledAxisValue2 = _axisValue * 2.0;
			for (int l = 0; l < points.Count; l++)
			{
				Point point4 = points[l];
				points[l] = new Point(scaledAxisValue2 - point4.X, point4.Y);
			}
		}
	}

	public void ReverseTransform(Memory<Point> points, Memory<Point> transformed)
	{
		throw new NotImplementedException();
	}
}
