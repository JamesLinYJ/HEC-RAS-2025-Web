using System;
using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Geospatial.Rasters;
using Utility;

namespace Geospatial.Vectors;

public readonly struct Triangle : ITriangle<Point>, IExtent
{
	public delegate void RasterizeRowDelegate(int row, int colStart, int colCount);

	public delegate void RasterizeRowZMDelegate(int row, int colStart, int colCount, double zStart, double mStart, double dz_dCol, double dm_dCol);

	private struct BaryDelta
	{
		public double StartValue;

		public double DeltaX;

		public double DeltaY;
	}

	private readonly struct BarycentricData
	{
		public readonly double originW;

		public readonly double dw_col;

		public readonly double dw_row;

		public BarycentricData(double a, double b, double c, double orginDeltaX, double originDeltaY, double cellSize)
		{
			double computed = (0.0 - a) / c;
			double computed2 = (0.0 - b) / c;
			originW = 1.0 - computed * orginDeltaX - computed2 * originDeltaY;
			dw_col = computed * cellSize;
			dw_row = computed2 * cellSize;
		}
	}

	public const double DefaultTolerance = 1E-06;

	public readonly Point PointA;

	public readonly Point PointB;

	public readonly Point PointC;

	public static bool UseNewRasterizerMethod = true;

	public double Area => ComputeArea(PointA, PointB, PointC);

	public double SignedArea => ComputeSignedArea(PointA, PointB, PointC);

	public Extent Extent => new Extent(MathHelpers.Min3(PointA.X, PointB.X, PointC.X), MathHelpers.Max3(PointA.X, PointB.X, PointC.X), MathHelpers.Min3(PointA.Y, PointB.Y, PointC.Y), MathHelpers.Max3(PointA.Y, PointB.Y, PointC.Y));

	public double AspectRatio => ComputeAspectRatio(PointA, PointB, PointC);

	public double PerimeterRatio => ComputePerimeterRatio(PointA, PointB, PointC);

	Point ITriangle<Point>.PointA => PointA;

	Point ITriangle<Point>.PointB => PointB;

	Point ITriangle<Point>.PointC => PointC;

	public Segment Edge0 => new Segment(PointA, PointB);

	public Segment Edge1 => new Segment(PointB, PointC);

	public Segment Edge2 => new Segment(PointC, PointA);

	public Triangle(Point apt0, Point apt1, Point apt2)
	{
		PointA = apt0;
		PointB = apt1;
		PointC = apt2;
	}

	public Triangle(IPoint apt0, IPoint apt1, IPoint apt2)
	{
		PointA = new Point(apt0);
		PointB = new Point(apt1);
		PointC = new Point(apt2);
	}

	public bool Contains(Point pt)
	{
		return Contains(pt.X, pt.Y);
	}

	public bool Contains(double px, double py)
	{
		if (double.IsNaN(px) || double.IsNaN(py))
		{
			return false;
		}
		double x = PointA.X;
		double y = PointA.Y;
		double x2 = PointB.X;
		double y2 = PointB.Y;
		double x3 = PointC.X;
		double y3 = PointC.Y;
		double x2PyTerm = (x2 - x) * (py - y) - (y2 - y) * (px - x);
		double x3X2Term = (x3 - x2) * (py - y2) - (y3 - y2) * (px - x2);
		if (x2PyTerm < 0.0 != x3X2Term < 0.0 && x2PyTerm != 0.0 && x3X2Term != 0.0)
		{
			return false;
		}
		double x3PyTerm = (x - x3) * (py - y3) - (y - y3) * (px - x3);
		bool condition = x2PyTerm < 0.0 || x3X2Term < 0.0 || x3PyTerm < 0.0;
		bool condition2 = x2PyTerm > 0.0 || x3X2Term > 0.0 || x3PyTerm > 0.0;
		return !(condition & condition2);
	}

	public bool ContainsCW(Point pt)
	{
		return ContainsCW(pt.X, pt.Y);
	}

	public bool ContainsCW(double px, double py)
	{
		double x = PointA.X;
		double y = PointA.Y;
		double x2 = PointB.X;
		double y2 = PointB.Y;
		double x3 = PointC.X;
		double y3 = PointC.Y;
		if ((x2 - x) * (py - y) - (y2 - y) * (px - x) > 0.0)
		{
			return false;
		}
		if ((x3 - x2) * (py - y2) - (y3 - y2) * (px - x2) > 0.0)
		{
			return false;
		}
		if ((x - x3) * (py - y3) - (y - y3) * (px - x3) > 0.0)
		{
			return false;
		}
		return true;
	}

	public bool Contains(Point pt, out BarycentricWeights weights)
	{
		bool result = Contains(pt.X, pt.Y, out var lambda, out var lambda2, out var lambda3);
		weights = new BarycentricWeights(lambda, lambda2, lambda3);
		return result;
	}

	public bool Contains(double ptx, double pty, out double lambda0, out double lambda1, out double lambda2, double tolerance = 1E-06)
	{
		InterpolationWeights(ptx, pty, out lambda0, out lambda1, out lambda2);
		if (lambda0 >= 0.0 - tolerance && lambda1 >= 0.0 - tolerance && lambda2 >= 0.0 - tolerance && lambda0 <= 1.0 + tolerance && lambda1 <= 1.0 + tolerance)
		{
			return lambda2 <= 1.0 + tolerance;
		}
		return false;
	}

	public static bool Contains(double pt0x, double pt0y, double pt1x, double pt1y, double pt2x, double pt2y, double ptx, double pty, out double lambda0, out double lambda1, out double lambda2, double tolerance = 1E-06)
	{
		InterpolationWeights(pt0x, pt0y, pt1x, pt1y, pt2x, pt2y, ptx, pty, out lambda0, out lambda1, out lambda2);
		if (lambda0 >= 0.0 - tolerance && lambda1 >= 0.0 - tolerance && lambda2 >= 0.0 - tolerance && lambda0 <= 1.0 + tolerance && lambda1 <= 1.0 + tolerance)
		{
			return lambda2 <= 1.0 + tolerance;
		}
		return false;
	}

	public static bool EqualWithinPointOrder(Triangle A, Triangle B)
	{
		if (A.PointA == B.PointA && A.PointB == B.PointB && A.PointC == B.PointC)
		{
			return true;
		}
		if (A.PointA == B.PointB && A.PointB == B.PointC && A.PointC == B.PointA)
		{
			return true;
		}
		if (A.PointA == B.PointC && A.PointB == B.PointA && A.PointC == B.PointB)
		{
			return true;
		}
		return false;
	}

	public void InterpolationWeights(Point pt, out double lambda0, out double lambda1, out double lambda2)
	{
		InterpolationWeights(pt.X, pt.Y, out lambda0, out lambda1, out lambda2);
	}

	public void InterpolationWeights(double px, double py, out double lambda0, out double lambda1, out double lambda2)
	{
		InterpolationWeights(PointA.X, PointA.Y, PointB.X, PointB.Y, PointC.X, PointC.Y, px, py, out lambda0, out lambda1, out lambda2);
	}

	public static void InterpolationWeights(Point pt0, Point pt1, Point pt2, double px, double py, out double lambda0, out double lambda1, out double lambda2)
	{
		InterpolationWeights(pt0.X, pt0.Y, pt1.X, pt1.Y, pt2.X, pt2.Y, px, py, out lambda0, out lambda1, out lambda2);
	}

	public static void InterpolationWeights(double pt0X, double pt0Y, double pt1X, double pt1Y, double pt2X, double pt2Y, double px, double py, out double lambda0, out double lambda1, out double lambda2)
	{
		double pt1YAbovePt2Y = pt1Y - pt2Y;
		double pt0XAbovePt2X = pt0X - pt2X;
		double pt2XAbovePt1X = pt2X - pt1X;
		double pt0YAbovePt2Y = pt0Y - pt2Y;
		double computed = pt1YAbovePt2Y * pt0XAbovePt2X + pt2XAbovePt1X * pt0YAbovePt2Y;
		double pxAbovePt2X = px - pt2X;
		double pyAbovePt2Y = py - pt2Y;
		lambda0 = (pt1YAbovePt2Y * pxAbovePt2X + pt2XAbovePt1X * pyAbovePt2Y) / computed;
		lambda1 = (pt0XAbovePt2X * pyAbovePt2Y - pt0YAbovePt2Y * pxAbovePt2X) / computed;
		lambda2 = 1.0 - lambda0 - lambda1;
	}

	public double SamplePoint(Point pt, double v0, double v1, double v2)
	{
		InterpolationWeights(pt.X, pt.Y, out var lambda, out var lambda2, out var lambda3);
		return v0 * lambda + v1 * lambda2 + v2 * lambda3;
	}

	public T SamplePoint<T>(Point pt, T v0, T v1, T v2) where T : unmanaged, IBinaryFloatingPointIeee754<T>
	{
		InterpolationWeights(pt.X, pt.Y, out var lambda, out var lambda2, out var lambda3);
		return v0 * T.CreateTruncating(lambda) + v1 * T.CreateTruncating(lambda2) + v2 * T.CreateTruncating(lambda3);
	}

	public Point ComputeLocation(BarycentricWeights weights)
	{
		return new Point(PointA.X * weights.Weight0 + PointB.X * weights.Weight1 + PointC.X * weights.Weight2, PointA.Y * weights.Weight0 + PointB.Y * weights.Weight1 + PointC.Y * weights.Weight2);
	}

	public bool IsColinear()
	{
		return IsColinear(PointA, PointB, PointC);
	}

	public static bool IsColinear(Point p0, Point p1, Point p2)
	{
		if (p0.Y == p1.Y && p1.Y == p2.Y)
		{
			return true;
		}
		if (p0.X == p1.X && p1.X == p2.X)
		{
			return true;
		}
		return false;
	}

	public static bool IsColinear(Point3D p0, Point3D p1, Point3D p2)
	{
		if (p0.Y == p1.Y && p1.Y == p2.Y)
		{
			return true;
		}
		if (p0.X == p1.X && p1.X == p2.X)
		{
			return true;
		}
		return false;
	}

	public static bool IsColinear(Point4D p0, Point4D p1, Point4D p2)
	{
		if (p0.Y == p1.Y && p1.Y == p2.Y)
		{
			return true;
		}
		if (p0.X == p1.X && p1.X == p2.X)
		{
			return true;
		}
		return false;
	}

	public static double ComputeAspectRatio(Point p0, Point p1, Point p2)
	{
		double distanceTo = p0.DistanceTo(p1);
		double distanceTo2 = p1.DistanceTo(p2);
		double distanceTo3 = p2.DistanceTo(p0);
		Point pt;
		Segment segment;
		if (distanceTo > distanceTo2 && distanceTo > distanceTo3)
		{
			pt = p2;
			segment = new Segment(p0, p1);
		}
		else if (distanceTo2 > distanceTo && distanceTo2 > distanceTo3)
		{
			pt = p0;
			segment = new Segment(p1, p2);
		}
		else
		{
			pt = p1;
			segment = new Segment(p2, p0);
		}
		double distanceToPoint = segment.DistanceToPoint(pt);
		if (distanceToPoint == 0.0)
		{
			return double.MaxValue;
		}
		return segment.Length / distanceToPoint;
	}

	public static double ComputePerimeterRatio(Point p0, Point p1, Point p2)
	{
		double distanceTo = p0.DistanceTo(p1);
		double distanceTo2 = p1.DistanceTo(p2);
		double distanceTo3 = p2.DistanceTo(p0);
		return Math.Sqrt(ComputeArea(p0, p1, p2)) / (distanceTo + distanceTo2 + distanceTo3);
	}

	public static bool PointInTriangle(Point pTarget, Point pA, Point pB, Point pC)
	{
		Vector other = new Vector(pA, pB);
		Vector other2 = new Vector(pA, pC);
		Vector other3 = new Vector(pA, pTarget);
		double dot = other.Dot(other);
		double dot2 = other.Dot(other2);
		double dot3 = other.Dot(other3);
		double dot4 = other2.Dot(other2);
		double dot5 = other2.Dot(other3);
		double dotDot4Term = 1.0 / (dot * dot4 - dot2 * dot2);
		double dot4Dot3Term = (dot4 * dot3 - dot2 * dot5) * dotDot4Term;
		double dotDot5Term = (dot * dot5 - dot2 * dot3) * dotDot4Term;
		if (dot4Dot3Term > 0.0 && dotDot5Term > 0.0)
		{
			return dot4Dot3Term + dotDot5Term < 1.0;
		}
		return false;
	}

	public static double ComputeArea(Point a, Point b, Point c)
	{
		return ComputeArea(a.X, a.Y, b.X, b.Y, c.X, c.Y);
	}

	public static double ComputeArea(double ax, double ay, double bx, double by, double cx, double cy)
	{
		return Math.Abs(Vector.CrossProduct(ax, ay, bx, by, cx, cy)) * 0.5;
	}

	public static double ComputeSignedArea(Point a, Point b, Point c)
	{
		return ComputeSignedArea(a.X, a.Y, b.X, b.Y, c.X, c.Y);
	}

	public static double ComputeSignedArea(double ax, double ay, double bx, double by, double cx, double cy)
	{
		return Vector.CrossProduct(ax, ay, bx, by, cx, cy) * 0.5;
	}

	public double MaxEdgeLength()
	{
		return MaxEdgeLength(PointA, PointB, PointC);
	}

	public static double MaxEdgeLength(Point pt0, Point pt1, Point pt2)
	{
		double d = pt0.DistanceTo(pt1);
		double d2 = pt1.DistanceTo(pt2);
		double d3 = pt2.DistanceTo(pt0);
		return MathHelpers.Max3(d, d2, d3);
	}

	public static double MaxEdgeLength(IPoint pt0, IPoint pt1, IPoint pt2)
	{
		double d = Point.Distance(pt0.X, pt0.Y, pt1.X, pt1.Y);
		double d2 = Point.Distance(pt1.X, pt1.Y, pt2.X, pt2.Y);
		double d3 = Point.Distance(pt2.X, pt2.Y, pt0.X, pt0.Y);
		return MathHelpers.Max3(d, d2, d3);
	}

	public Polygon AsPolygon()
	{
		return new Polygon(new Point[3] { PointA, PointB, PointC });
	}

	public Point Centroid()
	{
		return Centroid(PointA, PointB, PointC);
	}

	public static Point Centroid(Point pt0, Point pt1, Point pt2)
	{
		double x = (pt0.X + pt1.X + pt2.X) / 3.0;
		double y = (pt0.Y + pt1.Y + pt2.Y) / 3.0;
		return new Point(x, y);
	}

	public Circle CircumCircle()
	{
		return ComputeCircumCircle(PointA, PointB, PointC);
	}

	public Point CircumCircleCenter()
	{
		return CircumCircle().Center;
	}

	public static Circle ComputeCircumCircle(Point A, Point B, Point C)
	{
		Point point = Circle.ComputeCenter(A, B, C);
		double rad = A.DistanceTo(point);
		return new Circle(point, rad);
	}

	public static Circle ComputeCircumCircle(IPoint A, IPoint B, IPoint C)
	{
		Point c = Circle.ComputeCenter(A, B, C);
		double rad = Point.Distance(A.X, A.Y, c.X, c.Y);
		return new Circle(c, rad);
	}

	public static void RasterizeZM(RasterDefinition raster, Point4D p0, Point4D p1, Point4D p2, Action<int, double, double> fillCellAction)
	{
		if (IsColinear(p0, p1, p2) || (p0.X < raster.MinX && p1.X < raster.MinX && p2.X < raster.MinX) || (p0.X > raster.MaxX && p1.X > raster.MaxX && p2.X > raster.MaxX) || (p0.Y < raster.MinY && p1.Y < raster.MinY && p2.Y < raster.MinY) || (p0.Y > raster.MaxY && p1.Y > raster.MaxY && p2.Y > raster.MaxY))
		{
			return;
		}
		Plane plane = new Plane(p0, p1, p2);
		double dZ_dXTimesCellSize = plane.dZ_dX * raster.CellSize;
		double dM_dXTimesCellSize = plane.dM_dX * raster.CellSize;
		Point4D.SortByY(ref p0, ref p1, ref p2);
		if (p0.Y == p2.Y)
		{
			return;
		}
		int row;
		if (p2.Y > raster.MaxY)
		{
			row = 0;
		}
		else
		{
			row = raster.Row(in p2.Y);
			if (p2.Y < raster.CellCenterY(row))
			{
				row++;
			}
		}
		int count;
		if (p0.Y < raster.MinY)
		{
			count = raster.Rows - 1;
		}
		else
		{
			count = raster.Row(in p0.Y);
			if (p0.Y > raster.CellCenterY(count))
			{
				count--;
			}
		}
		if (row > count)
		{
			return;
		}
		double p2P0Term = (p2.X - p0.X) / (p2.Y - p0.Y);
		double p2P1Term = (p2.X - p1.X) / (p2.Y - p1.Y);
		double p1P0Term = (p1.X - p0.X) / (p1.Y - p0.Y);
		double cellSizeRasterTerm = p2P0Term * raster.CellSize;
		double cellSizeRasterTerm2 = p2P1Term * raster.CellSize;
		double cellSizeRasterTerm3 = p1P0Term * raster.CellSize;
		double cellCenterY = raster.CellCenterY(row);
		double p2CellCenterYTerm = p2.X + p2P0Term * (cellCenterY - p2.Y);
		double p2CellCenterYTerm2 = p2.X + p2P1Term * (cellCenterY - p2.Y);
		double p1CellCenterYTerm = p1.X + p1P0Term * (cellCenterY - p1.Y);
		for (int i = row; i <= count; i++)
		{
			int colsRasterTerm = i * raster.Cols;
			double smaller = p2CellCenterYTerm;
			double larger = ((!(cellCenterY > p1.Y) && !double.IsNaN(p1CellCenterYTerm)) ? p1CellCenterYTerm : p2CellCenterYTerm2);
			MathHelpers.SwapIfLarger(ref smaller, ref larger);
			int colStart = raster.Col(in smaller);
			if (raster.CellCenterX(colStart) < smaller)
			{
				colStart++;
			}
			int colEnd = raster.Col(in larger);
			if (raster.CellCenterX(colEnd) > larger)
			{
				colEnd--;
			}
			if (colEnd >= 0 && colStart < raster.Cols)
			{
				if (colStart < 0)
				{
					colStart = 0;
				}
				if (colEnd >= raster.Cols)
				{
					colEnd = raster.Cols - 1;
				}
				smaller = raster.CellCenterX(colStart);
				double dZ_dXTimesCellSizeSum = plane.Z(smaller, cellCenterY);
				double dM_dXTimesCellSizeSum = plane.M(smaller, cellCenterY);
				for (int j = colStart; j <= colEnd; j++)
				{
					fillCellAction(colsRasterTerm + j, dZ_dXTimesCellSizeSum, dM_dXTimesCellSizeSum);
					dZ_dXTimesCellSizeSum += dZ_dXTimesCellSize;
					dM_dXTimesCellSizeSum += dM_dXTimesCellSize;
				}
			}
			cellCenterY -= raster.CellSize;
			p2CellCenterYTerm -= cellSizeRasterTerm;
			p2CellCenterYTerm2 -= cellSizeRasterTerm2;
			p1CellCenterYTerm -= cellSizeRasterTerm3;
		}
	}

	public static void RasterizeZ(RasterDefinition raster, Point3D p0, Point3D p1, Point3D p2, Action<int, double> fillCellAction)
	{
		if (UseNewRasterizerMethod)
		{
			RasterizeInternalBarycentricDeltas(raster, p0, p1, p2, fillCellAction);
		}
		else
		{
			RasterizeZInternal(raster, p0, p1, p2, fillCellAction);
		}
	}

	private static void RasterizeZInternal(RasterDefinition raster, Point3D p0, Point3D p1, Point3D p2, Action<int, double> fillCellAction)
	{
		if (IsColinear(p0, p1, p2))
		{
			return;
		}
		Plane plane = new Plane(p0, p1, p2);
		double dZ_dXTimesCellSize = plane.dZ_dX * raster.CellSize;
		Point3D.SortByY(ref p0, ref p1, ref p2);
		int row;
		if (p2.Y > raster.MaxY)
		{
			row = 0;
		}
		else
		{
			row = raster.Row(in p2.Y);
			if (p2.Y < raster.CellCenterY(row))
			{
				row++;
			}
		}
		int count;
		if (p0.Y < raster.MinY)
		{
			count = raster.Rows - 1;
		}
		else
		{
			count = raster.Row(in p0.Y);
			if (p0.Y > raster.CellCenterY(count))
			{
				count--;
			}
		}
		if (row > count)
		{
			return;
		}
		double p2P0Term = (p2.X - p0.X) / (p2.Y - p0.Y);
		double p2P1Term = (p2.X - p1.X) / (p2.Y - p1.Y);
		double p1P0Term = (p1.X - p0.X) / (p1.Y - p0.Y);
		double cellSizeRasterTerm = p2P0Term * raster.CellSize;
		double cellSizeRasterTerm2 = p2P1Term * raster.CellSize;
		double cellSizeRasterTerm3 = p1P0Term * raster.CellSize;
		double cellCenterY = raster.CellCenterY(row);
		double p2CellCenterYTerm = p2.X + p2P0Term * (cellCenterY - p2.Y);
		double p2CellCenterYTerm2 = p2.X + p2P1Term * (cellCenterY - p2.Y);
		double p1CellCenterYTerm = p1.X + p1P0Term * (cellCenterY - p1.Y);
		for (int i = row; i <= count; i++)
		{
			int colsRasterTerm = i * raster.Cols;
			double smaller = p2CellCenterYTerm;
			double larger = ((!(cellCenterY > p1.Y) && !double.IsNaN(p1CellCenterYTerm)) ? p1CellCenterYTerm : p2CellCenterYTerm2);
			MathHelpers.SwapIfLarger(ref smaller, ref larger);
			int colStart = raster.Col(in smaller);
			if (raster.CellCenterX(colStart) < smaller)
			{
				colStart++;
			}
			int colEnd = raster.Col(in larger);
			if (raster.CellCenterX(colEnd) > larger)
			{
				colEnd--;
			}
			if (colEnd >= 0 && colStart < raster.Cols)
			{
				if (colStart < 0)
				{
					colStart = 0;
				}
				if (colEnd >= raster.Cols)
				{
					colEnd = raster.Cols - 1;
				}
				smaller = raster.CellCenterX(colStart);
				double dZ_dXTimesCellSizeSum = plane.Z(smaller, cellCenterY);
				for (int j = colStart; j <= colEnd; j++)
				{
					fillCellAction(colsRasterTerm + j, dZ_dXTimesCellSizeSum);
					dZ_dXTimesCellSizeSum += dZ_dXTimesCellSize;
				}
			}
			cellCenterY -= raster.CellSize;
			p2CellCenterYTerm -= cellSizeRasterTerm;
			p2CellCenterYTerm2 -= cellSizeRasterTerm2;
			p1CellCenterYTerm -= cellSizeRasterTerm3;
		}
	}

	private static void RasterizeInternalBarycentricDeltas(RasterDefinition raster, Point3D p0, Point3D p1, Point3D p2, Action<int, double> fillCellAction)
	{
		if (IsColinear(p0, p1, p2))
		{
			return;
		}
		Span<Point> points = stackalloc Point[3];
		points[0] = p0.AsPoint2D();
		points[1] = p1.AsPoint2D();
		points[2] = p2.AsPoint2D();
		Extent ext = Extent.Union(points);
		PixelRange pixelRange = raster.GetPixelRange(ext, PixelRangeOptions.CenterPixelCovered);
		if (pixelRange.IsNull())
		{
			return;
		}
		Point origin = new Point(raster.CellCenterX(pixelRange.MinCol), raster.CellCenterY(pixelRange.MaxRow));
		BaryDelta bary = GetBary(p1, p2, origin);
		BaryDelta bary2 = GetBary(p2, p0, origin);
		BaryDelta bary3 = GetBary(p0, p1, origin);
		double deltaXTimesCellSize = bary.DeltaX * raster.CellSize;
		double deltaXTimesCellSize2 = bary2.DeltaX * raster.CellSize;
		double deltaXTimesCellSize3 = bary3.DeltaX * raster.CellSize;
		double deltaYTimesCellSize = bary.DeltaY * raster.CellSize;
		double deltaYTimesCellSize2 = bary2.DeltaY * raster.CellSize;
		double deltaYTimesCellSize3 = bary3.DeltaY * raster.CellSize;
		double startValueBaryTerm = bary.StartValue + bary2.StartValue + bary3.StartValue;
		for (int maxRow = pixelRange.MaxRow; maxRow >= pixelRange.MinRow; maxRow--)
		{
			double startValue = bary.StartValue;
			double startValue2 = bary2.StartValue;
			double startValue3 = bary3.StartValue;
			for (int i = pixelRange.MinCol; i <= pixelRange.MaxCol; i++)
			{
				if (startValue > 0.0 && startValue2 > 0.0 && startValue3 > 0.0)
				{
					double startValueP0Term = (startValue * p0.Z + startValue2 * p1.Z + startValue3 * p2.Z) / startValueBaryTerm;
					fillCellAction(maxRow * raster.Cols + i, startValueP0Term);
				}
				startValue += deltaXTimesCellSize;
				startValue2 += deltaXTimesCellSize2;
				startValue3 += deltaXTimesCellSize3;
			}
			bary.StartValue += deltaYTimesCellSize;
			bary2.StartValue += deltaYTimesCellSize2;
			bary3.StartValue += deltaYTimesCellSize3;
		}
	}

	private unsafe void RasterizeInternalBarycentricDeltas_AVX(RasterDefinition raster, Point3D p0, Point3D p1, Point3D p2, double[] data)
	{
		if (IsColinear(p0, p1, p2))
		{
			return;
		}
		Extent ext = new Extent(MathHelpers.Min3(p0.X, p1.X, p2.X), MathHelpers.Max3(p0.X, p1.X, p2.X), MathHelpers.Min3(p0.Y, p1.Y, p2.Y), MathHelpers.Max3(p0.Y, p1.Y, p2.Y));
		PixelRange pixelRange = raster.GetPixelRange(ext, PixelRangeOptions.CenterPixelCovered);
		if (pixelRange.IsNull())
		{
			return;
		}
		Point origin = new Point(raster.CellCenterX(pixelRange.MinCol), raster.CellCenterY(pixelRange.MaxRow));
		BaryDelta bary = GetBary(p1, p2, origin);
		BaryDelta bary2 = GetBary(p2, p0, origin);
		BaryDelta bary3 = GetBary(p0, p1, origin);
		double deltaXTimesCellSize = bary.DeltaX * raster.CellSize;
		double deltaXTimesCellSize2 = bary2.DeltaX * raster.CellSize;
		double deltaXTimesCellSize3 = bary3.DeltaX * raster.CellSize;
		double deltaYTimesCellSize = bary.DeltaY * raster.CellSize;
		double deltaYTimesCellSize2 = bary2.DeltaY * raster.CellSize;
		double deltaYTimesCellSize3 = bary3.DeltaY * raster.CellSize;
		double startValueBaryTerm = bary.StartValue + bary2.StartValue + bary3.StartValue;
		Avx.BroadcastScalarToVector256(&startValueBaryTerm);
		double* localValue = stackalloc double[4];
		double* localValue2 = stackalloc double[4];
		byte* localValue3 = stackalloc byte[32];
		*localValue = 0.0;
		localValue[1] = deltaXTimesCellSize;
		localValue[2] = 2.0 * deltaXTimesCellSize;
		localValue[3] = 3.0 * deltaXTimesCellSize;
		*localValue2 = 0.0;
		localValue2[1] = deltaXTimesCellSize2;
		localValue2[2] = 2.0 * deltaXTimesCellSize2;
		localValue[3] = 3.0 * deltaXTimesCellSize2;
		*(double*)localValue3 = 0.0;
		((double*)localValue3)[1] = deltaXTimesCellSize3;
		((double*)localValue3)[2] = 2.0 * deltaXTimesCellSize3;
		localValue[3] = 3.0 * deltaXTimesCellSize3;
		Vector256<double> right = Avx.LoadVector256(localValue);
		Vector256<double> right2 = Avx.LoadVector256(localValue2);
		Vector256<double> right3 = Avx.LoadVector256((double*)localValue3);
		double computed = deltaXTimesCellSize * 4.0;
		Vector256<double> right4 = Avx.BroadcastScalarToVector256(&computed);
		computed = deltaXTimesCellSize2 * 4.0;
		Vector256<double> right5 = Avx.BroadcastScalarToVector256(&computed);
		computed = deltaXTimesCellSize3 * 4.0;
		Vector256<double> right6 = Avx.BroadcastScalarToVector256(&computed);
		Avx.BroadcastScalarToVector256(&deltaYTimesCellSize);
		Avx.BroadcastScalarToVector256(&deltaYTimesCellSize2);
		Avx.BroadcastScalarToVector256(&deltaYTimesCellSize3);
		Vector256<double> left = Avx.BroadcastScalarToVector256(&bary.StartValue);
		Vector256<double> left2 = Avx.BroadcastScalarToVector256(&bary2.StartValue);
		Vector256<double> left3 = Avx.BroadcastScalarToVector256(&bary3.StartValue);
		left = Avx.Add(left, right);
		left2 = Avx.Add(left2, right2);
		left3 = Avx.Add(left3, right3);
		double* localValue4 = stackalloc double[4];
		for (int maxRow = pixelRange.MaxRow; maxRow >= pixelRange.MinRow; maxRow--)
		{
			for (int i = pixelRange.MinCol; i <= pixelRange.MaxCol; i += 4)
			{
				Vector256<double> left4 = Avx.Compare(left, Vector256<double>.Zero, FloatComparisonMode.OrderedGreaterThanNonSignaling);
				Vector256<double> right7 = Avx.Compare(left2, Vector256<double>.Zero, FloatComparisonMode.OrderedGreaterThanNonSignaling);
				Vector256<double> left5 = Avx.And(left4, right7);
				Vector256<double> right8 = Avx.Compare(left3, Vector256<double>.Zero, FloatComparisonMode.OrderedGreaterThanNonSignaling);
				left5 = Avx.And(left5, right8);
				Avx.Store(localValue4, left5);
				_ = *localValue4;
				_ = 0.0;
				left = Avx.Add(left, right4);
				left2 = Avx.Add(left2, right5);
				left3 = Avx.Add(left3, right6);
			}
			bary.StartValue += deltaYTimesCellSize;
			bary2.StartValue += deltaYTimesCellSize2;
			bary3.StartValue += deltaYTimesCellSize3;
		}
		throw new NotImplementedException();
	}

	public void Rasterize<T>(RasterDefinition raster, T payload, Action<int, T> fillCellAction)
	{
		Rasterize(raster, PointA, PointB, PointC, payload, fillCellAction);
	}

	public static void Rasterize<T>(RasterDefinition raster, Point p0, Point p1, Point p2, T payload, Action<int, T> fillCellAction)
	{
		if (IsColinear(p0, p1, p2))
		{
			return;
		}
		Point.SortByY(ref p0, ref p1, ref p2);
		int row;
		if (p2.Y > raster.MaxY)
		{
			row = 0;
		}
		else
		{
			row = raster.Row(in p2.Y);
			if (p2.Y < raster.CellCenterY(row))
			{
				row++;
			}
		}
		int count;
		if (p0.Y < raster.MinY)
		{
			count = raster.Rows - 1;
		}
		else
		{
			count = raster.Row(in p0.Y);
			if (p0.Y > raster.CellCenterY(count))
			{
				count--;
			}
		}
		if (row > count)
		{
			return;
		}
		double p2P0Term = (p2.X - p0.X) / (p2.Y - p0.Y);
		double p2P1Term = (p2.X - p1.X) / (p2.Y - p1.Y);
		double p1P0Term = (p1.X - p0.X) / (p1.Y - p0.Y);
		double cellSizeRasterTerm = p2P0Term * raster.CellSize;
		double cellSizeRasterTerm2 = p2P1Term * raster.CellSize;
		double cellSizeRasterTerm3 = p1P0Term * raster.CellSize;
		double cellCenterY = raster.CellCenterY(row);
		double p2CellCenterYTerm = p2.X + p2P0Term * (cellCenterY - p2.Y);
		double p2CellCenterYTerm2 = p2.X + p2P1Term * (cellCenterY - p2.Y);
		double p1CellCenterYTerm = p1.X + p1P0Term * (cellCenterY - p1.Y);
		for (int i = row; i <= count; i++)
		{
			int colsRasterTerm = i * raster.Cols;
			double smaller = p2CellCenterYTerm;
			double larger = ((!(cellCenterY > p1.Y) && !double.IsNaN(p1CellCenterYTerm)) ? p1CellCenterYTerm : p2CellCenterYTerm2);
			MathHelpers.SwapIfLarger(ref smaller, ref larger);
			int colStart = raster.Col(in smaller);
			if (raster.CellCenterX(colStart) < smaller)
			{
				colStart++;
			}
			int colEnd = raster.Col(in larger);
			if (raster.CellCenterX(colEnd) > larger)
			{
				colEnd--;
			}
			if (colEnd >= 0 && colStart < raster.Cols)
			{
				if (colStart < 0)
				{
					colStart = 0;
				}
				if (colEnd >= raster.Cols)
				{
					colEnd = raster.Cols - 1;
				}
				for (int j = colStart; j <= colEnd; j++)
				{
					fillCellAction(colsRasterTerm + j, payload);
				}
			}
			cellCenterY -= raster.CellSize;
			p2CellCenterYTerm -= cellSizeRasterTerm;
			p2CellCenterYTerm2 -= cellSizeRasterTerm2;
			p1CellCenterYTerm -= cellSizeRasterTerm3;
		}
	}

	public static void Rasterize(RasterDefinition raster, Point4D p0, Point4D p1, Point4D p2, Action<int, double, double, double, double> fillCellAction)
	{
		if (IsColinear(p0, p1, p2) || (p0.X < raster.MinX && p1.X < raster.MinX && p2.X < raster.MinX) || (p0.X > raster.MaxX && p1.X > raster.MaxX && p2.X > raster.MaxX) || (p0.Y < raster.MinY && p1.Y < raster.MinY && p2.Y < raster.MinY) || (p0.Y > raster.MaxY && p1.Y > raster.MaxY && p2.Y > raster.MaxY))
		{
			return;
		}
		Plane plane = new Plane(p0, p1, p2);
		double dZ_dXTimesCellSize = plane.dZ_dX * raster.CellSize;
		double dM_dXTimesCellSize = plane.dM_dX * raster.CellSize;
		Point4D.SortByY(ref p0, ref p1, ref p2);
		if (p0.Y == p2.Y)
		{
			return;
		}
		int row;
		if (p2.Y > raster.MaxY)
		{
			row = 0;
		}
		else
		{
			row = raster.Row(in p2.Y);
			if (p2.Y < raster.CellCenterY(row))
			{
				row++;
			}
		}
		int count;
		if (p0.Y < raster.MinY)
		{
			count = raster.Rows - 1;
		}
		else
		{
			count = raster.Row(in p0.Y);
			if (p0.Y > raster.CellCenterY(count))
			{
				count--;
			}
		}
		if (row > count)
		{
			return;
		}
		double p2P0Term = (p2.X - p0.X) / (p2.Y - p0.Y);
		double p2P1Term = (p2.X - p1.X) / (p2.Y - p1.Y);
		double p1P0Term = (p1.X - p0.X) / (p1.Y - p0.Y);
		double cellSizeRasterTerm = p2P0Term * raster.CellSize;
		double cellSizeRasterTerm2 = p2P1Term * raster.CellSize;
		double cellSizeRasterTerm3 = p1P0Term * raster.CellSize;
		double cellCenterY = raster.CellCenterY(row);
		double p2CellCenterYTerm = p2.X + p2P0Term * (cellCenterY - p2.Y);
		double p2CellCenterYTerm2 = p2.X + p2P1Term * (cellCenterY - p2.Y);
		double p1CellCenterYTerm = p1.X + p1P0Term * (cellCenterY - p1.Y);
		double computed;
		double unity;
		double planeTerm;
		if (Math.Abs(plane.F) > Math.Abs(plane.E))
		{
			unity = 1.0;
			planeTerm = (0.0 - plane.E) / plane.F;
			computed = Math.Sqrt(1.0 + planeTerm * planeTerm);
		}
		else
		{
			planeTerm = 1.0;
			unity = (0.0 - plane.F) / plane.E;
			computed = Math.Sqrt(1.0 + unity * unity);
		}
		unity /= computed;
		planeTerm /= computed;
		if ((plane.A * unity + plane.B * planeTerm) * plane.C < 0.0)
		{
			unity = 0.0 - unity;
			planeTerm = 0.0 - planeTerm;
		}
		for (int i = row; i <= count; i++)
		{
			int colsRasterTerm = i * raster.Cols;
			double n = p2CellCenterYTerm;
			double n2 = ((!(cellCenterY > p1.Y) && !double.IsNaN(p1CellCenterYTerm)) ? p1CellCenterYTerm : p2CellCenterYTerm2);
			if (n > n2)
			{
				MathHelpers.Swap(ref n, ref n2);
			}
			int colStart = raster.Col(in n);
			if (raster.CellCenterX(colStart) < n)
			{
				colStart++;
			}
			int colEnd = raster.Col(in n2);
			if (raster.CellCenterX(colEnd) > n2)
			{
				colEnd--;
			}
			if (colEnd >= 0 && colStart < raster.Cols)
			{
				if (colStart < 0)
				{
					colStart = 0;
				}
				if (colEnd >= raster.Cols)
				{
					colEnd = raster.Cols - 1;
				}
				n = raster.CellCenterX(colStart);
				double dZ_dXTimesCellSizeSum = plane.Z(n, cellCenterY);
				double dM_dXTimesCellSizeSum = plane.M(n, cellCenterY);
				for (int j = colStart; j <= colEnd; j++)
				{
					fillCellAction(colsRasterTerm + j, dZ_dXTimesCellSizeSum, dM_dXTimesCellSizeSum, unity, planeTerm);
					dZ_dXTimesCellSizeSum += dZ_dXTimesCellSize;
					dM_dXTimesCellSizeSum += dM_dXTimesCellSize;
				}
			}
			cellCenterY -= raster.CellSize;
			p2CellCenterYTerm -= cellSizeRasterTerm;
			p2CellCenterYTerm2 -= cellSizeRasterTerm2;
			p1CellCenterYTerm -= cellSizeRasterTerm3;
		}
	}

	public static void RasterizeByRow(RasterDefinition raster, Point p0, Point p1, Point p2, RasterizeRowDelegate fillRow)
	{
		if (IsColinear(p0, p1, p2) || (p0.X < raster.MinX && p1.X < raster.MinX && p2.X < raster.MinX) || (p0.X > raster.MaxX && p1.X > raster.MaxX && p2.X > raster.MaxX) || (p0.Y < raster.MinY && p1.Y < raster.MinY && p2.Y < raster.MinY) || (p0.Y > raster.MaxY && p1.Y > raster.MaxY && p2.Y > raster.MaxY))
		{
			return;
		}
		Point.SortByY(ref p0, ref p1, ref p2);
		int row;
		if (p2.Y > raster.MaxY)
		{
			row = 0;
		}
		else
		{
			row = raster.Row(in p2.Y);
			if (p2.Y < raster.CellCenterY(row))
			{
				row++;
			}
		}
		int count;
		if (p0.Y < raster.MinY)
		{
			count = raster.Rows - 1;
		}
		else
		{
			count = raster.Row(in p0.Y);
			if (p0.Y > raster.CellCenterY(count))
			{
				count--;
			}
		}
		if (row > count)
		{
			return;
		}
		double p2P0Term = (p2.X - p0.X) / (p2.Y - p0.Y);
		double p2P1Term = (p2.X - p1.X) / (p2.Y - p1.Y);
		double p1P0Term = (p1.X - p0.X) / (p1.Y - p0.Y);
		double cellSizeRasterTerm = p2P0Term * raster.CellSize;
		double cellSizeRasterTerm2 = p2P1Term * raster.CellSize;
		double cellSizeRasterTerm3 = p1P0Term * raster.CellSize;
		double cellCenterY = raster.CellCenterY(row);
		double p2CellCenterYTerm = p2.X + p2P0Term * (cellCenterY - p2.Y);
		double p2CellCenterYTerm2 = p2.X + p2P1Term * (cellCenterY - p2.Y);
		double p1CellCenterYTerm = p1.X + p1P0Term * (cellCenterY - p1.Y);
		for (int i = row; i <= count; i++)
		{
			double n = p2CellCenterYTerm;
			double n2 = ((!(cellCenterY > p1.Y) && !double.IsNaN(p1CellCenterYTerm)) ? p1CellCenterYTerm : p2CellCenterYTerm2);
			if (n > n2)
			{
				MathHelpers.Swap(ref n, ref n2);
			}
			int col = raster.Col(in n);
			if (raster.CellCenterX(col) < n)
			{
				col++;
			}
			int col2 = raster.Col(in n2);
			if (raster.CellCenterX(col2) > n2)
			{
				col2--;
			}
			if (col2 >= 0 && col < raster.Cols)
			{
				if (col < 0)
				{
					col = 0;
				}
				if (col2 >= raster.Cols)
				{
					col2 = raster.Cols - 1;
				}
				n = raster.CellCenterX(col);
				fillRow(i, col, col2 - col + 1);
			}
			cellCenterY -= raster.CellSize;
			p2CellCenterYTerm -= cellSizeRasterTerm;
			p2CellCenterYTerm2 -= cellSizeRasterTerm2;
			p1CellCenterYTerm -= cellSizeRasterTerm3;
		}
	}

	public static void RasterizeByRow(RasterDefinition raster, Point4D p0, Point4D p1, Point4D p2, RasterizeRowZMDelegate fillRow)
	{
		if (IsColinear(p0, p1, p2) || (p0.X < raster.MinX && p1.X < raster.MinX && p2.X < raster.MinX) || (p0.X > raster.MaxX && p1.X > raster.MaxX && p2.X > raster.MaxX) || (p0.Y < raster.MinY && p1.Y < raster.MinY && p2.Y < raster.MinY) || (p0.Y > raster.MaxY && p1.Y > raster.MaxY && p2.Y > raster.MaxY))
		{
			return;
		}
		Plane plane = new Plane(p0, p1, p2);
		double dz_dCol = plane.dZ_dX * raster.CellSize;
		double dm_dCol = plane.dM_dX * raster.CellSize;
		Point4D.SortByY(ref p0, ref p1, ref p2);
		if (p0.Y == p2.Y)
		{
			return;
		}
		int row;
		if (p2.Y > raster.MaxY)
		{
			row = 0;
		}
		else
		{
			row = raster.Row(in p2.Y);
			if (p2.Y < raster.CellCenterY(row))
			{
				row++;
			}
		}
		int count;
		if (p0.Y < raster.MinY)
		{
			count = raster.Rows - 1;
		}
		else
		{
			count = raster.Row(in p0.Y);
			if (p0.Y > raster.CellCenterY(count))
			{
				count--;
			}
		}
		if (row > count)
		{
			return;
		}
		double p2P0Term = (p2.X - p0.X) / (p2.Y - p0.Y);
		double p2P1Term = (p2.X - p1.X) / (p2.Y - p1.Y);
		double p1P0Term = (p1.X - p0.X) / (p1.Y - p0.Y);
		double cellSizeRasterTerm = p2P0Term * raster.CellSize;
		double cellSizeRasterTerm2 = p2P1Term * raster.CellSize;
		double cellSizeRasterTerm3 = p1P0Term * raster.CellSize;
		double cellCenterY = raster.CellCenterY(row);
		double p2CellCenterYTerm = p2.X + p2P0Term * (cellCenterY - p2.Y);
		double p2CellCenterYTerm2 = p2.X + p2P1Term * (cellCenterY - p2.Y);
		double p1CellCenterYTerm = p1.X + p1P0Term * (cellCenterY - p1.Y);
		for (int i = row; i <= count; i++)
		{
			_ = raster.Cols;
			double n = p2CellCenterYTerm;
			double n2 = ((!(cellCenterY > p1.Y) && !double.IsNaN(p1CellCenterYTerm)) ? p1CellCenterYTerm : p2CellCenterYTerm2);
			if (n > n2)
			{
				MathHelpers.Swap(ref n, ref n2);
			}
			int col = raster.Col(in n);
			if (raster.CellCenterX(col) < n)
			{
				col++;
			}
			int col2 = raster.Col(in n2);
			if (raster.CellCenterX(col2) > n2)
			{
				col2--;
			}
			if (col2 >= 0 && col < raster.Cols)
			{
				if (col < 0)
				{
					col = 0;
				}
				if (col2 >= raster.Cols)
				{
					col2 = raster.Cols - 1;
				}
				n = raster.CellCenterX(col);
				double zStart = plane.Z(n, cellCenterY);
				double mStart = plane.M(n, cellCenterY);
				fillRow(i, col, col2 - col + 1, zStart, mStart, dz_dCol, dm_dCol);
			}
			cellCenterY -= raster.CellSize;
			p2CellCenterYTerm -= cellSizeRasterTerm;
			p2CellCenterYTerm2 -= cellSizeRasterTerm2;
			p1CellCenterYTerm -= cellSizeRasterTerm3;
		}
	}

	public static void ComputeSlope(in Point4D p0, in Point4D p1, in Point4D p2, out double dzX, out double dzY)
	{
		Plane plane = new Plane(p0, p1, p2);
		double sqrtDzYPolynomial;
		if (Math.Abs(plane.F) > Math.Abs(plane.E))
		{
			dzX = 1.0;
			dzY = (0.0 - plane.E) / plane.F;
			sqrtDzYPolynomial = Math.Sqrt(1.0 + dzY * dzY);
		}
		else
		{
			dzY = 1.0;
			dzX = (0.0 - plane.F) / plane.E;
			sqrtDzYPolynomial = Math.Sqrt(1.0 + dzX * dzX);
		}
		dzX /= sqrtDzYPolynomial;
		dzY /= sqrtDzYPolynomial;
		if ((plane.A * dzX + plane.B * dzY) * plane.C < 0.0)
		{
			dzX = 0.0 - dzX;
			dzY = 0.0 - dzY;
		}
	}

	private static BaryDelta GetBary(Point v0, Point v1, Point origin)
	{
		double deltaX = v0.Y - v1.Y;
		double deltaY = v1.X - v0.X;
		double v0V1Term = v0.X * v1.Y - v0.Y * v1.X;
		BaryDelta result = default;
		result.DeltaX = deltaX;
		result.DeltaY = deltaY;
		result.StartValue = deltaX * origin.X + deltaY * origin.Y + v0V1Term;
		return result;
	}

	private static BaryDelta GetBary(Point3D v0, Point3D v1, Point origin)
	{
		double deltaX = v0.Y - v1.Y;
		double deltaY = v1.X - v0.X;
		double v0V1Term = v0.X * v1.Y - v0.Y * v1.X;
		BaryDelta result = default;
		result.DeltaX = deltaX;
		result.DeltaY = deltaY;
		result.StartValue = deltaX * origin.X + deltaY * origin.Y + v0V1Term;
		return result;
	}

	public void Rasterize(RasterDefinition raster, Action<int, BarycentricWeights> fillCellAction)
	{
		Rasterize(raster, PointA, PointB, PointC, fillCellAction);
	}

	public static void Rasterize(RasterDefinition raster, Point p0, Point p1, Point p2, Action<int, BarycentricWeights> fillCellAction)
	{
		if ((p0.Y == p1.Y && p1.Y == p2.Y) || (p0.X == p1.X && p1.X == p2.X))
		{
			return;
		}
		MathHelpers.MaxMin3(p0.X, p1.X, p2.X, out var max, out var min);
		if (max < raster.MinX || min > raster.MaxX)
		{
			return;
		}
		MathHelpers.MaxMin3(p0.Y, p1.Y, p2.Y, out var max2, out var min2);
		if (min2 > raster.MaxY || max2 < raster.MinY)
		{
			return;
		}
		int colStart = ((!(min < raster.MinX)) ? raster.Col(in min) : 0);
		int colEnd = ((!(max > raster.MaxX)) ? raster.Col(in max) : (raster.Cols - 1));
		int rowStart = ((!(max2 > raster.MaxY)) ? raster.Row(in max2) : 0);
		int rowEnd = ((!(min2 < raster.MinY)) ? raster.Row(in min2) : (raster.Rows - 1));
		double cellCenterX = raster.CellCenterX(colStart);
		double cellCenterY = raster.CellCenterY(rowStart);
		double p1P0Term = p1.X - p0.X;
		double p1P0Term2 = p1.Y - p0.Y;
		double p2P0Term = p2.X - p0.X;
		double p2P0Term2 = p2.Y - p0.Y;
		double computed = p1P0Term * p2P0Term2 - p1P0Term2 * p2P0Term;
		if (computed == 0.0)
		{
			return;
		}
		double unity = -1.0;
		double unity2 = -1.0;
		BarycentricData barycentricData = new BarycentricData(p1P0Term2 * unity2 - unity * p2P0Term2, 0.0 - (p1P0Term * unity2 - unity * p2P0Term), computed, p0.X - cellCenterX, p0.Y - cellCenterY, raster.CellSize);
		unity = 1.0;
		unity2 = 0.0;
		BarycentricData barycentricData2 = new BarycentricData(p1P0Term2 * unity2 - unity * p2P0Term2, 0.0 - (p1P0Term * unity2 - unity * p2P0Term), computed, p1.X - cellCenterX, p1.Y - cellCenterY, raster.CellSize);
		unity = 0.0;
		unity2 = 1.0;
		BarycentricData barycentricData3 = new BarycentricData(p1P0Term2 * unity2 - unity * p2P0Term2, 0.0 - (p1P0Term * unity2 - unity * p2P0Term), computed, p2.X - cellCenterX, p2.Y - cellCenterY, raster.CellSize);
		double originW = barycentricData.originW;
		double originW2 = barycentricData2.originW;
		double originW3 = barycentricData3.originW;
		for (int i = rowStart; i <= rowEnd; i++)
		{
			double originW4 = originW;
			double originW5 = originW2;
			double originW6 = originW3;
			int colsRasterTerm = i * raster.Cols + colStart;
			for (int j = colStart; j <= colEnd; j++)
			{
				if (originW4 > -1E-09 && originW5 > -1E-09 && originW6 > -1E-09)
				{
					fillCellAction(colsRasterTerm, new BarycentricWeights(originW4, originW5, originW6));
				}
				colsRasterTerm++;
				originW4 += barycentricData.dw_col;
				originW5 += barycentricData2.dw_col;
				originW6 += barycentricData3.dw_col;
			}
			originW -= barycentricData.dw_row;
			originW2 -= barycentricData2.dw_row;
			originW3 -= barycentricData3.dw_row;
		}
	}

	public void Rasterize<T>(RasterDefinition raster, T payload, Action<T, int, BarycentricWeights> fillCellAction) where T : allows ref struct
	{
		Rasterize(raster, payload, PointA, PointB, PointC, fillCellAction);
	}

	public static void Rasterize<T>(RasterDefinition raster, T payload, Point p0, Point p1, Point p2, Action<T, int, BarycentricWeights> fillCellAction) where T : allows ref struct
	{
		if ((p0.Y == p1.Y && p1.Y == p2.Y) || (p0.X == p1.X && p1.X == p2.X))
		{
			return;
		}
		MathHelpers.MaxMin3(p0.X, p1.X, p2.X, out var max, out var min);
		if (max < raster.MinX || min > raster.MaxX)
		{
			return;
		}
		MathHelpers.MaxMin3(p0.Y, p1.Y, p2.Y, out var max2, out var min2);
		if (min2 > raster.MaxY || max2 < raster.MinY)
		{
			return;
		}
		int colStart = ((!(min < raster.MinX)) ? raster.Col(in min) : 0);
		int colEnd = ((!(max > raster.MaxX)) ? raster.Col(in max) : (raster.Cols - 1));
		int rowStart = ((!(max2 > raster.MaxY)) ? raster.Row(in max2) : 0);
		int rowEnd = ((!(min2 < raster.MinY)) ? raster.Row(in min2) : (raster.Rows - 1));
		double cellCenterX = raster.CellCenterX(colStart);
		double cellCenterY = raster.CellCenterY(rowStart);
		double p1P0Term = p1.X - p0.X;
		double p1P0Term2 = p1.Y - p0.Y;
		double p2P0Term = p2.X - p0.X;
		double p2P0Term2 = p2.Y - p0.Y;
		double computed = p1P0Term * p2P0Term2 - p1P0Term2 * p2P0Term;
		if (computed == 0.0)
		{
			return;
		}
		double unity = -1.0;
		double unity2 = -1.0;
		BarycentricData barycentricData = new BarycentricData(p1P0Term2 * unity2 - unity * p2P0Term2, 0.0 - (p1P0Term * unity2 - unity * p2P0Term), computed, p0.X - cellCenterX, p0.Y - cellCenterY, raster.CellSize);
		unity = 1.0;
		unity2 = 0.0;
		BarycentricData barycentricData2 = new BarycentricData(p1P0Term2 * unity2 - unity * p2P0Term2, 0.0 - (p1P0Term * unity2 - unity * p2P0Term), computed, p1.X - cellCenterX, p1.Y - cellCenterY, raster.CellSize);
		unity = 0.0;
		unity2 = 1.0;
		BarycentricData barycentricData3 = new BarycentricData(p1P0Term2 * unity2 - unity * p2P0Term2, 0.0 - (p1P0Term * unity2 - unity * p2P0Term), computed, p2.X - cellCenterX, p2.Y - cellCenterY, raster.CellSize);
		double originW = barycentricData.originW;
		double originW2 = barycentricData2.originW;
		double originW3 = barycentricData3.originW;
		for (int i = rowStart; i <= rowEnd; i++)
		{
			double originW4 = originW;
			double originW5 = originW2;
			double originW6 = originW3;
			int colsRasterTerm = i * raster.Cols + colStart;
			for (int j = colStart; j <= colEnd; j++)
			{
				if (originW4 > -1E-09 && originW5 > -1E-09 && originW6 > -1E-09)
				{
					fillCellAction(payload, colsRasterTerm, new BarycentricWeights(originW4, originW5, originW6));
				}
				colsRasterTerm++;
				originW4 += barycentricData.dw_col;
				originW5 += barycentricData2.dw_col;
				originW6 += barycentricData3.dw_col;
			}
			originW -= barycentricData.dw_row;
			originW2 -= barycentricData2.dw_row;
			originW3 -= barycentricData3.dw_row;
		}
	}

	public void Rasterize(RasterDefinition raster, float[] buffer, float za, float zb, float zc)
	{
		Rasterize(raster, buffer, PointA, PointB, PointC, za, zb, zc);
	}

	public static void Rasterize(RasterDefinition raster, float[] buffer, Point p0, Point p1, Point p2, float za, float zb, float zc)
	{
		if ((p0.Y == p1.Y && p1.Y == p2.Y) || (p0.X == p1.X && p1.X == p2.X))
		{
			return;
		}
		MathHelpers.MaxMin3(p0.X, p1.X, p2.X, out var max, out var min);
		if (max < raster.MinX || min > raster.MaxX)
		{
			return;
		}
		MathHelpers.MaxMin3(p0.Y, p1.Y, p2.Y, out var max2, out var min2);
		if (min2 > raster.MaxY || max2 < raster.MinY)
		{
			return;
		}
		int colStart = ((!(min < raster.MinX)) ? raster.Col(in min) : 0);
		int colEnd = ((!(max > raster.MaxX)) ? raster.Col(in max) : (raster.Cols - 1));
		int rowStart = ((!(max2 > raster.MaxY)) ? raster.Row(in max2) : 0);
		int rowEnd = ((!(min2 < raster.MinY)) ? raster.Row(in min2) : (raster.Rows - 1));
		double cellCenterX = raster.CellCenterX(colStart);
		double cellCenterY = raster.CellCenterY(rowStart);
		double p1P0Term = p1.X - p0.X;
		double p1P0Term2 = p1.Y - p0.Y;
		double p2P0Term = p2.X - p0.X;
		double p2P0Term2 = p2.Y - p0.Y;
		double computed = p1P0Term * p2P0Term2 - p1P0Term2 * p2P0Term;
		if (computed == 0.0)
		{
			return;
		}
		double unity = -1.0;
		double unity2 = -1.0;
		BarycentricData barycentricData = new BarycentricData(p1P0Term2 * unity2 - unity * p2P0Term2, 0.0 - (p1P0Term * unity2 - unity * p2P0Term), computed, p0.X - cellCenterX, p0.Y - cellCenterY, raster.CellSize);
		unity = 1.0;
		unity2 = 0.0;
		BarycentricData barycentricData2 = new BarycentricData(p1P0Term2 * unity2 - unity * p2P0Term2, 0.0 - (p1P0Term * unity2 - unity * p2P0Term), computed, p1.X - cellCenterX, p1.Y - cellCenterY, raster.CellSize);
		unity = 0.0;
		unity2 = 1.0;
		BarycentricData barycentricData3 = new BarycentricData(p1P0Term2 * unity2 - unity * p2P0Term2, 0.0 - (p1P0Term * unity2 - unity * p2P0Term), computed, p2.X - cellCenterX, p2.Y - cellCenterY, raster.CellSize);
		double originW = barycentricData.originW;
		double originW2 = barycentricData2.originW;
		double originW3 = barycentricData3.originW;
		for (int i = rowStart; i <= rowEnd; i++)
		{
			double originW4 = originW;
			double originW5 = originW2;
			double originW6 = originW3;
			int colsRasterTerm = i * raster.Cols + colStart;
			for (int j = colStart; j <= colEnd; j++)
			{
				if (originW4 > -1E-09 && originW5 > -1E-09 && originW6 > -1E-09)
				{
					buffer[colsRasterTerm] = (float)(originW4 * (double)za + originW5 * (double)zb + originW6 * (double)zc);
				}
				colsRasterTerm++;
				originW4 += barycentricData.dw_col;
				originW5 += barycentricData2.dw_col;
				originW6 += barycentricData3.dw_col;
			}
			originW -= barycentricData.dw_row;
			originW2 -= barycentricData2.dw_row;
			originW3 -= barycentricData3.dw_row;
		}
	}

	public void RasterizePartialPixel(RasterDefinition raster, Action<Pixel> pixelAction)
	{
		Extent extent = Extent;
		PixelRange pxRange = raster.GetPixelRange(extent);
		Tuple<int, int>[] rows = new Tuple<int, int>[pxRange.RowCount];
		Edge0.Rasterize(raster, EdgeAction);
		Edge1.Rasterize(raster, EdgeAction);
		Edge2.Rasterize(raster, EdgeAction);
		for (int i = 0; i < rows.Length; i++)
		{
			if (rows[i] != null)
			{
				int r = i + pxRange.MinRow;
				for (int j = pxRange.MinCol; j <= pxRange.MaxCol; j++)
				{
					pixelAction(new Pixel(r, j));
				}
			}
		}
		void EdgeAction(Pixel px)
		{
			int rowAboveMinRow = px.Row - pxRange.MinRow;
			Tuple<int, int> tuple = rows[rowAboveMinRow];
			if (tuple == null)
			{
				rows[rowAboveMinRow] = new Tuple<int, int>(px.Column, px.Column);
			}
			else
			{
				rows[rowAboveMinRow] = new Tuple<int, int>(Math.Min(tuple.Item1, px.Column), Math.Max(tuple.Item2, px.Column));
			}
		}
	}
}
