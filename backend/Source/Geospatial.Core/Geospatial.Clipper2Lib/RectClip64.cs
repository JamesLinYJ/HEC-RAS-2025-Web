using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Geospatial.Clipper2Lib;

public class RectClip64
{
	protected enum Location
	{
		left,
		top,
		right,
		bottom,
		inside
	}

	protected readonly Rect64 rect_;

	protected readonly Point64 mp_;

	protected readonly Path64 rectPath_;

	protected Rect64 pathBounds_;

	protected List<OutPt2?> results_;

	protected List<OutPt2?>[] edges_;

	protected int currIdx_;

	internal RectClip64(Rect64 rect)
	{
		currIdx_ = -1;
		rect_ = rect;
		mp_ = rect.MidPoint();
		rectPath_ = rect_.AsPath();
		results_ = new List<OutPt2>();
		edges_ = new List<OutPt2>[8];
		for (int i = 0; i < 8; i++)
		{
			edges_[i] = new List<OutPt2>();
		}
	}

	internal OutPt2 Add(Point64 pt, bool startingNewPath = false)
	{
		int count = results_.Count;
		OutPt2 outPt;
		if ((count == 0) | startingNewPath)
		{
			outPt = new OutPt2(pt);
			results_.Add(outPt);
			outPt.ownerIdx = count;
			outPt.prev = outPt;
			outPt.next = outPt;
		}
		else
		{
			count--;
			OutPt2 outPt2 = results_[count];
			if (outPt2.pt == pt)
			{
				return outPt2;
			}
			outPt = new OutPt2(pt)
			{
				ownerIdx = count,
				next = outPt2.next
			};
			outPt2.next.prev = outPt;
			outPt2.next = outPt;
			outPt.prev = outPt2;
			results_[count] = outPt;
		}
		return outPt;
	}

	private static bool Path1ContainsPath2(Path64 path1, Path64 path2)
	{
		int num = 0;
		foreach (Point64 item in path2)
		{
			switch (InternalClipper.PointInPolygon(item, path1))
			{
			case PointInPolygonResult.IsInside:
				num--;
				break;
			case PointInPolygonResult.IsOutside:
				num++;
				break;
			}
			if (Math.Abs(num) > 1)
			{
				break;
			}
		}
		return num <= 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsClockwise(Location prev, Location curr, Point64 prevPt, Point64 currPt, Point64 rectMidPoint)
	{
		if (AreOpposites(prev, curr))
		{
			return InternalClipper.CrossProduct(prevPt, rectMidPoint, currPt) < 0.0;
		}
		return HeadingClockwise(prev, curr);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool AreOpposites(Location prev, Location curr)
	{
		return Math.Abs(prev - curr) == 2;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool HeadingClockwise(Location prev, Location curr)
	{
		return (int)(prev + 1) % 4 == (int)curr;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Location GetAdjacentLocation(Location loc, bool isClockwise)
	{
		int num = (isClockwise ? 1 : 3);
		return (Location)((int)(loc + num) % 4);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static OutPt2? UnlinkOp(OutPt2 op)
	{
		if (op.next == op)
		{
			return null;
		}
		op.prev.next = op.next;
		op.next.prev = op.prev;
		return op.next;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static OutPt2? UnlinkOpBack(OutPt2 op)
	{
		if (op.next == op)
		{
			return null;
		}
		op.prev.next = op.next;
		op.next.prev = op.prev;
		return op.prev;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static uint GetEdgesForPt(Point64 pt, Rect64 rec)
	{
		uint num = 0u;
		if (pt.X == rec.left)
		{
			num = 1u;
		}
		else if (pt.X == rec.right)
		{
			num = 4u;
		}
		if (pt.Y == rec.top)
		{
			num += 2;
		}
		else if (pt.Y == rec.bottom)
		{
			num += 8;
		}
		return num;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsHeadingClockwise(Point64 pt1, Point64 pt2, int edgeIdx)
	{
		return edgeIdx switch
		{
			0 => pt2.Y < pt1.Y, 
			1 => pt2.X > pt1.X, 
			2 => pt2.Y > pt1.Y, 
			_ => pt2.X < pt1.X, 
		};
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool HasHorzOverlap(Point64 left1, Point64 right1, Point64 left2, Point64 right2)
	{
		if (left1.X < right2.X)
		{
			return right1.X > left2.X;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool HasVertOverlap(Point64 top1, Point64 bottom1, Point64 top2, Point64 bottom2)
	{
		if (top1.Y < bottom2.Y)
		{
			return bottom1.Y > top2.Y;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void AddToEdge(List<OutPt2?> edge, OutPt2 op)
	{
		if (op.edge == null)
		{
			op.edge = edge;
			edge.Add(op);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void UncoupleEdge(OutPt2 op)
	{
		if (op.edge == null)
		{
			return;
		}
		for (int i = 0; i < op.edge.Count; i++)
		{
			if (op.edge[i] == op)
			{
				op.edge[i] = null;
				break;
			}
		}
		op.edge = null;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void SetNewOwner(OutPt2 op, int newIdx)
	{
		op.ownerIdx = newIdx;
		for (OutPt2 next = op.next; next != op; next = next.next)
		{
			next.ownerIdx = newIdx;
		}
	}

	private void AddCorner(Location prev, Location curr)
	{
		Add(HeadingClockwise(prev, curr) ? rectPath_[(int)prev] : rectPath_[(int)curr]);
	}

	private void AddCorner(ref Location loc, bool isClockwise)
	{
		if (isClockwise)
		{
			Add(rectPath_[(int)loc]);
			loc = GetAdjacentLocation(loc, isClockwise: true);
		}
		else
		{
			loc = GetAdjacentLocation(loc, isClockwise: false);
			Add(rectPath_[(int)loc]);
		}
	}

	protected static bool GetLocation(Rect64 rec, Point64 pt, out Location loc)
	{
		if (pt.X == rec.left && pt.Y >= rec.top && pt.Y <= rec.bottom)
		{
			loc = Location.left;
			return false;
		}
		if (pt.X == rec.right && pt.Y >= rec.top && pt.Y <= rec.bottom)
		{
			loc = Location.right;
			return false;
		}
		if (pt.Y == rec.top && pt.X >= rec.left && pt.X <= rec.right)
		{
			loc = Location.top;
			return false;
		}
		if (pt.Y == rec.bottom && pt.X >= rec.left && pt.X <= rec.right)
		{
			loc = Location.bottom;
			return false;
		}
		if (pt.X < rec.left)
		{
			loc = Location.left;
		}
		else if (pt.X > rec.right)
		{
			loc = Location.right;
		}
		else if (pt.Y < rec.top)
		{
			loc = Location.top;
		}
		else if (pt.Y > rec.bottom)
		{
			loc = Location.bottom;
		}
		else
		{
			loc = Location.inside;
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsHorizontal(Point64 pt1, Point64 pt2)
	{
		return pt1.Y == pt2.Y;
	}

	private static bool GetSegmentIntersection(Point64 p1, Point64 p2, Point64 p3, Point64 p4, out Point64 ip)
	{
		double num = InternalClipper.CrossProduct(p1, p3, p4);
		double num2 = InternalClipper.CrossProduct(p2, p3, p4);
		if (num == 0.0)
		{
			ip = p1;
			if (num2 == 0.0)
			{
				return false;
			}
			if (p1 == p3 || p1 == p4)
			{
				return true;
			}
			if (IsHorizontal(p3, p4))
			{
				return p1.X > p3.X == p1.X < p4.X;
			}
			return p1.Y > p3.Y == p1.Y < p4.Y;
		}
		if (num2 == 0.0)
		{
			ip = p2;
			if (p2 == p3 || p2 == p4)
			{
				return true;
			}
			if (IsHorizontal(p3, p4))
			{
				return p2.X > p3.X == p2.X < p4.X;
			}
			return p2.Y > p3.Y == p2.Y < p4.Y;
		}
		if (num > 0.0 == num2 > 0.0)
		{
			ip = new Point64(0L, 0L);
			return false;
		}
		double num3 = InternalClipper.CrossProduct(p3, p1, p2);
		double num4 = InternalClipper.CrossProduct(p4, p1, p2);
		if (num3 == 0.0)
		{
			ip = p3;
			if (p3 == p1 || p3 == p2)
			{
				return true;
			}
			if (IsHorizontal(p1, p2))
			{
				return p3.X > p1.X == p3.X < p2.X;
			}
			return p3.Y > p1.Y == p3.Y < p2.Y;
		}
		if (num4 == 0.0)
		{
			ip = p4;
			if (p4 == p1 || p4 == p2)
			{
				return true;
			}
			if (IsHorizontal(p1, p2))
			{
				return p4.X > p1.X == p4.X < p2.X;
			}
			return p4.Y > p1.Y == p4.Y < p2.Y;
		}
		if (num3 > 0.0 == num4 > 0.0)
		{
			ip = new Point64(0L, 0L);
			return false;
		}
		return InternalClipper.GetSegmentIntersectPt(p1, p2, p3, p4, out ip);
	}

	protected static bool GetIntersection(Path64 rectPath, Point64 p, Point64 p2, ref Location loc, out Point64 ip)
	{
		ip = default;
		switch (loc)
		{
		case Location.left:
			if (GetSegmentIntersection(p, p2, rectPath[0], rectPath[3], out ip))
			{
				return true;
			}
			if (p.Y < rectPath[0].Y && GetSegmentIntersection(p, p2, rectPath[0], rectPath[1], out ip))
			{
				loc = Location.top;
				return true;
			}
			if (!GetSegmentIntersection(p, p2, rectPath[2], rectPath[3], out ip))
			{
				return false;
			}
			loc = Location.bottom;
			return true;
		case Location.right:
			if (GetSegmentIntersection(p, p2, rectPath[1], rectPath[2], out ip))
			{
				return true;
			}
			if (p.Y < rectPath[0].Y && GetSegmentIntersection(p, p2, rectPath[0], rectPath[1], out ip))
			{
				loc = Location.top;
				return true;
			}
			if (!GetSegmentIntersection(p, p2, rectPath[2], rectPath[3], out ip))
			{
				return false;
			}
			loc = Location.bottom;
			return true;
		case Location.top:
			if (GetSegmentIntersection(p, p2, rectPath[0], rectPath[1], out ip))
			{
				return true;
			}
			if (p.X < rectPath[0].X && GetSegmentIntersection(p, p2, rectPath[0], rectPath[3], out ip))
			{
				loc = Location.left;
				return true;
			}
			if (p.X <= rectPath[1].X || !GetSegmentIntersection(p, p2, rectPath[1], rectPath[2], out ip))
			{
				return false;
			}
			loc = Location.right;
			return true;
		case Location.bottom:
			if (GetSegmentIntersection(p, p2, rectPath[2], rectPath[3], out ip))
			{
				return true;
			}
			if (p.X < rectPath[3].X && GetSegmentIntersection(p, p2, rectPath[0], rectPath[3], out ip))
			{
				loc = Location.left;
				return true;
			}
			if (p.X <= rectPath[2].X || !GetSegmentIntersection(p, p2, rectPath[1], rectPath[2], out ip))
			{
				return false;
			}
			loc = Location.right;
			return true;
		default:
			if (GetSegmentIntersection(p, p2, rectPath[0], rectPath[3], out ip))
			{
				loc = Location.left;
				return true;
			}
			if (GetSegmentIntersection(p, p2, rectPath[0], rectPath[1], out ip))
			{
				loc = Location.top;
				return true;
			}
			if (GetSegmentIntersection(p, p2, rectPath[1], rectPath[2], out ip))
			{
				loc = Location.right;
				return true;
			}
			if (!GetSegmentIntersection(p, p2, rectPath[2], rectPath[3], out ip))
			{
				return false;
			}
			loc = Location.bottom;
			return true;
		}
	}

	protected void GetNextLocation(Path64 path, ref Location loc, ref int i, int highI)
	{
		switch (loc)
		{
		default:
			return;
		case Location.left:
			while (i <= highI && path[i].X <= rect_.left)
			{
				i++;
			}
			if (i <= highI)
			{
				if (path[i].X >= rect_.right)
				{
					loc = Location.right;
				}
				else if (path[i].Y <= rect_.top)
				{
					loc = Location.top;
				}
				else if (path[i].Y >= rect_.bottom)
				{
					loc = Location.bottom;
				}
				else
				{
					loc = Location.inside;
				}
			}
			return;
		case Location.top:
			while (i <= highI && path[i].Y <= rect_.top)
			{
				i++;
			}
			if (i <= highI)
			{
				if (path[i].Y >= rect_.bottom)
				{
					loc = Location.bottom;
				}
				else if (path[i].X <= rect_.left)
				{
					loc = Location.left;
				}
				else if (path[i].X >= rect_.right)
				{
					loc = Location.right;
				}
				else
				{
					loc = Location.inside;
				}
			}
			return;
		case Location.right:
			while (i <= highI && path[i].X >= rect_.right)
			{
				i++;
			}
			if (i <= highI)
			{
				if (path[i].X <= rect_.left)
				{
					loc = Location.left;
				}
				else if (path[i].Y <= rect_.top)
				{
					loc = Location.top;
				}
				else if (path[i].Y >= rect_.bottom)
				{
					loc = Location.bottom;
				}
				else
				{
					loc = Location.inside;
				}
			}
			return;
		case Location.bottom:
			while (i <= highI && path[i].Y >= rect_.bottom)
			{
				i++;
			}
			if (i <= highI)
			{
				if (path[i].Y <= rect_.top)
				{
					loc = Location.top;
				}
				else if (path[i].X <= rect_.left)
				{
					loc = Location.left;
				}
				else if (path[i].X >= rect_.right)
				{
					loc = Location.right;
				}
				else
				{
					loc = Location.inside;
				}
			}
			return;
		case Location.inside:
			break;
		}
		while (i <= highI)
		{
			if (path[i].X < rect_.left)
			{
				loc = Location.left;
				break;
			}
			if (path[i].X > rect_.right)
			{
				loc = Location.right;
				break;
			}
			if (path[i].Y > rect_.bottom)
			{
				loc = Location.bottom;
				break;
			}
			if (path[i].Y < rect_.top)
			{
				loc = Location.top;
				break;
			}
			Add(path[i]);
			i++;
		}
	}

	private static bool StartLocsAreClockwise(List<Location> startLocs)
	{
		int num = 0;
		for (int i = 1; i < startLocs.Count; i++)
		{
			switch (startLocs[i] - startLocs[i - 1])
			{
			case -1:
				num--;
				break;
			case 1:
				num++;
				break;
			case -3:
				num++;
				break;
			case 3:
				num--;
				break;
			}
		}
		return num > 0;
	}

	private void ExecuteInternal(Path64 path)
	{
		if (path.Count < 3 || rect_.IsEmpty())
		{
			return;
		}
		List<Location> list = new List<Location>();
		Location location = Location.inside;
		Location loc = location;
		Location loc2 = location;
		int num = path.Count - 1;
		int num2;
		if (!GetLocation(rect_, path[num], out var loc3))
		{
			num2 = num - 1;
			while (num2 >= 0 && !GetLocation(rect_, path[num2], out loc2))
			{
				num2--;
			}
			if (num2 < 0)
			{
				{
					foreach (Point64 item in path)
					{
						Add(item);
					}
					return;
				}
			}
			if (loc2 == Location.inside)
			{
				loc3 = Location.inside;
			}
		}
		Location location2 = loc3;
		num2 = 0;
		while (num2 <= num)
		{
			loc2 = loc3;
			Location location3 = loc;
			GetNextLocation(path, ref loc3, ref num2, num);
			if (num2 > num)
			{
				break;
			}
			Point64 point = ((num2 == 0) ? path[num] : path[num2 - 1]);
			loc = loc3;
			if (!GetIntersection(rectPath_, path[num2], point, ref loc, out var ip))
			{
				if (location3 == Location.inside)
				{
					bool isClockwise = IsClockwise(loc2, loc3, point, path[num2], mp_);
					do
					{
						list.Add(loc2);
						loc2 = GetAdjacentLocation(loc2, isClockwise);
					}
					while (loc2 != loc3);
					loc = location3;
				}
				else if (loc2 != Location.inside && loc2 != loc3)
				{
					bool isClockwise2 = IsClockwise(loc2, loc3, point, path[num2], mp_);
					do
					{
						AddCorner(ref loc2, isClockwise2);
					}
					while (loc2 != loc3);
				}
				num2++;
				continue;
			}
			if (loc3 == Location.inside)
			{
				if (location == Location.inside)
				{
					location = loc;
					list.Add(loc2);
				}
				else if (loc2 != loc)
				{
					bool isClockwise3 = IsClockwise(loc2, loc, point, path[num2], mp_);
					do
					{
						AddCorner(ref loc2, isClockwise3);
					}
					while (loc2 != loc);
				}
			}
			else if (loc2 != Location.inside)
			{
				loc3 = loc2;
				GetIntersection(rectPath_, point, path[num2], ref loc3, out var ip2);
				if (location3 != Location.inside && location3 != loc3)
				{
					AddCorner(location3, loc3);
				}
				if (location == Location.inside)
				{
					location = loc3;
					list.Add(loc2);
				}
				loc3 = loc;
				Add(ip2);
				if (ip == ip2)
				{
					GetLocation(rect_, path[num2], out loc3);
					AddCorner(loc, loc3);
					loc = loc3;
					continue;
				}
			}
			else
			{
				loc3 = loc;
				if (location == Location.inside)
				{
					location = loc;
				}
			}
			Add(ip);
		}
		if (location == Location.inside)
		{
			if (location2 != Location.inside && pathBounds_.Contains(rect_) && Path1ContainsPath2(path, rectPath_))
			{
				bool flag = StartLocsAreClockwise(list);
				for (int i = 0; i < 4; i++)
				{
					int num3 = (flag ? i : (3 - i));
					Add(rectPath_[num3]);
					AddToEdge(edges_[num3 * 2], results_[0]);
				}
			}
		}
		else
		{
			if (loc3 == Location.inside || (loc3 == location && list.Count <= 2))
			{
				return;
			}
			if (list.Count > 0)
			{
				loc2 = loc3;
				foreach (Location item2 in list)
				{
					if (loc2 != item2)
					{
						AddCorner(ref loc2, HeadingClockwise(loc2, item2));
						loc2 = item2;
					}
				}
				loc3 = loc2;
			}
			if (loc3 != location)
			{
				AddCorner(ref loc3, HeadingClockwise(loc3, location));
			}
		}
	}

	public Paths64 Execute(Paths64 paths)
	{
		Paths64 paths2 = new Paths64();
		if (rect_.IsEmpty())
		{
			return paths2;
		}
		foreach (Path64 path2 in paths)
		{
			if (path2.Count < 3)
			{
				continue;
			}
			pathBounds_ = Clipper.GetBounds(path2);
			if (!rect_.Intersects(pathBounds_))
			{
				continue;
			}
			if (rect_.Contains(pathBounds_))
			{
				paths2.Add(path2);
				continue;
			}
			ExecuteInternal(path2);
			CheckEdges();
			for (int i = 0; i < 4; i++)
			{
				TidyEdgePair(i, edges_[i * 2], edges_[i * 2 + 1]);
			}
			foreach (OutPt2 item in results_)
			{
				Path64 path = GetPath(item);
				if (path.Count > 0)
				{
					paths2.Add(path);
				}
			}
			results_.Clear();
			for (int j = 0; j < 8; j++)
			{
				edges_[j].Clear();
			}
		}
		return paths2;
	}

	private void CheckEdges()
	{
		for (int i = 0; i < results_.Count; i++)
		{
			OutPt2 outPt = results_[i];
			OutPt2 outPt2 = outPt;
			if (outPt == null)
			{
				continue;
			}
			do
			{
				if (InternalClipper.IsCollinear(outPt2.prev.pt, outPt2.pt, outPt2.next.pt))
				{
					if (outPt2 == outPt)
					{
						outPt2 = UnlinkOpBack(outPt2);
						if (outPt2 == null)
						{
							break;
						}
						outPt = outPt2.prev;
					}
					else
					{
						outPt2 = UnlinkOpBack(outPt2);
						if (outPt2 == null)
						{
							break;
						}
					}
				}
				else
				{
					outPt2 = outPt2.next;
				}
			}
			while (outPt2 != outPt);
			if (outPt2 == null)
			{
				results_[i] = null;
				continue;
			}
			results_[i] = outPt2;
			uint num = GetEdgesForPt(outPt.prev.pt, rect_);
			outPt2 = outPt;
			do
			{
				uint edgesForPt = GetEdgesForPt(outPt2.pt, rect_);
				if (edgesForPt != 0 && outPt2.edge == null)
				{
					uint num2 = num & edgesForPt;
					for (int j = 0; j < 4; j++)
					{
						if ((num2 & (1 << j)) != 0L)
						{
							if (IsHeadingClockwise(outPt2.prev.pt, outPt2.pt, j))
							{
								AddToEdge(edges_[j * 2], outPt2);
							}
							else
							{
								AddToEdge(edges_[j * 2 + 1], outPt2);
							}
						}
					}
				}
				num = edgesForPt;
				outPt2 = outPt2.next;
			}
			while (outPt2 != outPt);
		}
	}

	private void TidyEdgePair(int idx, List<OutPt2?> cw, List<OutPt2?> ccw)
	{
		if (ccw.Count == 0)
		{
			return;
		}
		bool flag = idx == 1 || idx == 3;
		bool flag2 = idx == 1 || idx == 2;
		int num = 0;
		int i = 0;
		while (num < cw.Count)
		{
			OutPt2 outPt = cw[num];
			if (outPt == null || outPt.next == outPt.prev)
			{
				cw[num++] = null;
				i = 0;
				continue;
			}
			int count;
			for (count = ccw.Count; i < count && (ccw[i] == null || ccw[i].next == ccw[i].prev); i++)
			{
			}
			if (i == count)
			{
				num++;
				i = 0;
				continue;
			}
			OutPt2 outPt2;
			OutPt2 outPt3;
			OutPt2 outPt4;
			if (flag2)
			{
				outPt = cw[num].prev;
				outPt2 = cw[num];
				outPt3 = ccw[i];
				outPt4 = ccw[i].prev;
			}
			else
			{
				outPt = cw[num];
				outPt2 = cw[num].prev;
				outPt3 = ccw[i].prev;
				outPt4 = ccw[i];
			}
			if ((flag && !HasHorzOverlap(outPt.pt, outPt2.pt, outPt3.pt, outPt4.pt)) || (!flag && !HasVertOverlap(outPt.pt, outPt2.pt, outPt3.pt, outPt4.pt)))
			{
				i++;
				continue;
			}
			bool flag3 = cw[num].ownerIdx != ccw[i].ownerIdx;
			if (flag3)
			{
				results_[outPt3.ownerIdx] = null;
				SetNewOwner(outPt3, outPt.ownerIdx);
			}
			if (flag2)
			{
				outPt.next = outPt3;
				outPt3.prev = outPt;
				outPt2.prev = outPt4;
				outPt4.next = outPt2;
			}
			else
			{
				outPt.prev = outPt3;
				outPt3.next = outPt;
				outPt2.next = outPt4;
				outPt4.prev = outPt2;
			}
			if (!flag3)
			{
				int count2 = results_.Count;
				results_.Add(outPt2);
				SetNewOwner(outPt2, count2);
			}
			OutPt2 outPt5;
			OutPt2 outPt6;
			if (flag2)
			{
				outPt5 = outPt3;
				outPt6 = outPt2;
			}
			else
			{
				outPt5 = outPt;
				outPt6 = outPt4;
			}
			results_[outPt5.ownerIdx] = outPt5;
			results_[outPt6.ownerIdx] = outPt6;
			bool flag4;
			bool flag5;
			if (flag)
			{
				flag4 = outPt5.pt.X > outPt5.prev.pt.X;
				flag5 = outPt6.pt.X > outPt6.prev.pt.X;
			}
			else
			{
				flag4 = outPt5.pt.Y > outPt5.prev.pt.Y;
				flag5 = outPt6.pt.Y > outPt6.prev.pt.Y;
			}
			if (outPt5.next == outPt5.prev || outPt5.pt == outPt5.prev.pt)
			{
				if (flag5 == flag2)
				{
					cw[num] = outPt6;
					ccw[i++] = null;
				}
				else
				{
					ccw[i] = outPt6;
					cw[num++] = null;
				}
			}
			else if (outPt6.next == outPt6.prev || outPt6.pt == outPt6.prev.pt)
			{
				if (flag4 == flag2)
				{
					cw[num] = outPt5;
					ccw[i++] = null;
				}
				else
				{
					ccw[i] = outPt5;
					cw[num++] = null;
				}
			}
			else if (flag4 == flag5)
			{
				if (flag4 == flag2)
				{
					cw[num] = outPt5;
					UncoupleEdge(outPt6);
					AddToEdge(cw, outPt6);
					ccw[i++] = null;
				}
				else
				{
					cw[num++] = null;
					ccw[i] = outPt6;
					UncoupleEdge(outPt5);
					AddToEdge(ccw, outPt5);
					i = 0;
				}
			}
			else
			{
				if (flag4 == flag2)
				{
					cw[num] = outPt5;
				}
				else
				{
					ccw[i] = outPt5;
				}
				if (flag5 == flag2)
				{
					cw[num] = outPt6;
				}
				else
				{
					ccw[i] = outPt6;
				}
			}
		}
	}

	private static Path64 GetPath(OutPt2? op)
	{
		Path64 path = new Path64();
		if (op == null || op.prev == op.next)
		{
			return path;
		}
		OutPt2 outPt = op.next;
		while (outPt != null && outPt != op)
		{
			if (InternalClipper.IsCollinear(outPt.prev.pt, outPt.pt, outPt.next.pt))
			{
				op = outPt.prev;
				outPt = UnlinkOp(outPt);
			}
			else
			{
				outPt = outPt.next;
			}
		}
		if (outPt == null)
		{
			return new Path64();
		}
		path.Add(op.pt);
		for (outPt = op.next; outPt != op; outPt = outPt.next)
		{
			path.Add(outPt.pt);
		}
		return path;
	}
}
