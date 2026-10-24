using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Geospatial.Vectors;

namespace Geospatial.Clipper2Lib;

public static class Clipper
{
	private static Rect64 invalidRect64 = new Rect64(isValid: false);

	private static RectD invalidRectD = new RectD(isValid: false);

	public static Rect64 InvalidRect64 => invalidRect64;

	public static RectD InvalidRectD => invalidRectD;

	public static Paths64 Intersect(Paths64 subject, Paths64 clip, FillRule fillRule)
	{
		return BooleanOp(ClipType.Intersection, subject, clip, fillRule);
	}

	public static PathsD Intersect(PathsD subject, PathsD clip, FillRule fillRule, int precision = 2)
	{
		return BooleanOp(ClipType.Intersection, subject, clip, fillRule, precision);
	}

	public static Paths64 Union(Paths64 subject, FillRule fillRule)
	{
		return BooleanOp(ClipType.Union, subject, null, fillRule);
	}

	public static Paths64 Union(Paths64 subject, Paths64 clip, FillRule fillRule)
	{
		return BooleanOp(ClipType.Union, subject, clip, fillRule);
	}

	public static PathsD Union(PathsD subject, FillRule fillRule)
	{
		return BooleanOp(ClipType.Union, subject, null, fillRule);
	}

	public static PathsD Union(PathsD subject, PathsD clip, FillRule fillRule, int precision = 2)
	{
		return BooleanOp(ClipType.Union, subject, clip, fillRule, precision);
	}

	public static Paths64 Difference(Paths64 subject, Paths64 clip, FillRule fillRule)
	{
		return BooleanOp(ClipType.Difference, subject, clip, fillRule);
	}

	public static PathsD Difference(PathsD subject, PathsD clip, FillRule fillRule, int precision = 2)
	{
		return BooleanOp(ClipType.Difference, subject, clip, fillRule, precision);
	}

	public static Paths64 Xor(Paths64 subject, Paths64 clip, FillRule fillRule)
	{
		return BooleanOp(ClipType.Xor, subject, clip, fillRule);
	}

	public static PathsD Xor(PathsD subject, PathsD clip, FillRule fillRule, int precision = 2)
	{
		return BooleanOp(ClipType.Xor, subject, clip, fillRule, precision);
	}

	public static Paths64 BooleanOp(ClipType clipType, Paths64? subject, Paths64? clip, FillRule fillRule)
	{
		Paths64 paths = new Paths64();
		if (subject == null)
		{
			return paths;
		}
		Clipper64 clipper = new Clipper64();
		clipper.AddPaths(subject, PathType.Subject);
		if (clip != null)
		{
			clipper.AddPaths(clip, PathType.Clip);
		}
		clipper.Execute(clipType, fillRule, paths);
		return paths;
	}

	public static void BooleanOp(ClipType clipType, Paths64? subject, Paths64? clip, PolyTree64 polytree, FillRule fillRule)
	{
		if (subject != null)
		{
			Clipper64 clipper = new Clipper64();
			clipper.AddPaths(subject, PathType.Subject);
			if (clip != null)
			{
				clipper.AddPaths(clip, PathType.Clip);
			}
			clipper.Execute(clipType, fillRule, polytree);
		}
	}

	public static PathsD BooleanOp(ClipType clipType, PathsD subject, PathsD? clip, FillRule fillRule, int precision = 2)
	{
		PathsD pathsD = new PathsD();
		ClipperD clipperD = new ClipperD(precision);
		clipperD.AddSubject(subject);
		if (clip != null)
		{
			clipperD.AddClip(clip);
		}
		clipperD.Execute(clipType, fillRule, pathsD);
		return pathsD;
	}

	public static void BooleanOp(ClipType clipType, PathsD? subject, PathsD? clip, PolyTreeD polytree, FillRule fillRule, int precision = 2)
	{
		if (subject != null)
		{
			ClipperD clipperD = new ClipperD(precision);
			clipperD.AddPaths(subject, PathType.Subject);
			if (clip != null)
			{
				clipperD.AddPaths(clip, PathType.Clip);
			}
			clipperD.Execute(clipType, fillRule, polytree);
		}
	}

	public static Paths64 InflatePaths(Paths64 paths, double delta, JoinType joinType, EndType endType, double miterLimit = 2.0, double arcTolerance = 0.0)
	{
		ClipperOffset clipperOffset = new ClipperOffset(miterLimit, arcTolerance);
		clipperOffset.AddPaths(paths, joinType, endType);
		Paths64 paths2 = new Paths64();
		clipperOffset.Execute(delta, paths2);
		return paths2;
	}

	public static PathsD InflatePaths(PathsD paths, double delta, JoinType joinType, EndType endType, double miterLimit = 2.0, int precision = 2, double arcTolerance = 0.0)
	{
		InternalClipper.CheckPrecision(precision);
		double num = Math.Pow(10.0, precision);
		Paths64 paths2 = ScalePaths64(paths, num);
		ClipperOffset clipperOffset = new ClipperOffset(miterLimit, num * arcTolerance);
		clipperOffset.AddPaths(paths2, joinType, endType);
		clipperOffset.Execute(delta * num, paths2);
		return ScalePathsD(paths2, 1.0 / num);
	}

	public static Paths64 RectClip(Rect64 rect, Paths64 paths)
	{
		if (rect.IsEmpty() || paths.Count == 0)
		{
			return new Paths64();
		}
		return new RectClip64(rect).Execute(paths);
	}

	public static Paths64 RectClip(Rect64 rect, Path64 path)
	{
		if (rect.IsEmpty() || path.Count == 0)
		{
			return new Paths64();
		}
		Paths64 paths = new Paths64 { path };
		return RectClip(rect, paths);
	}

	public static PathsD RectClip(RectD rect, PathsD paths, int precision = 2)
	{
		InternalClipper.CheckPrecision(precision);
		if (rect.IsEmpty() || paths.Count == 0)
		{
			return new PathsD();
		}
		double num = Math.Pow(10.0, precision);
		Rect64 rect2 = ScaleRect(rect, num);
		Paths64 paths2 = ScalePaths64(paths, num);
		paths2 = new RectClip64(rect2).Execute(paths2);
		return ScalePathsD(paths2, 1.0 / num);
	}

	public static PathsD RectClip(RectD rect, PathD path, int precision = 2)
	{
		if (rect.IsEmpty() || path.Count == 0)
		{
			return new PathsD();
		}
		PathsD paths = new PathsD { path };
		return RectClip(rect, paths, precision);
	}

	public static Paths64 RectClipLines(Rect64 rect, Paths64 paths)
	{
		if (rect.IsEmpty() || paths.Count == 0)
		{
			return new Paths64();
		}
		return new RectClipLines64(rect).Execute(paths);
	}

	public static Paths64 RectClipLines(Rect64 rect, Path64 path)
	{
		if (rect.IsEmpty() || path.Count == 0)
		{
			return new Paths64();
		}
		Paths64 paths = new Paths64 { path };
		return RectClipLines(rect, paths);
	}

	public static PathsD RectClipLines(RectD rect, PathsD paths, int precision = 2)
	{
		InternalClipper.CheckPrecision(precision);
		if (rect.IsEmpty() || paths.Count == 0)
		{
			return new PathsD();
		}
		double num = Math.Pow(10.0, precision);
		Rect64 rect2 = ScaleRect(rect, num);
		Paths64 paths2 = ScalePaths64(paths, num);
		paths2 = new RectClipLines64(rect2).Execute(paths2);
		return ScalePathsD(paths2, 1.0 / num);
	}

	public static PathsD RectClipLines(RectD rect, PathD path, int precision = 2)
	{
		if (rect.IsEmpty() || path.Count == 0)
		{
			return new PathsD();
		}
		PathsD paths = new PathsD { path };
		return RectClipLines(rect, paths, precision);
	}

	public static Paths64 MinkowskiSum(Path64 pattern, Path64 path, bool isClosed)
	{
		return Minkowski.Sum(pattern, path, isClosed);
	}

	public static PathsD MinkowskiSum(PathD pattern, PathD path, bool isClosed)
	{
		return Minkowski.Sum(pattern, path, isClosed);
	}

	public static Paths64 MinkowskiDiff(Path64 pattern, Path64 path, bool isClosed)
	{
		return Minkowski.Diff(pattern, path, isClosed);
	}

	public static PathsD MinkowskiDiff(PathD pattern, PathD path, bool isClosed)
	{
		return Minkowski.Diff(pattern, path, isClosed);
	}

	public static double Area(Path64 path)
	{
		double num = 0.0;
		int count = path.Count;
		if (count < 3)
		{
			return 0.0;
		}
		Point64 point = path[count - 1];
		foreach (Point64 item in path)
		{
			num += (double)(point.Y + item.Y) * (double)(point.X - item.X);
			point = item;
		}
		return num * 0.5;
	}

	public static double Area(Paths64 paths)
	{
		double num = 0.0;
		foreach (Path64 path in paths)
		{
			num += Area(path);
		}
		return num;
	}

	public static double Area(PathD path)
	{
		double num = 0.0;
		int count = path.Count;
		if (count < 3)
		{
			return 0.0;
		}
		Point point = path[count - 1];
		foreach (Point item in path)
		{
			num += (point.Y + item.Y) * (point.X - item.X);
			point = item;
		}
		return num * 0.5;
	}

	public static double Area(PathsD paths)
	{
		double num = 0.0;
		foreach (PathD path in paths)
		{
			num += Area(path);
		}
		return num;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsPositive(Path64 poly)
	{
		return Area(poly) >= 0.0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsPositive(PathD poly)
	{
		return Area(poly) >= 0.0;
	}

	public static string Path64ToString(Path64 path)
	{
		string text = "";
		foreach (Point64 item in path)
		{
			text += item;
		}
		return text + "\n";
	}

	public static string Paths64ToString(Paths64 paths)
	{
		string text = "";
		foreach (Path64 path in paths)
		{
			text += Path64ToString(path);
		}
		return text;
	}

	public static string PathDToString(PathD path)
	{
		string text = "";
		foreach (Point item in path)
		{
			text += item.ToString();
		}
		return text + "\n";
	}

	public static string PathsDToString(PathsD paths)
	{
		string text = "";
		foreach (PathD path in paths)
		{
			text += PathDToString(path);
		}
		return text;
	}

	public static Path64 OffsetPath(Path64 path, long dx, long dy)
	{
		Path64 path2 = new Path64(path.Count);
		foreach (Point64 item in path)
		{
			path2.Add(new Point64(item.X + dx, item.Y + dy));
		}
		return path2;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Point64 ScalePoint64(Point64 pt, double scale)
	{
		return new Point64
		{
			X = (long)Math.Round((double)pt.X * scale, MidpointRounding.AwayFromZero),
			Y = (long)Math.Round((double)pt.Y * scale, MidpointRounding.AwayFromZero)
		};
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Point ScalePointD(Point64 pt, double scale)
	{
		return new Point
		{
			X = (double)pt.X * scale,
			Y = (double)pt.Y * scale
		};
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Rect64 ScaleRect(RectD rec, double scale)
	{
		return new Rect64
		{
			left = (long)(rec.left * scale),
			top = (long)(rec.top * scale),
			right = (long)(rec.right * scale),
			bottom = (long)(rec.bottom * scale)
		};
	}

	public static Path64 ScalePath(Path64 path, double scale)
	{
		if (InternalClipper.IsAlmostZero(scale - 1.0))
		{
			return path;
		}
		Path64 path2 = new Path64(path.Count);
		foreach (Point64 item in path)
		{
			path2.Add(new Point64((double)item.X * scale, (double)item.Y * scale));
		}
		return path2;
	}

	public static Paths64 ScalePaths(Paths64 paths, double scale)
	{
		if (InternalClipper.IsAlmostZero(scale - 1.0))
		{
			return paths;
		}
		Paths64 paths2 = new Paths64(paths.Count);
		foreach (Path64 path in paths)
		{
			paths2.Add(ScalePath(path, scale));
		}
		return paths2;
	}

	public static PathD ScalePath(PathD path, double scale)
	{
		if (InternalClipper.IsAlmostZero(scale - 1.0))
		{
			return path;
		}
		PathD pathD = new PathD(path.Count);
		foreach (Point item in path)
		{
			pathD.Add(new Point(item, scale));
		}
		return pathD;
	}

	public static PathsD ScalePaths(PathsD paths, double scale)
	{
		if (InternalClipper.IsAlmostZero(scale - 1.0))
		{
			return paths;
		}
		PathsD pathsD = new PathsD(paths.Count);
		foreach (PathD path in paths)
		{
			pathsD.Add(ScalePath(path, scale));
		}
		return pathsD;
	}

	public static Path64 ScalePath64(PathD path, double scale)
	{
		Path64 path2 = new Path64(path.Count);
		foreach (Point item in path)
		{
			path2.Add(new Point64(item, scale));
		}
		return path2;
	}

	public static Paths64 ScalePaths64(PathsD paths, double scale)
	{
		Paths64 paths2 = new Paths64(paths.Count);
		foreach (PathD path in paths)
		{
			paths2.Add(ScalePath64(path, scale));
		}
		return paths2;
	}

	public static PathD ScalePathD(Path64 path, double scale)
	{
		PathD pathD = new PathD(path.Count);
		foreach (Point64 item in path)
		{
			pathD.Add(new Point(item, scale));
		}
		return pathD;
	}

	public static PathsD ScalePathsD(Paths64 paths, double scale)
	{
		PathsD pathsD = new PathsD(paths.Count);
		foreach (Path64 path in paths)
		{
			pathsD.Add(ScalePathD(path, scale));
		}
		return pathsD;
	}

	public static Path64 Path64(PathD path)
	{
		Path64 path2 = new Path64(path.Count);
		foreach (Point item in path)
		{
			path2.Add(new Point64(item));
		}
		return path2;
	}

	public static Paths64 Paths64(PathsD paths)
	{
		Paths64 paths2 = new Paths64(paths.Count);
		foreach (PathD path in paths)
		{
			paths2.Add(Path64(path));
		}
		return paths2;
	}

	public static PathsD PathsD(Paths64 paths)
	{
		PathsD pathsD = new PathsD(paths.Count);
		foreach (Path64 path in paths)
		{
			pathsD.Add(PathD(path));
		}
		return pathsD;
	}

	public static PathD PathD(Path64 path)
	{
		PathD pathD = new PathD(path.Count);
		foreach (Point64 item in path)
		{
			pathD.Add(new Point(item));
		}
		return pathD;
	}

	public static Path64 TranslatePath(Path64 path, long dx, long dy)
	{
		Path64 path2 = new Path64(path.Count);
		foreach (Point64 item in path)
		{
			path2.Add(new Point64(item.X + dx, item.Y + dy));
		}
		return path2;
	}

	public static Paths64 TranslatePaths(Paths64 paths, long dx, long dy)
	{
		Paths64 paths2 = new Paths64(paths.Count);
		foreach (Path64 path in paths)
		{
			paths2.Add(OffsetPath(path, dx, dy));
		}
		return paths2;
	}

	public static PathD TranslatePath(PathD path, double dx, double dy)
	{
		PathD pathD = new PathD(path.Count);
		foreach (Point item in path)
		{
			pathD.Add(new Point(item.X + dx, item.Y + dy));
		}
		return pathD;
	}

	public static PathsD TranslatePaths(PathsD paths, double dx, double dy)
	{
		PathsD pathsD = new PathsD(paths.Count);
		foreach (PathD path in paths)
		{
			pathsD.Add(TranslatePath(path, dx, dy));
		}
		return pathsD;
	}

	public static Path64 ReversePath(Path64 path)
	{
		Path64 path2 = new Path64(path);
		path2.Reverse();
		return path2;
	}

	public static PathD ReversePath(PathD path)
	{
		PathD pathD = new PathD(path);
		pathD.Reverse();
		return pathD;
	}

	public static Paths64 ReversePaths(Paths64 paths)
	{
		Paths64 paths2 = new Paths64(paths.Count);
		foreach (Path64 path in paths)
		{
			paths2.Add(ReversePath(path));
		}
		return paths2;
	}

	public static PathsD ReversePaths(PathsD paths)
	{
		PathsD pathsD = new PathsD(paths.Count);
		foreach (PathD path in paths)
		{
			pathsD.Add(ReversePath(path));
		}
		return pathsD;
	}

	public static Rect64 GetBounds(Path64 path)
	{
		Rect64 result = InvalidRect64;
		foreach (Point64 item in path)
		{
			if (item.X < result.left)
			{
				result.left = item.X;
			}
			if (item.X > result.right)
			{
				result.right = item.X;
			}
			if (item.Y < result.top)
			{
				result.top = item.Y;
			}
			if (item.Y > result.bottom)
			{
				result.bottom = item.Y;
			}
		}
		if (result.left != long.MaxValue)
		{
			return result;
		}
		return default;
	}

	public static Rect64 GetBounds(Paths64 paths)
	{
		Rect64 result = InvalidRect64;
		foreach (Path64 path in paths)
		{
			foreach (Point64 item in path)
			{
				if (item.X < result.left)
				{
					result.left = item.X;
				}
				if (item.X > result.right)
				{
					result.right = item.X;
				}
				if (item.Y < result.top)
				{
					result.top = item.Y;
				}
				if (item.Y > result.bottom)
				{
					result.bottom = item.Y;
				}
			}
		}
		if (result.left != long.MaxValue)
		{
			return result;
		}
		return default;
	}

	public static RectD GetBounds(PathD path)
	{
		RectD result = InvalidRectD;
		foreach (Point item in path)
		{
			if (item.X < result.left)
			{
				result.left = item.X;
			}
			if (item.X > result.right)
			{
				result.right = item.X;
			}
			if (item.Y < result.top)
			{
				result.top = item.Y;
			}
			if (item.Y > result.bottom)
			{
				result.bottom = item.Y;
			}
		}
		if (!(Math.Abs(result.left - double.MaxValue) < 1E-12))
		{
			return result;
		}
		return default;
	}

	public static RectD GetBounds(PathsD paths)
	{
		RectD result = InvalidRectD;
		foreach (PathD path in paths)
		{
			foreach (Point item in path)
			{
				if (item.X < result.left)
				{
					result.left = item.X;
				}
				if (item.X > result.right)
				{
					result.right = item.X;
				}
				if (item.Y < result.top)
				{
					result.top = item.Y;
				}
				if (item.Y > result.bottom)
				{
					result.bottom = item.Y;
				}
			}
		}
		if (!(Math.Abs(result.left - double.MaxValue) < 1E-12))
		{
			return result;
		}
		return default;
	}

	public static Path64 MakePath(int[] arr)
	{
		int num = arr.Length / 2;
		Path64 path = new Path64(num);
		for (int i = 0; i < num; i++)
		{
			path.Add(new Point64(arr[i * 2], arr[i * 2 + 1]));
		}
		return path;
	}

	public static Path64 MakePath(long[] arr)
	{
		int num = arr.Length / 2;
		Path64 path = new Path64(num);
		for (int i = 0; i < num; i++)
		{
			path.Add(new Point64(arr[i * 2], arr[i * 2 + 1]));
		}
		return path;
	}

	public static PathD MakePath(double[] arr)
	{
		int num = arr.Length / 2;
		PathD pathD = new PathD(num);
		for (int i = 0; i < num; i++)
		{
			pathD.Add(new Point(arr[i * 2], arr[i * 2 + 1]));
		}
		return pathD;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static double Sqr(double val)
	{
		return val * val;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static double Sqr(long val)
	{
		return (double)val * (double)val;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static double DistanceSqr(Point64 pt1, Point64 pt2)
	{
		return Sqr(pt1.X - pt2.X) + Sqr(pt1.Y - pt2.Y);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Point64 MidPoint(Point64 pt1, Point64 pt2)
	{
		return new Point64((pt1.X + pt2.X) / 2, (pt1.Y + pt2.Y) / 2);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Point MidPoint(Point pt1, Point pt2)
	{
		return new Point((pt1.X + pt2.X) / 2.0, (pt1.Y + pt2.Y) / 2.0);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void InflateRect(ref Rect64 rec, int dx, int dy)
	{
		rec.left -= dx;
		rec.right += dx;
		rec.top -= dy;
		rec.bottom += dy;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void InflateRect(ref RectD rec, double dx, double dy)
	{
		rec.left -= dx;
		rec.right += dx;
		rec.top -= dy;
		rec.bottom += dy;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool PointsNearEqual(Point pt1, Point pt2, double distanceSqrd)
	{
		return Sqr(pt1.X - pt2.X) + Sqr(pt1.Y - pt2.Y) < distanceSqrd;
	}

	public static PathD StripNearDuplicates(PathD path, double minEdgeLenSqrd, bool isClosedPath)
	{
		int count = path.Count;
		PathD pathD = new PathD(count);
		if (count == 0)
		{
			return pathD;
		}
		Point point = path[0];
		pathD.Add(point);
		for (int i = 1; i < count; i++)
		{
			if (!PointsNearEqual(point, path[i], minEdgeLenSqrd))
			{
				point = path[i];
				pathD.Add(point);
			}
		}
		if (isClosedPath && PointsNearEqual(point, pathD[0], minEdgeLenSqrd))
		{
			pathD.RemoveAt(pathD.Count - 1);
		}
		return pathD;
	}

	public static Path64 StripDuplicates(Path64 path, bool isClosedPath)
	{
		int count = path.Count;
		Path64 path2 = new Path64(count);
		if (count == 0)
		{
			return path2;
		}
		Point64 point = path[0];
		path2.Add(point);
		for (int i = 1; i < count; i++)
		{
			if (point != path[i])
			{
				point = path[i];
				path2.Add(point);
			}
		}
		if (isClosedPath && point == path2[0])
		{
			path2.RemoveAt(path2.Count - 1);
		}
		return path2;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void AddPolyNodeToPaths(PolyPath64 polyPath, Paths64 paths)
	{
		if (polyPath.Polygon.Count > 0)
		{
			paths.Add(polyPath.Polygon);
		}
		for (int i = 0; i < polyPath.Count; i++)
		{
			AddPolyNodeToPaths((PolyPath64)polyPath._childs[i], paths);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Paths64 PolyTreeToPaths64(PolyTree64 polyTree)
	{
		Paths64 paths = new Paths64();
		for (int i = 0; i < polyTree.Count; i++)
		{
			AddPolyNodeToPaths((PolyPath64)polyTree._childs[i], paths);
		}
		return paths;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void AddPolyNodeToPathsD(PolyPathD polyPath, PathsD paths)
	{
		if (polyPath.Polygon.Count > 0)
		{
			paths.Add(polyPath.Polygon);
		}
		for (int i = 0; i < polyPath.Count; i++)
		{
			AddPolyNodeToPathsD((PolyPathD)polyPath._childs[i], paths);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static PathsD PolyTreeToPathsD(PolyTreeD polyTree)
	{
		PathsD pathsD = new PathsD();
		foreach (PolyPathD item in polyTree)
		{
			AddPolyNodeToPathsD(item, pathsD);
		}
		return pathsD;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static double PerpendicDistFromLineSqrd(Point pt, Point line1, Point line2)
	{
		double num = pt.X - line1.X;
		double num2 = pt.Y - line1.Y;
		double num3 = line2.X - line1.X;
		double num4 = line2.Y - line1.Y;
		if (num3 == 0.0 && num4 == 0.0)
		{
			return 0.0;
		}
		return Sqr(num * num4 - num3 * num2) / (num3 * num3 + num4 * num4);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static double PerpendicDistFromLineSqrd(Point64 pt, Point64 line1, Point64 line2)
	{
		double num = (double)pt.X - (double)line1.X;
		double num2 = (double)pt.Y - (double)line1.Y;
		double num3 = (double)line2.X - (double)line1.X;
		double num4 = (double)line2.Y - (double)line1.Y;
		if (num3 == 0.0 && num4 == 0.0)
		{
			return 0.0;
		}
		return Sqr(num * num4 - num3 * num2) / (num3 * num3 + num4 * num4);
	}

	internal static void RDP(Path64 path, int begin, int end, double epsSqrd, List<bool> flags)
	{
		while (true)
		{
			int num = 0;
			double num2 = 0.0;
			while (end > begin && path[begin] == path[end])
			{
				flags[end--] = false;
			}
			for (int i = begin + 1; i < end; i++)
			{
				double num3 = PerpendicDistFromLineSqrd(path[i], path[begin], path[end]);
				if (!(num3 <= num2))
				{
					num2 = num3;
					num = i;
				}
			}
			if (num2 <= epsSqrd)
			{
				break;
			}
			flags[num] = true;
			if (num > begin + 1)
			{
				RDP(path, begin, num, epsSqrd, flags);
			}
			if (num < end - 1)
			{
				begin = num;
				continue;
			}
			break;
		}
	}

	public static Path64 RamerDouglasPeucker(Path64 path, double epsilon)
	{
		int count = path.Count;
		if (count < 5)
		{
			return path;
		}
		List<bool> list = new List<bool>(new bool[count])
		{
			[0] = true,
			[count - 1] = true
		};
		RDP(path, 0, count - 1, Sqr(epsilon), list);
		Path64 path2 = new Path64(count);
		for (int i = 0; i < count; i++)
		{
			if (list[i])
			{
				path2.Add(path[i]);
			}
		}
		return path2;
	}

	public static Paths64 RamerDouglasPeucker(Paths64 paths, double epsilon)
	{
		Paths64 paths2 = new Paths64(paths.Count);
		foreach (Path64 path in paths)
		{
			paths2.Add(RamerDouglasPeucker(path, epsilon));
		}
		return paths2;
	}

	internal static void RDP(PathD path, int begin, int end, double epsSqrd, List<bool> flags)
	{
		while (true)
		{
			int num = 0;
			double num2 = 0.0;
			while (end > begin && path[begin] == path[end])
			{
				flags[end--] = false;
			}
			for (int i = begin + 1; i < end; i++)
			{
				double num3 = PerpendicDistFromLineSqrd(path[i], path[begin], path[end]);
				if (!(num3 <= num2))
				{
					num2 = num3;
					num = i;
				}
			}
			if (num2 <= epsSqrd)
			{
				break;
			}
			flags[num] = true;
			if (num > begin + 1)
			{
				RDP(path, begin, num, epsSqrd, flags);
			}
			if (num < end - 1)
			{
				begin = num;
				continue;
			}
			break;
		}
	}

	public static PathD RamerDouglasPeucker(PathD path, double epsilon)
	{
		int count = path.Count;
		if (count < 5)
		{
			return path;
		}
		List<bool> list = new List<bool>(new bool[count])
		{
			[0] = true,
			[count - 1] = true
		};
		RDP(path, 0, count - 1, Sqr(epsilon), list);
		PathD pathD = new PathD(count);
		for (int i = 0; i < count; i++)
		{
			if (list[i])
			{
				pathD.Add(path[i]);
			}
		}
		return pathD;
	}

	public static PathsD RamerDouglasPeucker(PathsD paths, double epsilon)
	{
		PathsD pathsD = new PathsD(paths.Count);
		foreach (PathD path in paths)
		{
			pathsD.Add(RamerDouglasPeucker(path, epsilon));
		}
		return pathsD;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int GetNext(int current, int high, ref bool[] flags)
	{
		current++;
		while (current <= high && flags[current])
		{
			current++;
		}
		if (current <= high)
		{
			return current;
		}
		current = 0;
		while (flags[current])
		{
			current++;
		}
		return current;
	}

	private static int GetPrior(int current, int high, ref bool[] flags)
	{
		current = ((current != 0) ? (current - 1) : high);
		while (current > 0 && flags[current])
		{
			current--;
		}
		if (!flags[current])
		{
			return current;
		}
		current = high;
		while (flags[current])
		{
			current--;
		}
		return current;
	}

	public static Path64 SimplifyPath(Path64 path, double epsilon, bool isClosedPath = true)
	{
		int count = path.Count;
		int num = count - 1;
		double num2 = Sqr(epsilon);
		if (count < 4)
		{
			return path;
		}
		bool[] flags = new bool[count];
		double[] array = new double[count];
		int num3 = 0;
		if (isClosedPath)
		{
			array[0] = PerpendicDistFromLineSqrd(path[0], path[num], path[1]);
			array[num] = PerpendicDistFromLineSqrd(path[num], path[0], path[num - 1]);
		}
		else
		{
			array[0] = double.MaxValue;
			array[num] = double.MaxValue;
		}
		for (int i = 1; i < num; i++)
		{
			array[i] = PerpendicDistFromLineSqrd(path[i], path[i - 1], path[i + 1]);
		}
		while (true)
		{
			if (array[num3] > num2)
			{
				int num4 = num3;
				do
				{
					num3 = GetNext(num3, num, ref flags);
				}
				while (num3 != num4 && array[num3] > num2);
				if (num3 == num4)
				{
					break;
				}
			}
			int num5 = GetPrior(num3, num, ref flags);
			int next = GetNext(num3, num, ref flags);
			if (next == num5)
			{
				break;
			}
			int index;
			if (array[next] < array[num3])
			{
				index = num5;
				num5 = num3;
				num3 = next;
				next = GetNext(next, num, ref flags);
			}
			else
			{
				index = GetPrior(num5, num, ref flags);
			}
			flags[num3] = true;
			num3 = next;
			next = GetNext(next, num, ref flags);
			if (isClosedPath || (num3 != num && num3 != 0))
			{
				array[num3] = PerpendicDistFromLineSqrd(path[num3], path[num5], path[next]);
			}
			if (isClosedPath || (num5 != 0 && num5 != num))
			{
				array[num5] = PerpendicDistFromLineSqrd(path[num5], path[index], path[num3]);
			}
		}
		Path64 path2 = new Path64(count);
		for (int j = 0; j < count; j++)
		{
			if (!flags[j])
			{
				path2.Add(path[j]);
			}
		}
		return path2;
	}

	public static Paths64 SimplifyPaths(Paths64 paths, double epsilon, bool isClosedPaths = true)
	{
		Paths64 paths2 = new Paths64(paths.Count);
		foreach (Path64 path in paths)
		{
			paths2.Add(SimplifyPath(path, epsilon, isClosedPaths));
		}
		return paths2;
	}

	public static PathD SimplifyPath(PathD path, double epsilon, bool isClosedPath = true)
	{
		int count = path.Count;
		int num = count - 1;
		double num2 = Sqr(epsilon);
		if (count < 4)
		{
			return path;
		}
		bool[] flags = new bool[count];
		double[] array = new double[count];
		int num3 = 0;
		if (isClosedPath)
		{
			array[0] = PerpendicDistFromLineSqrd(path[0], path[num], path[1]);
			array[num] = PerpendicDistFromLineSqrd(path[num], path[0], path[num - 1]);
		}
		else
		{
			array[0] = double.MaxValue;
			array[num] = double.MaxValue;
		}
		for (int i = 1; i < num; i++)
		{
			array[i] = PerpendicDistFromLineSqrd(path[i], path[i - 1], path[i + 1]);
		}
		while (true)
		{
			if (array[num3] > num2)
			{
				int num4 = num3;
				do
				{
					num3 = GetNext(num3, num, ref flags);
				}
				while (num3 != num4 && array[num3] > num2);
				if (num3 == num4)
				{
					break;
				}
			}
			int num5 = GetPrior(num3, num, ref flags);
			int next = GetNext(num3, num, ref flags);
			if (next == num5)
			{
				break;
			}
			int index;
			if (array[next] < array[num3])
			{
				index = num5;
				num5 = num3;
				num3 = next;
				next = GetNext(next, num, ref flags);
			}
			else
			{
				index = GetPrior(num5, num, ref flags);
			}
			flags[num3] = true;
			num3 = next;
			next = GetNext(next, num, ref flags);
			if (isClosedPath || (num3 != num && num3 != 0))
			{
				array[num3] = PerpendicDistFromLineSqrd(path[num3], path[num5], path[next]);
			}
			if (isClosedPath || (num5 != 0 && num5 != num))
			{
				array[num5] = PerpendicDistFromLineSqrd(path[num5], path[index], path[num3]);
			}
		}
		PathD pathD = new PathD(count);
		for (int j = 0; j < count; j++)
		{
			if (!flags[j])
			{
				pathD.Add(path[j]);
			}
		}
		return pathD;
	}

	public static PathsD SimplifyPaths(PathsD paths, double epsilon, bool isClosedPath = true)
	{
		PathsD pathsD = new PathsD(paths.Count);
		foreach (PathD path in paths)
		{
			pathsD.Add(SimplifyPath(path, epsilon, isClosedPath));
		}
		return pathsD;
	}

	public static Path64 TrimCollinear(Path64 path, bool isOpen = false)
	{
		int num = path.Count;
		int i = 0;
		if (!isOpen)
		{
			for (; i < num - 1 && InternalClipper.IsCollinear(path[num - 1], path[i], path[i + 1]); i++)
			{
			}
			while (i < num - 1 && InternalClipper.IsCollinear(path[num - 2], path[num - 1], path[i]))
			{
				num--;
			}
		}
		if (num - i < 3)
		{
			if (!isOpen || num < 2 || path[0] == path[1])
			{
				return new Path64();
			}
			return path;
		}
		Path64 path2 = new Path64(num - i);
		Point64 point = path[i];
		path2.Add(point);
		for (i++; i < num - 1; i++)
		{
			if (!InternalClipper.IsCollinear(point, path[i], path[i + 1]))
			{
				point = path[i];
				path2.Add(point);
			}
		}
		if (isOpen)
		{
			path2.Add(path[num - 1]);
		}
		else if (!InternalClipper.IsCollinear(point, path[num - 1], path2[0]))
		{
			path2.Add(path[num - 1]);
		}
		else
		{
			while (path2.Count > 2 && InternalClipper.IsCollinear(path2[path2.Count - 1], path2[path2.Count - 2], path2[0]))
			{
				path2.RemoveAt(path2.Count - 1);
			}
			if (path2.Count < 3)
			{
				path2.Clear();
			}
		}
		return path2;
	}

	public static PathD TrimCollinear(PathD path, int precision, bool isOpen = false)
	{
		InternalClipper.CheckPrecision(precision);
		double num = Math.Pow(10.0, precision);
		return ScalePathD(TrimCollinear(ScalePath64(path, num), isOpen), 1.0 / num);
	}

	public static PointInPolygonResult PointInPolygon(Point64 pt, Path64 polygon)
	{
		return InternalClipper.PointInPolygon(pt, polygon);
	}

	public static PointInPolygonResult PointInPolygon(Point pt, PathD polygon, int precision = 2)
	{
		InternalClipper.CheckPrecision(precision);
		double scale = Math.Pow(10.0, precision);
		Point64 pt2 = new Point64(pt, scale);
		Path64 polygon2 = ScalePath64(polygon, scale);
		return InternalClipper.PointInPolygon(pt2, polygon2);
	}

	public static Path64 Ellipse(Point64 center, double radiusX, double radiusY = 0.0, int steps = 0)
	{
		if (radiusX <= 0.0)
		{
			return new Path64();
		}
		if (radiusY <= 0.0)
		{
			radiusY = radiusX;
		}
		if (steps <= 2)
		{
			steps = (int)Math.Ceiling(Math.PI * Math.Sqrt((radiusX + radiusY) / 2.0));
		}
		double num = Math.Sin(Math.PI * 2.0 / (double)steps);
		double num2 = Math.Cos(Math.PI * 2.0 / (double)steps);
		double num3 = num2;
		double num4 = num;
		Path64 path = new Path64(steps)
		{
			new Point64((double)center.X + radiusX, center.Y)
		};
		for (int i = 1; i < steps; i++)
		{
			path.Add(new Point64((double)center.X + radiusX * num3, (double)center.Y + radiusY * num4));
			double num5 = num3 * num2 - num4 * num;
			num4 = num4 * num2 + num3 * num;
			num3 = num5;
		}
		return path;
	}

	public static PathD Ellipse(Point center, double radiusX, double radiusY = 0.0, int steps = 0)
	{
		if (radiusX <= 0.0)
		{
			return new PathD();
		}
		if (radiusY <= 0.0)
		{
			radiusY = radiusX;
		}
		if (steps <= 2)
		{
			steps = (int)Math.Ceiling(Math.PI * Math.Sqrt((radiusX + radiusY) / 2.0));
		}
		double num = Math.Sin(Math.PI * 2.0 / (double)steps);
		double num2 = Math.Cos(Math.PI * 2.0 / (double)steps);
		double num3 = num2;
		double num4 = num;
		PathD pathD = new PathD(steps)
		{
			new Point(center.X + radiusX, center.Y)
		};
		for (int i = 1; i < steps; i++)
		{
			pathD.Add(new Point(center.X + radiusX * num3, center.Y + radiusY * num4));
			double num5 = num3 * num2 - num4 * num;
			num4 = num4 * num2 + num3 * num;
			num3 = num5;
		}
		return pathD;
	}

	private static void ShowPolyPathStructure(PolyPath64 pp, int level)
	{
		string text = new string(' ', level * 2);
		string text2 = (pp.IsHole ? "Hole " : "Outer ");
		if (pp.Count == 0)
		{
			Console.WriteLine(text + text2);
			return;
		}
		Console.WriteLine(text + text2 + $"({pp.Count})");
		foreach (PolyPath64 item in pp)
		{
			ShowPolyPathStructure(item, level + 1);
		}
	}

	public static void ShowPolyTreeStructure(PolyTree64 polytree)
	{
		Console.WriteLine("Polytree Root");
		foreach (PolyPath64 item in polytree)
		{
			ShowPolyPathStructure(item, 1);
		}
	}

	private static void ShowPolyPathStructure(PolyPathD pp, int level)
	{
		string text = new string(' ', level * 2);
		string text2 = (pp.IsHole ? "Hole " : "Outer ");
		if (pp.Count == 0)
		{
			Console.WriteLine(text + text2);
			return;
		}
		Console.WriteLine(text + text2 + $"({pp.Count})");
		foreach (PolyPathD item in pp)
		{
			ShowPolyPathStructure(item, level + 1);
		}
	}

	public static void ShowPolyTreeStructure(PolyTreeD polytree)
	{
		Console.WriteLine("Polytree Root");
		foreach (PolyPathD item in polytree)
		{
			ShowPolyPathStructure(item, 1);
		}
	}
}
