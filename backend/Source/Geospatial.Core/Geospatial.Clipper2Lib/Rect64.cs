using System;

namespace Geospatial.Clipper2Lib;

public struct Rect64
{
	public long left;

	public long top;

	public long right;

	public long bottom;

	public long Width
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

	public long Height
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

	public Rect64(long l, long t, long r, long b)
	{
		left = l;
		top = t;
		right = r;
		bottom = b;
	}

	public Rect64(bool isValid)
	{
		if (isValid)
		{
			left = 0L;
			top = 0L;
			right = 0L;
			bottom = 0L;
		}
		else
		{
			left = long.MaxValue;
			top = long.MaxValue;
			right = long.MinValue;
			bottom = long.MinValue;
		}
	}

	public Rect64(Rect64 rec)
	{
		left = rec.left;
		top = rec.top;
		right = rec.right;
		bottom = rec.bottom;
	}

	public readonly bool IsEmpty()
	{
		if (bottom > top)
		{
			return right <= left;
		}
		return true;
	}

	public readonly bool IsValid()
	{
		return left < long.MaxValue;
	}

	public readonly Point64 MidPoint()
	{
		return new Point64((left + right) / 2, (top + bottom) / 2);
	}

	public readonly bool Contains(Point64 pt)
	{
		if (pt.X > left && pt.X < right && pt.Y > top)
		{
			return pt.Y < bottom;
		}
		return false;
	}

	public readonly bool Contains(Rect64 rec)
	{
		if (rec.left >= left && rec.right <= right && rec.top >= top)
		{
			return rec.bottom <= bottom;
		}
		return false;
	}

	public readonly bool Intersects(Rect64 rec)
	{
		if (Math.Max(left, rec.left) <= Math.Min(right, rec.right))
		{
			return Math.Max(top, rec.top) <= Math.Min(bottom, rec.bottom);
		}
		return false;
	}

	public readonly Path64 AsPath()
	{
		return new Path64(4)
		{
			new Point64(left, top),
			new Point64(right, top),
			new Point64(right, bottom),
			new Point64(left, bottom)
		};
	}
}
