using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Geospatial.Vectors;

namespace Geospatial.Clipper2Lib;

public class ClipperOffset
{
	private class Group
	{
		internal Paths64 inPaths;

		internal JoinType joinType;

		internal EndType endType;

		internal bool pathsReversed;

		internal int lowestPathIdx;

		public Group(Paths64 paths, JoinType joinType, EndType endType = EndType.Polygon)
		{
			this.joinType = joinType;
			this.endType = endType;
			bool isClosedPath = endType == EndType.Polygon || endType == EndType.Joined;
			inPaths = new Paths64(paths.Count);
			foreach (Path64 path in paths)
			{
				inPaths.Add(Clipper.StripDuplicates(path, isClosedPath));
			}
			if (endType == EndType.Polygon)
			{
				GetLowestPathInfo(inPaths, out lowestPathIdx, out var isNegArea);
				pathsReversed = (lowestPathIdx >= 0) & isNegArea;
			}
			else
			{
				lowestPathIdx = -1;
				pathsReversed = false;
			}
		}
	}

	public delegate double DeltaCallback64(Path64 path, PathD path_norms, int currPt, int prevPt);

	private const double Tolerance = 1E-12;

	private const double arc_const = 0.002;

	private readonly List<Group> _groupList = new List<Group>();

	private Path64 pathOut = new Path64();

	private readonly PathD _normals = new PathD();

	private Paths64 _solution = new Paths64();

	private PolyTree64 _solutionTree;

	private double _groupDelta;

	private double _delta;

	private double _mitLimSqr;

	private double _stepsPerRad;

	private double _stepSin;

	private double _stepCos;

	private JoinType _joinType;

	private EndType _endType;

	public double ArcTolerance { get; set; }

	public bool MergeGroups { get; set; }

	public double MiterLimit { get; set; }

	public bool PreserveCollinear { get; set; }

	public bool ReverseSolution { get; set; }

	public DeltaCallback64 DeltaCallback { get; set; }

	public ClipperOffset(double miterLimit = 2.0, double arcTolerance = 0.0, bool preserveCollinear = false, bool reverseSolution = false)
	{
		MiterLimit = miterLimit;
		ArcTolerance = arcTolerance;
		MergeGroups = true;
		PreserveCollinear = preserveCollinear;
		ReverseSolution = reverseSolution;
	}

	public void Clear()
	{
		_groupList.Clear();
	}

	public void AddPath(Path64 path, JoinType joinType, EndType endType)
	{
		if (path.Count != 0)
		{
			Paths64 paths = new Paths64(1) { path };
			AddPaths(paths, joinType, endType);
		}
	}

	public void AddPaths(Paths64 paths, JoinType joinType, EndType endType)
	{
		if (paths.Count != 0)
		{
			_groupList.Add(new Group(paths, joinType, endType));
		}
	}

	private int CalcSolutionCapacity()
	{
		int num = 0;
		foreach (Group group in _groupList)
		{
			num += ((group.endType == EndType.Joined) ? (group.inPaths.Count * 2) : group.inPaths.Count);
		}
		return num;
	}

	internal bool CheckPathsReversed()
	{
		bool result = false;
		foreach (Group group in _groupList)
		{
			if (group.endType == EndType.Polygon)
			{
				result = group.pathsReversed;
				break;
			}
		}
		return result;
	}

	private void ExecuteInternal(double delta)
	{
		if (_groupList.Count == 0)
		{
			return;
		}
		_solution.EnsureCapacity(CalcSolutionCapacity());
		if (Math.Abs(delta) < 0.5)
		{
			foreach (Group group in _groupList)
			{
				foreach (Path64 inPath in group.inPaths)
				{
					_solution.Add(inPath);
				}
			}
			return;
		}
		_delta = delta;
		_mitLimSqr = ((MiterLimit <= 1.0) ? 2.0 : (2.0 / Clipper.Sqr(MiterLimit)));
		foreach (Group group2 in _groupList)
		{
			DoGroupOffset(group2);
		}
		if (_groupList.Count != 0)
		{
			bool flag = CheckPathsReversed();
			FillRule fillRule = (flag ? FillRule.Negative : FillRule.Positive);
			Clipper64 clipper = new Clipper64();
			clipper.PreserveCollinear = PreserveCollinear;
			clipper.ReverseSolution = ReverseSolution != flag;
			clipper.AddSubject(_solution);
			if (_solutionTree != null)
			{
				clipper.Execute(ClipType.Union, fillRule, _solutionTree);
			}
			else
			{
				clipper.Execute(ClipType.Union, fillRule, _solution);
			}
		}
	}

	public void Execute(double delta, Paths64 solution)
	{
		solution.Clear();
		_solution = solution;
		ExecuteInternal(delta);
	}

	public void Execute(double delta, PolyTree64 solutionTree)
	{
		solutionTree.Clear();
		_solutionTree = solutionTree;
		_solution.Clear();
		ExecuteInternal(delta);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static Point GetUnitNormal(Point64 pt1, Point64 pt2)
	{
		double num = pt2.X - pt1.X;
		double num2 = pt2.Y - pt1.Y;
		if (num == 0.0 && num2 == 0.0)
		{
			return default;
		}
		double num3 = 1.0 / Math.Sqrt(num * num + num2 * num2);
		num *= num3;
		num2 *= num3;
		return new Point(num2, 0.0 - num);
	}

	public void Execute(DeltaCallback64 deltaCallback, Paths64 solution)
	{
		DeltaCallback = deltaCallback;
		Execute(1.0, solution);
	}

	internal static void GetLowestPathInfo(Paths64 paths, out int idx, out bool isNegArea)
	{
		idx = -1;
		isNegArea = false;
		Point64 point = new Point64(long.MaxValue, long.MinValue);
		for (int i = 0; i < paths.Count; i++)
		{
			double num = double.MaxValue;
			foreach (Point64 item in paths[i])
			{
				if (item.Y < point.Y || (item.Y == point.Y && item.X >= point.X))
				{
					continue;
				}
				if (num == double.MaxValue)
				{
					num = Clipper.Area(paths[i]);
					if (num == 0.0)
					{
						break;
					}
					isNegArea = num < 0.0;
				}
				idx = i;
				point.X = item.X;
				point.Y = item.Y;
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Point TranslatePoint(Point pt, double dx, double dy)
	{
		return new Point(pt.X + dx, pt.Y + dy);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Point ReflectPoint(Point pt, Point pivot)
	{
		return new Point(pivot.X + (pivot.X - pt.X), pivot.Y + (pivot.Y - pt.Y));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool AlmostZero(double value, double epsilon = 0.001)
	{
		return Math.Abs(value) < epsilon;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static double Hypotenuse(double x, double y)
	{
		return Math.Sqrt(Math.Pow(x, 2.0) + Math.Pow(y, 2.0));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Point NormalizeVector(Point vec)
	{
		double num = Hypotenuse(vec.X, vec.Y);
		if (AlmostZero(num))
		{
			return new Point(0.0, 0.0);
		}
		double num2 = 1.0 / num;
		return new Point(vec.X * num2, vec.Y * num2);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Point GetAvgUnitVector(Point vec1, Point vec2)
	{
		return NormalizeVector(new Point(vec1.X + vec2.X, vec1.Y + vec2.Y));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Point IntersectPoint(Point pt1a, Point pt1b, Point pt2a, Point pt2b)
	{
		if (InternalClipper.IsAlmostZero(pt1a.X - pt1b.X))
		{
			if (InternalClipper.IsAlmostZero(pt2a.X - pt2b.X))
			{
				return new Point(0.0, 0.0);
			}
			double num = (pt2b.Y - pt2a.Y) / (pt2b.X - pt2a.X);
			double num2 = pt2a.Y - num * pt2a.X;
			return new Point(pt1a.X, num * pt1a.X + num2);
		}
		if (InternalClipper.IsAlmostZero(pt2a.X - pt2b.X))
		{
			double num3 = (pt1b.Y - pt1a.Y) / (pt1b.X - pt1a.X);
			double num4 = pt1a.Y - num3 * pt1a.X;
			return new Point(pt2a.X, num3 * pt2a.X + num4);
		}
		double num5 = (pt1b.Y - pt1a.Y) / (pt1b.X - pt1a.X);
		double num6 = pt1a.Y - num5 * pt1a.X;
		double num7 = (pt2b.Y - pt2a.Y) / (pt2b.X - pt2a.X);
		double num8 = pt2a.Y - num7 * pt2a.X;
		if (InternalClipper.IsAlmostZero(num5 - num7))
		{
			return new Point(0.0, 0.0);
		}
		double num9 = (num8 - num6) / (num5 - num7);
		return new Point(num9, num5 * num9 + num6);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private Point64 GetPerpendic(Point64 pt, Point norm)
	{
		return new Point64((double)pt.X + norm.X * _groupDelta, (double)pt.Y + norm.Y * _groupDelta);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private Point GetPerpendicD(Point64 pt, Point norm)
	{
		return new Point((double)pt.X + norm.X * _groupDelta, (double)pt.Y + norm.Y * _groupDelta);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void DoBevel(Path64 path, int j, int k)
	{
		Point64 item;
		Point64 item2;
		if (j == k)
		{
			double num = Math.Abs(_groupDelta);
			item = new Point64((double)path[j].X - num * _normals[j].X, (double)path[j].Y - num * _normals[j].Y);
			item2 = new Point64((double)path[j].X + num * _normals[j].X, (double)path[j].Y + num * _normals[j].Y);
		}
		else
		{
			item = new Point64((double)path[j].X + _groupDelta * _normals[k].X, (double)path[j].Y + _groupDelta * _normals[k].Y);
			item2 = new Point64((double)path[j].X + _groupDelta * _normals[j].X, (double)path[j].Y + _groupDelta * _normals[j].Y);
		}
		pathOut.Add(item);
		pathOut.Add(item2);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void DoSquare(Path64 path, int j, int k)
	{
		Point point = ((j != k) ? GetAvgUnitVector(new Point(0.0 - _normals[k].Y, _normals[k].X), new Point(_normals[j].Y, 0.0 - _normals[j].X)) : new Point(_normals[j].Y, 0.0 - _normals[j].X));
		double num = Math.Abs(_groupDelta);
		Point pt = new Point(path[j]);
		pt = TranslatePoint(pt, num * point.X, num * point.Y);
		Point pt1a = TranslatePoint(pt, _groupDelta * point.Y, _groupDelta * (0.0 - point.X));
		Point pt1b = TranslatePoint(pt, _groupDelta * (0.0 - point.Y), _groupDelta * point.X);
		Point perpendicD = GetPerpendicD(path[k], _normals[k]);
		if (j == k)
		{
			Point pt2 = IntersectPoint(pt2b: new Point(perpendicD.X + point.X * _groupDelta, perpendicD.Y + point.Y * _groupDelta), pt1a: pt1a, pt1b: pt1b, pt2a: perpendicD);
			pathOut.Add(new Point64(ReflectPoint(pt2, pt)));
			pathOut.Add(new Point64(pt2));
		}
		else
		{
			Point perpendicD2 = GetPerpendicD(path[j], _normals[k]);
			Point pt3 = IntersectPoint(pt1a, pt1b, perpendicD, perpendicD2);
			pathOut.Add(new Point64(pt3));
			pathOut.Add(new Point64(ReflectPoint(pt3, pt)));
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void DoMiter(Path64 path, int j, int k, double cosA)
	{
		double num = _groupDelta / (cosA + 1.0);
		pathOut.Add(new Point64((double)path[j].X + (_normals[k].X + _normals[j].X) * num, (double)path[j].Y + (_normals[k].Y + _normals[j].Y) * num));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void DoRound(Path64 path, int j, int k, double angle)
	{
		if (DeltaCallback != null)
		{
			double num = Math.Abs(_groupDelta);
			double num2 = ((ArcTolerance > 0.01) ? ArcTolerance : (num * 0.002));
			double num3 = Math.PI / Math.Acos(1.0 - num2 / num);
			_stepSin = Math.Sin(Math.PI * 2.0 / num3);
			_stepCos = Math.Cos(Math.PI * 2.0 / num3);
			if (_groupDelta < 0.0)
			{
				_stepSin = 0.0 - _stepSin;
			}
			_stepsPerRad = num3 / (Math.PI * 2.0);
		}
		Point64 pt = path[j];
		Point point = new Point(_normals[k].X * _groupDelta, _normals[k].Y * _groupDelta);
		if (j == k)
		{
			point.Negate();
		}
		pathOut.Add(new Point64((double)pt.X + point.X, (double)pt.Y + point.Y));
		int num4 = (int)Math.Ceiling(_stepsPerRad * Math.Abs(angle));
		for (int i = 1; i < num4; i++)
		{
			point = new Point(point.X * _stepCos - _stepSin * point.Y, point.X * _stepSin + point.Y * _stepCos);
			pathOut.Add(new Point64((double)pt.X + point.X, (double)pt.Y + point.Y));
		}
		pathOut.Add(GetPerpendic(pt, _normals[j]));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void BuildNormals(Path64 path)
	{
		int count = path.Count;
		_normals.Clear();
		if (count != 0)
		{
			_normals.EnsureCapacity(count);
			for (int i = 0; i < count - 1; i++)
			{
				_normals.Add(GetUnitNormal(path[i], path[i + 1]));
			}
			_normals.Add(GetUnitNormal(path[count - 1], path[0]));
		}
	}

	private void OffsetPoint(Group group, Path64 path, int j, ref int k)
	{
		if (path[j] == path[k])
		{
			k = j;
			return;
		}
		double num = InternalClipper.CrossProduct(_normals[j], _normals[k]);
		double num2 = InternalClipper.DotProduct(_normals[j], _normals[k]);
		if (num > 1.0)
		{
			num = 1.0;
		}
		else if (num < -1.0)
		{
			num = -1.0;
		}
		if (DeltaCallback != null)
		{
			_groupDelta = DeltaCallback(path, _normals, j, k);
			if (group.pathsReversed)
			{
				_groupDelta = 0.0 - _groupDelta;
			}
		}
		if (Math.Abs(_groupDelta) < 1E-12)
		{
			pathOut.Add(path[j]);
			return;
		}
		if (num2 > -0.999 && num * _groupDelta < 0.0)
		{
			pathOut.Add(GetPerpendic(path[j], _normals[k]));
			pathOut.Add(path[j]);
			pathOut.Add(GetPerpendic(path[j], _normals[j]));
		}
		else if (num2 > 0.999 && _joinType != JoinType.Round)
		{
			DoMiter(path, j, k, num2);
		}
		else
		{
			switch (_joinType)
			{
			case JoinType.Miter:
				if (num2 > _mitLimSqr - 1.0)
				{
					DoMiter(path, j, k, num2);
				}
				else
				{
					DoSquare(path, j, k);
				}
				break;
			case JoinType.Round:
				DoRound(path, j, k, Math.Atan2(num, num2));
				break;
			case JoinType.Bevel:
				DoBevel(path, j, k);
				break;
			default:
				DoSquare(path, j, k);
				break;
			}
		}
		k = j;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void OffsetPolygon(Group group, Path64 path)
	{
		pathOut = new Path64();
		int count = path.Count;
		int k = count - 1;
		for (int i = 0; i < count; i++)
		{
			OffsetPoint(group, path, i, ref k);
		}
		_solution.Add(pathOut);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void OffsetOpenJoined(Group group, Path64 path)
	{
		OffsetPolygon(group, path);
		path = Clipper.ReversePath(path);
		BuildNormals(path);
		OffsetPolygon(group, path);
	}

	private void OffsetOpenPath(Group group, Path64 path)
	{
		pathOut = new Path64();
		int num = path.Count - 1;
		if (DeltaCallback != null)
		{
			_groupDelta = DeltaCallback(path, _normals, 0, 0);
		}
		if (Math.Abs(_groupDelta) < 1E-12)
		{
			pathOut.Add(path[0]);
		}
		else
		{
			switch (_endType)
			{
			case EndType.Butt:
				DoBevel(path, 0, 0);
				break;
			case EndType.Round:
				DoRound(path, 0, 0, Math.PI);
				break;
			default:
				DoSquare(path, 0, 0);
				break;
			}
		}
		int i = 1;
		int k = 0;
		for (; i < num; i++)
		{
			OffsetPoint(group, path, i, ref k);
		}
		for (int num2 = num; num2 > 0; num2--)
		{
			_normals[num2] = new Point(0.0 - _normals[num2 - 1].X, 0.0 - _normals[num2 - 1].Y);
		}
		_normals[0] = _normals[num];
		if (DeltaCallback != null)
		{
			_groupDelta = DeltaCallback(path, _normals, num, num);
		}
		if (Math.Abs(_groupDelta) < 1E-12)
		{
			pathOut.Add(path[num]);
		}
		else
		{
			switch (_endType)
			{
			case EndType.Butt:
				DoBevel(path, num, num);
				break;
			case EndType.Round:
				DoRound(path, num, num, Math.PI);
				break;
			default:
				DoSquare(path, num, num);
				break;
			}
		}
		int num3 = num - 1;
		int k2 = num;
		while (num3 > 0)
		{
			OffsetPoint(group, path, num3, ref k2);
			num3--;
		}
		_solution.Add(pathOut);
	}

	private void DoGroupOffset(Group group)
	{
		if (group.endType == EndType.Polygon)
		{
			if (group.lowestPathIdx < 0)
			{
				_delta = Math.Abs(_delta);
			}
			_groupDelta = (group.pathsReversed ? (0.0 - _delta) : _delta);
		}
		else
		{
			_groupDelta = Math.Abs(_delta);
		}
		double num = Math.Abs(_groupDelta);
		_joinType = group.joinType;
		_endType = group.endType;
		if (group.joinType == JoinType.Round || group.endType == EndType.Round)
		{
			double num2 = ((ArcTolerance > 0.01) ? ArcTolerance : (num * 0.002));
			double num3 = Math.PI / Math.Acos(1.0 - num2 / num);
			_stepSin = Math.Sin(Math.PI * 2.0 / num3);
			_stepCos = Math.Cos(Math.PI * 2.0 / num3);
			if (_groupDelta < 0.0)
			{
				_stepSin = 0.0 - _stepSin;
			}
			_stepsPerRad = num3 / (Math.PI * 2.0);
		}
		foreach (Path64 inPath in group.inPaths)
		{
			pathOut = new Path64();
			switch (inPath.Count)
			{
			case 1:
			{
				Point64 center = inPath[0];
				if (DeltaCallback != null)
				{
					_groupDelta = DeltaCallback(inPath, _normals, 0, 0);
					if (group.pathsReversed)
					{
						_groupDelta = 0.0 - _groupDelta;
					}
					num = Math.Abs(_groupDelta);
				}
				if (group.endType == EndType.Round)
				{
					int steps = (int)Math.Ceiling(_stepsPerRad * 2.0 * Math.PI);
					pathOut = Clipper.Ellipse(center, num, num, steps);
				}
				else
				{
					int num4 = (int)Math.Ceiling(_groupDelta);
					pathOut = new Rect64(center.X - num4, center.Y - num4, center.X + num4, center.Y + num4).AsPath();
				}
				_solution.Add(pathOut);
				continue;
			}
			case 2:
				if (group.endType == EndType.Joined)
				{
					_endType = ((group.joinType == JoinType.Round) ? EndType.Round : EndType.Square);
				}
				break;
			}
			BuildNormals(inPath);
			switch (_endType)
			{
			case EndType.Polygon:
				OffsetPolygon(group, inPath);
				break;
			case EndType.Joined:
				OffsetOpenJoined(group, inPath);
				break;
			default:
				OffsetOpenPath(group, inPath);
				break;
			}
		}
	}
}
