using System;
using System.Runtime.CompilerServices;

namespace Geospatial.Clipper2Lib;

public class ClipperD : ClipperBase
{
	private const string precision_range_error = "Error: Precision is out of range.";

	private readonly double _scale;

	private readonly double _invScale;

	public ClipperD(int roundingDecimalPrecision = 2)
	{
		if (roundingDecimalPrecision < -8 || roundingDecimalPrecision > 8)
		{
			throw new ClipperLibException("Error: Precision is out of range.");
		}
		_scale = Math.Pow(10.0, roundingDecimalPrecision);
		_invScale = 1.0 / _scale;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void AddPath(PathD path, PathType polytype, bool isOpen = false)
	{
		AddPath(Clipper.ScalePath64(path, _scale), polytype, isOpen);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void AddPaths(PathsD paths, PathType polytype, bool isOpen = false)
	{
		AddPaths(Clipper.ScalePaths64(paths, _scale), polytype, isOpen);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void AddSubject(PathD path)
	{
		AddPath(path, PathType.Subject);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void AddOpenSubject(PathD path)
	{
		AddPath(path, PathType.Subject, isOpen: true);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void AddClip(PathD path)
	{
		AddPath(path, PathType.Clip);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void AddSubject(PathsD paths)
	{
		AddPaths(paths, PathType.Subject);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void AddOpenSubject(PathsD paths)
	{
		AddPaths(paths, PathType.Subject, isOpen: true);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void AddClip(PathsD paths)
	{
		AddPaths(paths, PathType.Clip);
	}

	public bool Execute(ClipType clipType, FillRule fillRule, PathsD solutionClosed, PathsD solutionOpen)
	{
		Paths64 paths = new Paths64();
		Paths64 paths2 = new Paths64();
		bool flag = true;
		solutionClosed.Clear();
		solutionOpen.Clear();
		try
		{
			ExecuteInternal(clipType, fillRule);
			BuildPaths(paths, paths2);
		}
		catch
		{
			flag = false;
		}
		ClearSolutionOnly();
		if (!flag)
		{
			return false;
		}
		solutionClosed.EnsureCapacity(paths.Count);
		foreach (Path64 item in paths)
		{
			solutionClosed.Add(Clipper.ScalePathD(item, _invScale));
		}
		solutionOpen.EnsureCapacity(paths2.Count);
		foreach (Path64 item2 in paths2)
		{
			solutionOpen.Add(Clipper.ScalePathD(item2, _invScale));
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Execute(ClipType clipType, FillRule fillRule, PathsD solutionClosed)
	{
		return Execute(clipType, fillRule, solutionClosed, new PathsD());
	}

	public bool Execute(ClipType clipType, FillRule fillRule, PolyTreeD polytree, PathsD openPaths)
	{
		polytree.Clear();
		openPaths.Clear();
		_using_polytree = true;
		((PolyPathD)polytree).Scale = _scale;
		Paths64 paths = new Paths64();
		bool flag = true;
		try
		{
			ExecuteInternal(clipType, fillRule);
			BuildTree(polytree, paths);
		}
		catch
		{
			flag = false;
		}
		ClearSolutionOnly();
		if (!flag)
		{
			return false;
		}
		if (paths.Count <= 0)
		{
			return true;
		}
		openPaths.EnsureCapacity(paths.Count);
		foreach (Path64 item in paths)
		{
			openPaths.Add(Clipper.ScalePathD(item, _invScale));
		}
		return true;
	}

	public bool Execute(ClipType clipType, FillRule fillRule, PolyTreeD polytree)
	{
		return Execute(clipType, fillRule, polytree, new PathsD());
	}
}
