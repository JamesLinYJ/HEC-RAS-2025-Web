using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Geospatial.DataStructures;
using Geospatial.Rasters;
using Geospatial.Vectors.Polylines;
using Utility;
using Utility.DataStructures;

namespace Geospatial.Vectors;

public class PolylineCollection : VectorCollectionBase<Polyline>, IPolylineCollection, IVectorCollection<Polyline>, IReadOnlyVectorCollection<Polyline>, IEnumerable<Polyline>, IEnumerable, ISpatialVersion, IExtent, IReadOnlyList<Polyline>, IReadOnlyCollection<Polyline>, ISpatiallyIndexedSegmentCollection, ILineSegmentSource
{
	private readonly Versioned<SpatialIndex<FeatureSegmentSubset>> _segSpIdxCache = new Versioned<SpatialIndex<FeatureSegmentSubset>>();

	private readonly Versioned<SpatialIndex<int>> _spIdxCache = new Versioned<SpatialIndex<int>>();

	private long _internalVxy;

	public override long VersionXY => _internalVxy;

	public PolylineCollection(IEnumerable<Polyline> polylines)
		: base(polylines)
	{
	}

	public PolylineCollection()
	{
	}

	public PolylineCollection(int capacity)
		: base(capacity)
	{
	}

	protected override void Init()
	{
		base.Init();
		for (int i = 0; i < Count; i++)
		{
			AddHooks(_elements[i]);
		}
	}

	public override void Add(Polyline localValue)
	{
		base.Add(localValue);
		AddHooks(localValue);
		_internalVxy++;
	}

	public override void Clear()
	{
		for (int i = 0; i < Count; i++)
		{
			RemoveHooks(_elements[i]);
		}
		base.Clear();
		_internalVxy++;
	}

	public override void Insert(int index, Polyline localValue)
	{
		base.Insert(index, localValue);
		AddHooks(localValue);
		_internalVxy++;
	}

	public override void RemoveAt(int index)
	{
		Polyline pl = _elements[index];
		RemoveHooks(pl);
		base.RemoveAt(index);
		_internalVxy++;
	}

	public void RemoveShorterThan(double minLength)
	{
		for (int countOffset = Count - 1; countOffset >= 0; countOffset--)
		{
			if (base[countOffset].Length < minLength)
			{
				RemoveAt(countOffset);
			}
		}
	}

	private void AddHooks(Polyline pl)
	{
		pl.OnDataModified += PolylineChanged;
	}

	private void RemoveHooks(Polyline pl)
	{
		pl.OnDataModified -= PolylineChanged;
	}

	private void PolylineChanged()
	{
		Interlocked.Increment(ref _internalVxy);
	}

	public SpatialIndex<FeatureSegmentSubset> GetSegmentSpatialIndex()
	{
		return _segSpIdxCache.Get(VersionXY, ComputeSegmentSpatialIndex);
	}

	public SpatialIndex<int> GetSpatialIndex()
	{
		return _spIdxCache.Get(VersionXY, ComputeSpatialIndex);
	}

	public Polyline GetLine(int fid)
	{
		return base[fid];
	}

	private SpatialIndex<FeatureSegmentSubset> ComputeSegmentSpatialIndex()
	{
		return SpatialIndex.ComputeSegmentSpatialIndex<Polyline>((IList<Polyline>)_elements, (RasterDefinition?)null);
	}

	private SpatialIndex<int> ComputeSpatialIndex()
	{
		return SpatialIndex.ComputeSpatialIndex(_elements);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool FixPolylineIndexFraction(ref int segIdx, ref double frac, double endPointTolerancePercent)
	{
		if (frac >= endPointTolerancePercent)
		{
			frac = 1.0;
		}
		if (frac <= 1.0 - endPointTolerancePercent)
		{
			frac = 0.0;
		}
		if (segIdx == 0)
		{
			return false;
		}
		if (frac != 0.0)
		{
			return true;
		}
		frac = 1.0;
		segIdx--;
		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static LineCollectionIntersection BuildLineCollectionIntersection(int aPlIdx, int bPlIdx, int aSegIdx, int bSegIdx, Segment aSeg, Polyline bPolyline, bool includeEndpointIntersections, double endPointTolerancePercent)
	{
		if (aPlIdx == bPlIdx)
		{
			int nextSegmentIndex = bPolyline.NextSegmentIndex(bSegIdx);
			int previousSegmentIndex = bPolyline.PreviousSegmentIndex(bSegIdx);
			if (aSegIdx == bSegIdx || aSegIdx == nextSegmentIndex || aSegIdx == previousSegmentIndex)
			{
				return LineCollectionIntersection.NullIntersection;
			}
		}
		if (aSeg.Intersects(bPolyline.Segments[bSegIdx], out var x, out var y, out var t, out var t2))
		{
			if (!includeEndpointIntersections && (t <= 9.999999974752427E-07 || t >= 0.9999989867210388) && (t2 <= 9.999999974752427E-07 || t2 >= 0.9999989867210388))
			{
				return LineCollectionIntersection.NullIntersection;
			}
			int segIdx = aSegIdx;
			int segIdx2 = bSegIdx;
			FixPolylineIndexFraction(ref segIdx, ref t, endPointTolerancePercent);
			FixPolylineIndexFraction(ref segIdx2, ref t2, endPointTolerancePercent);
			return new LineCollectionIntersection(new LineIntersection(x, y, segIdx, t, segIdx2, t2), aPlIdx, bPlIdx);
		}
		return LineCollectionIntersection.NullIntersection;
	}

	public static List<LineCollectionIntersection> GetAllIntersections(IList<Polyline> plines, SpatialIndex<FeatureSegmentSubset> cachedSpIdx = null, bool includeEndptIntersections = true, double endPtTolPercent = 1.0, bool checkForIdenticalEndpoint = false, bool debugForceSlowMethod = false)
	{
		if (plines.Count == 0)
		{
			return new List<LineCollectionIntersection>();
		}
		SpatialIndex<FeatureSegmentSubset> spatialIndex = cachedSpIdx ?? SpatialIndex.ComputeSegmentSpatialIndex<Polyline>(plines, (RasterDefinition?)null);
		ThreadLocal<List<LineCollectionIntersection>> tlLineIntersectionList = new ThreadLocal<List<LineCollectionIntersection>>(() => new List<LineCollectionIntersection>(), trackAllValues: true);
		bool isPolygon = plines[0] is Polygon;
		System.Threading.Tasks.Parallel.ForEach(spatialIndex.Bins, (List<FeatureSegmentSubset> bin) =>
		{
			int count = bin.Count;
			if (count != 0)
			{
				List<LineCollectionIntersection> lineCollectionIntersections = tlLineIntersectionList.Value;
				if (debugForceSlowMethod | isPolygon)
				{
					for (int i = 0; i < count; i++)
					{
						FeatureSegmentSubset featureSegmentSubset = bin[i];
						int fid = featureSegmentSubset.Fid;
						Polyline polyline = plines[fid];
						int count2 = featureSegmentSubset.Ranges.Count;
						for (int j = 0; j < count2; j++)
						{
							Range1D range1D = featureSegmentSubset.Ranges[j];
							for (int k = range1D.Start; k <= range1D.End; k++)
							{
								Segment aSeg = polyline.Segments[k];
								for (int l = i; l < count; l++)
								{
									FeatureSegmentSubset featureSegmentSubset2 = bin[l];
									int fid2 = featureSegmentSubset2.Fid;
									Polyline bPolyline = plines[fid2];
									int count3 = featureSegmentSubset2.Ranges.Count;
									for (int m = 0; m < count3; m++)
									{
										Range1D range1D2 = featureSegmentSubset2.Ranges[m];
										for (int n = range1D2.Start; n <= range1D2.End; n++)
										{
											LineCollectionIntersection lineCollectionIntersection = BuildLineCollectionIntersection(fid, fid2, k, n, aSeg, bPolyline, includeEndptIntersections, endPtTolPercent);
											if (!lineCollectionIntersection.IsNull)
											{
												lineCollectionIntersections.Add(lineCollectionIntersection);
											}
										}
									}
								}
							}
						}
					}
				}
				else
				{
					for (int intervalIndex = 0; intervalIndex < count; intervalIndex++)
					{
						FeatureSegmentSubset featureSegmentSubset3 = bin[intervalIndex];
						int fid3 = featureSegmentSubset3.Fid;
						Polyline pline = plines[fid3];
						FastSegmentIterator fastSegmentIterator = new FastSegmentIterator(pline, isClosed: false);
						int count4 = featureSegmentSubset3.Ranges.Count;
						for (int intervalIndex2 = 0; intervalIndex2 < count4; intervalIndex2++)
						{
							Range1D range1D3 = featureSegmentSubset3.Ranges[intervalIndex2];
							int start = range1D3.Start;
							for (int start2 = start; start2 <= range1D3.End; start2++)
							{
								ref Segment reference = ref fastSegmentIterator[start2];
								for (int ordinal = intervalIndex; ordinal < count; ordinal++)
								{
									FeatureSegmentSubset featureSegmentSubset4 = bin[ordinal];
									int fid4 = featureSegmentSubset4.Fid;
									Polyline pline2 = plines[fid4];
									FastSegmentIterator fastSegmentIterator2 = new FastSegmentIterator(pline2, isClosed: false);
									int count5 = featureSegmentSubset4.Ranges.Count;
									for (int intervalIndex3 = 0; intervalIndex3 < count5; intervalIndex3++)
									{
										Range1D range1D4 = featureSegmentSubset4.Ranges[intervalIndex3];
										int start3 = range1D4.Start;
										if (fid3 == fid4 && intervalIndex2 == intervalIndex3)
										{
											start3 = start + 2;
										}
										for (int start4 = start3; start4 <= range1D4.End; start4++)
										{
											if (fid3 == fid4)
											{
												int start4Offset = start4 + 1;
												int start4Offset2 = start4 - 1;
												if (start2 == start4 || start2 == start4Offset || start2 == start4Offset2)
												{
													continue;
												}
											}
											ref Segment reference2 = ref fastSegmentIterator2[start4];
											if (reference.Intersects(in reference2, out var x, out var y, out var t, out var t2) && (includeEndptIntersections || (((!(t <= 9.999999974752427E-07) && !(t >= 0.9999989867210388)) || (!(t2 <= 9.999999974752427E-07) && !(t2 >= 0.9999989867210388))) && (!checkForIdenticalEndpoint || (!reference.A.EqualXY(in reference2.A) && !reference.A.EqualXY(in reference2.B) && !reference.B.EqualXY(in reference2.A) && !reference.B.EqualXY(in reference2.B))))))
											{
												int segIdx = start2;
												int segIdx2 = start4;
												FixPolylineIndexFraction(ref segIdx, ref t, endPtTolPercent);
												FixPolylineIndexFraction(ref segIdx2, ref t2, endPtTolPercent);
												LineIntersection intersection = new LineIntersection(x, y, segIdx, t, segIdx2, t2);
												lineCollectionIntersections.Add(new LineCollectionIntersection(intersection, fid3, fid4));
											}
										}
									}
								}
							}
						}
					}
				}
			}
		});
		List<LineCollectionIntersection> lineCollectionIntersectionsList = new List<LineCollectionIntersection>();
		HashSet<(int, int, int, int)> valueTuples = new HashSet<(int, int, int, int)>();
		for (int intervalIndex4 = 0; intervalIndex4 < tlLineIntersectionList.Values.Count; intervalIndex4++)
		{
			List<LineCollectionIntersection> lineCollectionIntersections2 = tlLineIntersectionList.Values[intervalIndex4];
			for (int intervalIndex5 = 0; intervalIndex5 < lineCollectionIntersections2.Count; intervalIndex5++)
			{
				LineCollectionIntersection lineCollectionIntersection2 = lineCollectionIntersections2[intervalIndex5];
				int aPolylineIndex = lineCollectionIntersection2.APolylineIndex;
				int bPolylineIndex = lineCollectionIntersection2.BPolylineIndex;
				int segmentIndex = lineCollectionIntersection2.Intersection.AIntersection.SegmentIndex;
				int segmentIndex2 = lineCollectionIntersection2.Intersection.BIntersection.SegmentIndex;
				(int, int, int, int) aPolylineIndexSegmentIndexTerm;
				if (aPolylineIndex < bPolylineIndex)
				{
					aPolylineIndexSegmentIndexTerm = (aPolylineIndex, segmentIndex, bPolylineIndex, segmentIndex2);
				}
				else if (aPolylineIndex > bPolylineIndex)
				{
					aPolylineIndexSegmentIndexTerm = (bPolylineIndex, segmentIndex2, aPolylineIndex, segmentIndex);
				}
				else
				{
					aPolylineIndexSegmentIndexTerm = ((segmentIndex >= segmentIndex2) ? (bPolylineIndex, segmentIndex2, aPolylineIndex, segmentIndex) : (aPolylineIndex, segmentIndex, bPolylineIndex, segmentIndex2));
				}
				if (valueTuples.Add(aPolylineIndexSegmentIndexTerm))
				{
					lineCollectionIntersectionsList.Add(lineCollectionIntersection2);
				}
			}
		}
		return lineCollectionIntersectionsList;
	}

	public static List<LineCollectionIntersection> GetIntersectionsWithLine<T>(Polyline subject, int subjectFid, IList<FeatureSegmentSubset> candidates, T model, Func<T, int, Polyline> getLine, bool includeEndptIntersections = true, double endPtTolPercent = 1.0, bool checkForIdenticalEndpoint = false)
	{
		List<LineCollectionIntersection> lineCollectionIntersections = new List<LineCollectionIntersection>();
		if (candidates.Count == 0)
		{
			return lineCollectionIntersections;
		}
		FastSegmentIterator fastSegmentIterator = new FastSegmentIterator(subject, isClosed: false);
		int countWhenOpen = fastSegmentIterator.CountWhenOpen;
		if (countWhenOpen <= 0)
		{
			return lineCollectionIntersections;
		}
		HashSet<(int, int, int, int)> valueTuples = new HashSet<(int, int, int, int)>();
		for (int i = 0; i < candidates.Count; i++)
		{
			FeatureSegmentSubset featureSegmentSubset = candidates[i];
			int fid = featureSegmentSubset.Fid;
			Polyline pline = ((fid == subjectFid) ? subject : getLine(model, fid));
			FastSegmentIterator fastSegmentIterator2 = new FastSegmentIterator(pline, isClosed: false);
			for (int j = 0; j < featureSegmentSubset.Ranges.Count; j++)
			{
				Range1D range1D = featureSegmentSubset.Ranges[j];
				for (int k = range1D.Start; k <= range1D.End; k++)
				{
					ref Segment reference = ref fastSegmentIterator2[k];
					for (int l = 0; l < countWhenOpen; l++)
					{
						if (fid == subjectFid && (l == k || l == k + 1 || l == k - 1))
						{
							continue;
						}
						ref Segment reference2 = ref fastSegmentIterator[l];
						if (reference2.Intersects(in reference, out var x, out var y, out var t, out var t2) && (includeEndptIntersections || (((!(t <= 9.999999974752427E-07) && !(t >= 0.9999989867210388)) || (!(t2 <= 9.999999974752427E-07) && !(t2 >= 0.9999989867210388))) && (!checkForIdenticalEndpoint || (!reference2.A.EqualXY(in reference.A) && !reference2.A.EqualXY(in reference.B) && !reference2.B.EqualXY(in reference.A) && !reference2.B.EqualXY(in reference.B))))))
						{
							int segIdx = l;
							int segIdx2 = k;
							FixPolylineIndexFraction(ref segIdx, ref t, endPtTolPercent);
							FixPolylineIndexFraction(ref segIdx2, ref t2, endPtTolPercent);
							(int, int, int, int) subjectFidFidTerm = ((subjectFid >= fid && (subjectFid != fid || segIdx > segIdx2)) ? (fid, segIdx2, subjectFid, segIdx) : (subjectFid, segIdx, fid, segIdx2));
							if (valueTuples.Add(subjectFidFidTerm))
							{
								LineIntersection intersection = new LineIntersection(x, y, segIdx, t, segIdx2, t2);
								lineCollectionIntersections.Add(new LineCollectionIntersection(intersection, subjectFid, fid));
							}
						}
					}
				}
			}
		}
		return lineCollectionIntersections;
	}

	public static IList<LineCollectionIntersection> GetAllIntersections<T>(IList<FeatureSegmentSubset> candidates, T model, Func<T, int, Polyline> getLine, bool includeEndptIntersections = true, double endPtTolPercent = 1.0)
	{
		if (candidates.Count == 0)
		{
			return CompactList<LineCollectionIntersection>.Empty;
		}
		CompactList<LineCollectionIntersection> compactList = default;
		for (int i = 0; i < candidates.Count; i++)
		{
			FeatureSegmentSubset featureSegmentSubset = candidates[i];
			int fid = featureSegmentSubset.Fid;
			Polyline polyline = getLine(model, fid);
			for (int j = 0; j < featureSegmentSubset.Ranges.Count; j++)
			{
				Range1D range1D = featureSegmentSubset.Ranges[j];
				for (int k = range1D.Start; k <= range1D.End; k++)
				{
					Segment aSeg = polyline.Segments[k];
					for (int l = i; l < candidates.Count; l++)
					{
						FeatureSegmentSubset featureSegmentSubset2 = candidates[l];
						int fid2 = featureSegmentSubset2.Fid;
						Polyline bPolyline = getLine(model, fid2);
						bool condition = i == l;
						for (int m = (condition ? j : 0); m < featureSegmentSubset2.Ranges.Count; m++)
						{
							Range1D range1D2 = featureSegmentSubset2.Ranges[m];
							bool condition2 = j == m;
							for (int n = ((condition & condition2) ? (k + 1) : range1D2.Start); n <= range1D2.End; n++)
							{
								LineCollectionIntersection lineCollectionIntersection = BuildLineCollectionIntersection(fid, fid2, k, n, aSeg, bPolyline, includeEndptIntersections, endPtTolPercent);
								if (!lineCollectionIntersection.IsNull)
								{
									compactList.Add(lineCollectionIntersection);
								}
							}
						}
					}
				}
			}
		}
		return compactList;
	}

	public static List<LineCollectionIntersection> GetAllPerimeterIntersections(IList<Polygon> polygons)
	{
		return GetAllIntersections(((IEnumerable<Polygon>)polygons).Select((Func<Polygon, Polyline>)((Polygon pg) => pg)).ToArray());
	}

	public static RasterDefinition GetCommonSpatialIndexRaster(params IList<Polyline>[] plCollections)
	{
		Extent targetExtent = Extent.Union(plCollections.Select((IList<Polyline> collection) => Extent.Union(collection))).Extend(0.01).RatioAroundCenter(1.01);
		int maxCellCount = SpatialIndex.ReasonableBinCount(plCollections.Max((IList<Polyline> collection) => collection.Sum((Polyline pl) => pl?.SegmentCount ?? 0)));
		return RasterDefinition.ComputeBoundingRaster(targetExtent, maxCellCount);
	}

	public static List<LineCollectionIntersection> GetABIntersections(IList<Polyline> aPolylines, IList<Polyline> bPolylines)
	{
		RasterDefinition commonSpatialIndexRaster = GetCommonSpatialIndexRaster(aPolylines, bPolylines);
		SpatialIndex<Tuple<int, int>> aSpatialIndex = SpatialIndex.ComputeSegmentSpatialIndex(aPolylines, commonSpatialIndexRaster);
		SpatialIndex<Tuple<int, int>> bSpatialIndex = SpatialIndex.ComputeSegmentSpatialIndex(bPolylines, commonSpatialIndexRaster);
		return GetABIntersections(aPolylines, bPolylines, aSpatialIndex, bSpatialIndex);
	}

	public static List<LineCollectionIntersection> GetABIntersections(IList<Polyline> aPolylines, IList<Polyline> bPolylines, SpatialIndex<Tuple<int, int>> aSpatialIndex, SpatialIndex<Tuple<int, int>> bSpatialIndex)
	{
		if (!aSpatialIndex.Raster.EqualRasterDefinition(bSpatialIndex.Raster))
		{
			throw new Exception("Spatial Indexes must use the same RasterDefinition to find AB intersections.");
		}
		RasterDefinition raster = aSpatialIndex.Raster;
		ThreadLocal<List<LineCollectionIntersection>> tlLineIntersectionList = new ThreadLocal<List<LineCollectionIntersection>>(() => new List<LineCollectionIntersection>(), trackAllValues: true);
		System.Threading.Tasks.Parallel.For(0, raster.CellCount, (int binIdx) =>
		{
			List<Tuple<int, int>> bin = aSpatialIndex.Bins[binIdx];
			List<Tuple<int, int>> binList = bSpatialIndex.Bins[binIdx];
			if (bin.Count != 0 && binList.Count != 0)
			{
				List<LineCollectionIntersection> lineCollectionIntersections = tlLineIntersectionList.Value;
				for (int i = 0; i < bin.Count; i++)
				{
					Tuple<int, int> tuple = bin[i];
					int tupleTerm = tuple.Item1;
					int tupleTerm2 = tuple.Item2;
					Polyline polyline = aPolylines[tupleTerm];
					Segment segment = polyline.Segments[tupleTerm2];
					for (int j = 0; j < binList.Count; j++)
					{
						Tuple<int, int> tuple2 = binList[j];
						int tuple2Term = tuple2.Item1;
						int tuple2Term2 = tuple2.Item2;
						Polyline polyline2 = bPolylines[tuple2Term];
						if (segment.Intersects(polyline2.Segments[tuple2Term2], out var x, out var y, out var t, out var t2, 1E-05))
						{
							if (Math.Abs(t - 1.0) < 1E-05 && tupleTerm2 < polyline.SegmentCount - 1)
							{
								tupleTerm2++;
								t = 0.0;
							}
							else if (t < 1E-05)
							{
								t = 0.0;
							}
							if (Math.Abs(t2 - 1.0) < 1E-05 && tuple2Term2 < polyline2.SegmentCount - 1)
							{
								tuple2Term2++;
								t2 = 0.0;
							}
							else if (t2 < 1E-05)
							{
								t2 = 0.0;
							}
							LineIntersection intersection = new LineIntersection(x, y, tupleTerm2, t, tuple2Term2, t2);
							lineCollectionIntersections.Add(new LineCollectionIntersection(intersection, tupleTerm, tuple2Term));
						}
					}
				}
			}
		});
		HashSet<Tuple<int, int, int, int>> tupleSet = new HashSet<Tuple<int, int, int, int>>();
		List<LineCollectionIntersection> lineCollectionIntersectionsList = new List<LineCollectionIntersection>();
		foreach (List<LineCollectionIntersection> lineCollectionIntersections2 in tlLineIntersectionList.Values)
		{
			foreach (LineCollectionIntersection lineCollectionIntersection in lineCollectionIntersections2)
			{
				int aPolylineIndex = lineCollectionIntersection.APolylineIndex;
				int bPolylineIndex = lineCollectionIntersection.BPolylineIndex;
				int segmentIndex = lineCollectionIntersection.Intersection.AIntersection.SegmentIndex;
				int segmentIndex2 = lineCollectionIntersection.Intersection.BIntersection.SegmentIndex;
				Tuple<int, int, int, int> aPolylineIndexBPolylineIndexTerm = new Tuple<int, int, int, int>(aPolylineIndex, bPolylineIndex, segmentIndex, segmentIndex2);
				if (tupleSet.Add(aPolylineIndexBPolylineIndexTerm))
				{
					lineCollectionIntersectionsList.Add(lineCollectionIntersection);
				}
			}
		}
		return lineCollectionIntersectionsList;
	}
}
