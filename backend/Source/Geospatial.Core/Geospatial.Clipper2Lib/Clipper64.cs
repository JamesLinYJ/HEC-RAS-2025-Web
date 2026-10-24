using System.Runtime.CompilerServices;

namespace Geospatial.Clipper2Lib;

public class Clipper64 : ClipperBase
{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal new void AddPath(Path64 path, PathType polytype, bool isOpen = false)
	{
		base.AddPath(path, polytype, isOpen);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public new void AddReuseableData(ReuseableDataContainer64 reuseableData)
	{
		base.AddReuseableData(reuseableData);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal new void AddPaths(Paths64 paths, PathType polytype, bool isOpen = false)
	{
		base.AddPaths(paths, polytype, isOpen);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void AddSubject(Paths64 paths)
	{
		AddPaths(paths, PathType.Subject);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void AddOpenSubject(Paths64 paths)
	{
		AddPaths(paths, PathType.Subject, isOpen: true);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void AddClip(Paths64 paths)
	{
		AddPaths(paths, PathType.Clip);
	}

	public bool Execute(ClipType clipType, FillRule fillRule, Paths64 solutionClosed, Paths64 solutionOpen)
	{
		solutionClosed.Clear();
		solutionOpen.Clear();
		try
		{
			ExecuteInternal(clipType, fillRule);
			BuildPaths(solutionClosed, solutionOpen);
		}
		catch
		{
			_succeeded = false;
		}
		ClearSolutionOnly();
		return _succeeded;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Execute(ClipType clipType, FillRule fillRule, Paths64 solutionClosed)
	{
		return Execute(clipType, fillRule, solutionClosed, new Paths64());
	}

	public bool Execute(ClipType clipType, FillRule fillRule, PolyTree64 polytree, Paths64 openPaths)
	{
		polytree.Clear();
		openPaths.Clear();
		_using_polytree = true;
		try
		{
			ExecuteInternal(clipType, fillRule);
			BuildTree(polytree, openPaths);
		}
		catch
		{
			_succeeded = false;
		}
		ClearSolutionOnly();
		return _succeeded;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Execute(ClipType clipType, FillRule fillRule, PolyTree64 polytree)
	{
		return Execute(clipType, fillRule, polytree, new Paths64());
	}
}
