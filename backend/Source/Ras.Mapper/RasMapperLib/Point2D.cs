using System;
using System.Linq;

namespace RasMapperLib;

public struct Point2D
{
	public float X;

	public float Y;

	public Point2D(float x, float y)
	{
		this = default;
		X = x;
		Y = y;
	}

	public Point2D(string commaDelimitedString)
		: this(commaDelimitedString, ',')
	{
	}

	public Point2D(string delimitedString, char delimeter)
	{
		this = default;
		string[] parts = delimitedString.Split(delimeter);
		if (parts.Count() < 1 || !float.TryParse(parts[0], out X))
		{
			X = float.NaN;
		}
		if (parts.Count() < 2 || !float.TryParse(parts[1], out Y))
		{
			Y = float.NaN;
		}
	}

	public void SetToUndefined()
	{
		X = float.NaN;
		Y = float.NaN;
	}

	public bool IsUndefined()
	{
		if (float.IsNaN(X) || float.IsNaN(Y))
		{
			return true;
		}
		return false;
	}

	public string ConvertToCSV(string xFormat = "", string yFormat = "")
	{
		return X.ToString(xFormat) + "," + Y.ToString(yFormat);
	}

	public bool Equal(Point2D pt)
	{
		if (X == pt.X && Y == pt.Y)
		{
			return true;
		}
		return false;
	}

	public bool Equal(float ax, float ay)
	{
		if (X == ax && Y == ay)
		{
			return true;
		}
		return false;
	}

	public float DistanceTo(Point2D point)
	{
		return (float)Math.Sqrt(Math.Pow(point.X - X, 2.0) + Math.Pow(point.Y - Y, 2.0));
	}

	public float DistanceTo_Mult(Point2D point)
	{
		float pointTerm = point.X - X;
		float pointTerm2 = point.Y - Y;
		return (float)Math.Sqrt(pointTerm * pointTerm + pointTerm2 * pointTerm2);
	}

	public float Length()
	{
		return (float)Math.Sqrt(X * X + Y * Y);
	}

	public static int CompareX(Point2D a, Point2D b)
	{
		if (a.X < b.X)
		{
			return -1;
		}
		if (a.X == b.X)
		{
			return 0;
		}
		return 1;
	}

	public static int CompareY(Point2D a, Point2D b)
	{
		if (a.Y < b.Y)
		{
			return -1;
		}
		if (a.Y == b.Y)
		{
			return 0;
		}
		return 1;
	}

	public PointM ToPointM()
	{
		return new PointM(X, Y);
	}

	public override string ToString()
	{
		return ConvertToCSV();
	}
}
