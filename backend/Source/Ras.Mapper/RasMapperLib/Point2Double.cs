using System;
using System.Linq;

namespace RasMapperLib;

public struct Point2Double
{
	public double X;

	public double Y;

	public Point2Double(double x, double y)
	{
		this = default;
		X = x;
		Y = y;
	}

	public Point2Double(PointM ptM)
	{
		this = default;
		X = ptM.X;
		Y = ptM.Y;
	}

	public Point2Double(string commaDelimitedString)
		: this(commaDelimitedString, ',')
	{
	}

	public Point2Double(string delimitedString, char delimeter)
	{
		this = default;
		string[] parts = delimitedString.Split(delimeter);
		if (parts.Count() < 1 || !double.TryParse(parts[0], out X))
		{
			X = double.NaN;
		}
		if (parts.Count() < 2 || !double.TryParse(parts[1], out Y))
		{
			Y = double.NaN;
		}
	}

	public PointM ToPointM()
	{
		return new PointM(this);
	}

	public static Point2Double UndefinedPoint()
	{
		return new Point2Double(double.NaN, double.NaN);
	}

	public bool IsUndefined()
	{
		if (double.IsNaN(X) || double.IsNaN(Y))
		{
			return true;
		}
		return false;
	}

	public string ConvertToCSV(string xFormat = "", string yFormat = "")
	{
		return X.ToString(xFormat) + "," + Y.ToString(yFormat);
	}

	public bool Equal(Point2Double pt)
	{
		if (X == pt.X && Y == pt.Y)
		{
			return true;
		}
		return false;
	}

	public Point2Double MidPoint(Point2Double pt)
	{
		return new Point2Double((X + pt.X) * 0.5, (Y + pt.Y) * 0.5);
	}

	public double DistanceTo(Point2Double point)
	{
		return Math.Sqrt(Math.Pow(point.X - X, 2.0) + Math.Pow(point.Y - Y, 2.0));
	}

	public static int CompareX(Point2Double a, Point2Double b)
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

	public static int CompareY(Point2Double a, Point2Double b)
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

	public override string ToString()
	{
		return ConvertToCSV();
	}
}
