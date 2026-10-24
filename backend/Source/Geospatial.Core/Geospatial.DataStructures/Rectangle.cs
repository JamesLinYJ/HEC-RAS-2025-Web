namespace Geospatial.DataStructures;

public class Rectangle
{
	public readonly double Left;

	public readonly double Right;

	public readonly double Top;

	public readonly double Bottom;

	public double Width => Right - Left;

	public double Height => Top - Bottom;

	public Rectangle(double l, double r, double t, double b)
	{
		Left = l;
		Right = r;
		Top = t;
		Bottom = b;
	}

	public bool Contains(Rectangle other)
	{
		if (Left > other.Left)
		{
			return false;
		}
		if (Top < other.Top)
		{
			return false;
		}
		if (Right < other.Right)
		{
			return false;
		}
		if (Bottom > other.Bottom)
		{
			return false;
		}
		return true;
	}

	public bool Contains(double x, double y)
	{
		if (Left > x)
		{
			return false;
		}
		if (Top < y)
		{
			return false;
		}
		if (Right < x)
		{
			return false;
		}
		if (Bottom > y)
		{
			return false;
		}
		return true;
	}

	public bool Intersects(Rectangle other)
	{
		if (Right < other.Left)
		{
			return false;
		}
		if (Bottom > other.Top)
		{
			return false;
		}
		if (Left > other.Right)
		{
			return false;
		}
		if (Top < other.Bottom)
		{
			return false;
		}
		return true;
	}
}
