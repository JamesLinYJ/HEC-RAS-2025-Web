using System;
using Geospatial.Vectors;

namespace Geospatial.Clipper2Lib;

public struct RectD
{
	public double left;

	public double top;

	public double right;

	public double bottom;

	public double Width
	{
		readonly get
		{
			return right - left;
		}
		set
		{
			right = left + value;
		}
	}

	public double Height
	{
		readonly get
		{
			return bottom - top;
		}
		set
		{
			bottom = top + value;
		}
	}

	public RectD(double l, double t, double r, double b)
	{
		left = l;
		top = t;
		right = r;
		bottom = b;
	}

	public RectD(RectD rec)
	{
		left = rec.left;
		top = rec.top;
		right = rec.right;
		bottom = rec.bottom;
	}

	public RectD(bool isValid)
	{
		if (isValid)
		{
			left = 0.0;
			top = 0.0;
			right = 0.0;
			bottom = 0.0;
		}
		else
		{
			left = double.MaxValue;
			top = double.MaxValue;
			right = double.MinValue;
			bottom = double.MinValue;
		}
	}

	public readonly bool IsEmpty()
	{
		if (!(bottom <= top))
		{
			return right <= left;
		}
		return true;
	}

	public readonly Point MidPoint()
	{
		return new Point((left + right) / 2.0, (top + bottom) / 2.0);
	}

	public readonly bool Contains(Point pt)
	{
		if (pt.X > left && pt.X < right && pt.Y > top)
		{
			return pt.Y < bottom;
		}
		return false;
	}

	public readonly bool Contains(RectD rec)
	{
		if (rec.left >= left && rec.right <= right && rec.top >= top)
		{
			return rec.bottom <= bottom;
		}
		return false;
	}

	public readonly bool Intersects(RectD rec)
	{
		if (Math.Max(left, rec.left) < Math.Min(right, rec.right))
		{
			return Math.Max(top, rec.top) < Math.Min(bottom, rec.bottom);
		}
		return false;
	}

	public readonly PathD AsPath()
	{
		return new PathD(4)
		{
			new Point(left, top),
			new Point(right, top),
			new Point(right, bottom),
			new Point(left, bottom)
		};
	}
}
