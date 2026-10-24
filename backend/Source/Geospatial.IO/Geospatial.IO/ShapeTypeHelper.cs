namespace Geospatial.IO;

public static class ShapeTypeHelper
{
	public const int NullShape = 0;

	public const int Point = 1;

	public const int Polyline = 3;

	public const int Polygon = 5;

	public const int Multipoint = 8;

	public const int PointZ = 11;

	public const int PolylineZ = 13;

	public const int PolygonZ = 15;

	public const int MultipointZ = 18;

	public const int PointM = 21;

	public const int PolylineM = 23;

	public const int PolygonM = 25;

	public const int MultipointM = 28;

	public const int Multipatch = 31;

	public static bool IsPointType(int shpType)
	{
		return shpType % 10 == 1;
	}

	public static bool IsPolylineType(int shpType)
	{
		return shpType % 10 == 3;
	}

	public static bool IsPolygonType(int shpType)
	{
		return shpType % 10 == 5;
	}

	public static bool IsMultipointType(int shpType)
	{
		return shpType % 10 == 8;
	}

	public static ShapeType GetShapeType(int shpType)
	{
		if (IsPointType(shpType))
		{
			return ShapeType.Point;
		}
		if (IsPolylineType(shpType))
		{
			return ShapeType.Line;
		}
		if (IsPolygonType(shpType))
		{
			return ShapeType.Polygon;
		}
		if (IsMultipointType(shpType))
		{
			return ShapeType.Multipoint;
		}
		return ShapeType.Unknown;
	}

	public static bool IsXY(int shpType)
	{
		return shpType < 10;
	}

	public static bool IsXYZM(int shpType)
	{
		if (shpType > 10)
		{
			return shpType < 20;
		}
		return false;
	}

	public static bool IsXYM(int shpType)
	{
		if (shpType > 20)
		{
			return shpType < 30;
		}
		return false;
	}

	public static ShapeOordinates GetOordinates(int shpType)
	{
		if (IsXY(shpType))
		{
			return ShapeOordinates.XY;
		}
		if (IsXYZM(shpType))
		{
			return ShapeOordinates.XYZM;
		}
		if (IsXYM(shpType))
		{
			return ShapeOordinates.XYM;
		}
		return ShapeOordinates.None;
	}

	public static int GetForPoint(bool hasZ, bool hasM)
	{
		if (hasZ)
		{
			return 11;
		}
		if (hasM)
		{
			return 21;
		}
		return 1;
	}

	public static int GetForMultipoint(bool hasZ, bool hasM)
	{
		if (hasZ)
		{
			return 18;
		}
		if (hasM)
		{
			return 28;
		}
		return 8;
	}

	public static int GetForPolyline(bool hasZ, bool hasM)
	{
		if (hasZ)
		{
			return 13;
		}
		if (hasM)
		{
			return 23;
		}
		return 3;
	}

	public static int GetForPolygon(bool hasZ, bool hasM)
	{
		if (hasZ)
		{
			return 15;
		}
		if (hasM)
		{
			return 25;
		}
		return 5;
	}
}
