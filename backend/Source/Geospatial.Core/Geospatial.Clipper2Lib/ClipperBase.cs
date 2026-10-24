using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Geospatial.Clipper2Lib;

public class ClipperBase
{
	[StructLayout(LayoutKind.Sequential, Size = 1)]
	private struct IntersectListSort : IComparer<IntersectNode>
	{
		public readonly int Compare(IntersectNode a, IntersectNode b)
		{
			if (a.pt.Y != b.pt.Y)
			{
				if (a.pt.Y <= b.pt.Y)
				{
					return 1;
				}
				return -1;
			}
			if (a.pt.X == b.pt.X)
			{
				return 0;
			}
			if (a.pt.X >= b.pt.X)
			{
				return 1;
			}
			return -1;
		}
	}

	private ClipType _cliptype;

	private FillRule _fillrule;

	private Active? _actives;

	private Active? _sel;

	private readonly List<LocalMinima> _minimaList;

	private readonly List<IntersectNode> _intersectList;

	private readonly List<Vertex> _vertexList;

	private readonly List<OutRec> _outrecList;

	private readonly List<long> _scanlineList;

	private readonly List<HorzSegment> _horzSegList;

	private readonly List<HorzJoin> _horzJoinList;

	private int _currentLocMin;

	private long _currentBotY;

	private bool _isSortedMinimaList;

	private bool _hasOpenPaths;

	internal bool _using_polytree;

	internal bool _succeeded;

	public bool PreserveCollinear { get; set; }

	public bool ReverseSolution { get; set; }

	public ClipperBase()
	{
		_minimaList = new List<LocalMinima>();
		_intersectList = new List<IntersectNode>();
		_vertexList = new List<Vertex>();
		_outrecList = new List<OutRec>();
		_scanlineList = new List<long>();
		_horzSegList = new List<HorzSegment>();
		_horzJoinList = new List<HorzJoin>();
		PreserveCollinear = true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsOdd(int val)
	{
		return (val & 1) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsHotEdge(Active ae)
	{
		return ae.outrec != null;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsOpen(Active ae)
	{
		return ae.localMin.isOpen;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsOpenEnd(Active ae)
	{
		if (ae.localMin.isOpen)
		{
			return IsOpenEnd(ae.vertexTop);
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsOpenEnd(Vertex v)
	{
		return (v.flags & (VertexFlags.OpenStart | VertexFlags.OpenEnd)) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Active? GetPrevHotEdge(Active ae)
	{
		Active prevInAEL = ae.prevInAEL;
		while (prevInAEL != null && (IsOpen(prevInAEL) || !IsHotEdge(prevInAEL)))
		{
			prevInAEL = prevInAEL.prevInAEL;
		}
		return prevInAEL;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsFront(Active ae)
	{
		return ae == ae.outrec.frontEdge;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static double GetDx(Point64 pt1, Point64 pt2)
	{
		double num = pt2.Y - pt1.Y;
		if (num != 0.0)
		{
			return (double)(pt2.X - pt1.X) / num;
		}
		if (pt2.X <= pt1.X)
		{
			return double.PositiveInfinity;
		}
		return double.NegativeInfinity;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static long TopX(Active ae, long currentY)
	{
		if (currentY == ae.top.Y || ae.top.X == ae.bot.X)
		{
			return ae.top.X;
		}
		if (currentY == ae.bot.Y)
		{
			return ae.bot.X;
		}
		return ae.bot.X + (long)Math.Round(ae.dx * (double)(currentY - ae.bot.Y), MidpointRounding.ToEven);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsHorizontal(Active ae)
	{
		return ae.top.Y == ae.bot.Y;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsHeadingRightHorz(Active ae)
	{
		return double.IsNegativeInfinity(ae.dx);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsHeadingLeftHorz(Active ae)
	{
		return double.IsPositiveInfinity(ae.dx);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void SwapActives(ref Active ae1, ref Active ae2)
	{
		Active active = ae1;
		Active active2 = ae2;
		ae2 = active;
		ae1 = active2;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static PathType GetPolyType(Active ae)
	{
		return ae.localMin.polytype;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsSamePolyType(Active ae1, Active ae2)
	{
		return ae1.localMin.polytype == ae2.localMin.polytype;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void SetDx(Active ae)
	{
		ae.dx = GetDx(ae.bot, ae.top);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Vertex NextVertex(Active ae)
	{
		if (ae.windDx <= 0)
		{
			return ae.vertexTop.prev;
		}
		return ae.vertexTop.next;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Vertex PrevPrevVertex(Active ae)
	{
		if (ae.windDx <= 0)
		{
			return ae.vertexTop.next.next;
		}
		return ae.vertexTop.prev.prev;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsMaxima(Vertex vertex)
	{
		return (vertex.flags & VertexFlags.LocalMax) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsMaxima(Active ae)
	{
		return IsMaxima(ae.vertexTop);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Active? GetMaximaPair(Active ae)
	{
		for (Active nextInAEL = ae.nextInAEL; nextInAEL != null; nextInAEL = nextInAEL.nextInAEL)
		{
			if (nextInAEL.vertexTop == ae.vertexTop)
			{
				return nextInAEL;
			}
		}
		return null;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Vertex? GetCurrYMaximaVertex_Open(Active ae)
	{
		Vertex vertex = ae.vertexTop;
		if (ae.windDx > 0)
		{
			while (vertex.next.pt.Y == vertex.pt.Y && (vertex.flags & (VertexFlags.OpenEnd | VertexFlags.LocalMax)) == 0)
			{
				vertex = vertex.next;
			}
		}
		else
		{
			while (vertex.prev.pt.Y == vertex.pt.Y && (vertex.flags & (VertexFlags.OpenEnd | VertexFlags.LocalMax)) == 0)
			{
				vertex = vertex.prev;
			}
		}
		if (!IsMaxima(vertex))
		{
			vertex = null;
		}
		return vertex;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Vertex? GetCurrYMaximaVertex(Active ae)
	{
		Vertex vertex = ae.vertexTop;
		if (ae.windDx > 0)
		{
			while (vertex.next.pt.Y == vertex.pt.Y)
			{
				vertex = vertex.next;
			}
		}
		else
		{
			while (vertex.prev.pt.Y == vertex.pt.Y)
			{
				vertex = vertex.prev;
			}
		}
		if (!IsMaxima(vertex))
		{
			vertex = null;
		}
		return vertex;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void SetSides(OutRec outrec, Active startEdge, Active endEdge)
	{
		outrec.frontEdge = startEdge;
		outrec.backEdge = endEdge;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void SwapOutrecs(Active ae1, Active ae2)
	{
		OutRec outrec = ae1.outrec;
		OutRec outrec2 = ae2.outrec;
		if (outrec == outrec2)
		{
			Active frontEdge = outrec.frontEdge;
			outrec.frontEdge = outrec.backEdge;
			outrec.backEdge = frontEdge;
			return;
		}
		if (outrec != null)
		{
			if (ae1 == outrec.frontEdge)
			{
				outrec.frontEdge = ae2;
			}
			else
			{
				outrec.backEdge = ae2;
			}
		}
		if (outrec2 != null)
		{
			if (ae2 == outrec2.frontEdge)
			{
				outrec2.frontEdge = ae1;
			}
			else
			{
				outrec2.backEdge = ae1;
			}
		}
		ae1.outrec = outrec2;
		ae2.outrec = outrec;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void SetOwner(OutRec outrec, OutRec newOwner)
	{
		while (newOwner.owner != null && newOwner.owner.pts == null)
		{
			newOwner.owner = newOwner.owner.owner;
		}
		OutRec outRec = newOwner;
		while (outRec != null && outRec != outrec)
		{
			outRec = outRec.owner;
		}
		if (outRec != null)
		{
			newOwner.owner = outrec.owner;
		}
		outrec.owner = newOwner;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static double Area(OutPt op)
	{
		double num = 0.0;
		OutPt outPt = op;
		do
		{
			num += (double)(outPt.prev.pt.Y + outPt.pt.Y) * (double)(outPt.prev.pt.X - outPt.pt.X);
			outPt = outPt.next;
		}
		while (outPt != op);
		return num * 0.5;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static double AreaTriangle(Point64 pt1, Point64 pt2, Point64 pt3)
	{
		return (double)(pt3.Y + pt1.Y) * (double)(pt3.X - pt1.X) + (double)(pt1.Y + pt2.Y) * (double)(pt1.X - pt2.X) + (double)(pt2.Y + pt3.Y) * (double)(pt2.X - pt3.X);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static OutRec? GetRealOutRec(OutRec? outRec)
	{
		while (outRec != null && outRec.pts == null)
		{
			outRec = outRec.owner;
		}
		return outRec;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsValidOwner(OutRec? outRec, OutRec? testOwner)
	{
		while (testOwner != null && testOwner != outRec)
		{
			testOwner = testOwner.owner;
		}
		return testOwner == null;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void UncoupleOutRec(Active ae)
	{
		OutRec outrec = ae.outrec;
		if (outrec != null)
		{
			outrec.frontEdge.outrec = null;
			outrec.backEdge.outrec = null;
			outrec.frontEdge = null;
			outrec.backEdge = null;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool OutrecIsAscending(Active hotEdge)
	{
		return hotEdge == hotEdge.outrec.frontEdge;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void SwapFrontBackSides(OutRec outrec)
	{
		Active frontEdge = outrec.frontEdge;
		outrec.frontEdge = outrec.backEdge;
		outrec.backEdge = frontEdge;
		outrec.pts = outrec.pts.next;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool EdgesAdjacentInAEL(IntersectNode inode)
	{
		if (inode.edge1.nextInAEL != inode.edge2)
		{
			return inode.edge1.prevInAEL == inode.edge2;
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	protected void ClearSolutionOnly()
	{
		while (_actives != null)
		{
			DeleteFromAEL(_actives);
		}
		_scanlineList.Clear();
		DisposeIntersectNodes();
		_outrecList.Clear();
		_horzSegList.Clear();
		_horzJoinList.Clear();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Clear()
	{
		ClearSolutionOnly();
		_minimaList.Clear();
		_vertexList.Clear();
		_currentLocMin = 0;
		_isSortedMinimaList = false;
		_hasOpenPaths = false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	protected void Reset()
	{
		if (!_isSortedMinimaList)
		{
			_minimaList.Sort(default(LocMinSorter));
			_isSortedMinimaList = true;
		}
		_scanlineList.EnsureCapacity(_minimaList.Count);
		for (int num = _minimaList.Count - 1; num >= 0; num--)
		{
			_scanlineList.Add(_minimaList[num].vertex.pt.Y);
		}
		_currentBotY = 0L;
		_currentLocMin = 0;
		_actives = null;
		_sel = null;
		_succeeded = true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void InsertScanline(long y)
	{
		int num = _scanlineList.BinarySearch(y);
		if (num < 0)
		{
			num = ~num;
			_scanlineList.Insert(num, y);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool PopScanline(out long y)
	{
		int num = _scanlineList.Count - 1;
		if (num < 0)
		{
			y = 0L;
			return false;
		}
		y = _scanlineList[num];
		_scanlineList.RemoveAt(num--);
		while (num >= 0 && y == _scanlineList[num])
		{
			_scanlineList.RemoveAt(num--);
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool HasLocMinAtY(long y)
	{
		if (_currentLocMin < _minimaList.Count)
		{
			return _minimaList[_currentLocMin].vertex.pt.Y == y;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private LocalMinima PopLocalMinima()
	{
		return _minimaList[_currentLocMin++];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void AddSubject(Path64 path)
	{
		AddPath(path, PathType.Subject);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void AddOpenSubject(Path64 path)
	{
		AddPath(path, PathType.Subject, isOpen: true);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void AddClip(Path64 path)
	{
		AddPath(path, PathType.Clip);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	protected void AddPath(Path64 path, PathType polytype, bool isOpen = false)
	{
		Paths64 paths = new Paths64(1) { path };
		AddPaths(paths, polytype, isOpen);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	protected void AddPaths(Paths64 paths, PathType polytype, bool isOpen = false)
	{
		if (isOpen)
		{
			_hasOpenPaths = true;
		}
		_isSortedMinimaList = false;
		ClipperEngine.AddPathsToVertexList(paths, polytype, isOpen, _minimaList, _vertexList);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	protected void AddReuseableData(ReuseableDataContainer64 reuseableData)
	{
		if (reuseableData._minimaList.Count == 0)
		{
			return;
		}
		_isSortedMinimaList = false;
		foreach (LocalMinima minima in reuseableData._minimaList)
		{
			_minimaList.Add(new LocalMinima(minima.vertex, minima.polytype, minima.isOpen));
			if (minima.isOpen)
			{
				_hasOpenPaths = true;
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool IsContributingClosed(Active ae)
	{
		switch (_fillrule)
		{
		case FillRule.Positive:
			if (ae.windCount != 1)
			{
				return false;
			}
			break;
		case FillRule.Negative:
			if (ae.windCount != -1)
			{
				return false;
			}
			break;
		case FillRule.NonZero:
			if (Math.Abs(ae.windCount) != 1)
			{
				return false;
			}
			break;
		}
		switch (_cliptype)
		{
		case ClipType.Intersection:
			return _fillrule switch
			{
				FillRule.Positive => ae.windCount2 > 0, 
				FillRule.Negative => ae.windCount2 < 0, 
				_ => ae.windCount2 != 0, 
			};
		case ClipType.Union:
			return _fillrule switch
			{
				FillRule.Positive => ae.windCount2 <= 0, 
				FillRule.Negative => ae.windCount2 >= 0, 
				_ => ae.windCount2 == 0, 
			};
		case ClipType.Difference:
		{
			bool flag = _fillrule switch
			{
				FillRule.Positive => ae.windCount2 <= 0, 
				FillRule.Negative => ae.windCount2 >= 0, 
				_ => ae.windCount2 == 0, 
			};
			if (GetPolyType(ae) != PathType.Subject)
			{
				return !flag;
			}
			return flag;
		}
		case ClipType.Xor:
			return true;
		default:
			return false;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool IsContributingOpen(Active ae)
	{
		bool flag;
		bool flag2;
		switch (_fillrule)
		{
		case FillRule.Positive:
			flag = ae.windCount > 0;
			flag2 = ae.windCount2 > 0;
			break;
		case FillRule.Negative:
			flag = ae.windCount < 0;
			flag2 = ae.windCount2 < 0;
			break;
		default:
			flag = ae.windCount != 0;
			flag2 = ae.windCount2 != 0;
			break;
		}
		return _cliptype switch
		{
			ClipType.Intersection => flag2, 
			ClipType.Union => !flag && !flag2, 
			_ => !flag2, 
		};
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void SetWindCountForClosedPathEdge(Active ae)
	{
		Active prevInAEL = ae.prevInAEL;
		PathType polyType = GetPolyType(ae);
		while (prevInAEL != null && (GetPolyType(prevInAEL) != polyType || IsOpen(prevInAEL)))
		{
			prevInAEL = prevInAEL.prevInAEL;
		}
		if (prevInAEL == null)
		{
			ae.windCount = ae.windDx;
			prevInAEL = _actives;
		}
		else if (_fillrule == FillRule.EvenOdd)
		{
			ae.windCount = ae.windDx;
			ae.windCount2 = prevInAEL.windCount2;
			prevInAEL = prevInAEL.nextInAEL;
		}
		else
		{
			if (prevInAEL.windCount * prevInAEL.windDx < 0)
			{
				if (Math.Abs(prevInAEL.windCount) > 1)
				{
					if (prevInAEL.windDx * ae.windDx < 0)
					{
						ae.windCount = prevInAEL.windCount;
					}
					else
					{
						ae.windCount = prevInAEL.windCount + ae.windDx;
					}
				}
				else
				{
					ae.windCount = (IsOpen(ae) ? 1 : ae.windDx);
				}
			}
			else if (prevInAEL.windDx * ae.windDx < 0)
			{
				ae.windCount = prevInAEL.windCount;
			}
			else
			{
				ae.windCount = prevInAEL.windCount + ae.windDx;
			}
			ae.windCount2 = prevInAEL.windCount2;
			prevInAEL = prevInAEL.nextInAEL;
		}
		if (_fillrule == FillRule.EvenOdd)
		{
			while (prevInAEL != ae)
			{
				if (GetPolyType(prevInAEL) != polyType && !IsOpen(prevInAEL))
				{
					ae.windCount2 = ((ae.windCount2 == 0) ? 1 : 0);
				}
				prevInAEL = prevInAEL.nextInAEL;
			}
			return;
		}
		while (prevInAEL != ae)
		{
			if (GetPolyType(prevInAEL) != polyType && !IsOpen(prevInAEL))
			{
				ae.windCount2 += prevInAEL.windDx;
			}
			prevInAEL = prevInAEL.nextInAEL;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void SetWindCountForOpenPathEdge(Active ae)
	{
		Active active = _actives;
		if (_fillrule == FillRule.EvenOdd)
		{
			int num = 0;
			int num2 = 0;
			while (active != ae)
			{
				if (GetPolyType(active) == PathType.Clip)
				{
					num2++;
				}
				else if (!IsOpen(active))
				{
					num++;
				}
				active = active.nextInAEL;
			}
			ae.windCount = (IsOdd(num) ? 1 : 0);
			ae.windCount2 = (IsOdd(num2) ? 1 : 0);
			return;
		}
		while (active != ae)
		{
			if (GetPolyType(active) == PathType.Clip)
			{
				ae.windCount2 += active.windDx;
			}
			else if (!IsOpen(active))
			{
				ae.windCount += active.windDx;
			}
			active = active.nextInAEL;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsValidAelOrder(Active resident, Active newcomer)
	{
		if (newcomer.curX != resident.curX)
		{
			return newcomer.curX > resident.curX;
		}
		double num = InternalClipper.CrossProduct(resident.top, newcomer.bot, newcomer.top);
		if (num != 0.0)
		{
			return num < 0.0;
		}
		if (!IsMaxima(resident) && resident.top.Y > newcomer.top.Y)
		{
			return InternalClipper.CrossProduct(newcomer.bot, resident.top, NextVertex(resident).pt) <= 0.0;
		}
		if (!IsMaxima(newcomer) && newcomer.top.Y > resident.top.Y)
		{
			return InternalClipper.CrossProduct(newcomer.bot, newcomer.top, NextVertex(newcomer).pt) >= 0.0;
		}
		long y = newcomer.bot.Y;
		bool isLeftBound = newcomer.isLeftBound;
		if (resident.bot.Y != y || resident.localMin.vertex.pt.Y != y)
		{
			return newcomer.isLeftBound;
		}
		if (resident.isLeftBound != isLeftBound)
		{
			return isLeftBound;
		}
		if (InternalClipper.IsCollinear(PrevPrevVertex(resident).pt, resident.bot, resident.top))
		{
			return true;
		}
		return InternalClipper.CrossProduct(PrevPrevVertex(resident).pt, newcomer.bot, PrevPrevVertex(newcomer).pt) > 0.0 == isLeftBound;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void InsertLeftEdge(Active ae)
	{
		if (_actives == null)
		{
			ae.prevInAEL = null;
			ae.nextInAEL = null;
			_actives = ae;
			return;
		}
		if (!IsValidAelOrder(_actives, ae))
		{
			ae.prevInAEL = null;
			ae.nextInAEL = _actives;
			_actives.prevInAEL = ae;
			_actives = ae;
			return;
		}
		Active active = _actives;
		while (active.nextInAEL != null && IsValidAelOrder(active.nextInAEL, ae))
		{
			active = active.nextInAEL;
		}
		if (active.joinWith == JoinWith.Right)
		{
			active = active.nextInAEL;
		}
		ae.nextInAEL = active.nextInAEL;
		if (active.nextInAEL != null)
		{
			active.nextInAEL.prevInAEL = ae;
		}
		ae.prevInAEL = active;
		active.nextInAEL = ae;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void InsertRightEdge(Active ae, Active ae2)
	{
		ae2.nextInAEL = ae.nextInAEL;
		if (ae.nextInAEL != null)
		{
			ae.nextInAEL.prevInAEL = ae2;
		}
		ae2.prevInAEL = ae;
		ae.nextInAEL = ae2;
	}

	private void InsertLocalMinimaIntoAEL(long botY)
	{
		while (HasLocMinAtY(botY))
		{
			LocalMinima localMin = PopLocalMinima();
			Active ae;
			if ((localMin.vertex.flags & VertexFlags.OpenStart) != 0)
			{
				ae = null;
			}
			else
			{
				ae = new Active
				{
					bot = localMin.vertex.pt,
					curX = localMin.vertex.pt.X,
					windDx = -1,
					vertexTop = localMin.vertex.prev,
					top = localMin.vertex.prev.pt,
					outrec = null,
					localMin = localMin
				};
				SetDx(ae);
			}
			Active ae2;
			if ((localMin.vertex.flags & VertexFlags.OpenEnd) != 0)
			{
				ae2 = null;
			}
			else
			{
				ae2 = new Active
				{
					bot = localMin.vertex.pt,
					curX = localMin.vertex.pt.X,
					windDx = 1,
					vertexTop = localMin.vertex.next,
					top = localMin.vertex.next.pt,
					outrec = null,
					localMin = localMin
				};
				SetDx(ae2);
			}
			if (ae != null && ae2 != null)
			{
				if (IsHorizontal(ae))
				{
					if (IsHeadingRightHorz(ae))
					{
						SwapActives(ref ae, ref ae2);
					}
				}
				else if (IsHorizontal(ae2))
				{
					if (IsHeadingLeftHorz(ae2))
					{
						SwapActives(ref ae, ref ae2);
					}
				}
				else if (ae.dx < ae2.dx)
				{
					SwapActives(ref ae, ref ae2);
				}
			}
			else if (ae == null)
			{
				ae = ae2;
				ae2 = null;
			}
			ae.isLeftBound = true;
			InsertLeftEdge(ae);
			bool flag;
			if (IsOpen(ae))
			{
				SetWindCountForOpenPathEdge(ae);
				flag = IsContributingOpen(ae);
			}
			else
			{
				SetWindCountForClosedPathEdge(ae);
				flag = IsContributingClosed(ae);
			}
			if (ae2 != null)
			{
				ae2.windCount = ae.windCount;
				ae2.windCount2 = ae.windCount2;
				InsertRightEdge(ae, ae2);
				if (flag)
				{
					AddLocalMinPoly(ae, ae2, ae.bot, isNew: true);
					if (!IsHorizontal(ae))
					{
						CheckJoinLeft(ae, ae.bot);
					}
				}
				while (ae2.nextInAEL != null && IsValidAelOrder(ae2.nextInAEL, ae2))
				{
					IntersectEdges(ae2, ae2.nextInAEL, ae2.bot);
					SwapPositionsInAEL(ae2, ae2.nextInAEL);
				}
				if (IsHorizontal(ae2))
				{
					PushHorz(ae2);
				}
				else
				{
					CheckJoinRight(ae2, ae2.bot);
					InsertScanline(ae2.top.Y);
				}
			}
			else if (flag)
			{
				StartOpenPath(ae, ae.bot);
			}
			if (IsHorizontal(ae))
			{
				PushHorz(ae);
			}
			else
			{
				InsertScanline(ae.top.Y);
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void PushHorz(Active ae)
	{
		ae.nextInSEL = _sel;
		_sel = ae;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool PopHorz(out Active? ae)
	{
		ae = _sel;
		if (_sel == null)
		{
			return false;
		}
		_sel = _sel.nextInSEL;
		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private OutPt AddLocalMinPoly(Active ae1, Active ae2, Point64 pt, bool isNew = false)
	{
		OutRec outRec = (ae2.outrec = (ae1.outrec = NewOutRec()));
		if (IsOpen(ae1))
		{
			outRec.owner = null;
			outRec.isOpen = true;
			if (ae1.windDx > 0)
			{
				SetSides(outRec, ae1, ae2);
			}
			else
			{
				SetSides(outRec, ae2, ae1);
			}
		}
		else
		{
			outRec.isOpen = false;
			Active prevHotEdge = GetPrevHotEdge(ae1);
			if (prevHotEdge != null)
			{
				if (_using_polytree)
				{
					SetOwner(outRec, prevHotEdge.outrec);
				}
				outRec.owner = prevHotEdge.outrec;
				if (OutrecIsAscending(prevHotEdge) == isNew)
				{
					SetSides(outRec, ae2, ae1);
				}
				else
				{
					SetSides(outRec, ae1, ae2);
				}
			}
			else
			{
				outRec.owner = null;
				if (isNew)
				{
					SetSides(outRec, ae1, ae2);
				}
				else
				{
					SetSides(outRec, ae2, ae1);
				}
			}
		}
		return outRec.pts = new OutPt(pt, outRec);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private OutPt? AddLocalMaxPoly(Active ae1, Active ae2, Point64 pt)
	{
		if (IsJoined(ae1))
		{
			Split(ae1, pt);
		}
		if (IsJoined(ae2))
		{
			Split(ae2, pt);
		}
		if (IsFront(ae1) == IsFront(ae2))
		{
			if (IsOpenEnd(ae1))
			{
				SwapFrontBackSides(ae1.outrec);
			}
			else
			{
				if (!IsOpenEnd(ae2))
				{
					_succeeded = false;
					return null;
				}
				SwapFrontBackSides(ae2.outrec);
			}
		}
		OutPt outPt = AddOutPt(ae1, pt);
		if (ae1.outrec == ae2.outrec)
		{
			OutRec outrec = ae1.outrec;
			outrec.pts = outPt;
			if (_using_polytree)
			{
				Active prevHotEdge = GetPrevHotEdge(ae1);
				if (prevHotEdge == null)
				{
					outrec.owner = null;
				}
				else
				{
					SetOwner(outrec, prevHotEdge.outrec);
				}
			}
			UncoupleOutRec(ae1);
		}
		else if (IsOpen(ae1))
		{
			if (ae1.windDx < 0)
			{
				JoinOutrecPaths(ae1, ae2);
			}
			else
			{
				JoinOutrecPaths(ae2, ae1);
			}
		}
		else if (ae1.outrec.idx < ae2.outrec.idx)
		{
			JoinOutrecPaths(ae1, ae2);
		}
		else
		{
			JoinOutrecPaths(ae2, ae1);
		}
		return outPt;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void JoinOutrecPaths(Active ae1, Active ae2)
	{
		OutPt pts = ae1.outrec.pts;
		OutPt pts2 = ae2.outrec.pts;
		OutPt next = pts.next;
		OutPt next2 = pts2.next;
		if (IsFront(ae1))
		{
			next2.prev = pts;
			pts.next = next2;
			pts2.next = next;
			next.prev = pts2;
			ae1.outrec.pts = pts2;
			ae1.outrec.frontEdge = ae2.outrec.frontEdge;
			if (ae1.outrec.frontEdge != null)
			{
				ae1.outrec.frontEdge.outrec = ae1.outrec;
			}
		}
		else
		{
			next.prev = pts2;
			pts2.next = next;
			pts.next = next2;
			next2.prev = pts;
			ae1.outrec.backEdge = ae2.outrec.backEdge;
			if (ae1.outrec.backEdge != null)
			{
				ae1.outrec.backEdge.outrec = ae1.outrec;
			}
		}
		ae2.outrec.frontEdge = null;
		ae2.outrec.backEdge = null;
		ae2.outrec.pts = null;
		SetOwner(ae2.outrec, ae1.outrec);
		if (IsOpenEnd(ae1))
		{
			ae2.outrec.pts = ae1.outrec.pts;
			ae1.outrec.pts = null;
		}
		ae1.outrec = null;
		ae2.outrec = null;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static OutPt AddOutPt(Active ae, Point64 pt)
	{
		OutRec outrec = ae.outrec;
		bool flag = IsFront(ae);
		OutPt pts = outrec.pts;
		OutPt next = pts.next;
		if (flag)
		{
			if (pt == pts.pt)
			{
				return pts;
			}
		}
		else if (pt == next.pt)
		{
			return next;
		}
		OutPt outPt = (next.prev = new OutPt(pt, outrec));
		outPt.prev = pts;
		outPt.next = next;
		pts.next = outPt;
		if (flag)
		{
			outrec.pts = outPt;
		}
		return outPt;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private OutRec NewOutRec()
	{
		OutRec outRec = new OutRec
		{
			idx = _outrecList.Count
		};
		_outrecList.Add(outRec);
		return outRec;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private OutPt StartOpenPath(Active ae, Point64 pt)
	{
		OutRec outRec = NewOutRec();
		outRec.isOpen = true;
		if (ae.windDx > 0)
		{
			outRec.frontEdge = ae;
			outRec.backEdge = null;
		}
		else
		{
			outRec.frontEdge = null;
			outRec.backEdge = ae;
		}
		ae.outrec = outRec;
		return outRec.pts = new OutPt(pt, outRec);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void UpdateEdgeIntoAEL(Active ae)
	{
		ae.bot = ae.top;
		ae.vertexTop = NextVertex(ae);
		ae.top = ae.vertexTop.pt;
		ae.curX = ae.bot.X;
		SetDx(ae);
		if (IsJoined(ae))
		{
			Split(ae, ae.bot);
		}
		if (IsHorizontal(ae))
		{
			if (!IsOpen(ae))
			{
				TrimHorz(ae, PreserveCollinear);
			}
		}
		else
		{
			InsertScanline(ae.top.Y);
			CheckJoinLeft(ae, ae.bot);
			CheckJoinRight(ae, ae.bot, checkCurrX: true);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Active? FindEdgeWithMatchingLocMin(Active e)
	{
		Active active;
		for (active = e.nextInAEL; active != null; active = ((IsHorizontal(active) || !(e.bot != active.bot)) ? active.nextInAEL : null))
		{
			if (active.localMin == e.localMin)
			{
				return active;
			}
		}
		for (active = e.prevInAEL; active != null; active = active.prevInAEL)
		{
			if (active.localMin == e.localMin)
			{
				return active;
			}
			if (!IsHorizontal(active) && e.bot != active.bot)
			{
				return null;
			}
		}
		return active;
	}

	private void IntersectEdges(Active ae1, Active ae2, Point64 pt)
	{
		if (_hasOpenPaths && (IsOpen(ae1) || IsOpen(ae2)))
		{
			if (IsOpen(ae1) && IsOpen(ae2))
			{
				return;
			}
			if (IsOpen(ae2))
			{
				SwapActives(ref ae1, ref ae2);
			}
			if (IsJoined(ae2))
			{
				Split(ae2, pt);
			}
			if (_cliptype == ClipType.Union)
			{
				if (!IsHotEdge(ae2))
				{
					return;
				}
			}
			else if (ae2.localMin.polytype == PathType.Subject)
			{
				return;
			}
			switch (_fillrule)
			{
			case FillRule.Positive:
				if (ae2.windCount != 1)
				{
					return;
				}
				break;
			case FillRule.Negative:
				if (ae2.windCount != -1)
				{
					return;
				}
				break;
			default:
				if (Math.Abs(ae2.windCount) != 1)
				{
					return;
				}
				break;
			}
			if (IsHotEdge(ae1))
			{
				AddOutPt(ae1, pt);
				if (IsFront(ae1))
				{
					ae1.outrec.frontEdge = null;
				}
				else
				{
					ae1.outrec.backEdge = null;
				}
				ae1.outrec = null;
			}
			else if (pt == ae1.localMin.vertex.pt && !IsOpenEnd(ae1.localMin.vertex))
			{
				Active active = FindEdgeWithMatchingLocMin(ae1);
				if (active != null && IsHotEdge(active))
				{
					ae1.outrec = active.outrec;
					if (ae1.windDx > 0)
					{
						SetSides(active.outrec, ae1, active);
					}
					else
					{
						SetSides(active.outrec, active, ae1);
					}
				}
				else
				{
					StartOpenPath(ae1, pt);
				}
			}
			else
			{
				StartOpenPath(ae1, pt);
			}
			return;
		}
		if (IsJoined(ae1))
		{
			Split(ae1, pt);
		}
		if (IsJoined(ae2))
		{
			Split(ae2, pt);
		}
		int windCount;
		if (ae1.localMin.polytype == ae2.localMin.polytype)
		{
			if (_fillrule == FillRule.EvenOdd)
			{
				windCount = ae1.windCount;
				ae1.windCount = ae2.windCount;
				ae2.windCount = windCount;
			}
			else
			{
				if (ae1.windCount + ae2.windDx == 0)
				{
					ae1.windCount = -ae1.windCount;
				}
				else
				{
					ae1.windCount += ae2.windDx;
				}
				if (ae2.windCount - ae1.windDx == 0)
				{
					ae2.windCount = -ae2.windCount;
				}
				else
				{
					ae2.windCount -= ae1.windDx;
				}
			}
		}
		else
		{
			if (_fillrule != FillRule.EvenOdd)
			{
				ae1.windCount2 += ae2.windDx;
			}
			else
			{
				ae1.windCount2 = ((ae1.windCount2 == 0) ? 1 : 0);
			}
			if (_fillrule != FillRule.EvenOdd)
			{
				ae2.windCount2 -= ae1.windDx;
			}
			else
			{
				ae2.windCount2 = ((ae2.windCount2 == 0) ? 1 : 0);
			}
		}
		int num;
		switch (_fillrule)
		{
		case FillRule.Positive:
			windCount = ae1.windCount;
			num = ae2.windCount;
			break;
		case FillRule.Negative:
			windCount = -ae1.windCount;
			num = -ae2.windCount;
			break;
		default:
			windCount = Math.Abs(ae1.windCount);
			num = Math.Abs(ae2.windCount);
			break;
		}
		bool flag = windCount == 0 || windCount == 1;
		bool flag2 = num == 0 || num == 1;
		if ((!IsHotEdge(ae1) && !flag) || (!IsHotEdge(ae2) && !flag2))
		{
			return;
		}
		if (IsHotEdge(ae1) && IsHotEdge(ae2))
		{
			if ((windCount != 0 && windCount != 1) || (num != 0 && num != 1) || (ae1.localMin.polytype != ae2.localMin.polytype && _cliptype != ClipType.Xor))
			{
				AddLocalMaxPoly(ae1, ae2, pt);
			}
			else if (IsFront(ae1) || ae1.outrec == ae2.outrec)
			{
				AddLocalMaxPoly(ae1, ae2, pt);
				AddLocalMinPoly(ae1, ae2, pt);
			}
			else
			{
				AddOutPt(ae1, pt);
				AddOutPt(ae2, pt);
				SwapOutrecs(ae1, ae2);
			}
			return;
		}
		if (IsHotEdge(ae1))
		{
			AddOutPt(ae1, pt);
			SwapOutrecs(ae1, ae2);
			return;
		}
		if (IsHotEdge(ae2))
		{
			AddOutPt(ae2, pt);
			SwapOutrecs(ae1, ae2);
			return;
		}
		long num2;
		long num3;
		switch (_fillrule)
		{
		case FillRule.Positive:
			num2 = ae1.windCount2;
			num3 = ae2.windCount2;
			break;
		case FillRule.Negative:
			num2 = -ae1.windCount2;
			num3 = -ae2.windCount2;
			break;
		default:
			num2 = Math.Abs(ae1.windCount2);
			num3 = Math.Abs(ae2.windCount2);
			break;
		}
		if (!IsSamePolyType(ae1, ae2))
		{
			AddLocalMinPoly(ae1, ae2, pt);
		}
		else
		{
			if (windCount != 1 || num != 1)
			{
				return;
			}
			switch (_cliptype)
			{
			case ClipType.Union:
				if (num2 <= 0 || num3 <= 0)
				{
					AddLocalMinPoly(ae1, ae2, pt);
				}
				break;
			case ClipType.Difference:
				if ((GetPolyType(ae1) == PathType.Clip && num2 > 0 && num3 > 0) || (GetPolyType(ae1) == PathType.Subject && num2 <= 0 && num3 <= 0))
				{
					AddLocalMinPoly(ae1, ae2, pt);
				}
				break;
			case ClipType.Xor:
				AddLocalMinPoly(ae1, ae2, pt);
				break;
			default:
				if (num2 > 0 && num3 > 0)
				{
					AddLocalMinPoly(ae1, ae2, pt);
				}
				break;
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void DeleteFromAEL(Active ae)
	{
		Active prevInAEL = ae.prevInAEL;
		Active nextInAEL = ae.nextInAEL;
		if (prevInAEL != null || nextInAEL != null || ae == _actives)
		{
			if (prevInAEL != null)
			{
				prevInAEL.nextInAEL = nextInAEL;
			}
			else
			{
				_actives = nextInAEL;
			}
			if (nextInAEL != null)
			{
				nextInAEL.prevInAEL = prevInAEL;
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void AdjustCurrXAndCopyToSEL(long topY)
	{
		for (Active active = (_sel = _actives); active != null; active = active.nextInAEL)
		{
			active.prevInSEL = active.prevInAEL;
			active.nextInSEL = active.nextInAEL;
			active.jump = active.nextInSEL;
			active.curX = ((active.joinWith == JoinWith.Left) ? active.prevInAEL.curX : TopX(active, topY));
		}
	}

	protected void ExecuteInternal(ClipType ct, FillRule fillRule)
	{
		if (ct == ClipType.NoClip)
		{
			return;
		}
		_fillrule = fillRule;
		_cliptype = ct;
		Reset();
		if (!PopScanline(out var y))
		{
			return;
		}
		while (_succeeded)
		{
			InsertLocalMinimaIntoAEL(y);
			Active ae;
			while (PopHorz(out ae))
			{
				DoHorizontal(ae);
			}
			if (_horzSegList.Count > 0)
			{
				ConvertHorzSegsToJoins();
				_horzSegList.Clear();
			}
			_currentBotY = y;
			if (!PopScanline(out y))
			{
				break;
			}
			DoIntersections(y);
			DoTopOfScanbeam(y);
			while (PopHorz(out ae))
			{
				DoHorizontal(ae);
			}
		}
		if (_succeeded)
		{
			ProcessHorzJoins();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void DoIntersections(long topY)
	{
		if (BuildIntersectList(topY))
		{
			ProcessIntersectList();
			DisposeIntersectNodes();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void DisposeIntersectNodes()
	{
		_intersectList.Clear();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void AddNewIntersectNode(Active ae1, Active ae2, long topY)
	{
		if (!InternalClipper.GetSegmentIntersectPt(ae1.bot, ae1.top, ae2.bot, ae2.top, out var ip))
		{
			ip = new Point64(ae1.curX, topY);
		}
		if (ip.Y > _currentBotY || ip.Y < topY)
		{
			double num = Math.Abs(ae1.dx);
			double num2 = Math.Abs(ae2.dx);
			if (num > 100.0)
			{
				if (num2 > 100.0)
				{
					ip = ((!(num > num2)) ? InternalClipper.GetClosestPtOnSegment(ip, ae2.bot, ae2.top) : InternalClipper.GetClosestPtOnSegment(ip, ae1.bot, ae1.top));
				}
				else
				{
					ip = InternalClipper.GetClosestPtOnSegment(ip, ae1.bot, ae1.top);
				}
			}
			else if (num2 > 100.0)
			{
				ip = InternalClipper.GetClosestPtOnSegment(ip, ae2.bot, ae2.top);
			}
			else
			{
				if (ip.Y < topY)
				{
					ip.Y = topY;
				}
				else
				{
					ip.Y = _currentBotY;
				}
				if (num < num2)
				{
					ip.X = TopX(ae1, ip.Y);
				}
				else
				{
					ip.X = TopX(ae2, ip.Y);
				}
			}
		}
		IntersectNode item = new IntersectNode(ip, ae1, ae2);
		_intersectList.Add(item);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Active? ExtractFromSEL(Active ae)
	{
		Active nextInSEL = ae.nextInSEL;
		if (nextInSEL != null)
		{
			nextInSEL.prevInSEL = ae.prevInSEL;
		}
		ae.prevInSEL.nextInSEL = nextInSEL;
		return nextInSEL;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void Insert1Before2InSEL(Active ae1, Active ae2)
	{
		ae1.prevInSEL = ae2.prevInSEL;
		if (ae1.prevInSEL != null)
		{
			ae1.prevInSEL.nextInSEL = ae1;
		}
		ae1.nextInSEL = ae2;
		ae2.prevInSEL = ae1;
	}

	private bool BuildIntersectList(long topY)
	{
		if (_actives?.nextInAEL == null)
		{
			return false;
		}
		AdjustCurrXAndCopyToSEL(topY);
		Active active = _sel;
		while (active.jump != null)
		{
			Active active2 = null;
			while (active?.jump != null)
			{
				Active active3 = active;
				Active active4 = active.jump;
				Active active5 = active4;
				Active active6 = (active.jump = active4.jump);
				while (active != active5 && active4 != active6)
				{
					if (active4.curX < active.curX)
					{
						Active prevInSEL = active4.prevInSEL;
						while (true)
						{
							AddNewIntersectNode(prevInSEL, active4, topY);
							if (prevInSEL == active)
							{
								break;
							}
							prevInSEL = prevInSEL.prevInSEL;
						}
						prevInSEL = active4;
						active4 = ExtractFromSEL(prevInSEL);
						active5 = active4;
						Insert1Before2InSEL(prevInSEL, active);
						if (active == active3)
						{
							active3 = prevInSEL;
							active3.jump = active6;
							if (active2 == null)
							{
								_sel = active3;
							}
							else
							{
								active2.jump = active3;
							}
						}
					}
					else
					{
						active = active.nextInSEL;
					}
				}
				active2 = active3;
				active = active6;
			}
			active = _sel;
		}
		return _intersectList.Count > 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ProcessIntersectList()
	{
		_intersectList.Sort(default(IntersectListSort));
		for (int i = 0; i < _intersectList.Count; i++)
		{
			if (!EdgesAdjacentInAEL(_intersectList[i]))
			{
				int j;
				for (j = i + 1; !EdgesAdjacentInAEL(_intersectList[j]); j++)
				{
				}
				List<IntersectNode> intersectList = _intersectList;
				int index = j;
				List<IntersectNode> intersectList2 = _intersectList;
				int index2 = i;
				IntersectNode value = _intersectList[i];
				IntersectNode value2 = _intersectList[j];
				intersectList[index] = value;
				intersectList2[index2] = value2;
			}
			IntersectNode intersectNode = _intersectList[i];
			IntersectEdges(intersectNode.edge1, intersectNode.edge2, intersectNode.pt);
			SwapPositionsInAEL(intersectNode.edge1, intersectNode.edge2);
			intersectNode.edge1.curX = intersectNode.pt.X;
			intersectNode.edge2.curX = intersectNode.pt.X;
			CheckJoinLeft(intersectNode.edge2, intersectNode.pt, checkCurrX: true);
			CheckJoinRight(intersectNode.edge1, intersectNode.pt, checkCurrX: true);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void SwapPositionsInAEL(Active ae1, Active ae2)
	{
		Active nextInAEL = ae2.nextInAEL;
		if (nextInAEL != null)
		{
			nextInAEL.prevInAEL = ae1;
		}
		Active prevInAEL = ae1.prevInAEL;
		if (prevInAEL != null)
		{
			prevInAEL.nextInAEL = ae2;
		}
		ae2.prevInAEL = prevInAEL;
		ae2.nextInAEL = ae1;
		ae1.prevInAEL = ae2;
		ae1.nextInAEL = nextInAEL;
		if (ae2.prevInAEL == null)
		{
			_actives = ae2;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool ResetHorzDirection(Active horz, Vertex? vertexMax, out long leftX, out long rightX)
	{
		if (horz.bot.X == horz.top.X)
		{
			leftX = horz.curX;
			rightX = horz.curX;
			Active nextInAEL = horz.nextInAEL;
			while (nextInAEL != null && nextInAEL.vertexTop != vertexMax)
			{
				nextInAEL = nextInAEL.nextInAEL;
			}
			return nextInAEL != null;
		}
		if (horz.curX < horz.top.X)
		{
			leftX = horz.curX;
			rightX = horz.top.X;
			return true;
		}
		leftX = horz.top.X;
		rightX = horz.curX;
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void TrimHorz(Active horzEdge, bool preserveCollinear)
	{
		bool flag = false;
		Point64 pt = NextVertex(horzEdge).pt;
		while (pt.Y == horzEdge.top.Y && (!preserveCollinear || pt.X < horzEdge.top.X == horzEdge.bot.X < horzEdge.top.X))
		{
			horzEdge.vertexTop = NextVertex(horzEdge);
			horzEdge.top = pt;
			flag = true;
			if (IsMaxima(horzEdge))
			{
				break;
			}
			pt = NextVertex(horzEdge).pt;
		}
		if (flag)
		{
			SetDx(horzEdge);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void AddToHorzSegList(OutPt op)
	{
		if (!op.outrec.isOpen)
		{
			_horzSegList.Add(new HorzSegment(op));
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static OutPt GetLastOp(Active hotEdge)
	{
		OutRec outrec = hotEdge.outrec;
		if (hotEdge != outrec.frontEdge)
		{
			return outrec.pts.next;
		}
		return outrec.pts;
	}

	private void DoHorizontal(Active horz)
	{
		bool flag = IsOpen(horz);
		long y = horz.bot.Y;
		Vertex vertex = (flag ? GetCurrYMaximaVertex_Open(horz) : GetCurrYMaximaVertex(horz));
		bool flag2 = ResetHorzDirection(horz, vertex, out var leftX, out var rightX);
		if (IsHotEdge(horz))
		{
			OutPt op = AddOutPt(horz, new Point64(horz.curX, y));
			AddToHorzSegList(op);
		}
		while (true)
		{
			Active active = (flag2 ? horz.nextInAEL : horz.prevInAEL);
			while (active != null)
			{
				if (active.vertexTop == vertex)
				{
					if (IsHotEdge(horz) && IsJoined(active))
					{
						Split(active, active.top);
					}
					if (IsHotEdge(horz))
					{
						while (horz.vertexTop != vertex)
						{
							AddOutPt(horz, horz.top);
							UpdateEdgeIntoAEL(horz);
						}
						if (flag2)
						{
							AddLocalMaxPoly(horz, active, horz.top);
						}
						else
						{
							AddLocalMaxPoly(active, horz, horz.top);
						}
					}
					DeleteFromAEL(active);
					DeleteFromAEL(horz);
					return;
				}
				Point64 pt;
				if (vertex != horz.vertexTop || IsOpenEnd(horz))
				{
					if ((flag2 && active.curX > rightX) || (!flag2 && active.curX < leftX))
					{
						break;
					}
					if (active.curX == horz.top.X && !IsHorizontal(active))
					{
						pt = NextVertex(horz).pt;
						if (IsOpen(active) && !IsSamePolyType(active, horz) && !IsHotEdge(active))
						{
							if ((flag2 && TopX(active, pt.Y) > pt.X) || (!flag2 && TopX(active, pt.Y) < pt.X))
							{
								break;
							}
						}
						else if ((flag2 && TopX(active, pt.Y) >= pt.X) || (!flag2 && TopX(active, pt.Y) <= pt.X))
						{
							break;
						}
					}
				}
				pt = new Point64(active.curX, y);
				if (flag2)
				{
					IntersectEdges(horz, active, pt);
					SwapPositionsInAEL(horz, active);
					CheckJoinLeft(active, pt);
					horz.curX = active.curX;
					active = horz.nextInAEL;
				}
				else
				{
					IntersectEdges(active, horz, pt);
					SwapPositionsInAEL(active, horz);
					CheckJoinRight(active, pt);
					horz.curX = active.curX;
					active = horz.prevInAEL;
				}
				if (IsHotEdge(horz))
				{
					AddToHorzSegList(GetLastOp(horz));
				}
			}
			if (flag && IsOpenEnd(horz))
			{
				if (IsHotEdge(horz))
				{
					AddOutPt(horz, horz.top);
					if (IsFront(horz))
					{
						horz.outrec.frontEdge = null;
					}
					else
					{
						horz.outrec.backEdge = null;
					}
					horz.outrec = null;
				}
				DeleteFromAEL(horz);
				return;
			}
			if (NextVertex(horz).pt.Y != horz.top.Y)
			{
				break;
			}
			if (IsHotEdge(horz))
			{
				AddOutPt(horz, horz.top);
			}
			UpdateEdgeIntoAEL(horz);
			flag2 = ResetHorzDirection(horz, vertex, out leftX, out rightX);
		}
		if (IsHotEdge(horz))
		{
			OutPt op2 = AddOutPt(horz, horz.top);
			AddToHorzSegList(op2);
		}
		UpdateEdgeIntoAEL(horz);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void DoTopOfScanbeam(long y)
	{
		_sel = null;
		Active active = _actives;
		while (active != null)
		{
			if (active.top.Y == y)
			{
				active.curX = active.top.X;
				if (IsMaxima(active))
				{
					active = DoMaxima(active);
					continue;
				}
				if (IsHotEdge(active))
				{
					AddOutPt(active, active.top);
				}
				UpdateEdgeIntoAEL(active);
				if (IsHorizontal(active))
				{
					PushHorz(active);
				}
			}
			else
			{
				active.curX = TopX(active, y);
			}
			active = active.nextInAEL;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private Active? DoMaxima(Active ae)
	{
		Active prevInAEL = ae.prevInAEL;
		Active nextInAEL = ae.nextInAEL;
		if (IsOpenEnd(ae))
		{
			if (IsHotEdge(ae))
			{
				AddOutPt(ae, ae.top);
			}
			if (IsHorizontal(ae))
			{
				return nextInAEL;
			}
			if (IsHotEdge(ae))
			{
				if (IsFront(ae))
				{
					ae.outrec.frontEdge = null;
				}
				else
				{
					ae.outrec.backEdge = null;
				}
				ae.outrec = null;
			}
			DeleteFromAEL(ae);
			return nextInAEL;
		}
		Active maximaPair = GetMaximaPair(ae);
		if (maximaPair == null)
		{
			return nextInAEL;
		}
		if (IsJoined(ae))
		{
			Split(ae, ae.top);
		}
		if (IsJoined(maximaPair))
		{
			Split(maximaPair, maximaPair.top);
		}
		while (nextInAEL != maximaPair)
		{
			IntersectEdges(ae, nextInAEL, ae.top);
			SwapPositionsInAEL(ae, nextInAEL);
			nextInAEL = ae.nextInAEL;
		}
		if (IsOpen(ae))
		{
			if (IsHotEdge(ae))
			{
				AddLocalMaxPoly(ae, maximaPair, ae.top);
			}
			DeleteFromAEL(maximaPair);
			DeleteFromAEL(ae);
			if (prevInAEL == null)
			{
				return _actives;
			}
			return prevInAEL.nextInAEL;
		}
		if (IsHotEdge(ae))
		{
			AddLocalMaxPoly(ae, maximaPair, ae.top);
		}
		DeleteFromAEL(ae);
		DeleteFromAEL(maximaPair);
		if (prevInAEL == null)
		{
			return _actives;
		}
		return prevInAEL.nextInAEL;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsJoined(Active e)
	{
		return e.joinWith != JoinWith.None;
	}

	private void Split(Active e, Point64 currPt)
	{
		if (e.joinWith == JoinWith.Right)
		{
			e.joinWith = JoinWith.None;
			e.nextInAEL.joinWith = JoinWith.None;
			AddLocalMinPoly(e, e.nextInAEL, currPt, isNew: true);
		}
		else
		{
			e.joinWith = JoinWith.None;
			e.prevInAEL.joinWith = JoinWith.None;
			AddLocalMinPoly(e.prevInAEL, e, currPt, isNew: true);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void CheckJoinLeft(Active e, Point64 pt, bool checkCurrX = false)
	{
		Active prevInAEL = e.prevInAEL;
		if (prevInAEL == null || !IsHotEdge(e) || !IsHotEdge(prevInAEL) || IsHorizontal(e) || IsHorizontal(prevInAEL) || IsOpen(e) || IsOpen(prevInAEL) || ((pt.Y < e.top.Y + 2 || pt.Y < prevInAEL.top.Y + 2) && (e.bot.Y > pt.Y || prevInAEL.bot.Y > pt.Y)))
		{
			return;
		}
		if (checkCurrX)
		{
			if (Clipper.PerpendicDistFromLineSqrd(pt, prevInAEL.bot, prevInAEL.top) > 0.25)
			{
				return;
			}
		}
		else if (e.curX != prevInAEL.curX)
		{
			return;
		}
		if (InternalClipper.IsCollinear(e.top, pt, prevInAEL.top))
		{
			if (e.outrec.idx == prevInAEL.outrec.idx)
			{
				AddLocalMaxPoly(prevInAEL, e, pt);
			}
			else if (e.outrec.idx < prevInAEL.outrec.idx)
			{
				JoinOutrecPaths(e, prevInAEL);
			}
			else
			{
				JoinOutrecPaths(prevInAEL, e);
			}
			prevInAEL.joinWith = JoinWith.Right;
			e.joinWith = JoinWith.Left;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void CheckJoinRight(Active e, Point64 pt, bool checkCurrX = false)
	{
		Active nextInAEL = e.nextInAEL;
		if (nextInAEL == null || !IsHotEdge(e) || !IsHotEdge(nextInAEL) || IsHorizontal(e) || IsHorizontal(nextInAEL) || IsOpen(e) || IsOpen(nextInAEL) || ((pt.Y < e.top.Y + 2 || pt.Y < nextInAEL.top.Y + 2) && (e.bot.Y > pt.Y || nextInAEL.bot.Y > pt.Y)))
		{
			return;
		}
		if (checkCurrX)
		{
			if (Clipper.PerpendicDistFromLineSqrd(pt, nextInAEL.bot, nextInAEL.top) > 0.25)
			{
				return;
			}
		}
		else if (e.curX != nextInAEL.curX)
		{
			return;
		}
		if (InternalClipper.IsCollinear(e.top, pt, nextInAEL.top))
		{
			if (e.outrec.idx == nextInAEL.outrec.idx)
			{
				AddLocalMaxPoly(e, nextInAEL, pt);
			}
			else if (e.outrec.idx < nextInAEL.outrec.idx)
			{
				JoinOutrecPaths(e, nextInAEL);
			}
			else
			{
				JoinOutrecPaths(nextInAEL, e);
			}
			e.joinWith = JoinWith.Right;
			nextInAEL.joinWith = JoinWith.Left;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void FixOutRecPts(OutRec outrec)
	{
		OutPt outPt = outrec.pts;
		do
		{
			outPt.outrec = outrec;
			outPt = outPt.next;
		}
		while (outPt != outrec.pts);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool SetHorzSegHeadingForward(HorzSegment hs, OutPt opP, OutPt opN)
	{
		if (opP.pt.X == opN.pt.X)
		{
			return false;
		}
		if (opP.pt.X < opN.pt.X)
		{
			hs.leftOp = opP;
			hs.rightOp = opN;
			hs.leftToRight = true;
		}
		else
		{
			hs.leftOp = opN;
			hs.rightOp = opP;
			hs.leftToRight = false;
		}
		return true;
	}

	private static bool UpdateHorzSegment(HorzSegment hs)
	{
		OutPt? leftOp = hs.leftOp;
		OutRec realOutRec = GetRealOutRec(leftOp.outrec);
		bool flag = realOutRec.frontEdge != null;
		long y = leftOp.pt.Y;
		OutPt outPt = leftOp;
		OutPt outPt2 = leftOp;
		if (flag)
		{
			OutPt pts = realOutRec.pts;
			OutPt next = pts.next;
			while (outPt != next && outPt.prev.pt.Y == y)
			{
				outPt = outPt.prev;
			}
			while (outPt2 != pts && outPt2.next.pt.Y == y)
			{
				outPt2 = outPt2.next;
			}
		}
		else
		{
			while (outPt.prev != outPt2 && outPt.prev.pt.Y == y)
			{
				outPt = outPt.prev;
			}
			while (outPt2.next != outPt && outPt2.next.pt.Y == y)
			{
				outPt2 = outPt2.next;
			}
		}
		int num;
		if (SetHorzSegHeadingForward(hs, outPt, outPt2))
		{
			num = ((hs.leftOp.horz == null) ? 1 : 0);
			if (num != 0)
			{
				hs.leftOp.horz = hs;
				return (byte)num != 0;
			}
		}
		else
		{
			num = 0;
		}
		hs.rightOp = null;
		return (byte)num != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static OutPt DuplicateOp(OutPt op, bool insert_after)
	{
		OutPt outPt = new OutPt(op.pt, op.outrec);
		if (insert_after)
		{
			outPt.next = op.next;
			outPt.next.prev = outPt;
			outPt.prev = op;
			op.next = outPt;
		}
		else
		{
			outPt.prev = op.prev;
			outPt.prev.next = outPt;
			outPt.next = op;
			op.prev = outPt;
		}
		return outPt;
	}

	private static int HorzSegSort(HorzSegment? hs1, HorzSegment? hs2)
	{
		if (hs1 == null || hs2 == null)
		{
			return 0;
		}
		if (hs1.rightOp == null)
		{
			return (hs2.rightOp != null) ? 1 : 0;
		}
		if (hs2.rightOp == null)
		{
			return -1;
		}
		return hs1.leftOp.pt.X.CompareTo(hs2.leftOp.pt.X);
	}

	private void ConvertHorzSegsToJoins()
	{
		int num = 0;
		foreach (HorzSegment horzSeg in _horzSegList)
		{
			if (UpdateHorzSegment(horzSeg))
			{
				num++;
			}
		}
		if (num < 2)
		{
			return;
		}
		_horzSegList.Sort(HorzSegSort);
		for (int i = 0; i < num - 1; i++)
		{
			HorzSegment horzSegment = _horzSegList[i];
			for (int j = i + 1; j < num; j++)
			{
				HorzSegment horzSegment2 = _horzSegList[j];
				if (horzSegment2.leftOp.pt.X >= horzSegment.rightOp.pt.X || horzSegment2.leftToRight == horzSegment.leftToRight || horzSegment2.rightOp.pt.X <= horzSegment.leftOp.pt.X)
				{
					continue;
				}
				long y = horzSegment.leftOp.pt.Y;
				if (horzSegment.leftToRight)
				{
					while (horzSegment.leftOp.next.pt.Y == y && horzSegment.leftOp.next.pt.X <= horzSegment2.leftOp.pt.X)
					{
						horzSegment.leftOp = horzSegment.leftOp.next;
					}
					while (horzSegment2.leftOp.prev.pt.Y == y && horzSegment2.leftOp.prev.pt.X <= horzSegment.leftOp.pt.X)
					{
						horzSegment2.leftOp = horzSegment2.leftOp.prev;
					}
					HorzJoin item = new HorzJoin(DuplicateOp(horzSegment.leftOp, insert_after: true), DuplicateOp(horzSegment2.leftOp, insert_after: false));
					_horzJoinList.Add(item);
				}
				else
				{
					while (horzSegment.leftOp.prev.pt.Y == y && horzSegment.leftOp.prev.pt.X <= horzSegment2.leftOp.pt.X)
					{
						horzSegment.leftOp = horzSegment.leftOp.prev;
					}
					while (horzSegment2.leftOp.next.pt.Y == y && horzSegment2.leftOp.next.pt.X <= horzSegment.leftOp.pt.X)
					{
						horzSegment2.leftOp = horzSegment2.leftOp.next;
					}
					HorzJoin item2 = new HorzJoin(DuplicateOp(horzSegment2.leftOp, insert_after: true), DuplicateOp(horzSegment.leftOp, insert_after: false));
					_horzJoinList.Add(item2);
				}
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Path64 GetCleanPath(OutPt op)
	{
		Path64 path = new Path64();
		OutPt outPt = op;
		while (outPt.next != op && ((outPt.pt.X == outPt.next.pt.X && outPt.pt.X == outPt.prev.pt.X) || (outPt.pt.Y == outPt.next.pt.Y && outPt.pt.Y == outPt.prev.pt.Y)))
		{
			outPt = outPt.next;
		}
		path.Add(outPt.pt);
		OutPt outPt2 = outPt;
		for (outPt = outPt.next; outPt != op; outPt = outPt.next)
		{
			if ((outPt.pt.X != outPt.next.pt.X || outPt.pt.X != outPt2.pt.X) && (outPt.pt.Y != outPt.next.pt.Y || outPt.pt.Y != outPt2.pt.Y))
			{
				path.Add(outPt.pt);
				outPt2 = outPt;
			}
		}
		return path;
	}

	private static PointInPolygonResult PointInOpPolygon(Point64 pt, OutPt op)
	{
		if (op == op.next || op.prev == op.next)
		{
			return PointInPolygonResult.IsOutside;
		}
		OutPt outPt = op;
		while (op.pt.Y == pt.Y)
		{
			op = op.next;
			if (op == outPt)
			{
				break;
			}
		}
		if (op.pt.Y == pt.Y)
		{
			return PointInPolygonResult.IsOutside;
		}
		bool flag = op.pt.Y < pt.Y;
		bool flag2 = flag;
		int num = 0;
		outPt = op.next;
		while (outPt != op)
		{
			if (flag)
			{
				while (outPt != op && outPt.pt.Y < pt.Y)
				{
					outPt = outPt.next;
				}
			}
			else
			{
				while (outPt != op && outPt.pt.Y > pt.Y)
				{
					outPt = outPt.next;
				}
			}
			if (outPt == op)
			{
				break;
			}
			if (outPt.pt.Y == pt.Y)
			{
				if (outPt.pt.X == pt.X || (outPt.pt.Y == outPt.prev.pt.Y && pt.X < outPt.prev.pt.X != pt.X < outPt.pt.X))
				{
					return PointInPolygonResult.IsOn;
				}
				outPt = outPt.next;
				if (outPt == op)
				{
					break;
				}
				continue;
			}
			if (outPt.pt.X <= pt.X || outPt.prev.pt.X <= pt.X)
			{
				if (outPt.prev.pt.X < pt.X && outPt.pt.X < pt.X)
				{
					num = 1 - num;
				}
				else
				{
					double num2 = InternalClipper.CrossProduct(outPt.prev.pt, outPt.pt, pt);
					if (num2 == 0.0)
					{
						return PointInPolygonResult.IsOn;
					}
					if (num2 < 0.0 == flag)
					{
						num = 1 - num;
					}
				}
			}
			flag = !flag;
			outPt = outPt.next;
		}
		if (flag == flag2)
		{
			if (num != 0)
			{
				return PointInPolygonResult.IsInside;
			}
			return PointInPolygonResult.IsOutside;
		}
		double num3 = InternalClipper.CrossProduct(outPt.prev.pt, outPt.pt, pt);
		if (num3 == 0.0)
		{
			return PointInPolygonResult.IsOn;
		}
		if (num3 < 0.0 == flag)
		{
			num = 1 - num;
		}
		if (num != 0)
		{
			return PointInPolygonResult.IsInside;
		}
		return PointInPolygonResult.IsOutside;
	}

	private static bool Path1InsidePath2(OutPt op1, OutPt op2)
	{
		PointInPolygonResult pointInPolygonResult = PointInPolygonResult.IsOn;
		OutPt outPt = op1;
		do
		{
			switch (PointInOpPolygon(outPt.pt, op2))
			{
			case PointInPolygonResult.IsOutside:
				if (pointInPolygonResult == PointInPolygonResult.IsOutside)
				{
					return false;
				}
				pointInPolygonResult = PointInPolygonResult.IsOutside;
				break;
			case PointInPolygonResult.IsInside:
				if (pointInPolygonResult == PointInPolygonResult.IsInside)
				{
					return true;
				}
				pointInPolygonResult = PointInPolygonResult.IsInside;
				break;
			}
			outPt = outPt.next;
		}
		while (outPt != op1);
		return InternalClipper.Path2ContainsPath1(GetCleanPath(op1), GetCleanPath(op2));
	}

	private static void MoveSplits(OutRec fromOr, OutRec toOr)
	{
		if (fromOr.splits == null)
		{
			return;
		}
		if (toOr.splits == null)
		{
			toOr.splits = new List<int>();
		}
		foreach (int split in fromOr.splits)
		{
			toOr.splits.Add(split);
		}
		fromOr.splits = null;
	}

	private void ProcessHorzJoins()
	{
		foreach (HorzJoin horzJoin in _horzJoinList)
		{
			OutRec realOutRec = GetRealOutRec(horzJoin.op1.outrec);
			OutRec realOutRec2 = GetRealOutRec(horzJoin.op2.outrec);
			OutPt next = horzJoin.op1.next;
			OutPt prev = horzJoin.op2.prev;
			horzJoin.op1.next = horzJoin.op2;
			horzJoin.op2.prev = horzJoin.op1;
			next.prev = prev;
			prev.next = next;
			if (realOutRec == realOutRec2)
			{
				realOutRec2 = NewOutRec();
				realOutRec2.pts = next;
				FixOutRecPts(realOutRec2);
				if (realOutRec.pts.outrec == realOutRec2)
				{
					realOutRec.pts = horzJoin.op1;
					realOutRec.pts.outrec = realOutRec;
				}
				if (_using_polytree)
				{
					OutRec outRec2;
					if (Path1InsidePath2(realOutRec.pts, realOutRec2.pts))
					{
						OutRec outRec = realOutRec2;
						outRec2 = realOutRec;
						OutPt pts = realOutRec.pts;
						OutPt pts2 = realOutRec2.pts;
						outRec.pts = pts;
						outRec2.pts = pts2;
						FixOutRecPts(realOutRec);
						FixOutRecPts(realOutRec2);
						realOutRec2.owner = realOutRec;
					}
					else if (Path1InsidePath2(realOutRec2.pts, realOutRec.pts))
					{
						realOutRec2.owner = realOutRec;
					}
					else
					{
						realOutRec2.owner = realOutRec.owner;
					}
					outRec2 = realOutRec;
					if (outRec2.splits == null)
					{
						outRec2.splits = new List<int>();
					}
					realOutRec.splits.Add(realOutRec2.idx);
				}
				else
				{
					realOutRec2.owner = realOutRec;
				}
			}
			else
			{
				realOutRec2.pts = null;
				if (_using_polytree)
				{
					SetOwner(realOutRec2, realOutRec);
					MoveSplits(realOutRec2, realOutRec);
				}
				else
				{
					realOutRec2.owner = realOutRec;
				}
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool PtsReallyClose(Point64 pt1, Point64 pt2)
	{
		if (Math.Abs(pt1.X - pt2.X) < 2)
		{
			return Math.Abs(pt1.Y - pt2.Y) < 2;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsVerySmallTriangle(OutPt op)
	{
		if (op.next.next == op.prev)
		{
			if (!PtsReallyClose(op.prev.pt, op.next.pt) && !PtsReallyClose(op.pt, op.next.pt))
			{
				return PtsReallyClose(op.pt, op.prev.pt);
			}
			return true;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsValidClosedPath(OutPt? op)
	{
		if (op != null && op.next != op)
		{
			if (op.next == op.prev)
			{
				return !IsVerySmallTriangle(op);
			}
			return true;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static OutPt? DisposeOutPt(OutPt op)
	{
		OutPt? result = ((op.next == op) ? null : op.next);
		op.prev.next = op.next;
		op.next.prev = op.prev;
		return result;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void CleanCollinear(OutRec? outrec)
	{
		outrec = GetRealOutRec(outrec);
		if (outrec == null || outrec.isOpen)
		{
			return;
		}
		if (!IsValidClosedPath(outrec.pts))
		{
			outrec.pts = null;
			return;
		}
		OutPt outPt = outrec.pts;
		OutPt outPt2 = outPt;
		while (true)
		{
			if (InternalClipper.IsCollinear(outPt2.prev.pt, outPt2.pt, outPt2.next.pt) && (outPt2.pt == outPt2.prev.pt || outPt2.pt == outPt2.next.pt || !PreserveCollinear || InternalClipper.DotProduct(outPt2.prev.pt, outPt2.pt, outPt2.next.pt) < 0.0))
			{
				if (outPt2 == outrec.pts)
				{
					outrec.pts = outPt2.prev;
				}
				outPt2 = DisposeOutPt(outPt2);
				if (!IsValidClosedPath(outPt2))
				{
					outrec.pts = null;
					return;
				}
				outPt = outPt2;
			}
			else
			{
				outPt2 = outPt2.next;
				if (outPt2 == outPt)
				{
					break;
				}
			}
		}
		FixSelfIntersects(outrec);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void DoSplitOp(OutRec outrec, OutPt splitOp)
	{
		OutPt prev = splitOp.prev;
		OutPt next = splitOp.next.next;
		outrec.pts = prev;
		InternalClipper.GetSegmentIntersectPt(prev.pt, splitOp.pt, splitOp.next.pt, next.pt, out var ip);
		double num = Area(prev);
		double num2 = Math.Abs(num);
		if (num2 < 2.0)
		{
			outrec.pts = null;
			return;
		}
		double num3 = AreaTriangle(ip, splitOp.pt, splitOp.next.pt);
		double num4 = Math.Abs(num3);
		if (ip == prev.pt || ip == next.pt)
		{
			next.prev = prev;
			prev.next = next;
		}
		else
		{
			prev.next = (next.prev = new OutPt(ip, outrec)
			{
				prev = prev,
				next = next
			});
		}
		if (!(num4 > 1.0) || (!(num4 > num2) && num3 > 0.0 != num > 0.0))
		{
			return;
		}
		OutRec outRec = NewOutRec();
		outRec.owner = outrec.owner;
		splitOp.outrec = outRec;
		splitOp.next.outrec = outRec;
		OutPt outPt = (splitOp.prev = (outRec.pts = new OutPt(ip, outRec)
		{
			prev = splitOp.next,
			next = splitOp
		}));
		splitOp.next.next = outPt;
		if (!_using_polytree)
		{
			return;
		}
		if (Path1InsidePath2(prev, outPt))
		{
			OutRec outRec2 = outRec;
			if (outRec2.splits == null)
			{
				outRec2.splits = new List<int>();
			}
			outRec.splits.Add(outrec.idx);
		}
		else
		{
			OutRec outRec2 = outrec;
			if (outRec2.splits == null)
			{
				outRec2.splits = new List<int>();
			}
			outrec.splits.Add(outRec.idx);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void FixSelfIntersects(OutRec outrec)
	{
		OutPt outPt = outrec.pts;
		if (outPt.prev == outPt.next.next)
		{
			return;
		}
		do
		{
			IL_001b:
			if (InternalClipper.SegsIntersect(outPt.prev.pt, outPt.pt, outPt.next.pt, outPt.next.next.pt))
			{
				if (!InternalClipper.SegsIntersect(outPt.prev.pt, outPt.pt, outPt.next.next.pt, outPt.next.next.next.pt))
				{
					if (outPt == outrec.pts || outPt.next == outrec.pts)
					{
						outrec.pts = outrec.pts.prev;
					}
					DoSplitOp(outrec, outPt);
					if (outrec.pts == null)
					{
						break;
					}
					outPt = outrec.pts;
					if (outPt.prev == outPt.next.next)
					{
						break;
					}
					goto IL_001b;
				}
				outPt = DuplicateOp(outPt, insert_after: false);
				outPt.pt = outPt.next.next.next.pt;
				outPt = outPt.next;
			}
			outPt = outPt.next;
		}
		while (outPt != outrec.pts);
	}

	internal static bool BuildPath(OutPt? op, bool reverse, bool isOpen, Path64 path)
	{
		if (op == null || op.next == op || (!isOpen && op.next == op.prev))
		{
			return false;
		}
		path.Clear();
		Point64 pt;
		OutPt outPt;
		if (reverse)
		{
			pt = op.pt;
			outPt = op.prev;
		}
		else
		{
			op = op.next;
			pt = op.pt;
			outPt = op.next;
		}
		path.Add(pt);
		while (outPt != op)
		{
			if (outPt.pt != pt)
			{
				pt = outPt.pt;
				path.Add(pt);
			}
			outPt = ((!reverse) ? outPt.next : outPt.prev);
		}
		if (!((path.Count != 3) | isOpen))
		{
			return !IsVerySmallTriangle(outPt);
		}
		return true;
	}

	protected bool BuildPaths(Paths64 solutionClosed, Paths64 solutionOpen)
	{
		solutionClosed.Clear();
		solutionOpen.Clear();
		solutionClosed.EnsureCapacity(_outrecList.Count);
		solutionOpen.EnsureCapacity(_outrecList.Count);
		int num = 0;
		while (num < _outrecList.Count)
		{
			OutRec outRec = _outrecList[num++];
			if (outRec.pts == null)
			{
				continue;
			}
			Path64 path = new Path64();
			if (outRec.isOpen)
			{
				if (BuildPath(outRec.pts, ReverseSolution, isOpen: true, path))
				{
					solutionOpen.Add(path);
				}
				continue;
			}
			CleanCollinear(outRec);
			if (BuildPath(outRec.pts, ReverseSolution, isOpen: false, path))
			{
				solutionClosed.Add(path);
			}
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool CheckBounds(OutRec outrec)
	{
		if (outrec.pts == null)
		{
			return false;
		}
		if (!outrec.bounds.IsEmpty())
		{
			return true;
		}
		CleanCollinear(outrec);
		if (outrec.pts == null || !BuildPath(outrec.pts, ReverseSolution, isOpen: false, outrec.path))
		{
			return false;
		}
		outrec.bounds = InternalClipper.GetBounds(outrec.path);
		return true;
	}

	private bool CheckSplitOwner(OutRec outrec, List<int>? splits)
	{
		foreach (int split in splits)
		{
			OutRec outRec = _outrecList[split];
			if (outRec.pts == null && outRec.splits != null && CheckSplitOwner(outrec, outRec.splits))
			{
				return true;
			}
			outRec = GetRealOutRec(outRec);
			if (outRec == null || outRec == outrec || outRec.recursiveSplit == outrec)
			{
				continue;
			}
			outRec.recursiveSplit = outrec;
			if (outRec.splits != null && CheckSplitOwner(outrec, outRec.splits))
			{
				return true;
			}
			if (CheckBounds(outRec) && outRec.bounds.Contains(outrec.bounds) && Path1InsidePath2(outrec.pts, outRec.pts))
			{
				if (!IsValidOwner(outrec, outRec))
				{
					outRec.owner = outrec.owner;
				}
				outrec.owner = outRec;
				return true;
			}
		}
		return false;
	}

	private void RecursiveCheckOwners(OutRec outrec, PolyPathBase polypath)
	{
		if (outrec.polypath != null || outrec.bounds.IsEmpty())
		{
			return;
		}
		while (outrec.owner != null && (outrec.owner.splits == null || !CheckSplitOwner(outrec, outrec.owner.splits)) && (outrec.owner.pts == null || !CheckBounds(outrec.owner) || !Path1InsidePath2(outrec.pts, outrec.owner.pts)))
		{
			outrec.owner = outrec.owner.owner;
		}
		if (outrec.owner != null)
		{
			if (outrec.owner.polypath == null)
			{
				RecursiveCheckOwners(outrec.owner, polypath);
			}
			outrec.polypath = outrec.owner.polypath.AddChild(outrec.path);
		}
		else
		{
			outrec.polypath = polypath.AddChild(outrec.path);
		}
	}

	protected void BuildTree(PolyPathBase polytree, Paths64 solutionOpen)
	{
		polytree.Clear();
		solutionOpen.Clear();
		if (_hasOpenPaths)
		{
			solutionOpen.EnsureCapacity(_outrecList.Count);
		}
		int num = 0;
		while (num < _outrecList.Count)
		{
			OutRec outRec = _outrecList[num++];
			if (outRec.pts == null)
			{
				continue;
			}
			if (outRec.isOpen)
			{
				Path64 path = new Path64();
				if (BuildPath(outRec.pts, ReverseSolution, isOpen: true, path))
				{
					solutionOpen.Add(path);
				}
			}
			else if (CheckBounds(outRec))
			{
				RecursiveCheckOwners(outRec, polytree);
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Rect64 GetBounds()
	{
		Rect64 invalidRect = Clipper.InvalidRect64;
		foreach (Vertex vertex2 in _vertexList)
		{
			Vertex vertex = vertex2;
			do
			{
				if (vertex.pt.X < invalidRect.left)
				{
					invalidRect.left = vertex.pt.X;
				}
				if (vertex.pt.X > invalidRect.right)
				{
					invalidRect.right = vertex.pt.X;
				}
				if (vertex.pt.Y < invalidRect.top)
				{
					invalidRect.top = vertex.pt.Y;
				}
				if (vertex.pt.Y > invalidRect.bottom)
				{
					invalidRect.bottom = vertex.pt.Y;
				}
				vertex = vertex.next;
			}
			while (vertex != vertex2);
		}
		if (!invalidRect.IsEmpty())
		{
			return invalidRect;
		}
		return new Rect64(0L, 0L, 0L, 0L);
	}
}
