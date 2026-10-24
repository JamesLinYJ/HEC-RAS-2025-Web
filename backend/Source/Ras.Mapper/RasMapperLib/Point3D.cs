using System;
using System.Linq;

namespace RasMapperLib;

public struct Point3D
{
	public float X;

	public float Y;

	public float Z;

	public Point3D(float x, float y, float z)
	{
		this = default;
		X = x;
		Y = y;
		Z = z;
	}

	public Point3D(string commaDelimitedString)
		: this(commaDelimitedString, ',')
	{
	}

	public Point3D(string delimitedString, char delimeter)
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
		if (parts.Count() < 3 || !float.TryParse(parts[2], out Z))
		{
			Z = float.NaN;
		}
	}

	public void SetToUndefined()
	{
		X = float.NaN;
		Y = float.NaN;
		Z = float.NaN;
	}

	public bool IsUndefined()
	{
		if (float.IsNaN(X) || float.IsNaN(Y) || float.IsNaN(Z))
		{
			return true;
		}
		return false;
	}

	public string ConvertToCSV(string xFormat = "", string yFormat = "", string zFormat = "")
	{
		return X.ToString(xFormat) + "," + Y.ToString(yFormat) + "," + Z.ToString(zFormat);
	}

	public bool Equal(Point3D pt)
	{
		if (X == pt.X && Y == pt.Y && Z == pt.Z)
		{
			return true;
		}
		return false;
	}

	public float DistanceToXY(Point3D point)
	{
		return (float)Math.Sqrt(Math.Pow(point.X - X, 2.0) + Math.Pow(point.Y - Y, 2.0));
	}

	public Point3D Mid(Point3D point)
	{
		float x = (X + point.X) * 0.5f;
		float y = (Y + point.Y) * 0.5f;
		float z = (Z + point.Z) * 0.5f;
		return new Point3D(x, y, z);
	}

	public Point3D PointAtY(Point3D point, float y)
	{
		float pointTerm = (point.Y - y) / (point.Y - Y);
		float x = X * pointTerm + point.X * (1f - pointTerm);
		float z = Z * pointTerm + point.Z * (1f - pointTerm);
		return new Point3D(x, y, z);
	}

	public static int CompareX(Point3D a, Point3D b)
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

	public static int CompareY(Point3D a, Point3D b)
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

	public static int CompareZ(Point3D a, Point3D b)
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

	public override string ToString()
	{
		return ConvertToCSV();
	}
}
