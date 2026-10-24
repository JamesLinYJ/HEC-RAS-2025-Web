using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Geospatial.DataStructures;
using Geospatial.Rasters;
using Geospatial.Vectors.Polylines;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.DataStructures;
using Utility.Extensions;

namespace Geospatial.Vectors;

[DebuggerDisplay("{PointCount} points, Length={ComputeLength()}")]
public class Polyline : PointCollection, IReadOnlyList<Segment>, IEnumerable<Segment>, IEnumerable, IReadOnlyCollection<Segment>, ICloneable
{
	private readonly struct BendStation(Point point, double station, double neighborStation)
	{
		public readonly Point Point = point;

		public readonly double Station = station;

		public readonly double NeighborStation = neighborStation;
	}

	private IReadOnlyList<Segment> _s2d;

	private IReadOnlyList<Segment3D> _s3d;

	private IReadOnlyList<Segment4D> _s4d;

	private Versioned<double> _lenVer;

	public virtual int SegmentCount => _points.Count - 1;

	public IReadOnlyList<Segment> Segments => LazyThreadsafe<IReadOnlyList<Segment>>.AllocateOnce(ref _s2d, this, this, (Polyline line) => new VirtualSegmentCollection(line));

	public IReadOnlyList<Segment3D> Segment3D => LazyThreadsafe<IReadOnlyList<Geospatial.Vectors.Segment3D>>.AllocateOnce(ref _s3d, this, this, (Polyline line) => new VirtualSegment3DCollection(line));

	public IReadOnlyList<Segment4D> Segment4D => LazyThreadsafe<IReadOnlyList<Geospatial.Vectors.Segment4D>>.AllocateOnce(ref _s4d, this, this, (Polyline line) => new VirtualSegment4DCollection(line));

	Segment IReadOnlyList<Segment>.this[int index] => Segments[index];

	public double Length => LazyThreadsafe<Versioned<double>>.AllocateOnce(ref _lenVer, this, () => new Versioned<double>()).Get(VersionXY, this, ComputeLength);

	public static bool IsValidPolyline(Polyline pl)
	{
		if (pl != null)
		{
			return pl.Points.Count >= 2;
		}
		return false;
	}

	public bool IsValidPolyline()
	{
		return IsValidPolyline(this);
	}

	public static Polyline NullPolyline()
	{
		return new Polyline();
	}

	public Polyline()
	{
		Initialize();
	}

	public Polyline(int capacity)
		: base(capacity)
	{
		Initialize();
	}

	public Polyline(PointCollection points)
		: base(points)
	{
		Initialize();
	}

	public Polyline(Point[] points)
		: this((IEnumerable<Point>)points)
	{
	}

	public Polyline(Memory<Point> points)
		: base(points)
	{
		Initialize();
	}

	public Polyline(Memory<Point> points, Memory<double> z)
		: base(points, z)
	{
		Initialize();
	}

	public Polyline(Memory<Point> points, Memory<double> z, Memory<double> m)
		: base(points, z, m)
	{
		Initialize();
	}

	public Polyline(List<Point> points)
		: base(points)
	{
		Initialize();
	}

	public Polyline(List<Point> points, List<double> z)
		: base(points, z)
	{
		Initialize();
	}

	public Polyline(List<Point> points, List<double> z, List<double> m)
		: base(points, z, m)
	{
		Initialize();
	}

	public Polyline(IEnumerable<Point> points)
		: base(points)
	{
		Initialize();
	}

	private void Initialize()
	{
	}

	public new static Polyline CreateWithBuffer(PointCollection points)
	{
		Polyline polyline = new Polyline();
		polyline.AssignBuffers(points);
		return polyline;
	}

	public new static Polyline CreateWithBuffer(List<Point> points)
	{
		Polyline polyline = new Polyline();
		polyline.AssignBuffers(points, null, null);
		return polyline;
	}

	public new static Polyline CreateWithBuffer(Point[] points, double[] z = null, double[] m = null)
	{
		Polyline polyline = new Polyline(0);
		polyline.AssignBuffers(points, z, m);
		return polyline;
	}

	public static Polyline FromSegment(Segment seg)
	{
		return new Polyline(2) { seg.A, seg.B };
	}

	public static Polyline FromFunction(double minX, double maxX, int nPoints, Func<double, double> function)
	{
		if (nPoints < 2)
		{
			throw new ArgumentException("nPoints");
		}
		int nPointsOffset = nPoints - 1;
		double maxXAboveMinXOverNPointsOffset = (maxX - minX) / (double)nPointsOffset;
		Polyline polyline = new Polyline(nPoints);
		for (int i = 0; i < nPoints; i++)
		{
			double minXTerm = minX + maxXAboveMinXOverNPointsOffset * (double)i;
			double y = function(minXTerm);
			polyline.Add(minXTerm, y);
		}
		return polyline;
	}

	public static Polyline SinWave(int nPoints)
	{
		return FromFunction(0.0, Math.PI * 2.0, nPoints, (double x) => Math.Sin(x));
	}

	public static Polyline Concatenate(List<Polyline> polylines, double tolerance = 0.0)
	{
		List<Point> points = new List<Point>();
		foreach (Polyline polyline in polylines)
		{
			if (points.Count == 0)
			{
				points.AddRange(polyline.Points);
				continue;
			}
			points.RemoveAt(points.Count - 1);
			points.AddRange(polyline.Points);
		}
		return new Polyline(points);
	}

	public bool Intersects(Polyline polyline)
	{
		return GetFirstIntersection(polyline).HasValue;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static LineIntersection? GetFirstIntersection_OpOuter(in Segment seg, ref FastSegmentIterator thatFsi, int a)
	{
		for (int i = 0; i < thatFsi.CountWhenOpen; i++)
		{
			LineIntersection? firstIntersection_OpInner = GetFirstIntersection_OpInner(in seg, in thatFsi[i], a, i);
			if (firstIntersection_OpInner.HasValue)
			{
				return firstIntersection_OpInner;
			}
		}
		if (thatFsi.ConnectsEndToStart)
		{
			return GetFirstIntersection_OpInner(in seg, thatFsi.GetClosingSegment(), a, thatFsi.CountWhenOpen);
		}
		return null;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static LineIntersection? GetFirstIntersection_OpInner(in Segment aSeg, in Segment bSeg, int a, int b)
	{
		if (aSeg.Intersects(in bSeg, out var x, out var y, out var t, out var t2))
		{
			return new LineIntersection(x, y, a, t, b, t2);
		}
		return null;
	}

	public LineIntersection? GetFirstIntersection(Polyline pline)
	{
		if (!Extent.Overlaps(pline.Extent))
		{
			return null;
		}
		FastSegmentIterator fastSegmentIterator = new FastSegmentIterator(this);
		FastSegmentIterator thatFsi = new FastSegmentIterator(pline);
		for (int i = 0; i < fastSegmentIterator.CountWhenOpen; i++)
		{
			LineIntersection? firstIntersection_OpOuter = GetFirstIntersection_OpOuter(in fastSegmentIterator[i], ref thatFsi, i);
			if (firstIntersection_OpOuter.HasValue)
			{
				return firstIntersection_OpOuter;
			}
		}
		if (fastSegmentIterator.ConnectsEndToStart)
		{
			return GetFirstIntersection_OpOuter(fastSegmentIterator.GetClosingSegment(), ref thatFsi, fastSegmentIterator.CountWhenOpen);
		}
		return null;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void GetAllIntersections_OpOuter(in Segment seg, ref FastSegmentIterator thatFsi, List<LineIntersection> retn, ref Point prevIntersection, int a)
	{
		for (int i = 0; i < thatFsi.CountWhenOpen; i++)
		{
			GetAllIntersections_OpInner(in seg, in thatFsi[i], retn, ref prevIntersection, a, i);
		}
		if (thatFsi.ConnectsEndToStart)
		{
			GetAllIntersections_OpInner(in seg, thatFsi.GetClosingSegment(), retn, ref prevIntersection, a, thatFsi.CountWhenOpen);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void GetAllIntersections_OpInner(in Segment segA, in Segment segB, List<LineIntersection> retn, ref Point prevIntersection, int a, int b)
	{
		if (segA.Intersects(in segB, out var x, out var y, out var t, out var t2) && (prevIntersection.X != x || prevIntersection.Y != y))
		{
			prevIntersection = new Point(x, y);
			retn.Add(new LineIntersection(x, y, a, t, b, t2));
		}
	}

	public List<LineIntersection> GetAllIntersections(Polyline pline)
	{
		List<LineIntersection> lineIntersections = new List<LineIntersection>();
		Point prevIntersection = Point.NullPoint;
		FastSegmentIterator fastSegmentIterator = new FastSegmentIterator(this);
		FastSegmentIterator thatFsi = new FastSegmentIterator(pline);
		for (int i = 0; i < fastSegmentIterator.CountWhenOpen; i++)
		{
			GetAllIntersections_OpOuter(in fastSegmentIterator[i], ref thatFsi, lineIntersections, ref prevIntersection, i);
		}
		if (fastSegmentIterator.ConnectsEndToStart)
		{
			GetAllIntersections_OpOuter(fastSegmentIterator.GetClosingSegment(), ref thatFsi, lineIntersections, ref prevIntersection, fastSegmentIterator.CountWhenOpen);
		}
		return lineIntersections;
	}

	public List<LineIntersection> GetAllSelfIntersections()
	{
		List<LineIntersection> lineIntersections = new List<LineIntersection>();
		SpatialIndex<int> spatialIndex = GenerateSegmentSpatialIndex();
		HashSet<Tuple<int, int>> valueTupleSet = new HashSet<Tuple<int, int>>();
		ThreadLocal<List<LineIntersection>> tlLineIntersectionList = new ThreadLocal<List<LineIntersection>>(() => new List<LineIntersection>(), trackAllValues: true);
		System.Threading.Tasks.Parallel.ForEach(spatialIndex.Bins, (List<int> bin) =>
		{
			List<LineIntersection> lineIntersectionsList = tlLineIntersectionList.Value;
			for (int i = 0; i <= bin.Count - 2; i++)
			{
				int bin2 = bin[i];
				Segment segment = Segments[bin2];
				for (int j = i + 1; j < bin.Count; j++)
				{
					int bin3 = bin[j];
					if (bin3 != bin2 && bin3 != bin2 - 1 && bin3 != bin2 + 1 && segment.Intersects(Segments[bin3], out var x, out var y, out var t, out var t2))
					{
						lineIntersectionsList.Add(new LineIntersection(x, y, bin2, t, bin3, t2));
					}
				}
			}
		});
		foreach (List<LineIntersection> lineIntersections2 in tlLineIntersectionList.Values)
		{
			foreach (LineIntersection lineIntersection in lineIntersections2)
			{
				int segmentIndex = lineIntersection.AIntersection.SegmentIndex;
				int segmentIndex2 = lineIntersection.BIntersection.SegmentIndex;
				Tuple<int, int> segmentIndexSegmentIndex2Term = ((segmentIndex >= segmentIndex2) ? new Tuple<int, int>(segmentIndex2, segmentIndex) : new Tuple<int, int>(segmentIndex, segmentIndex2));
				if (valueTupleSet.Add(segmentIndexSegmentIndex2Term))
				{
					lineIntersections.Add(lineIntersection);
				}
			}
		}
		return lineIntersections;
	}

	public static List<LineIntersection> GetAllSelfIntersections_Slow(IReadOnlyList<Point> pts)
	{
		VirtualSegmentCollection vsg = new VirtualSegmentCollection(pts);
		List<LineIntersection> lineIntersections = new List<LineIntersection>();
		SpatialIndex<int> spatialIndex = GenerateSegmentSpatialIndex_Slow(vsg);
		HashSet<Tuple<int, int>> valueTupleSet = new HashSet<Tuple<int, int>>();
		ThreadLocal<List<LineIntersection>> tlLineIntersectionList = new ThreadLocal<List<LineIntersection>>(() => new List<LineIntersection>(), trackAllValues: true);
		System.Threading.Tasks.Parallel.ForEach(spatialIndex.Bins, (List<int> bin) =>
		{
			List<LineIntersection> lineIntersectionsList = tlLineIntersectionList.Value;
			for (int i = 0; i <= bin.Count - 2; i++)
			{
				int bin2 = bin[i];
				Segment segment = vsg[bin2];
				for (int j = i + 1; j < bin.Count; j++)
				{
					int bin3 = bin[j];
					if (bin3 != bin2 && bin3 != bin2 - 1 && bin3 != bin2 + 1 && segment.Intersects(vsg[bin3], out var x, out var y, out var t, out var t2))
					{
						lineIntersectionsList.Add(new LineIntersection(x, y, bin2, t, bin3, t2));
					}
				}
			}
		});
		foreach (List<LineIntersection> lineIntersections2 in tlLineIntersectionList.Values)
		{
			foreach (LineIntersection lineIntersection in lineIntersections2)
			{
				int segmentIndex = lineIntersection.AIntersection.SegmentIndex;
				int segmentIndex2 = lineIntersection.BIntersection.SegmentIndex;
				Tuple<int, int> segmentIndexSegmentIndex2Term = ((segmentIndex >= segmentIndex2) ? new Tuple<int, int>(segmentIndex2, segmentIndex) : new Tuple<int, int>(segmentIndex, segmentIndex2));
				if (valueTupleSet.Add(segmentIndexSegmentIndex2Term))
				{
					lineIntersections.Add(lineIntersection);
				}
			}
		}
		return lineIntersections;
	}

	public static bool Conflict(Polyline plineA, Polyline plineB, double Epsilon = 1E-06)
	{
		if (plineA.PointCount == 2 && plineB.PointCount == 2)
		{
			return false;
		}
		for (int i = 0; i < plineA.SegmentCount; i++)
		{
			Segment segment = plineA.Segments[i];
			for (int j = 0; j < plineB.SegmentCount; j++)
			{
				Segment seg = plineB.Segments[j];
				if (segment.IntersectPointCheckNodes(seg, out var _, out var _, out var t, out var t2, Epsilon))
				{
					bool segmentCountPlineATerm = (i == 0 && t == 0.0) || (i == plineA.SegmentCount - 1 && t == 1.0);
					bool t2IsZero = (j == 0 && t2 == 0.0) || (j == plineB.SegmentCount - 1 && t2 == 1.0);
					if (!(segmentCountPlineATerm & t2IsZero))
					{
						return true;
					}
				}
			}
		}
		return false;
	}

	public bool Intersects(Segment seg)
	{
		return GetFirstIntersection(seg).HasValue;
	}

	public LineIntersection? GetFirstIntersection(Segment seg)
	{
		using (IEnumerator<LineIntersection> allIntersections = GetAllIntersections(seg).GetEnumerator())
		{
			if (allIntersections.MoveNext())
			{
				return allIntersections.Current;
			}
		}
		return null;
	}

	public IEnumerable<LineIntersection> GetAllIntersections(Segment seg)
	{
		for (int a = 0; a < SegmentCount; a++)
		{
			if (Segments[a].Intersects(in seg, out var x, out var y, out var t, out var t2))
			{
				yield return new LineIntersection(x, y, a, t, -1, t2);
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool Intersects_Op(in FastSegmentIterator meSegs, in Segment themSeg, double nearDistance)
	{
		return Intersects(in meSegs, in themSeg, nearDistance);
	}

	public bool Intersects(Polyline polyline, double nearDistance)
	{
		FastSegmentIterator fastSegmentIterator = new FastSegmentIterator(polyline);
		FastSegmentIterator meSegs = new FastSegmentIterator(this);
		int countWhenOpen = fastSegmentIterator.CountWhenOpen;
		for (int i = 0; i < countWhenOpen; i++)
		{
			if (Intersects_Op(in meSegs, in fastSegmentIterator[i], nearDistance))
			{
				return true;
			}
		}
		if (fastSegmentIterator.ConnectsEndToStart)
		{
			return Intersects_Op(in meSegs, fastSegmentIterator.GetClosingSegment(), nearDistance);
		}
		return false;
	}

	public bool Intersects(Segment seg, double nearDistance)
	{
		for (int i = 0; i < SegmentCount; i++)
		{
			Segment segment = Segments[i];
			if (segment.Intersects(seg))
			{
				return true;
			}
			Point a = seg.A;
			Point b = seg.B;
			Point a2 = segment.A;
			Point b2 = segment.B;
			if (segment.DistanceToPoint(a) < nearDistance)
			{
				return true;
			}
			if (segment.DistanceToPoint(b) < nearDistance)
			{
				return true;
			}
			if (seg.DistanceToPoint(a2) < nearDistance)
			{
				return true;
			}
			if (seg.DistanceToPoint(b2) < nearDistance)
			{
				return true;
			}
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool Intersects_Op(in Segment mySeg, in Segment seg, double nearDistance)
	{
		if (mySeg.Intersects(seg))
		{
			return true;
		}
		Point a = seg.A;
		Point b = seg.B;
		Point a2 = mySeg.A;
		Point b2 = mySeg.B;
		if (mySeg.DistanceToPoint(a) < nearDistance)
		{
			return true;
		}
		if (mySeg.DistanceToPoint(b) < nearDistance)
		{
			return true;
		}
		if (seg.DistanceToPoint(a2) < nearDistance)
		{
			return true;
		}
		if (seg.DistanceToPoint(b2) < nearDistance)
		{
			return true;
		}
		return false;
	}

	public static bool Intersects(in FastSegmentIterator segs, in Segment seg, double nearDistance)
	{
		int countWhenOpen = segs.CountWhenOpen;
		for (int i = 0; i < countWhenOpen; i++)
		{
			if (Intersects_Op(in segs[i], in seg, nearDistance))
			{
				return true;
			}
		}
		if (segs.ConnectsEndToStart)
		{
			return Intersects_Op(segs.GetClosingSegment(), in seg, nearDistance);
		}
		return false;
	}

	public bool Intersects(Point pt, double nearDistance)
	{
		FastSegmentIterator fastSegmentIterator = new FastSegmentIterator(this);
		for (int i = 0; i < fastSegmentIterator.CountWhenOpen; i++)
		{
			if (fastSegmentIterator[i].CloseTo(pt, nearDistance))
			{
				return true;
			}
		}
		if (fastSegmentIterator.ConnectsEndToStart)
		{
			return fastSegmentIterator.GetClosingSegment().CloseTo(pt, nearDistance);
		}
		return false;
	}

	public virtual int PreviousSegmentIndex(int segIdx)
	{
		return segIdx - 1;
	}

	public virtual int NextSegmentIndex(int segIdx)
	{
		int segIdxOffset = segIdx + 1;
		if (segIdxOffset >= SegmentCount)
		{
			return -1;
		}
		return segIdxOffset;
	}

	public bool MBROverlaps(Extent ext)
	{
		return Extent.Overlaps(in ext);
	}

	private double ComputeLength()
	{
		return ComputeLength(this);
	}

	private static double ComputeLength(Polyline pl)
	{
		return ComputeLength(pl.GetPointBufferUnsafe().Span);
	}

	public static double ComputeLength(Span<Point> pl)
	{
		double segmentLengthSum = 0.0;
		for (int i = 0; i < pl.Length - 1; i++)
		{
			segmentLengthSum += new Segment(pl[i], pl[i + 1]).Length;
		}
		return segmentLengthSum;
	}

	public double[] CumulativeSegmentLengths()
	{
		double[] doubleValues = new double[SegmentCount];
		CumulativeSegmentLengths(doubleValues.AsSpan());
		return doubleValues;
	}

	public void CumulativeSegmentLengths(Span<double> lengths)
	{
		if (lengths.Length != SegmentCount)
		{
			throw new ArgumentException("Incorrectly sized input buffer");
		}
		FastSegmentIterator fastSegmentIterator = new FastSegmentIterator(this);
		double lengths2 = 0.0;
		for (int i = 0; i < fastSegmentIterator.CountWhenOpen; i++)
		{
			lengths2 += fastSegmentIterator[i].Length;
			lengths[i] = lengths2;
		}
		if (fastSegmentIterator.ConnectsEndToStart)
		{
			lengths2 += fastSegmentIterator.GetClosingSegment().Length;
			lengths[lengths.Length - 1] = lengths2;
		}
	}

	public double[] CumulativePointStations()
	{
		double[] doubleValues = new double[PointCount];
		doubleValues[0] = 0.0;
		FastSegmentIterator fastSegmentIterator = new FastSegmentIterator(this);
		double fastSegmentIteratorTerm = 0.0;
		for (int i = 0; i < fastSegmentIterator.CountWhenOpen; i++)
		{
			fastSegmentIteratorTerm = (doubleValues[i + 1] = fastSegmentIteratorTerm + fastSegmentIterator[i].Length);
		}
		return doubleValues;
	}

	public double DistanceAlong(PointOnPolyline ptOnPoly)
	{
		FastSegmentIterator fastSegmentIterator = new FastSegmentIterator(this);
		double fastSegmentIteratorLengthSum = 0.0;
		for (int i = 0; i < ptOnPoly.SegmentIndex; i++)
		{
			fastSegmentIteratorLengthSum += fastSegmentIterator[i].Length;
		}
		if (ptOnPoly.SegmentIndex == fastSegmentIterator.CountWhenOpen)
		{
			return fastSegmentIteratorLengthSum + fastSegmentIterator.GetClosingSegment().Length * ptOnPoly.Fraction;
		}
		return fastSegmentIteratorLengthSum + fastSegmentIterator[ptOnPoly.SegmentIndex].Length * ptOnPoly.Fraction;
	}

	public double[] DistancesAlongSorted(IList<PointOnPolyline> pts)
	{
		if (pts == null)
		{
			return null;
		}
		double[] doubleValues = new double[pts.Count];
		if (pts.Count == 0)
		{
			return doubleValues;
		}
		double lengthSum = 0.0;
		int ordinal = 0;
		PointOnPolyline pointOnPolyline = pts.First();
		for (int i = 0; i <= SegmentCount; i++)
		{
			double length = Segments[i].Length;
			while (pointOnPolyline.SegmentIndex == i)
			{
				double fraction = pointOnPolyline.Fraction;
				doubleValues[ordinal] = lengthSum + fraction * length;
				ordinal++;
				if (ordinal == pts.Count)
				{
					break;
				}
				pointOnPolyline = pts[ordinal];
			}
			lengthSum += length;
		}
		return doubleValues;
	}

	public double LengthAlong(PointOnPolyline startPtOnPoly, PointOnPolyline stopPtOnPoly)
	{
		if (startPtOnPoly.SegmentIndex == stopPtOnPoly.SegmentIndex)
		{
			return Segments[startPtOnPoly.SegmentIndex].Length * (stopPtOnPoly.Fraction * startPtOnPoly.Fraction);
		}
		int segmentIndex = startPtOnPoly.SegmentIndex;
		double segmentLengthSum = Segments[segmentIndex].Length * (1.0 - startPtOnPoly.Fraction);
		for (segmentIndex++; segmentIndex < stopPtOnPoly.SegmentIndex; segmentIndex++)
		{
			segmentLengthSum += Segments[segmentIndex].Length;
		}
		return segmentLengthSum + Segments[segmentIndex].Length * stopPtOnPoly.Fraction;
	}

	public double DistanceAlongFast(PointOnPolyline ptOnPoly, ref double[] cumulativeSegmentLengths)
	{
		if (cumulativeSegmentLengths.IsNullOrEmpty())
		{
			cumulativeSegmentLengths = CumulativeSegmentLengths();
		}
		double cumulativeSegmentLengthSum = 0.0;
		if (ptOnPoly.SegmentIndex > 0)
		{
			cumulativeSegmentLengthSum += cumulativeSegmentLengths[ptOnPoly.SegmentIndex - 1];
		}
		return cumulativeSegmentLengthSum + Segments[ptOnPoly.SegmentIndex].Length * ptOnPoly.Fraction;
	}

	public double DistanceAlongFast(PointOnPolyline ptOnPoly, List<double> cumulativeSegmentLengths)
	{
		double cumulativeSegmentLengthSum = 0.0;
		if (ptOnPoly.SegmentIndex > 0)
		{
			cumulativeSegmentLengthSum += cumulativeSegmentLengths[ptOnPoly.SegmentIndex - 1];
		}
		return cumulativeSegmentLengthSum + Segments[ptOnPoly.SegmentIndex].Length * ptOnPoly.Fraction;
	}

	public virtual bool HasConsecutiveIdenticalPoints(double tol = 0.0)
	{
		if (!IsValidPolyline())
		{
			return false;
		}
		if (tol == 0.0)
		{
			for (int i = 1; i < PointCount; i++)
			{
				if (Points[i - 1].EqualXY(Points[i]))
				{
					return true;
				}
			}
		}
		else
		{
			for (int j = 1; j < PointCount; j++)
			{
				Point point = Points[j - 1];
				Point other = Points[j];
				if (point.DistanceTo(other) < tol)
				{
					return true;
				}
			}
		}
		return false;
	}

	public bool CLeftOfAB(int a, int b, int c)
	{
		double pointsTerm = Points[b].X - Points[a].X;
		double pointsTerm2 = Points[b].Y - Points[a].Y;
		double pointsTerm3 = Points[c].X - Points[a].X;
		double pointsTerm4 = Points[c].Y - Points[a].Y;
		return pointsTerm * pointsTerm4 - pointsTerm2 * pointsTerm3 > 0.0;
	}

	public List<Point> DouglasPeuckerReduction(double tolerance)
	{
		List<Point> points = new List<Point>();
		PolylineSmoothing.SimplifyDouglasPeucker(GetPointBufferUnsafe().Span, tolerance, points);
		return points;
	}

	public List<Point> LangSimplificaiton(double tolerance, int lookAhead)
	{
		if (_points == null || _points.Count < 3 || tolerance <= 0.0 || lookAhead < 1)
		{
			return _points.ToList();
		}
		List<Point> points = new List<Point>();
		if (lookAhead > _points.Count - 1)
		{
			lookAhead = _points.Count - 1;
		}
		points.Add(_points[0]);
		for (int i = 0; i <= _points.Count; i++)
		{
			if (i + lookAhead > _points.Count)
			{
				lookAhead = _points.Count - i - 1;
			}
			int recursiveTolerance2 = recursiveTolerance(i, lookAhead, tolerance);
			if ((recursiveTolerance2 > 0) & (i + recursiveTolerance2 < _points.Count))
			{
				points.Add(new Point(_points[i + recursiveTolerance2].X, _points[i + recursiveTolerance2].Y));
				i += recursiveTolerance2 - 1;
			}
		}
		return points;
	}

	private int recursiveTolerance(int i, int lookAhead, double tolerance)
	{
		int lookAhead2 = lookAhead;
		Point point = _points[i];
		if (i + lookAhead2 >= _points.Count)
		{
			return lookAhead2;
		}
		Point point2 = new Point(_points[i + lookAhead2].X - point.X, _points[i + lookAhead2].Y - point.Y);
		for (int j = 1; j <= lookAhead2; j++)
		{
			Point point3 = _points[i + j];
			Point point4 = new Point(point3.X - point.X, point3.Y - point.Y);
			double a = Math.Acos((point2.X * point4.X + point2.Y * point4.Y) / (Math.Sqrt(Math.Pow(point2.Y, 2.0) + Math.Pow(point2.X, 2.0)) * Math.Sqrt(Math.Pow(point4.Y, 2.0) + Math.Pow(point4.X, 2.0))));
			Point point5 = new Point(point3.X - point.X, point3.Y - point.Y);
			double point5Term = Math.Sqrt(Math.Pow(point5.X, 2.0) + Math.Pow(point5.Y, 2.0));
			if (Math.Sin(a) * point5Term >= tolerance)
			{
				lookAhead2--;
				if (lookAhead2 > 0)
				{
					return recursiveTolerance(i, lookAhead2, tolerance);
				}
				return 0;
			}
		}
		return lookAhead2;
	}

	public List<Point> VisvaligamWhyattSimplify(int numToKeep)
	{
		List<Point> points = new List<Point>(_points);
		int count = points.Count - numToKeep;
		for (int i = 0; i <= count; i++)
		{
			int index = 1;
			double area = Triangle.ComputeArea(points[0], points[1], points[2]);
			for (int j = 2; j <= points.Count - 1; j++)
			{
				double area2 = Triangle.ComputeArea(points[j - 1], points[j], points[j + 1]);
				if (!(area2 >= area))
				{
					index = j;
					area = area2;
				}
			}
			points.RemoveAt(index);
		}
		return points;
	}

	public Polyline CloneFiltered(double tolerance)
	{
		Polyline polyline = Clone();
		polyline.Filter(tolerance);
		return polyline;
	}

	public Polyline CloneDedupe(double absTolerance = 0.001)
	{
		Polyline polyline = new Polyline();
		if (Count == 0)
		{
			return polyline;
		}
		Point point = Points[0];
		polyline.Add(point);
		for (int i = 1; i < Count; i++)
		{
			Point point2 = Points[i];
			if (!(point2.DistanceTo(point) < absTolerance))
			{
				point = point2;
				polyline.Add(point2);
			}
		}
		return polyline;
	}

	public Polyline CloneWithChaikinSmoothing(int iterations)
	{
		return CloneWithChaikinSmoothing(this, iterations);
	}

	private static Polyline CloneWithChaikinSmoothing(Polyline source, int iterations)
	{
		iterations = Math.Max(0, iterations);
		if (iterations == 0)
		{
			return source.Clone();
		}
		List<Point> points = new List<Point>(source.PointCount * 2);
		PolylineSmoothing.ChaikinSmooth(source, iterations, 180.0, closed: false, points);
		return new Polyline(points);
	}

	public Polyline CloneWithMovingAverageSmoothing(int windowSize, int passes)
	{
		return CloneWithMovingAverageSmoothing(this, windowSize, passes);
	}

	private static Polyline CloneWithMovingAverageSmoothing(Polyline source, int windowSize, int passes)
	{
		windowSize = Math.Max(3, windowSize);
		if (windowSize % 2 == 0)
		{
			windowSize++;
		}
		passes = Math.Max(1, passes);
		Polyline polyline = source.Clone();
		for (int i = 0; i < passes; i++)
		{
			if (polyline.PointCount <= 2)
			{
				break;
			}
			Polyline polyline2 = new Polyline(polyline.PointCount);
			Point point = polyline.Points[0];
			Point point2 = polyline.Points[polyline.PointCount - 1];
			polyline2.Add(new Point(point.X, point.Y));
			int scaledWindowSize = windowSize / 2;
			for (int j = 1; j < polyline.PointCount - 1; j++)
			{
				int ordinal = Math.Max(0, j - scaledWindowSize);
				int count = Math.Min(polyline.PointCount - 1, j + scaledWindowSize);
				double computed = 0.0;
				double computed2 = 0.0;
				int ordinal2 = 0;
				for (int k = ordinal; k <= count; k++)
				{
					Point point3 = polyline.Points[k];
					computed += point3.X;
					computed2 += point3.Y;
					ordinal2++;
				}
				polyline2.Add(new Point(computed / (double)ordinal2, computed2 / (double)ordinal2));
			}
			polyline2.Add(new Point(point2.X, point2.Y));
			polyline = polyline2;
		}
		return polyline;
	}

	public void InplaceDepupe(double absTolerance = 0.0)
	{
		int unity = 1;
		absTolerance *= absTolerance;
		for (int i = 1; i < Count; i++)
		{
			if (Points[i] == Points[unity - 1])
			{
				continue;
			}
			if (absTolerance != 0.0 && Points[i].DistanceSquaredTo(Points[unity - 1]) <= absTolerance)
			{
				if (i + 1 == Count)
				{
					Points[unity - 1] = Points[i];
				}
				continue;
			}
			if (i != unity)
			{
				Points[unity] = Points[i];
			}
			unity++;
		}
		if (unity < Count)
		{
			RemoveRange(unity);
		}
	}

	public virtual void FilterByRadialDistance(double eps)
	{
		if (!(eps > 0.0) || Count < 3)
		{
			return;
		}
		double epsPolynomial = eps * eps;
		int unity = 1;
		Point point = _points[0];
		int count = Count - 1;
		for (int i = 1; i < count; i++)
		{
			if (!(point.DistanceSquaredTo(_points[i]) < epsPolynomial))
			{
				if (i != unity)
				{
					MovePoint(i, unity);
				}
				point = _points[unity];
				unity++;
			}
		}
		if (count != unity)
		{
			MovePoint(count, unity);
		}
		unity++;
		if (unity < Count)
		{
			RemoveRange(unity);
		}
	}

	public Polyline CloneFilteredRadialDistance(double eps)
	{
		Polyline polyline = Clone();
		polyline.FilterByRadialDistance(eps);
		return polyline;
	}

	public virtual void FilterByCornerKeeper(double eps, double cornerEps)
	{
		if (Count < 3)
		{
			return;
		}
		double epsPolynomial = ((eps > 0.0) ? (eps * eps) : 0.0);
		int unity = 1;
		Point point = _points[0];
		int count = Count - 1;
		for (int i = 1; i < count; i++)
		{
			Point other = _points[i];
			if (point.DistanceSquaredTo(other) < epsPolynomial)
			{
				Point point2 = _points[i + 1];
				double otherPointTerm = other.X - point.X;
				double otherPointTerm2 = other.Y - point.Y;
				double point2OtherTerm = point2.X - other.X;
				double point2OtherTerm2 = point2.Y - other.Y;
				if (Math.Abs(otherPointTerm * point2OtherTerm2 - otherPointTerm2 * point2OtherTerm) < cornerEps)
				{
					continue;
				}
			}
			if (i != unity)
			{
				MovePoint(i, unity);
			}
			point = _points[unity];
			unity++;
		}
		if (count != unity)
		{
			MovePoint(count, unity);
		}
		unity++;
		if (unity < Count)
		{
			RemoveRange(unity);
		}
	}

	public Polyline CloneFilteredCornerKeeper(double eps, double cornerEps)
	{
		Polyline polyline = Clone();
		polyline.FilterByCornerKeeper(eps, cornerEps);
		return polyline;
	}

	public virtual void FilterByGridQuantization(double cellSize)
	{
		if (!(cellSize > 0.0) || Count < 2)
		{
			return;
		}
		double scaledCellSize = 1.0 / cellSize;
		Point point = _points[0];
		Point points = new Point(Math.Floor(point.X * scaledCellSize) * cellSize, Math.Floor(point.Y * scaledCellSize) * cellSize);
		Points[0] = points;
		int unity = 1;
		for (int i = 1; i < Count; i++)
		{
			Point point2 = _points[i];
			Point point3 = new Point(Math.Floor(point2.X * scaledCellSize) * cellSize, Math.Floor(point2.Y * scaledCellSize) * cellSize);
			if (point3.X == points.X && point3.Y == points.Y)
			{
				continue;
			}
			if (i != unity)
			{
				if (HasZ)
				{
					_z[unity] = _z[i];
				}
				if (HasM)
				{
					_m[unity] = _m[i];
				}
			}
			Points[unity] = point3;
			points = point3;
			unity++;
		}
		if (unity < Count)
		{
			RemoveRange(unity);
		}
	}

	public Polyline CloneFilteredGridQuantization(double cellSize)
	{
		Polyline polyline = Clone();
		polyline.FilterByGridQuantization(cellSize);
		return polyline;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int Densify_Op(in Segment seg, Polyline pl, double maxSegmentLength, Span<Point> ptBuffer)
	{
		int internalDensificationPoints = seg.GetInternalDensificationPoints(maxSegmentLength, ptBuffer);
		if (internalDensificationPoints > ptBuffer.Length)
		{
			return internalDensificationPoints;
		}
		pl.AddRange(ptBuffer.Slice(0, internalDensificationPoints));
		pl.Add(seg.B);
		return internalDensificationPoints;
	}

	public virtual void Densify(double maxSegmentLength)
	{
		if (!IsValidPolyline())
		{
			return;
		}
		int densifyTotalCount = GetDensifyTotalCount(maxSegmentLength);
		if (densifyTotalCount > Count)
		{
			Point[] points = new Point[densifyTotalCount];
			points[0] = _points[0];
			Span<Point> outPoints = points.AsSpan(1);
			FastSegmentIterator fastSegmentIterator = new FastSegmentIterator(this);
			for (int i = 0; i < fastSegmentIterator.CountWhenOpen; i++)
			{
				int internalDensificationPoints = fastSegmentIterator[i].GetInternalDensificationPoints(maxSegmentLength, outPoints);
				outPoints[internalDensificationPoints] = fastSegmentIterator[i].B;
				outPoints = outPoints.Slice(internalDensificationPoints + 1);
			}
			if (fastSegmentIterator.ConnectsEndToStart)
			{
				Segment closingSegment = fastSegmentIterator.GetClosingSegment();
				int internalDensificationPoints2 = closingSegment.GetInternalDensificationPoints(maxSegmentLength, outPoints);
				outPoints[internalDensificationPoints2] = closingSegment.B;
			}
			if (!HasZ && !HasM)
			{
				AssignBuffers(points, null, null);
				return;
			}
			Clear();
			AddRange(points);
		}
	}

	private int GetDensifyTotalCount(double maxSegmentLength)
	{
		int internalDensifyPointCountSum = 1;
		FastSegmentIterator fastSegmentIterator = new FastSegmentIterator(this);
		for (int i = 0; i < fastSegmentIterator.CountWhenOpen; i++)
		{
			internalDensifyPointCountSum += fastSegmentIterator[i].InternalDensifyPointCount(maxSegmentLength);
			internalDensifyPointCountSum++;
		}
		if (fastSegmentIterator.ConnectsEndToStart)
		{
			internalDensifyPointCountSum += fastSegmentIterator.GetClosingSegment().InternalDensifyPointCount(maxSegmentLength);
			internalDensifyPointCountSum++;
		}
		return internalDensifyPointCountSum;
	}

	public virtual void Filter(double tolerance)
	{
		Filter(tolerance, 0, PointCount - 1);
	}

	public virtual void Filter(double tolerance, int first, int last)
	{
		if (double.IsNaN(tolerance))
		{
			return;
		}
		List<int> intList = new List<int>();
		if (last - first + 1 <= 2)
		{
			return;
		}
		intList.Clear();
		for (int i = 0; i <= first; i++)
		{
			intList.Add(i);
		}
		intList.Add(last);
		Point point = Points[first];
		for (int j = last; j >= first; j += -1)
		{
			if (!point.EqualXY(Points[j]))
			{
				last = j;
				break;
			}
		}
		FilterDouglasPeucker(first, last, tolerance, intList);
		for (int k = last + 1; k < PointCount; k++)
		{
			intList.Add(k);
		}
		if (intList.Count < PointCount)
		{
			CompressList(intList);
		}
	}

	private void FilterDouglasPeucker(int first, int last, double tolerance, List<int> keepIndex)
	{
		if (last - first <= 1)
		{
			return;
		}
		Stack<(int, int)> valueTuples = new Stack<(int, int)>();
		valueTuples.Push((first, last));
		while (valueTuples.Count > 0)
		{
			var (localValue, localValue2) = valueTuples.Pop();
			if (localValue2 - localValue <= 1)
			{
				continue;
			}
			double computed = 0.0;
			int ordinal = 0;
			double pointsTerm = Points[localValue2].X - Points[localValue].X;
			double pointsTerm2 = Points[localValue2].Y - Points[localValue].Y;
			for (int i = localValue + 1; i < localValue2; i++)
			{
				double pointsTerm3 = Math.Abs(pointsTerm * (Points[i].Y - Points[localValue].Y) - pointsTerm2 * (Points[i].X - Points[localValue].X));
				if (pointsTerm3 > computed)
				{
					computed = pointsTerm3;
					ordinal = i;
				}
			}
			double computed2 = Math.Sqrt(pointsTerm * pointsTerm + pointsTerm2 * pointsTerm2);
			if (computed / computed2 > tolerance)
			{
				keepIndex.Add(ordinal);
				valueTuples.Push((localValue, ordinal));
				valueTuples.Push((ordinal, localValue2));
			}
		}
	}

	public void FilterXYIterative(int iterationCount, int targetPointCount)
	{
		if (Count <= targetPointCount)
		{
			return;
		}
		Polyline polyline = Clone();
		double unity = 1.0;
		polyline.Filter(unity);
		if (polyline.PointCount == targetPointCount)
		{
			_points = polyline._points;
			_z = polyline._z;
			_m = polyline._m;
			return;
		}
		double computed;
		Polyline polyline2;
		double computed2;
		if (polyline.PointCount < targetPointCount)
		{
			computed = unity;
			polyline2 = polyline;
			double computed3 = unity;
			int ordinal = 0;
			while (true)
			{
				computed3 /= 2.0;
				Polyline polyline3 = Clone();
				polyline3.Filter(computed3);
				if (polyline3.PointCount == targetPointCount)
				{
					Clear();
					polyline3.CopyTo(this);
					return;
				}
				if (polyline3.PointCount >= targetPointCount)
				{
					break;
				}
				computed = computed3;
				polyline2 = polyline3;
				ordinal++;
				if (ordinal == 100)
				{
					return;
				}
			}
			computed2 = computed3;
		}
		else
		{
			computed2 = unity;
			double computed4 = unity;
			int ordinal2 = 0;
			Polyline polyline4;
			while (true)
			{
				computed4 *= 2.0;
				polyline4 = Clone();
				polyline4.Filter(computed4);
				if (polyline4.PointCount == targetPointCount)
				{
					Clear();
					polyline4.CopyTo(this);
					return;
				}
				if (polyline4.PointCount <= targetPointCount)
				{
					break;
				}
				computed2 = computed4;
				ordinal2++;
				if (ordinal2 == 100)
				{
					return;
				}
			}
			computed = computed4;
			polyline2 = polyline4;
		}
		for (int i = 0; i < iterationCount; i++)
		{
			double computed5 = (computed2 + computed) / 2.0;
			Polyline polyline5 = Clone();
			polyline5.Filter(computed5);
			if (polyline5.PointCount == targetPointCount)
			{
				Clear();
				polyline5.CopyTo(this);
				return;
			}
			if (polyline5.PointCount > targetPointCount)
			{
				computed2 = computed5;
				continue;
			}
			computed = computed5;
			polyline2 = polyline5;
		}
		Clear();
		polyline2.CopyTo(this);
	}

	public List<PointOnPolyline> SplitLineEvenly(double approxSplitLength, out double actualSplitLength)
	{
		double length = Length;
		int lengthApproxSplitLengthTerm = (int)Math.Round(length / approxSplitLength);
		if (lengthApproxSplitLengthTerm <= 1)
		{
			actualSplitLength = length;
			return new PointOnPolyline[2]
			{
				FirstPointOnPolyline(),
				LastPointOnPolyline()
			}.ToList();
		}
		actualSplitLength = length / (double)lengthApproxSplitLengthTerm;
		List<PointOnPolyline> splitLineExactly = SplitLineExactly(actualSplitLength);
		if (splitLineExactly.Count != lengthApproxSplitLengthTerm + 1)
		{
			splitLineExactly.Add(LastPointOnPolyline());
		}
		return splitLineExactly;
	}

	public List<PointOnPolyline> SplitLineExactly(double splitLength, bool addLastPoint = false)
	{
		if (splitLength <= 0.0)
		{
			return new List<PointOnPolyline>();
		}
		double length = Length;
		List<PointOnPolyline> pointOnPolylines = new List<PointOnPolyline>((int)Math.Ceiling(length / splitLength) + 1);
		pointOnPolylines.Add(FirstPointOnPolyline());
		double length2Sum = 0.0;
		double length2Sum2 = 0.0;
		double splitLength2 = splitLength;
		for (int i = 0; i < SegmentCount; i++)
		{
			length2Sum2 = length2Sum;
			Segment segment = Segments[i];
			double length2 = segment.Length;
			length2Sum += length2;
			while (length2Sum >= splitLength2)
			{
				double splitLength2AboveLength2Sum2OverLength2 = (splitLength2 - length2Sum2) / length2;
				pointOnPolylines.Add(new PointOnPolyline(segment.PointAtFraction(splitLength2AboveLength2Sum2OverLength2), i, splitLength2AboveLength2Sum2OverLength2));
				splitLength2 += splitLength;
				if (splitLength2 > length)
				{
					break;
				}
			}
		}
		if (addLastPoint && pointOnPolylines.Last().Point.DistanceTo(LastPointOnPolyline().Point) > 0.0001)
		{
			pointOnPolylines.Add(LastPointOnPolyline());
		}
		return pointOnPolylines;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static double DiscretizeLine_Op(in Segment segment, Polyline discretized, double actualStepSize, ref double nextStep, double cumulativeDistance)
	{
		double length = segment.Length;
		if (length == 0.0)
		{
			return 0.0;
		}
		while (!(nextStep > cumulativeDistance + length))
		{
			double t = (nextStep - cumulativeDistance) / length;
			discretized.Add(segment.PointAtFraction(t));
			nextStep += actualStepSize;
		}
		return length;
	}

	public Polyline DiscretizeLine(double targetSegmentLength)
	{
		if (!IsValidPolyline())
		{
			return null;
		}
		double length = Length;
		if (length == 0.0)
		{
			return null;
		}
		int lengthTargetSegmentLengthTerm = (int)Math.Ceiling(length / targetSegmentLength);
		double lengthTerm = length / (double)lengthTargetSegmentLengthTerm;
		Polyline polyline = new Polyline(lengthTargetSegmentLengthTerm + 1) { Points[0] };
		FastSegmentIterator fastSegmentIterator = new FastSegmentIterator(this);
		double discretizeLine_OpSum = 0.0;
		double nextStep = lengthTerm;
		for (int i = 0; i < fastSegmentIterator.CountWhenOpen; i++)
		{
			discretizeLine_OpSum += DiscretizeLine_Op(in fastSegmentIterator[i], polyline, lengthTerm, ref nextStep, discretizeLine_OpSum);
		}
		if (fastSegmentIterator.ConnectsEndToStart)
		{
			DiscretizeLine_Op(fastSegmentIterator.GetClosingSegment(), polyline, lengthTerm, ref nextStep, discretizeLine_OpSum);
		}
		if (Last().DistanceTo(polyline.Last()) > lengthTerm * 0.01)
		{
			polyline.Add(Last());
		}
		return polyline;
	}

	public void Warp(Point newStart, Point newStop)
	{
		Vector vector = new Vector(0.0, 0.0);
		Vector vector2 = new Vector(0.0, 0.0);
		if (!newStart.IsNull())
		{
			vector = newStart - Points.First();
		}
		if (!newStop.IsNull())
		{
			vector2 = newStop - Points.Last();
		}
		if (PointCount > 2)
		{
			double distanceTo = Points[1].DistanceTo(Points[0]);
			double length = Length;
			for (int i = 1; i < PointCount - 1; i++)
			{
				double distanceTo2 = Points[i + 1].DistanceTo(Points[i]);
				double distanceToOverLength = distanceTo / length;
				Points[i] = Points[i] + (1.0 - distanceToOverLength) * vector + distanceToOverLength * vector2;
				distanceTo += distanceTo2;
			}
		}
		if (!newStart.IsNull())
		{
			Points[0] = newStart;
		}
		if (!newStop.IsNull())
		{
			Points[PointCount - 1] = newStop;
		}
	}

	public PointOnPolyline PointOnLine(int segIndex, double fraction)
	{
		return new PointOnPolyline(Segments[segIndex].PointAtFraction(fraction), segIndex, fraction);
	}

	public int NearestPointIndex(PointOnPolyline ptOnPoly, out double distance)
	{
		int segmentIndex = 0;
		segmentIndex = ((!(ptOnPoly.Fraction < 0.5)) ? (ptOnPoly.SegmentIndex + 1) : ptOnPoly.SegmentIndex);
		distance = Points[segmentIndex].DistanceTo(ptOnPoly.Point);
		return segmentIndex;
	}

	public int NearestPointIndex(Point pt, out double distance)
	{
		return NearestPointIndex(NearestPointOnPolyline(pt), out distance);
	}

	public PointOnPolyline FirstPointOnPolyline()
	{
		return new PointOnPolyline(Points[0], 0, 0.0);
	}

	public static PointOnPolyline FirstPointOnPolyline(IReadOnlyList<Point> pts)
	{
		return new PointOnPolyline(pts[0], 0, 0.0);
	}

	public PointOnPolyline LastPointOnPolyline()
	{
		return new PointOnPolyline(Points.Last(), SegmentCount - 1, 1.0);
	}

	public static PointOnPolyline LastPointOnPolyline(Memory<Point> pts)
	{
		Span<Point> ptsSpan = pts.Span;
		return new PointOnPolyline(ptsSpan[ptsSpan.Length - 1], pts.Length - 2, 1.0);
	}

	public static PointOnPolyline LastPointOnPolyline(IReadOnlyList<Point> pts)
	{
		return new PointOnPolyline(pts[pts.Count - 1], pts.Count - 2, 1.0);
	}

	public PointOnPolyline PointAtFraction(double fractionAlongLine)
	{
		if (!IsValidPolyline() || double.IsNaN(fractionAlongLine) || fractionAlongLine < 0.0 || fractionAlongLine > 1.0)
		{
			return PointOnPolyline.NullPointOnPolyline;
		}
		if (fractionAlongLine <= 0.0)
		{
			return FirstPointOnPolyline();
		}
		if (fractionAlongLine >= 1.0)
		{
			return LastPointOnPolyline();
		}
		return PointAtDistance(fractionAlongLine * Length);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static PointOnPolyline PointAtDistance_Op(in Segment seg, int s, double dist, ref double cumulativeLength)
	{
		double length = seg.Length;
		if (dist == cumulativeLength)
		{
			return new PointOnPolyline(seg.A, s, 0.0);
		}
		if (dist < cumulativeLength + length)
		{
			double distAboveCumulativeLengthOverLength = (dist - cumulativeLength) / length;
			return new PointOnPolyline(seg.PointAtFraction(distAboveCumulativeLengthOverLength), s, distAboveCumulativeLengthOverLength);
		}
		cumulativeLength += length;
		return PointOnPolyline.NullPointOnPolyline;
	}

	public PointOnPolyline PointAtDistance(double dist)
	{
		if (!IsValidPolyline() || double.IsNaN(dist) || dist < 0.0)
		{
			return PointOnPolyline.NullPointOnPolyline;
		}
		double cumulativeLength = 0.0;
		FastSegmentIterator fastSegmentIterator = new FastSegmentIterator(this);
		for (int i = 0; i < fastSegmentIterator.CountWhenOpen; i++)
		{
			PointOnPolyline result = PointAtDistance_Op(in fastSegmentIterator[i], i, dist, ref cumulativeLength);
			if (!result.IsNull())
			{
				return result;
			}
		}
		if (fastSegmentIterator.ConnectsEndToStart)
		{
			PointOnPolyline result2 = PointAtDistance_Op(fastSegmentIterator.GetClosingSegment(), fastSegmentIterator.CountWhenOpen, dist, ref cumulativeLength);
			if (!result2.IsNull())
			{
				return result2;
			}
		}
		if (dist == cumulativeLength)
		{
			return LastPointOnPolyline();
		}
		return PointOnPolyline.NullPointOnPolyline;
	}

	public PointOnPolyline PointAtDistanceFast(double dist, double[] cumulativeSegmentLengths)
	{
		if (!IsValidPolyline() || double.IsNaN(dist) || dist < 0.0)
		{
			return PointOnPolyline.NullPointOnPolyline;
		}
		int binarySearch = MathHelpers.BinarySearch(cumulativeSegmentLengths, dist);
		if (binarySearch < 0)
		{
			if (dist == 0.0)
			{
				return FirstPointOnPolyline();
			}
			binarySearch = ~binarySearch;
			if (binarySearch < SegmentCount)
			{
				Segment segment = Segments[binarySearch];
				double cumulativeSegmentLength = cumulativeSegmentLengths[binarySearch];
				double cumulativeSegmentLength2 = ((binarySearch != 0) ? cumulativeSegmentLengths[binarySearch - 1] : 0.0);
				double submergenceRatio = (dist - cumulativeSegmentLength2) / (cumulativeSegmentLength - cumulativeSegmentLength2);
				return new PointOnPolyline(segment.PointAtFraction(submergenceRatio), binarySearch, submergenceRatio);
			}
			return PointOnPolyline.NullPointOnPolyline;
		}
		if (binarySearch + 1 < SegmentCount)
		{
			return new PointOnPolyline(Points[binarySearch + 1], binarySearch + 1, 0.0);
		}
		return LastPointOnPolyline();
	}

	public static PointOnPolyline PointAtDistance(Memory<Point> pts, double dist)
	{
		double lengthSum = 0.0;
		Span<Point> ptsSpan = pts.Span;
		for (int i = 0; i < pts.Length - 1; i++)
		{
			Segment segment = new Segment(ptsSpan[i], ptsSpan[i + 1]);
			double length = segment.Length;
			if (dist == lengthSum)
			{
				return new PointOnPolyline(segment.A, i, 0.0);
			}
			if (dist < lengthSum + length)
			{
				double distAboveLengthSumOverLength = (dist - lengthSum) / length;
				return new PointOnPolyline(segment.PointAtFraction(distAboveLengthSumOverLength), i, distAboveLengthSumOverLength);
			}
			lengthSum += length;
		}
		if (dist == lengthSum)
		{
			return LastPointOnPolyline(pts);
		}
		return PointOnPolyline.NullPointOnPolyline;
	}

	public PointOnPolyline MidPointOnPolyline()
	{
		return PointAtFraction(0.5);
	}

	public List<PointOnPolyline> PointsAtDistancesSorted(IList<double> distances)
	{
		if (distances.IsNullOrEmpty())
		{
			return null;
		}
		List<PointOnPolyline> pointOnPolylines = new List<PointOnPolyline>(distances.Count);
		double lengthSum = 0.0;
		double lengthSum2 = 0.0;
		int ordinal = 0;
		double distancesTerm = distances.First();
		for (int i = 0; i < SegmentCount; i++)
		{
			lengthSum2 = lengthSum;
			Segment segment = Segments[i];
			double length = segment.Length;
			lengthSum += length;
			while (lengthSum >= distancesTerm)
			{
				double lengthSum2LengthTerm = (distancesTerm - lengthSum2) / length;
				pointOnPolylines.Add(new PointOnPolyline(segment.PointAtFraction(lengthSum2LengthTerm), i, lengthSum2LengthTerm));
				ordinal++;
				if (ordinal == distances.Count)
				{
					break;
				}
				distancesTerm = distances[ordinal];
			}
		}
		PointOnPolyline lastPointOnPolyline = LastPointOnPolyline();
		while (distances.Count > pointOnPolylines.Count)
		{
			pointOnPolylines.Add(lastPointOnPolyline);
		}
		return pointOnPolylines;
	}

	public double DistanceToPoint(Point pt)
	{
		FastSegmentIterator fastSegmentIterator = new FastSegmentIterator(this);
		double maxValue = double.MaxValue;
		for (int i = 0; i < fastSegmentIterator.CountWhenOpen; i++)
		{
			double distanceToPoint = fastSegmentIterator[i].DistanceToPoint(pt);
			if (distanceToPoint < maxValue)
			{
				maxValue = distanceToPoint;
			}
		}
		if (fastSegmentIterator.ConnectsEndToStart)
		{
			double distanceToPoint2 = fastSegmentIterator.GetClosingSegment().DistanceToPoint(pt);
			if (distanceToPoint2 < maxValue)
			{
				maxValue = distanceToPoint2;
			}
		}
		return maxValue;
	}

	public PointOnPolyline NearestEndpoint(Point pt)
	{
		PointOnPolyline result = FirstPointOnPolyline();
		PointOnPolyline result2 = LastPointOnPolyline();
		if (!(result.Point.DistanceTo(pt) < result2.Point.DistanceTo(pt)))
		{
			return result2;
		}
		return result;
	}

	public static PointOnPolyline NearestEndpoint(IReadOnlyList<Point> pts, Point pt)
	{
		PointOnPolyline result = FirstPointOnPolyline(pts);
		PointOnPolyline result2 = LastPointOnPolyline(pts);
		if (!(result.Point.DistanceTo(pt) < result2.Point.DistanceTo(pt)))
		{
			return result2;
		}
		return result;
	}

	public int NearestPoint(Point pt)
	{
		return NearestPoint(this, pt);
	}

	public static int NearestPoint(IReadOnlyList<Point> pts, Point pt)
	{
		double maxValue = double.MaxValue;
		int result = -1;
		for (int i = 0; i < pts.Count; i++)
		{
			double distanceTo = pts[i].DistanceTo(pt);
			if (distanceTo == 0.0)
			{
				return i;
			}
			if (distanceTo < maxValue)
			{
				maxValue = distanceTo;
				result = i;
			}
		}
		return result;
	}

	public PointOnPolyline NearestPointOnPolyline(Point pt)
	{
		double actualDistance;
		return NearestPointOnPolyline(pt, out actualDistance);
	}

	public PointOnPolyline NearestPointOnPolyline(Point pt, out double actualDistance)
	{
		return NearestPointOnPolyline(pt, new CompactList<Range1D>(new Range1D(0, SegmentCount - 1)), out actualDistance);
	}

	public PointOnPolyline NearestPointOnPolyline(Point pt, PointOnPolyline start, out double actualDistance)
	{
		return NearestPointOnPolyline(pt, new CompactList<Range1D>(new Range1D(start.SegmentIndex, SegmentCount - 1)), out actualDistance);
	}

	public PointOnPolyline NearestPointOnPolyline(Point pt, int startSegment, int endSegment, out double actualDistance)
	{
		return NearestPointOnPolyline(pt, new CompactList<Range1D>(new Range1D(startSegment, endSegment)), out actualDistance);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void NearestPointOnPolyline_Op(in Segment seg, in Point pt, ref double actualDistanceSq, ref int segIndex, int i)
	{
		double actualDistanceSq2 = seg.DistanceToPointSquared(pt);
		if (actualDistanceSq2 < actualDistanceSq)
		{
			actualDistanceSq = actualDistanceSq2;
			segIndex = i;
		}
	}

	public PointOnPolyline NearestPointOnPolyline<T>(Point pt, T searchSpace, out double actualDistance) where T : IList<Range1D>
	{
		if (PointCount == 0)
		{
			actualDistance = double.NaN;
			return PointOnPolyline.NullPointOnPolyline;
		}
		if (PointCount == 1)
		{
			actualDistance = pt.DistanceTo(First());
			return FirstPointOnPolyline();
		}
		if (!pt.IsValid() || double.IsInfinity(pt.X * pt.X) || double.IsInfinity(pt.Y * pt.Y))
		{
			throw new Exception("This number is too large, it will overflow distance checks.");
		}
		FastSegmentIterator fastSegmentIterator = new FastSegmentIterator(this);
		double actualDistanceSq = double.MaxValue;
		int segIndex = -1;
		int endSearchSpaceTerm;
		if (fastSegmentIterator.ConnectsEndToStart)
		{
			endSearchSpaceTerm = ((searchSpace[searchSpace.Count - 1].End == fastSegmentIterator.CountWhenOpen) ? 1 : 0);
		}
		else
		{
			endSearchSpaceTerm = 0;
		}
		bool condition = (byte)endSearchSpaceTerm != 0;
		for (int i = 0; i < searchSpace.Count; i++)
		{
			Range1D range1D = searchSpace[i];
			bool searchSpaceTerm = condition && i == searchSpace.Count - 1;
			int count = range1D.End - (searchSpaceTerm ? 1 : 0);
			for (int j = range1D.Start; j <= count; j++)
			{
				NearestPointOnPolyline_Op(in fastSegmentIterator[j], in pt, ref actualDistanceSq, ref segIndex, j);
			}
		}
		if (condition)
		{
			NearestPointOnPolyline_Op(fastSegmentIterator.GetClosingSegment(), in pt, ref actualDistanceSq, ref segIndex, fastSegmentIterator.CountWhenOpen);
		}
		Point pt2 = ((segIndex == fastSegmentIterator.CountWhenOpen) ? fastSegmentIterator.GetClosingSegment() : fastSegmentIterator[segIndex]).NearestPoint(pt, out var t);
		actualDistance = Math.Sqrt(actualDistanceSq);
		return new PointOnPolyline(pt2, segIndex, t);
	}

	public PointOnPolyline NearestPointOnPolyline(Point pt, Vector vec, double tolerance = 1E-06)
	{
		Extent extent = Extent;
		double factor = Math.Pow(Math.Pow(extent.MaxX - extent.MinX, 2.0) + Math.Pow(extent.MaxY - extent.MinY, 2.0), 0.5);
		Vector vec2 = vec.UnitVector().Ratio(factor);
		Point ptB = pt.Translate(vec2);
		Segment seg = new Segment(pt, ptB);
		PointOnPolyline result = PointOnPolyline.NullPointOnPolyline;
		double maxValue = double.MaxValue;
		for (int i = 0; i < SegmentCount; i++)
		{
			Segment segment = Segments[i];
			if (segment.Intersects(in seg, out var _, out var _, out var t, out var t2, tolerance) && t2 < maxValue)
			{
				result = new PointOnPolyline(segment.PointAtFraction(t), i, t);
				maxValue = t2;
			}
		}
		if (maxValue == double.MaxValue)
		{
			return NearestPointOnPolyline(pt);
		}
		return result;
	}

	public List<PointOnPolyline> NearestPointOnPolylineOrdered(IList<Point> src)
	{
		List<PointOnPolyline> pointOnPolylines = new List<PointOnPolyline>(new PointOnPolyline[src.Count]);
		if (src.First() == Points.First())
		{
			pointOnPolylines[0] = FirstPointOnPolyline();
		}
		else
		{
			pointOnPolylines[0] = NearestPointOnPolyline(src[0]);
		}
		if (src.Last() == Points.Last())
		{
			pointOnPolylines[pointOnPolylines.Count - 1] = LastPointOnPolyline();
		}
		else
		{
			pointOnPolylines[pointOnPolylines.Count - 1] = NearestPointOnPolyline(src.Last());
		}
		Stack<(int, int)> valueTuples = new Stack<(int, int)>();
		if (pointOnPolylines.Count > 2)
		{
			valueTuples.Push((0, pointOnPolylines.Count - 1));
		}
		while (valueTuples.Count > 0)
		{
			(int, int) tuple = valueTuples.Pop();
			int tupleTerm = tuple.Item1;
			int tupleTerm2 = tuple.Item2;
			int ordinal = (tupleTerm + tupleTerm2) / 2;
			pointOnPolylines[ordinal] = NearestPointOnPolyline(src[ordinal], pointOnPolylines[tupleTerm].SegmentIndex, pointOnPolylines[tupleTerm2].SegmentIndex, out var _);
			if (tupleTerm + 1 < ordinal)
			{
				valueTuples.Push((tupleTerm, ordinal));
			}
			if (ordinal + 1 < tupleTerm2)
			{
				valueTuples.Push((ordinal, tupleTerm2));
			}
		}
		return pointOnPolylines;
	}

	public static PointOnPolyline NearestPointOnPolyline(IReadOnlyList<Point> points, Point pt, out double actualDistance, bool isOpenEnded = true)
	{
		switch (points.Count)
		{
		case 0:
			actualDistance = double.NaN;
			return PointOnPolyline.NullPointOnPolyline;
		case 1:
			actualDistance = pt.DistanceTo(points[0]);
			return FirstPointOnPolyline(points);
		default:
		{
			int pointCount = points.Count;
			if (isOpenEnded)
			{
				pointCount--;
			}
			actualDistance = double.MaxValue;
			int ordinal = -1;
			for (int i = 0; i < pointCount; i++)
			{
				Segment segment;
				if (isOpenEnded || i < pointCount - 1)
				{
					segment = new Segment(points[i], points[i + 1]);
				}
				else
				{
					segment = new Segment(points[points.Count - 1], points[0]);
				}
				double actualDistance2 = segment.DistanceToPoint(pt);
				if (actualDistance2 < actualDistance)
				{
					actualDistance = actualDistance2;
					ordinal = i;
				}
			}
			double t;
			return new PointOnPolyline(new Segment(points[ordinal], points[(isOpenEnded || ordinal != points.Count - 1) ? (ordinal + 1) : 0]).NearestPoint(pt, out t), ordinal, t);
		}
		}
	}

	public double DistancePointLine(Point pt, out Point intersectingPoint, out int preceedingPointIndex)
	{
		intersectingPoint = new Point(-1.0, -1.0);
		preceedingPointIndex = -1;
		double maxValue = double.MaxValue;
		for (int i = 0; i < _points.Count; i++)
		{
			double pointsTerm = _points[i + 1].X - _points[i].X;
			double pointsTerm2 = _points[i + 1].Y - _points[i].Y;
			double computed = pointsTerm * pointsTerm + pointsTerm2 * pointsTerm2;
			double ptPointsTerm = -1.0;
			if (computed != 0.0)
			{
				ptPointsTerm = ((pt.X - _points[i].X) * pointsTerm + (pt.Y - _points[i].Y) * pointsTerm2) / computed;
			}
			double x;
			double y;
			if (ptPointsTerm < 0.0)
			{
				x = _points[i].X;
				y = _points[i].Y;
			}
			else if (ptPointsTerm > 1.0)
			{
				x = _points[i + 1].X;
				y = _points[i + 1].Y;
			}
			else
			{
				x = _points[i].X + ptPointsTerm * pointsTerm;
				y = _points[i].Y + ptPointsTerm * pointsTerm2;
			}
			double distanceTo = pt.DistanceTo(x, y);
			if (!(distanceTo >= maxValue))
			{
				maxValue = distanceTo;
				intersectingPoint = new Point(x, y);
				preceedingPointIndex = i;
			}
		}
		return maxValue;
	}

	public virtual Polyline Subsection(double startSta, double endSta)
	{
		if (startSta < 0.0 || endSta < 0.0 || startSta >= endSta)
		{
			return null;
		}
		return Subsection(PointAtDistance(startSta), PointAtDistance(endSta));
	}

	public virtual Polyline Subsection(PointOnPolyline startPoint, PointOnPolyline endPoint)
	{
		if (!startPoint.IsValid() || !endPoint.IsValid())
		{
			return null;
		}
		Polyline polyline = new Polyline();
		if (startPoint.Fraction < 1.0)
		{
			polyline.Add(startPoint.Point);
		}
		for (int i = startPoint.SegmentIndex + 1; i <= endPoint.SegmentIndex; i++)
		{
			polyline.Add(Points[i]);
		}
		if (endPoint.Fraction > 0.0)
		{
			polyline.Add(endPoint.Point);
		}
		return polyline;
	}

	public Polyline SubsectionDirected(PointOnPolyline from, PointOnPolyline to)
	{
		if (!from.IsValid() || !to.IsValid())
		{
			return null;
		}
		if (from > to)
		{
			Polyline polyline = Subsection(to, from);
			if (polyline != null)
			{
				polyline.Reverse();
				return polyline;
			}
			return polyline;
		}
		return Subsection(from, to);
	}

	public Polyline SubsectionBefore(PointOnPolyline point)
	{
		Polyline polyline = new Polyline();
		for (int i = 0; i <= point.SegmentIndex; i++)
		{
			polyline.Add(base[i]);
		}
		if (point.Fraction > 0.0)
		{
			polyline.Add(point.Point);
		}
		return polyline;
	}

	public Polyline SubsectionAfter(PointOnPolyline point)
	{
		Polyline polyline = new Polyline();
		if (point.Fraction < 1.0)
		{
			polyline.Add(point.Point);
		}
		for (int i = point.SegmentIndex + 1; i <= Count - 1; i++)
		{
			polyline.Add(Points[i]);
		}
		return polyline;
	}

	public void TrimBefore(PointOnPolyline pop)
	{
		if (!IsValidPolyline())
		{
			return;
		}
		if (pop.SegmentIndex == 0)
		{
			if (pop.Fraction > 0.0)
			{
				_points[0] = pop.Point;
			}
		}
		else
		{
			_points[0] = pop.Point;
			RemoveRange(1, pop.SegmentIndex);
		}
	}

	public void TrimAfter(PointOnPolyline pt)
	{
		if (pt.SegmentIndex == SegmentCount - 1)
		{
			if (pt.Fraction < 1.0)
			{
				_points[Count - 1] = pt.Point;
			}
		}
		else
		{
			RemoveRange(pt.SegmentIndex + 1);
			Add(pt.Point);
		}
	}

	public void TrimSelfIntersections(bool allowRingComplete = false, bool colinearMsAtBends = false)
	{
		if (PointCount <= 3)
		{
			return;
		}
		int i = 0;
		if (allowRingComplete)
		{
			i = 1;
		}
		List<BendStation> bendStations = null;
		for (; i < SegmentCount; i++)
		{
			Segment segment = Segments[i];
			for (int segmentCountOffset = SegmentCount - 1; segmentCountOffset >= i + 2; segmentCountOffset--)
			{
				if (segment.Intersects(Segments[segmentCountOffset], out var x, out var y, out var t, out var t2))
				{
					double interpolateValue = (HasZ ? InterpolateValue(Z, i, t) : double.NaN);
					double interpolateValue2 = (HasZ ? InterpolateValue(Z, segmentCountOffset, t2) : double.NaN);
					double interpolateValue3 = (HasM ? InterpolateValue(M, i, t) : double.NaN);
					double interpolateValue4 = (HasM ? InterpolateValue(M, segmentCountOffset, t2) : double.NaN);
					Point point = new Point(x, y);
					_points[i + 1] = point;
					if (HasZ)
					{
						Z[i + 1] = (interpolateValue + interpolateValue2) * 0.5;
					}
					if (HasM)
					{
						if (colinearMsAtBends && interpolateValue3 != interpolateValue4)
						{
							M[i + 1] = interpolateValue3;
							if (bendStations == null)
							{
								bendStations = new List<BendStation>();
							}
							bendStations.Add(new BendStation(point, interpolateValue3, interpolateValue4));
						}
						else
						{
							M[i + 1] = (interpolateValue3 + interpolateValue4) * 0.5;
						}
					}
					int count = segmentCountOffset - (i + 1);
					RemoveRange(i + 2, count);
					break;
				}
			}
		}
		if (bendStations != null)
		{
			InsertColinearBendStations(bendStations);
		}
	}

	private static double InterpolateValue(IList<double> values, int segmentIndex, double fraction)
	{
		return values[segmentIndex] + fraction * (values[segmentIndex + 1] - values[segmentIndex]);
	}

	private void InsertColinearBendStations(List<BendStation> bendStations)
	{
		for (int i = 0; i < PointCount; i++)
		{
			for (int j = 0; j < bendStations.Count; j++)
			{
				if (!(_points[i] != bendStations[j].Point) && M[i] == bendStations[j].Station)
				{
					Insert(i + 1, _points[i]);
					if (HasZ)
					{
						Z[i + 1] = Z[i];
					}
					M[i + 1] = bendStations[j].NeighborStation;
					i++;
					break;
				}
			}
		}
	}

	public Polygon GetOffsetPolygon(double width, out Polyline leftOffset, out Polyline rightOffset)
	{
		OffsetPolylineFactory.ComputeSimpleOffset(width, this, out leftOffset, out rightOffset);
		return OffsetPolylineFactory.BufferedPolygonEdgesToPolygon(leftOffset, rightOffset);
	}

	public Polygon GetOffsetPolygon(double distLeft, double distRight, out Polyline leftOffset, out Polyline rightOffset)
	{
		OffsetPolylineFactory.ComputeDirectionalOffset(distLeft, distRight, this, out leftOffset, out rightOffset);
		return OffsetPolylineFactory.BufferedPolygonEdgesToPolygon(leftOffset, rightOffset);
	}

	public void GetOffsetPolygons(double distLeft, double distRight, out Polygon leftPolygon, out Polygon rightPolygon)
	{
		OffsetPolylineFactory.ComputeDirectionalOffset(distLeft, distRight, this, out var leftOffset, out var rightOffset);
		leftPolygon = new Polygon(leftOffset.Count + Count);
		for (int i = 0; i < leftOffset.Count; i++)
		{
			leftPolygon.Add(leftOffset[i]);
		}
		for (int j = Count - 1; j >= 0; j += -1)
		{
			leftPolygon.Add(Points[j]);
		}
		rightPolygon = new Polygon(rightOffset.Count + Count);
		for (int k = 0; k < Count; k++)
		{
			rightPolygon.Add(Points[k]);
		}
		for (int countOffset = rightOffset.Count - 1; countOffset >= 0; countOffset--)
		{
			rightPolygon.Add(rightOffset[countOffset]);
		}
	}

	public void GetOffsetPolygons(double distLeft, double distRight, double distBufferUpAndDown, out Polygon leftPolygon, out Polygon rightPolygon)
	{
		new Polyline();
		new Polyline();
		Segment segment = Segments.First();
		segment.ExtendA(segment.Length + distBufferUpAndDown);
		Segment segment2 = Segments.Last();
		segment2.ExtendB(segment2.Length + distBufferUpAndDown);
		Polyline polyline = new Polyline(PointCount + 2);
		polyline.Add(segment.A);
		polyline.AddRange(Points);
		polyline.Add(segment2.B);
		polyline.GetOffsetPolygons(distLeft, distRight, out leftPolygon, out rightPolygon);
	}

	public void RasterizePolyline(RasterDefinition raster, Action<Pixel> action)
	{
		if (SegmentCount == 0)
		{
			return;
		}
		Pixel prev = Pixel.NullPixel;
		foreach (Segment segment in Segments)
		{
			segment.Rasterize(raster, (Pixel px) =>
			{
				if (!(prev == px))
				{
					prev = px;
					action(px);
				}
			});
		}
	}

	public SpatialIndex<int> GenerateSegmentSpatialIndex()
	{
		FastSegmentIterator fastSegmentIterator = new FastSegmentIterator(this);
		if (Count == 0)
		{
			return SpatialIndex<int>.DefaultSpatialIndex();
		}
		if (Extent.IsInfiniteOrInvalid())
		{
			PointCollection._logger.LogWarning("Polyline point count > 0, but extent is infinite. Points are NaN maybe?");
			return SpatialIndex<int>.DefaultSpatialIndex();
		}
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(Extent);
		for (int i = 0; i < fastSegmentIterator.CountWhenOpen; i++)
		{
			spatialIndex.Add(fastSegmentIterator[i], i);
		}
		if (fastSegmentIterator.ConnectsEndToStart)
		{
			spatialIndex.Add(fastSegmentIterator.GetClosingSegment(), fastSegmentIterator.CountWhenOpen);
		}
		return spatialIndex;
	}

	public static SpatialIndex<int> GenerateSegmentSpatialIndex_Slow(IReadOnlyList<Segment> segs, bool isOpenEnded = false)
	{
		if (segs.Count == 0)
		{
			return SpatialIndex<int>.DefaultSpatialIndex();
		}
		Extent ext = Extent.Union(segs);
		if (ext.IsInfiniteOrInvalid())
		{
			PointCollection._logger.LogWarning("Polyline point count > 0, but extent is infinite. Points are NaN maybe?");
			return SpatialIndex<int>.DefaultSpatialIndex();
		}
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(ext);
		for (int i = 0; i < segs.Count; i++)
		{
			spatialIndex.Add(segs[i], i);
		}
		return spatialIndex;
	}

	public bool RoughlyFollows(Polyline plineOther, double nearDistance)
	{
		PointOnPolyline pointOnPolyline = plineOther.NearestPointOnPolyline(First(), out var actualDistance);
		if (actualDistance > nearDistance)
		{
			return false;
		}
		for (int i = 1; i < PointCount - 1; i++)
		{
			Point pt = Points[i];
			plineOther.NearestPointOnPolyline(pt, out actualDistance);
			if (actualDistance > nearDistance)
			{
				return false;
			}
		}
		PointOnPolyline pointOnPolyline2 = plineOther.NearestPointOnPolyline(Last(), out actualDistance);
		if (actualDistance > nearDistance)
		{
			return false;
		}
		int segmentIndexOffset = pointOnPolyline.SegmentIndex + 1;
		int segmentIndex = pointOnPolyline2.SegmentIndex;
		for (int j = segmentIndexOffset; j <= segmentIndex; j++)
		{
			Point pt2 = plineOther.Points[j];
			NearestPointOnPolyline(pt2, out actualDistance);
			if (actualDistance > nearDistance)
			{
				return false;
			}
		}
		return true;
	}

	public bool RoughlyFollowsOrIsOutside(Polygon pgon, double tol, Tin devTin = null)
	{
		if (devTin == null)
		{
			devTin = pgon.DevTin;
		}
		int t = 0;
		for (int i = 0; i < PointCount; i++)
		{
			if (devTin.ContainsPointBufferedFavorOutOfBounds(Points[i], tol, ref t))
			{
				return false;
			}
		}
		PointOnPolyline pointOnPolyline = pgon.NearestPointOnPolyline(First(), out var actualDistance);
		PointOnPolyline pointOnPolyline2 = pgon.NearestPointOnPolyline(Last(), out actualDistance);
		int segmentIndexOffset = pointOnPolyline.SegmentIndex + 1;
		int segmentIndex = pointOnPolyline2.SegmentIndex;
		for (int j = segmentIndexOffset; j <= segmentIndex; j++)
		{
			Point pt = pgon.Points[j];
			PointOnPolyline pointOnPolyline3 = NearestPointOnPolyline(pt, out var actualDistance2);
			if (actualDistance2 > tol && devTin.ContainsPointBufferedFavorOutOfBounds(pointOnPolyline3.Point, tol, ref t))
			{
				return false;
			}
		}
		return true;
	}

	public bool RoughlyFollowsOrIsInside(Polygon pgon, double tol, Tin devTin = null)
	{
		if (devTin == null)
		{
			devTin = pgon.DevTin;
		}
		int t = 0;
		for (int i = 0; i < PointCount; i++)
		{
			if (!devTin.ContainsPointBufferedFavorInBounds(Points[i], tol, ref t))
			{
				return false;
			}
		}
		PointOnPolyline pointOnPolyline = pgon.NearestPointOnPolyline(First(), out var _);
		PointOnPolyline pointOnPolyline2 = pgon.NearestPointOnPolyline(Last(), out var actualDistance2);
		int segmentIndexOffset = pointOnPolyline.SegmentIndex + 1;
		int segmentIndex = pointOnPolyline2.SegmentIndex;
		for (int j = segmentIndexOffset; j <= segmentIndex; j++)
		{
			Point pt = pgon[j];
			PointOnPolyline pointOnPolyline3 = NearestPointOnPolyline(pt, out actualDistance2);
			if (actualDistance2 > tol && devTin.ContainsPointBufferedFavorOutOfBounds(pointOnPolyline3.Point, tol, ref t))
			{
				return false;
			}
		}
		throw new NotImplementedException();
	}

	public bool IsStrictlyInside(Polygon pgon, double nearDistance, Tin devTin = null)
	{
		if (devTin == null)
		{
			devTin = pgon.DevTin;
		}
		int t = 0;
		for (int i = 0; i < PointCount; i++)
		{
			if (!devTin.ContainsPointBufferedFavorInBounds(Points[i], nearDistance, ref t))
			{
				return false;
			}
		}
		PointOnPolyline pointOnPolyline = pgon.NearestPointOnPolyline(First());
		PointOnPolyline pointOnPolyline2 = pgon.NearestPointOnPolyline(Last());
		int segmentIndexOffset = pointOnPolyline.SegmentIndex + 1;
		int segmentIndex = pointOnPolyline2.SegmentIndex;
		for (int j = segmentIndexOffset; j <= segmentIndex; j++)
		{
			Point pt = pgon.Points[j];
			if (!devTin.ContainsPointBufferedFavorInBounds(NearestPointOnPolyline(pt).Point, nearDistance, ref t))
			{
				return false;
			}
		}
		return true;
	}

	public new Polyline Clone()
	{
		return new Polyline(this);
	}

	object ICloneable.Clone()
	{
		return Clone();
	}

	IEnumerator<Segment> IEnumerable<Segment>.GetEnumerator()
	{
		return Segments.GetEnumerator();
	}
}
