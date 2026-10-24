using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Geospatial.DataStructures;
using Geospatial.Rasters;
using Geospatial.Vectors.Polygons;
using Geospatial.Vectors.Polylines;
using Utility;
using Utility.Extensions;
using Utility.Logging;

namespace Geospatial.Vectors;

public class ComplexPolygon : IMutableSpatialVersion, ISpatialVersion, IExtent, IReadOnlyList<Segment>, IEnumerable<Segment>, IEnumerable, IReadOnlyCollection<Segment>
{
	protected class ComplexPolygonPointEnumerator : IReadOnlyList<Point>, IEnumerable<Point>, IEnumerable, IReadOnlyCollection<Point>
	{
		private readonly ComplexPolygon _poly;

		public Point this[int index]
		{
			get
			{
				int pointCountSum = 0;
				for (int i = 0; i < _poly.RingCount; i++)
				{
					Polygon polygon = _poly.Rings[i];
					int pointCount = polygon.PointCount;
					pointCountSum += pointCount;
					if (index < pointCountSum)
					{
						return polygon.Points[pointCount - (pointCountSum - index)];
					}
				}
				throw new ArgumentOutOfRangeException();
			}
		}

		public int Count
		{
			get
			{
				int pointCountSum = 0;
				for (int i = 0; i < _poly.Rings.Count; i++)
				{
					pointCountSum += _poly.Rings[i].PointCount;
				}
				return pointCountSum;
			}
		}

		public ComplexPolygonPointEnumerator(ComplexPolygon poly)
		{
			_poly = poly;
		}

		public IEnumerator<Point> GetEnumerator()
		{
			return GetEnumeratorInternal();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumeratorInternal();
		}

		private IEnumerator<Point> GetEnumeratorInternal()
		{
			foreach (Polygon pgon in _poly.Rings)
			{
				for (int i = 0; i < pgon.PointCount; i++)
				{
					yield return pgon.Points[i];
				}
			}
		}
	}

	public static class MessageCodes
	{
		public static readonly MessageClass NoRings = new MessageClass("ComplexPolygon_NoRings", MessageSeverity.Error, "Complex polygon requires at least 1 outer (positive area) ring.");

		public static readonly MessageClass OuterRingNegativeArea = new MessageClass("ComplexPolygon_OuterRingNegativeArea", MessageSeverity.Error, "Complex polygon requires outer ring to have positive area.");

		public static readonly MessageClass InnerRingNoParent = new MessageClass("ComplexPolygon_InnerRingNoParent", MessageSeverity.Error, "Complex polygon has a ring that is not inside the largest ring near {Coordinate}.");

		public static readonly MessageClass RingsIntersect = new MessageClass("ComplexPolygon_RingsIntersect", MessageSeverity.Error, "Polygon rings intersect, first detected at {Coordinate}");

		public static readonly MessageClass NestedRingSameOrientation = new MessageClass("ComplexPolygon_NestedRingSameOrientation", MessageSeverity.Error, "Nested rings have the same orientation (e.g. clockwise ring within another clockwise ring).");

		public static Message GetNoRings()
		{
			return NoRings.Create();
		}

		public static Message GetOuterRingNegativeArea()
		{
			return OuterRingNegativeArea.Create();
		}

		public static Message GetInnerRingNoParent(Point pt)
		{
			return InnerRingNoParent.Create(pt);
		}

		public static Message GetRingsIntersect(Point pt)
		{
			return RingsIntersect.Create(pt);
		}

		public static Message GetNestedRingSameOrientation()
		{
			return NestedRingSameOrientation.Create();
		}
	}

	private readonly IList<Polygon> _polygonCollection;

	private IReadOnlyList<Segment> _s2d;

	private long _versionXy = 1L;

	private Freezer _freezer;

	private readonly Versioned<Point> _bestLabelPointCache = new Versioned<Point>();

	public int RingCount => _polygonCollection.Count;

	public IReadOnlyList<Segment> Segments => LazyThreadsafe<IReadOnlyList<Segment>>.AllocateOnce(ref _s2d, this, _polygonCollection, (IList<Polygon> line) => new Geospatial.Vectors.Polygons.VirtualSegmentCollection(line));

	Segment IReadOnlyList<Segment>.this[int index] => Segments[index];

	int IReadOnlyCollection<Segment>.Count => SegmentCount;

	public int SegmentCount => Segments.Count;

	public Extent Extent => OuterRing.Polygon.Extent;

	public bool IsValid { get; private set; }

	public bool IsSimpleRing
	{
		get
		{
			if (Rings.Count == 1)
			{
				return Rings[0].Area > 0.0;
			}
			return false;
		}
	}

	public PolygonContainer OuterRing { get; private set; }

	public IReadOnlyList<Polygon> Rings => _polygonCollection.AsReadOnly();

	public IReadOnlyList<Point> Points { get; private set; }

	public long VersionXY => _versionXy;

	public Point BestLabelPoint => _bestLabelPointCache.Get(VersionXY, this, (ComplexPolygon self) => self.ComputeBestLabelPoint(0.05, 0.01, double.MaxValue, 20, PolygonFilters.CornerKeeper));

	public void ForceSetVersion(long version)
	{
		_versionXy = version;
	}

	public void FreezeOne()
	{
		_freezer.FreezeOne(this);
	}

	public void ThawOne()
	{
		_freezer.ThawOne();
	}

	public ComplexPolygon()
	{
		_polygonCollection = new List<Polygon>();
		Init();
	}

	public ComplexPolygon(PolygonContainer rootNode)
	{
		OuterRing = rootNode;
		_polygonCollection = rootNode.RecurseGetAllPolygons();
		Init();
	}

	public static ComplexPolygon FromOuterRing(Polygon pgon)
	{
		return new ComplexPolygon(new PolygonContainer(pgon));
	}

	private void Init()
	{
		Points = new ComplexPolygonPointEnumerator(this);
		IsValid = true;
	}

	public void AddWithoutValidation(Polygon pgon)
	{
		_polygonCollection.Add(pgon);
		Interlocked.Increment(ref _versionXy);
	}

	public SpatialIndex<Tuple<int, int>> CreateSegmentSpatialIndex()
	{
		if (RingCount == 0)
		{
			return SpatialIndex<Tuple<int, int>>.DefaultSpatialIndex();
		}
		SpatialIndex<Tuple<int, int>> spatialIndex = new SpatialIndex<Tuple<int, int>>(Extent);
		for (int i = 0; i < _polygonCollection.Count; i++)
		{
			Polygon polygon = _polygonCollection[i];
			for (int j = 0; j < polygon.SegmentCount; j++)
			{
				spatialIndex.Add(polygon.Segments[j], new Tuple<int, int>(i, j));
			}
		}
		return spatialIndex;
	}

	public double ComputeArea()
	{
		double areaSum = 0.0;
		for (int i = 0; i < _polygonCollection.Count; i++)
		{
			areaSum += _polygonCollection[i].Area;
		}
		return areaSum;
	}

	public static OperationResult TryCreateComplexPolygon(IList<Polygon> polygons, out ComplexPolygon poly, bool checkIntersections = true)
	{
		try
		{
			OperationResult operationResult = IsValidComplexPolygon(polygons, out var rootNode, checkIntersections);
			if ((bool)operationResult)
			{
				poly = new ComplexPolygon(rootNode);
				return true;
			}
			poly = null;
			return operationResult;
		}
		catch (Exception ex)
		{
			poly = null;
			return OperationResult.Fail("Create Complex Polygon", ex);
		}
	}

	public static OperationResult IsValidComplexPolygon(IList<Polygon> polygons, out PolygonContainer rootNode, bool checkIntersections = true)
	{
		if (polygons.IsNullOrEmpty())
		{
			rootNode = null;
			return OperationResult.Fail(MessageCodes.GetNoRings());
		}
		if (polygons.Count == 1)
		{
			rootNode = new PolygonContainer(polygons.First());
			if (polygons[0].Area <= 0.0)
			{
				return OperationResult.Fail(MessageCodes.GetOuterRingNegativeArea());
			}
			return true;
		}
		if (checkIntersections)
		{
			List<LineCollectionIntersection> allPerimeterIntersections = PolylineCollection.GetAllPerimeterIntersections(polygons);
			if (allPerimeterIntersections.Count > 0)
			{
				rootNode = null;
				return OperationResult.Fail(MessageCodes.GetRingsIntersect(allPerimeterIntersections[0].Intersection.Point));
			}
		}
		List<Polygon> orderByDescending = polygons.OrderByDescending((Polygon p) => Math.Abs(p.Area)).ToList();
		Dictionary<Polygon, PolygonContainer> polygonContainerByPolygon = new Dictionary<Polygon, PolygonContainer>();
		Polygon polygon = orderByDescending.First();
		rootNode = new PolygonContainer(polygon);
		polygonContainerByPolygon[polygon] = rootNode;
		if (polygon.Area < 0.0)
		{
			return false;
		}
		Tin[] tins = new Tin[polygons.Count];
		for (int unity = 1; unity < orderByDescending.Count; unity++)
		{
			Polygon polygon2 = orderByDescending[unity];
			PolygonContainer polygonContainer = (polygonContainerByPolygon[polygon2] = new PolygonContainer(polygon2));
			bool condition = false;
			for (int ordinal = unity - 1; ordinal >= 0; ordinal--)
			{
				Polygon polygon3 = orderByDescending[ordinal];
				if (polygon3.Extent.ContainsOrEquals(polygon2.Extent))
				{
					Tin tin = tins[ordinal];
					if (tin == null)
					{
						tin = (tins[ordinal] = polygon3.DevTin);
					}
					if (tin.ContainsAll(polygon2.Points))
					{
						condition = true;
						polygonContainerByPolygon[polygon3].Children.Add(polygonContainer);
						break;
					}
				}
			}
			if (!condition)
			{
				return OperationResult.Fail(MessageCodes.GetInnerRingNoParent(polygon2.Points[0]));
			}
		}
		if (!rootNode.RecurseCheckAreaInverts())
		{
			return OperationResult.Fail(MessageCodes.GetNestedRingSameOrientation());
		}
		return true;
	}

	public static bool TryCreateComplexPolygons(IList<Polygon> rings, out List<ComplexPolygon> complexPolys)
	{
		if (rings.Count == 0)
		{
			complexPolys = new List<ComplexPolygon>();
			return true;
		}
		if (rings.Count == 1)
		{
			if (rings[0].Area > 0.0)
			{
				complexPolys = new List<ComplexPolygon>();
				complexPolys.Add(FromOuterRing(rings[0]));
				return true;
			}
			complexPolys = null;
			return false;
		}
		List<Polygon> orderByDescending = rings.OrderByDescending((Polygon p) => Math.Abs(p.Area)).ToList();
		Dictionary<Polygon, PolygonContainer> polygonContainerByPolygon = new Dictionary<Polygon, PolygonContainer>();
		Polygon polygon = orderByDescending.First();
		List<PolygonContainer> polygonContainers = new List<PolygonContainer>();
		polygonContainers.Add(new PolygonContainer(polygon));
		polygonContainerByPolygon[polygon] = polygonContainers.First();
		for (int unity = 1; unity < orderByDescending.Count; unity++)
		{
			Polygon polygon2 = orderByDescending[unity];
			PolygonContainer polygonContainer = (polygonContainerByPolygon[polygon2] = new PolygonContainer(polygon2));
			bool condition = false;
			for (int ordinal = unity - 1; ordinal >= 0; ordinal--)
			{
				Polygon polygon3 = orderByDescending[ordinal];
				if (polygon3.Extent.ContainsOrEquals(polygon2.Extent) && polygon3.Contains(polygon2))
				{
					condition = true;
					polygonContainerByPolygon[polygon3].Children.Add(polygonContainer);
					break;
				}
			}
			if (!condition)
			{
				polygonContainers.Add(polygonContainer);
			}
		}
		foreach (PolygonContainer polygonContainer2 in polygonContainers)
		{
			if (polygonContainer2.Polygon.Area < 0.0)
			{
				complexPolys = null;
				return false;
			}
			if (!polygonContainer2.RecurseCheckAreaInverts())
			{
				complexPolys = null;
				return false;
			}
		}
		complexPolys = polygonContainers.Select((PolygonContainer c) => new ComplexPolygon(c)).ToList();
		return true;
	}

	public static bool TryCreateComplexPolygonByOrientation_OnlyLakesHotpath(IList<Polygon> rings, out ComplexPolygon poly)
	{
		poly = null;
		if (rings.IsNullOrEmpty())
		{
			return false;
		}
		int ordinal = -1;
		for (int i = 0; i < rings.Count; i++)
		{
			if (rings[i].Area > 0.0)
			{
				if (ordinal >= 0)
				{
					return false;
				}
				ordinal = i;
			}
		}
		if (ordinal < 0)
		{
			return false;
		}
		PolygonContainer polygonContainer = new PolygonContainer(rings[ordinal]);
		for (int j = 0; j < rings.Count; j++)
		{
			if (j != ordinal)
			{
				polygonContainer.Children.Add(new PolygonContainer(rings[j]));
			}
		}
		poly = new ComplexPolygon(polygonContainer);
		return true;
	}

	public void Translate(double x, double y)
	{
		foreach (Polygon polygonCollection in _polygonCollection)
		{
			polygonCollection.Translate(x, y);
		}
		Interlocked.Increment(ref _versionXy);
	}

	public Point ComputeBestLabelPoint(double precisionRatio = 0.01, double absoluteMinPrecision = 0.01, double absoluteMaxPrecision = double.MaxValue, int maxSearchDepth = 20, PolygonFilter filter = null)
	{
		double area = OuterRing.Polygon.Area;
		if (area == 0.0)
		{
			return Point.NullPoint;
		}
		if (Math.Abs(area) < Math.Pow(absoluteMinPrecision, 2.0))
		{
			return OuterRing.Polygon.ComputeCentroid();
		}
		double areaPrecisionRatioTerm = Math.Sqrt(Math.Abs(area)) * precisionRatio;
		if (areaPrecisionRatioTerm < absoluteMinPrecision)
		{
			areaPrecisionRatioTerm = absoluteMinPrecision;
		}
		if (areaPrecisionRatioTerm > absoluteMaxPrecision)
		{
			areaPrecisionRatioTerm = absoluteMaxPrecision;
		}
		Extent extent = Extent;
		if (extent.Area <= 0.0)
		{
			return extent.Center;
		}
		PolygonFilter polygonFilter = filter ?? PolygonFilters.Default;
		int count = _polygonCollection.Count;
		Polygon[] polygons = new Polygon[count];
		for (int i = 0; i < count; i++)
		{
			polygons[i] = polygonFilter(_polygonCollection[i], areaPrecisionRatioTerm);
		}
		return BestLabelPointSearch.Run(polygons, extent, areaPrecisionRatioTerm, maxSearchDepth);
	}

	public Point ComputeGoodIshNearestInteriorPointForView(Point pt, Extent view)
	{
		int ordinal = 64;
		double computed = 1.0 / (double)ordinal;
		double maxValue = double.MaxValue;
		Point result = Point.NullPoint;
		Range1D range1D = Range1D.NullRange;
		int countStart = -1;
		bool condition = true;
		Point[] points = new Point[ordinal * 4 - 4];
		Range1D range1D2 = Range1D.NullRange;
		Range1D range1D3 = Range1D.NullRange;
		for (int i = 0; i < 4; i++)
		{
			int ordinal2 = i * ordinal - i;
			for (int j = 0; j < ordinal - ((i == 3) ? 1 : 0); j++)
			{
				int countStart2 = ordinal2 + j;
				double heightViewTerm = (double)j * computed * view.Height;
				double widthViewTerm = (double)j * computed * view.Width;
				Point point = Point.NullPoint;
				switch (i)
				{
				case 0:
					point = new Point(view.Left, view.Bot + heightViewTerm);
					break;
				case 2:
					point = new Point(view.Right, view.Top - heightViewTerm);
					break;
				case 1:
					point = new Point(view.Left + widthViewTerm, view.Top);
					break;
				case 3:
					point = new Point(view.Right - widthViewTerm, view.Bot);
					break;
				}
				if (!Extent.Contains(point))
				{
					continue;
				}
				points[countStart2] = point;
				if (ContainsPoint_Slow(point))
				{
					if (range1D3.IsNull())
					{
						range1D3 = new Range1D(countStart2, countStart2);
					}
					range1D3 = new Range1D(range1D3.Start, countStart2);
					double absoluteDistanceTo = Math.Abs(point.DistanceTo(pt));
					if (absoluteDistanceTo < maxValue)
					{
						maxValue = absoluteDistanceTo;
						result = point;
						countStart = countStart2;
						condition = true;
					}
					if (condition)
					{
						range1D = range1D3;
						if (countStart2 == 0 || range1D2.Start == 0)
						{
							range1D2 = range1D;
						}
					}
				}
				else
				{
					range1D3 = Range1D.NullRange;
					condition = false;
				}
			}
		}
		if (range1D.Start == 0 && range1D.End == points.Length - 1)
		{
			return result;
		}
		if (!range1D2.IsNull() && range1D2 != range1D && range1D.End == points.Length - 1)
		{
			range1D = new Range1D(range1D.Start, range1D2.End + points.Length);
		}
		if (range1D.End - range1D.Start > 2)
		{
			int countEnd = (range1D.End - range1D.Start) / 2 + range1D.Start;
			if (countStart == countEnd)
			{
				return result;
			}
			double widthViewTerm2 = Math.Max(view.Width, view.Height);
			double maxValueTerm = maxValue / (widthViewTerm2 * 1.0);
			maxValueTerm = Math.Min(1.0, maxValueTerm);
			Polyline polyline;
			Polyline polyline2;
			if (countStart < countEnd)
			{
				polyline = new Polyline(countEnd - countStart + 1);
				for (int k = countStart; k <= countEnd; k++)
				{
					polyline.Add(points[k % points.Length]);
				}
				polyline2 = new Polyline(countStart - range1D.Start + 1);
				for (int l = range1D.Start; l <= countStart; l++)
				{
					polyline2.Add(points[l % points.Length]);
				}
			}
			else
			{
				polyline = new Polyline(countStart - countEnd + 1);
				for (int count = countStart; count >= countEnd; count--)
				{
					polyline.Add(points[count % points.Length]);
				}
				polyline2 = new Polyline(range1D.End - countStart + 1);
				for (int m = countStart; m <= range1D.End; m++)
				{
					polyline2.Add(points[m % points.Length]);
				}
			}
			double computed2 = 0.25 * widthViewTerm2;
			maxValueTerm = Math.Min(Math.Max(0.0, computed2 - polyline2.Length) / polyline.Length, maxValueTerm);
			result = polyline.PointAtFraction(maxValueTerm).Point;
		}
		return result;
	}

	public bool ContainsPoint_Slow(Point pt)
	{
		if (OuterRing == null)
		{
			return false;
		}
		if (!OuterRing.Polygon.Extent.Contains(pt))
		{
			return false;
		}
		return ContainsPoint_Slow_IterativeHelper(OuterRing, pt);
	}

	private bool ContainsPoint_Slow_IterativeHelper(PolygonContainer container, Point pt)
	{
		if (!container.Polygon.Contains(pt))
		{
			return false;
		}
		if (container.Children == null || container.Children.Count == 0)
		{
			return true;
		}
		for (int i = 0; i < container.Children.Count; i++)
		{
			PolygonContainer polygonContainer = container.Children[i];
			if (polygonContainer.Polygon.Extent.Contains(pt) && polygonContainer.Polygon.Contains(pt))
			{
				return !ContainsPoint_Slow_IterativeHelper(polygonContainer, pt);
			}
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool SignedPointToPolygonDistance_Op(in Segment seg, Point pt, ref double minDistSq)
	{
		bool result = false;
		Point a = seg.A;
		Point b = seg.B;
		if (a.Y > pt.Y != b.Y > pt.Y && pt.X < (b.X - a.X) * (pt.Y - a.Y) / (b.Y - a.Y) + a.X)
		{
			result = true;
		}
		double distanceToPointSquared = seg.DistanceToPointSquared(pt);
		minDistSq = Math.Min(minDistSq, distanceToPointSquared);
		return result;
	}

	public double SignedPointToPolygonDistance(Point pt)
	{
		return SignedPointToPolygonDistance(_polygonCollection, pt);
	}

	public static double SignedPointToPolygonDistance(IList<Polygon> rings, Point pt)
	{
		double minDistSq = double.MaxValue;
		bool condition = false;
		int count = rings.Count;
		for (int i = 0; i < count; i++)
		{
			FastSegmentIterator fastSegmentIterator = new FastSegmentIterator(rings[i], isClosed: true);
			int countWhenOpen = fastSegmentIterator.CountWhenOpen;
			for (int j = 0; j < countWhenOpen; j++)
			{
				if (SignedPointToPolygonDistance_Op(in fastSegmentIterator[j], pt, ref minDistSq))
				{
					condition = !condition;
				}
			}
			if (SignedPointToPolygonDistance_Op(fastSegmentIterator.GetClosingSegment(), pt, ref minDistSq))
			{
				condition = !condition;
			}
		}
		if (condition)
		{
			return Math.Sqrt(minDistSq);
		}
		return 0.0 - Math.Sqrt(minDistSq);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void SignedPointsToPolygonDistance_Op(in Segment seg, Span<Point> pts, Span<double> minDistSqs, Span<bool> isInsides)
	{
		for (int i = 0; i < pts.Length; i++)
		{
			ref Point reference = ref pts[i];
			Point a = seg.A;
			Point b = seg.B;
			if (a.Y > reference.Y != b.Y > reference.Y && reference.X < (b.X - a.X) * (reference.Y - a.Y) / (b.Y - a.Y) + a.X)
			{
				isInsides[i] = !isInsides[i];
			}
			double distanceToPointSquared = seg.DistanceToPointSquared(reference);
			minDistSqs[i] = Math.Min(minDistSqs[i], distanceToPointSquared);
		}
	}

	public void SignedPointsToPolygonDistance(Span<Point> pts, Span<double> distances)
	{
		SignedPointsToPolygonDistance(_polygonCollection, pts, distances);
	}

	public static void SignedPointsToPolygonDistance(IList<Polygon> rings, Span<Point> pts, Span<double> distances)
	{
		if (pts.Length != distances.Length)
		{
			throw new ArgumentException("pts length must equal distances length");
		}
		Span<bool> boolSpan = ((pts.Length > 32) ? ((Span<bool>)new bool[pts.Length]) : stackalloc bool[pts.Length]);
		Span<bool> isInsides = boolSpan;
		int count = rings.Count;
		for (int i = 0; i < count; i++)
		{
			FastSegmentIterator fastSegmentIterator = new FastSegmentIterator(rings[i], isClosed: true);
			int countWhenOpen = fastSegmentIterator.CountWhenOpen;
			for (int j = 0; j < countWhenOpen; j++)
			{
				SignedPointsToPolygonDistance_Op(in fastSegmentIterator[j], pts, distances, isInsides);
			}
			SignedPointsToPolygonDistance_Op(fastSegmentIterator.GetClosingSegment(), pts, distances, isInsides);
		}
		for (int k = 0; k < pts.Length; k++)
		{
			if (isInsides[k])
			{
				distances[k] = Math.Sqrt(distances[k]);
			}
			else
			{
				distances[k] = 0.0 - Math.Sqrt(distances[k]);
			}
		}
	}

	public IEnumerator<Segment> GetEnumerator()
	{
		return Segments.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}
}
