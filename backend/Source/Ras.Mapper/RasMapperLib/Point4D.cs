using System;
using System.Linq;

namespace RasMapperLib;

public class Point4D
{
	public float X;

	public float Y;

	public float Z;

	public float M;

	public Point4D(float x, float y, float z, float m)
	{
		X = x;
		Y = y;
		Z = z;
		M = m;
	}

	public Point4D(string commaDelimitedString)
		: this(commaDelimitedString, ',')
	{
	}

	public Point4D(string delimitedString, char delimeter)
	{
		string[] parts = delimitedString.Split(delimeter);
		if (parts.Count() < 1 || !float.TryParse(parts[0], out X))
		{
			X = float.NaN;
		}
		if (parts.Count() < 2 || !float.TryParse(parts[1], out Y))
		{
			Y = float.NaN;
		}
		if (parts.Count() < 3 || !float.TryParse(parts[2], out Z))
		{
			Z = float.NaN;
		}
		if (parts.Count() < 3 || !float.TryParse(parts[3], out M))
		{
			M = float.NaN;
		}
	}

	public void SetToUndefined()
	{
		X = float.NaN;
		Y = float.NaN;
		Z = float.NaN;
		M = float.NaN;
	}

	public bool IsUndefined()
	{
		if (float.IsNaN(X) || float.IsNaN(Y) || float.IsNaN(Z) || float.IsNaN(M))
		{
			return true;
		}
		return false;
	}

	public string ConvertToCSV(string xFormat = "", string yFormat = "", string zFormat = "", string mFormat = "")
	{
		return X.ToString(xFormat) + "," + Y.ToString(yFormat) + "," + Z.ToString(zFormat) + "," + M.ToString(mFormat);
	}

	public bool Equal(Point4D pt)
	{
		if (X == pt.X && Y == pt.Y && Z == pt.Z)
		{
			return true;
		}
		return false;
	}

	public float DistanceToXY(Point4D point)
	{
		return (float)Math.Sqrt(Math.Pow(point.X - X, 2.0) + Math.Pow(point.Y - Y, 2.0));
	}

	public static int CompareX(Point4D a, Point4D b)
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

	public static int CompareY(Point4D a, Point4D b)
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

	public static int CompareZ(Point4D a, Point4D b)
	{
		if (a.Z < b.Z)
		{
			return -1;
		}
		if (a.Z == b.Z)
		{
			return 0;
		}
		return 1;
	}

	public static int CompareM(Point4D a, Point4D b)
	{
		if (a.M < b.M)
		{
			return -1;
		}
		if (a.M == b.M)
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
