using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.DataStructures;
using Geospatial.Vectors;

namespace Geospatial.Helpers;

public static class HitTestHelpers
{
	public static IEnumerable<T> HitTest<T>(Point pt, SpatialIndex<T> spIdx, Func<T, Point, double, bool> hitTest, double hitDistance = double.NaN, Func<T, Point, double> distanceTo = null, HitTestSortOrder sortOrder = HitTestSortOrder.None)
	{
		if (spIdx == null)
		{
			yield break;
		}
		if (sortOrder == HitTestSortOrder.Distance)
		{
			SortedList<double, T> sortedList = HitTestByDistance(pt, spIdx, hitDistance, distanceTo);
			foreach (KeyValuePair<double, T> sortedList2 in sortedList)
			{
				yield return sortedList2.Value;
			}
			yield break;
		}
		IEnumerable<T> enumerable = spIdx.GetElements(pt, hitDistance);
		if (sortOrder == HitTestSortOrder.Index)
		{
			enumerable = enumerable.OrderBy((T i) => i);
		}
		foreach (T enumerable2 in enumerable)
		{
			if (hitTest(enumerable2, pt, hitDistance))
			{
				yield return enumerable2;
			}
		}
	}

	public static SortedList<K, T> HitTestBy<T, K>(Point pt, SpatialIndex<T> spIdx, double hitDistance, K sufficientVal, Func<T, Point, K> getVal, IComparer<K> comparer = null)
	{
		if (spIdx == null)
		{
			return new SortedList<K, T>();
		}
		IEnumerable<T> elements = spIdx.GetElements(pt, hitDistance);
		if (!elements.Any())
		{
			return new SortedList<K, T>();
		}
		SortedList<K, T> sortedList = new SortedList<K, T>(comparer);
		foreach (T element in elements)
		{
			K getVal2 = getVal(element, pt);
			if (comparer.Compare(getVal2, sufficientVal) < 0 && !sortedList.ContainsKey(getVal2))
			{
				sortedList.Add(getVal2, element);
			}
		}
		return sortedList;
	}

	public static bool TryHitTestByBest<T, K, J>(Point pt, SpatialIndex<T> spIdx, K sufficientVal, Func<T, Point, K, bool> sufficient, Func<T, Point, J> getVal, IComparer<J> comparer, out (T, J) result)
	{
		result = default;
		if (spIdx == null)
		{
			return false;
		}
		IEnumerable<T> elements = spIdx.GetElements(pt);
		if (!elements.Any())
		{
			return false;
		}
		bool condition = true;
		J getVal2 = default;
		T element = default;
		foreach (T element2 in elements)
		{
			if (sufficient(element2, pt, sufficientVal))
			{
				J getVal3 = getVal(element2, pt);
				if (condition || comparer.Compare(getVal3, getVal2) < 0)
				{
					condition = false;
					getVal2 = getVal3;
					element = element2;
				}
			}
		}
		if (condition)
		{
			return false;
		}
		result = (element, getVal2);
		return true;
	}

	public static SortedList<double, T> HitTestByDistance<T>(Point pt, SpatialIndex<T> spIdx, double hitDistance, Func<T, Point, double> distanceTo)
	{
		if (spIdx == null)
		{
			return new SortedList<double, T>();
		}
		IEnumerable<T> elements = spIdx.GetElements(pt, hitDistance);
		if (!elements.Any())
		{
			return new SortedList<double, T>();
		}
		SortedList<double, T> sortedList = new SortedList<double, T>(Comparer<double>.Default);
		foreach (T element in elements)
		{
			double distanceTo2 = distanceTo(element, pt);
			if (distanceTo2 < hitDistance && !sortedList.ContainsKey(distanceTo2))
			{
				sortedList.Add(distanceTo2, element);
			}
		}
		return sortedList;
	}

	public static bool TryHitTestByBestDistance<T>(Point pt, SpatialIndex<T> spIdx, double hitDistance, Func<T, Point, double> distanceTo, out (T, double) result)
	{
		return TryHitTestByBest(pt, spIdx, hitDistance, (T candidate, Point point, double computed) => distanceTo(candidate, point) < computed, distanceTo, Comparer<double>.Default, out result);
	}

	public static bool TryHitTestClosestFeature<T>(Point pt, double hitDistance, SpatialIndex<T> spIdx, Func<Point, T, double> distanceTo, out T nearestFeature)
	{
		if (spIdx == null)
		{
			nearestFeature = default;
			return false;
		}
		List<T> elements = spIdx.GetElements(pt, hitDistance, dedupe: false);
		T nearestFeature2 = default;
		double maxValue = double.MaxValue;
		for (int i = 0; i < elements.Count; i++)
		{
			T element = elements[i];
			double distanceTo2 = distanceTo(pt, element);
			if (distanceTo2 < hitDistance && distanceTo2 < maxValue)
			{
				nearestFeature2 = element;
				maxValue = distanceTo2;
			}
		}
		nearestFeature = default;
		if (maxValue == double.MaxValue)
		{
			return false;
		}
		nearestFeature = nearestFeature2;
		return true;
	}

	public static bool TryHitTestClosestFeature(this IPolylineCollection lyr, Point pt, double hitDistance, out int plIdx)
	{
		if (!TryHitTestClosestFeature(pt, hitDistance, lyr.GetSpatialIndex(), (Point args, int idx) => DistanceToFeature(lyr, args, idx), out var nearestFeature))
		{
			plIdx = -1;
			return false;
		}
		plIdx = nearestFeature;
		return true;
	}

	private static double DistanceToFeature(IReadOnlyVectorCollection<Polyline> lyr, Point pt, int idx)
	{
		return lyr[idx].DistanceToPoint(pt);
	}

	public static bool TryHitTestByBestArea(this IReadOnlyList<Polygon> polygons, Point pt, SpatialIndex<int> spIdx, double hitDistance, out (int, double) result)
	{
		return TryHitTestByBest(pt, spIdx, hitDistance, (int idx, Point point, double computed) =>
		{
			Polygon polygon = polygons[idx];
			return polygon.Contains(point) || polygon.DistanceToPoint(point) < computed;
		}, (int idx, Point point) => Math.Abs(polygons[idx].Area), Comparer<double>.Default, out result);
	}

	public static bool TryHitTestByBestExtentArea(this IReadOnlyList<ComplexPolygon> polygons, Point pt, SpatialIndex<int> spIdx, double hitDistance, out (int, double) result)
	{
		return TryHitTestByBest(pt, spIdx, hitDistance, (int idx, Point pt2, double computed) =>
		{
			ComplexPolygon complexPolygon = polygons[idx];
			return complexPolygon.ContainsPoint_Slow(pt2) || complexPolygon.SignedPointToPolygonDistance(pt2) < computed;
		}, (int idx, Point point) => Math.Abs(polygons[idx].Extent.Area), Comparer<double>.Default, out result);
	}

	public static IEnumerable<int> HitTest(this IReadOnlyPolygonCollection polygons, Point pt, SpatialIndex<int> spIdx, double hitDistance = double.NaN, Func<int, Point, double> distanceTo = null, HitTestSortOrder sortOrder = HitTestSortOrder.None)
	{
		return HitTest(pt, spIdx, polygons.HitTest, hitDistance, distanceTo, sortOrder);
	}

	public static bool HitTest(this IReadOnlyPolygonCollection gons, int candidateIdx, Point point, double hitDistance)
	{
		return HitTest(gons[candidateIdx], point, hitDistance);
	}

	public static bool HitTest(Polygon pgon, Point point, double hitDistance)
	{
		return pgon.Contains(point);
	}

	public static bool HitTest(this IReadOnlyPolygonCollection gons, int candidateIdx, Extent ext, double hitDistance)
	{
		return HitTest(gons[candidateIdx], ext, hitDistance);
	}

	public static bool HitTest(Polygon pgon, Extent ext, double hitDistance)
	{
		return Polygon.FromExtent(ext).ContainsOrIntersects(pgon);
	}

	public static bool HitTest(this IReadOnlyPolygonCollection gons, int candidateIdx, Polyline line, double hitDistance)
	{
		return HitTest(gons[candidateIdx], line, hitDistance);
	}

	public static bool HitTest(Polygon pgon, Polyline line, double hitDistance)
	{
		return pgon.ContainsOrIntersects(line);
	}
}
