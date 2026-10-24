using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.VisualBasic.CompilerServices;
using Utility.Extensions;

namespace RasMapperLib;

[StandardModule]
public sealed class GeoProcessing
{
	public static Polygon MergeTin(Tin tin, double filterTolerance = double.NaN, double precisionShift = 1000.0)
	{
		checked
		{
			IntPoint[] intPoints = new IntPoint[tin.Count + 1];
			int count = tin.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM pointM = tin.PointM(i);
				intPoints[i].X = (long)Math.Round(pointM.X * precisionShift);
				intPoints[i].Y = (long)Math.Round(pointM.Y * precisionShift);
			}
			List<IntPoint> intPointsList = new List<IntPoint>();
			Clipper clipper = new Clipper();
			int count2 = tin.TriangleCount() - 1;
			int p = default;
			int p2 = default;
			int p3 = default;
			for (int j = 0; j <= count2; j++)
			{
				tin.TrianglePoints(j, ref p, ref p2, ref p3);
				intPointsList.Clear();
				intPointsList.Add(intPoints[p]);
				intPointsList.Add(intPoints[p2]);
				intPointsList.Add(intPoints[p3]);
				intPointsList.Add(intPoints[p]);
				if (j == 0)
				{
					clipper.AddPath(intPointsList, PolyType.ptSubject, x: true);
				}
				else
				{
					clipper.AddPath(intPointsList, PolyType.ptClip, x: true);
				}
			}
			return ConvertClipperPointsToPolygon(MergeClipperPoints(clipper), filterTolerance, precisionShift);
		}
	}

	public static IntPoint MakeIntPoint(PointM pt, double precisionShift = 1000.0)
	{
		checked
		{
			IntPoint result = default;
			result.X = (long)Math.Round(pt.X * precisionShift);
			result.Y = (long)Math.Round(pt.Y * precisionShift);
			return result;
		}
	}

	public static void PointMsToIntPoints(PointMs pts, List<IntPoint> intPoints, double precisionShift = 1000.0)
	{
		checked
		{
			int count = pts.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				intPoints.Add(MakeIntPoint(pts[i], precisionShift));
			}
		}
	}

	public static Polygon ContourTINM(Tin tin, float minM, float maxM, double filterTolerance = double.NaN, double precisionShift = 1000.0)
	{
		return ContourTIN(tin, minM, maxM, [SpecialName] (PointM ptm) => ptm.M, filterTolerance, precisionShift);
	}

	public static Polygon ContourTINZ(Tin tin, float minZ, float maxZ, double filterTolerance = double.NaN, double precisionShift = 1000.0)
	{
		return ContourTIN(tin, minZ, maxZ, [SpecialName] (PointM ptm) => ptm.Z, filterTolerance, precisionShift);
	}

	public static Polygon ContourTIN(Tin tin, float minV, float maxV, Func<PointM, double> V, double filterTolerance = double.NaN, double precisionShift = 1000.0)
	{
		Clipper clipper = new Clipper();
		PointMs pointMs = new PointMs();
		List<IntPoint> intPoints = new List<IntPoint>();
		bool condition = false;
		checked
		{
			int count = tin.TriangleCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				tin.TriangleContour(i, minV, maxV, V, pointMs);
				if (pointMs.Count != 0)
				{
					intPoints.Clear();
					PointMsToIntPoints(pointMs, intPoints, precisionShift);
					if (!condition)
					{
						condition = true;
						clipper.AddPath(intPoints, PolyType.ptSubject, x: true);
					}
					else
					{
						clipper.AddPath(intPoints, PolyType.ptClip, x: true);
					}
				}
			}
			return ConvertClipperPointsToPolygon(MergeClipperPoints(clipper), filterTolerance, precisionShift);
		}
	}

	public static Polygon MergePolygons(IList<Polygon> polygons, double precisionShift = 1000.0)
	{
		List<IntPoint> intPolygon = new List<IntPoint>();
		Clipper clipper = new Clipper();
		double x = polygons[0].FirstPointM().X;
		double y = polygons[0].FirstPointM().Y;
		x = 0.0;
		y = 0.0;
		checked
		{
			int count = polygons.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				ConvertSimpleFeatureToClipperPoints(polygons[i], intPolygon, precisionShift, x, y);
				if (i == 0)
				{
					clipper.AddPath(intPolygon, PolyType.ptSubject, x: true);
				}
				else
				{
					clipper.AddPath(intPolygon, PolyType.ptClip, x: true);
				}
			}
			return ConvertClipperPointsToPolygon2(MergeClipperPoints(clipper), double.NaN, precisionShift, x, y);
		}
	}

	public static Polygon ClipPolygon(Polygon preservePgon, Polygon trimPgon, double precisionShift = 1000.0)
	{
		List<IntPoint> intPolygon = new List<IntPoint>();
		Clipper clipper = new Clipper();
		double x = preservePgon.FirstPointM().X;
		double y = preservePgon.FirstPointM().Y;
		x = 0.0;
		y = 0.0;
		ConvertSimpleFeatureToClipperPoints(preservePgon, intPolygon, precisionShift, x, y);
		clipper.AddPath(intPolygon, PolyType.ptClip, x: true);
		ConvertSimpleFeatureToClipperPoints(trimPgon, intPolygon, precisionShift, x, y);
		clipper.AddPath(intPolygon, PolyType.ptSubject, x: true);
		List<List<IntPoint>> mergedClip = new List<List<IntPoint>>();
		Polygon result;
		try
		{
			clipper.Execute(ClipType.ctDifference, mergedClip, PolyFillType.pftNonZero, PolyFillType.pftNonZero);
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			result = null;
			ProjectData.ClearProjectError();
			goto IL_0099;
		}
		result = ConvertClipperPointsToPolygon(mergedClip, double.NaN, precisionShift);
		goto IL_0099;
		IL_0099:
		return result;
	}

	public static Polygon XORPolygon(Polygon preservePgon, Polygon trimPgon, double precisionShift = 1000.0)
	{
		List<IntPoint> intPolygon = new List<IntPoint>();
		Clipper clipper = new Clipper();
		double x = preservePgon.FirstPointM().X;
		double y = preservePgon.FirstPointM().Y;
		x = 0.0;
		y = 0.0;
		ConvertSimpleFeatureToClipperPoints(preservePgon, intPolygon, precisionShift, x, y);
		clipper.AddPath(intPolygon, PolyType.ptClip, x: true);
		ConvertSimpleFeatureToClipperPoints(trimPgon, intPolygon, precisionShift, x, y);
		clipper.AddPath(intPolygon, PolyType.ptSubject, x: true);
		List<List<IntPoint>> mergedClip = new List<List<IntPoint>>();
		Polygon result;
		try
		{
			clipper.Execute(ClipType.ctXor, mergedClip, PolyFillType.pftNonZero, PolyFillType.pftNonZero);
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			result = null;
			ProjectData.ClearProjectError();
			goto IL_0099;
		}
		result = ConvertClipperPointsToPolygon(mergedClip, double.NaN, precisionShift);
		goto IL_0099;
		IL_0099:
		return result;
	}

	public static Polygon ClipPolygon(List<Polygon> preservePgons, Polygon trimPgon, double precisionShift = 1000.0)
	{
		List<IntPoint> intPolygon = new List<IntPoint>();
		Clipper clipper = new Clipper();
		double x = trimPgon.FirstPointM().X;
		double y = trimPgon.FirstPointM().Y;
		x = 0.0;
		y = 0.0;
		Polygon result;
		checked
		{
			int count = preservePgons.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				ConvertSimpleFeatureToClipperPoints(preservePgons[i], intPolygon, precisionShift, x, y);
				clipper.AddPath(intPolygon, PolyType.ptClip, x: true);
			}
			ConvertSimpleFeatureToClipperPoints(trimPgon, intPolygon, precisionShift, x, y);
			clipper.AddPath(intPolygon, PolyType.ptSubject, x: true);
			List<List<IntPoint>> mergedClip = new List<List<IntPoint>>();
			try
			{
				clipper.Execute(ClipType.ctDifference, mergedClip, PolyFillType.pftNonZero, PolyFillType.pftNonZero);
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				result = null;
				ProjectData.ClearProjectError();
				goto IL_00bb;
			}
			result = ConvertClipperPointsToPolygon(mergedClip, double.NaN, precisionShift);
			goto IL_00bb;
		}
		IL_00bb:
		return result;
	}

	public static Polygon BufferLine(Polyline line, double bufDistance, double precisionShift = 1000.0, bool withEndCap = true)
	{
		ClipperOffset clipperOffset = new ClipperOffset();
		double x = line.FirstPointM().X;
		double y = line.FirstPointM().Y;
		x = 0.0;
		y = 0.0;
		List<IntPoint> intPoints = new List<IntPoint>();
		ConvertSimpleFeatureToClipperPoints(line, intPoints, precisionShift, x, y);
		if (withEndCap)
		{
			clipperOffset.AddPath(intPoints, PolyType.ptClip, x: false);
		}
		else
		{
			clipperOffset.AddPath(intPoints, PolyType.ptClip, x: true);
		}
		List<List<IntPoint>> intPointListList = new List<List<IntPoint>>();
		intPointListList.Add(intPoints);
		Polygon result;
		try
		{
			clipperOffset.Execute(intPointListList, bufDistance * precisionShift);
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			result = null;
			ProjectData.ClearProjectError();
			goto IL_009d;
		}
		result = ConvertClipperPointsToPolygon(intPointListList, double.NaN, precisionShift);
		goto IL_009d;
		IL_009d:
		return result;
	}

	public static void ConvertSimpleFeatureToClipperPoints(MultiPoint pts, List<IntPoint> intPolygon, double precisionShift, double offsetX, double offsetY)
	{
		intPolygon.Clear();
		if (pts.Count > intPolygon.Capacity)
		{
			intPolygon.Capacity = pts.Count;
		}
		checked
		{
			for (int i = pts.Count - 1; i >= 0; i += -1)
			{
				PointM pointM = pts.PointM(i);
				intPolygon.Add(new IntPoint((pointM.X - offsetX) * precisionShift, (pointM.Y - offsetY) * precisionShift));
			}
		}
	}

	public static Polygon ConvertClipperPointsToPolygon2(List<List<IntPoint>> mergedClip, double filterTolerance = double.NaN, double precisionShift = 1000.0, double offsetX = 0.0, double offsetY = 0.0)
	{
		if (mergedClip == null || mergedClip.Count == 0)
		{
			return null;
		}
		double scaledPrecisionShift = 1.0 / precisionShift;
		int capacity = 0;
		checked
		{
			foreach (List<IntPoint> mergedClipList in mergedClip)
			{
				if (mergedClipList != null)
				{
					capacity += mergedClipList.Count + 1;
				}
			}
			PointMs pointMs = new PointMs();
			pointMs.Capacity = capacity;
			PointMs pointMs2 = new PointMs();
			List<int> intList = new List<int>();
			foreach (List<IntPoint> mergedClip2 in mergedClip)
			{
				if (mergedClip2.Count != 0)
				{
					pointMs2.Clear();
					if (pointMs2.Capacity < mergedClip2.Count)
					{
						pointMs2.Capacity = mergedClip2.Count;
					}
					int count = mergedClip2.Count - 1;
					for (int i = 0; i <= count; i++)
					{
						IntPoint intPoint = mergedClip2[i];
						pointMs2.Add((double)intPoint.X * scaledPrecisionShift + offsetX, (double)intPoint.Y * scaledPrecisionShift + offsetY);
					}
					if (!double.IsNaN(filterTolerance))
					{
						pointMs2.FilterXY(filterTolerance);
					}
					pointMs2.Add(pointMs2.First());
					bool areaLengthTerm = true;
					double area = pointMs2.ComputeArea();
					if (area < 0.0)
					{
						double length = pointMs2.ComputeLength();
						areaLengthTerm = Math.Abs(area / length) > scaledPrecisionShift;
					}
					if (areaLengthTerm)
					{
						intList.Add(pointMs.Count);
						pointMs.AddRangeReverse(pointMs2);
					}
				}
			}
			if (pointMs.Count > 0)
			{
				return new Polygon(pointMs, intList.ToArray());
			}
			return null;
		}
	}

	public static Polygon MergePolygons(Polygon a, Polygon b, double precisionShift = 1000.0)
	{
		if ((a == null) & (b == null))
		{
			return null;
		}
		if (a == null)
		{
			return b;
		}
		if (b == null)
		{
			return a;
		}
		Clipper clipper = new Clipper();
		List<IntPoint> intPolygon = new List<IntPoint>();
		ConvertSimplePolygonToClipperPoints(a, intPolygon, precisionShift);
		clipper.AddPath(intPolygon, PolyType.ptSubject, x: true);
		ConvertSimplePolygonToClipperPoints(b, intPolygon, precisionShift);
		clipper.AddPath(intPolygon, PolyType.ptClip, x: true);
		List<List<IntPoint>> intPolygon2 = MergeClipperPoints(clipper);
		Clipper clipper2 = new Clipper();
		clipper2.AddPaths(intPolygon2, PolyType.ptSubject, x: true);
		return ConvertClipperPointsToPolygon(MergeClipperPoints(clipper2), double.NaN, precisionShift);
	}

	public static Polygon BufferPolygon(Polygon a, double dist, double precisionShift = 1000.0)
	{
		if (a == null)
		{
			return null;
		}
		ClipperOffset clipperOffset = new ClipperOffset();
		List<IntPoint> intPolygon = new List<IntPoint>();
		ConvertSimplePolygonToClipperPoints(a, intPolygon, precisionShift);
		clipperOffset.AddPath(intPolygon, PolyType.ptClip, x: true);
		List<List<IntPoint>> intPointListList = new List<List<IntPoint>>();
		dist *= precisionShift;
		clipperOffset.Execute(intPointListList, dist);
		return ConvertClipperPointsToPolygon(intPointListList, double.NaN, precisionShift);
	}

	public static Polygon IntersectPolygon(Polygon preservePgon, Polygon clippedPgon, double precisionShift = 1000.0)
	{
		Clipper clipper = new Clipper();
		List<IntPoint> intPolygon = new List<IntPoint>();
		ConvertSimplePolygonToClipperPoints(preservePgon, intPolygon, precisionShift);
		clipper.AddPath(intPolygon, PolyType.ptSubject, x: true);
		ConvertSimplePolygonToClipperPoints(clippedPgon, intPolygon, precisionShift);
		clipper.AddPath(intPolygon, PolyType.ptClip, x: true);
		List<List<IntPoint>> mergedClip = new List<List<IntPoint>>();
		Polygon result;
		try
		{
			clipper.Execute(ClipType.ctIntersection, mergedClip, PolyFillType.pftNonZero, PolyFillType.pftNonZero);
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			result = null;
			ProjectData.ClearProjectError();
			goto IL_0062;
		}
		result = ConvertClipperPointsToPolygon(mergedClip, double.NaN, precisionShift);
		goto IL_0062;
		IL_0062:
		return result;
	}

	public static List<List<IntPoint>> MergeClipperPoints(Clipper clipper)
	{
		List<List<IntPoint>> intPointListList = new List<List<IntPoint>>();
		List<List<IntPoint>> result;
		try
		{
			clipper.Execute(ClipType.ctUnion, intPointListList, PolyFillType.pftNonZero, PolyFillType.pftNonZero);
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			result = null;
			ProjectData.ClearProjectError();
			goto IL_0024;
		}
		result = intPointListList;
		goto IL_0024;
		IL_0024:
		return result;
	}

	private static List<List<IntPoint>> IntersectClipperPoints(Clipper clipper)
	{
		List<List<IntPoint>> intPointListList = new List<List<IntPoint>>();
		List<List<IntPoint>> result;
		try
		{
			clipper.Execute(ClipType.ctIntersection, intPointListList, PolyFillType.pftNonZero, PolyFillType.pftNonZero);
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			result = null;
			ProjectData.ClearProjectError();
			goto IL_0024;
		}
		result = intPointListList;
		goto IL_0024;
		IL_0024:
		return result;
	}

	public static void ConvertSimplePolygonToClipperPoints(Polygon polygon, List<IntPoint> intPolygon, double precisionShift = 1000.0)
	{
		intPolygon.Clear();
		if (polygon.Count > intPolygon.Capacity)
		{
			intPolygon.Capacity = polygon.Count;
		}
		checked
		{
			for (int i = polygon.Count - 1; i >= 0; i += -1)
			{
				PointM pointM = polygon.PointM(i);
				intPolygon.Add(new IntPoint(pointM.X * precisionShift, pointM.Y * precisionShift));
			}
		}
	}

	public static void ConvertDoublePointsToLongPoints(PointMs points, List<IntPoint> intPolygon, double precisionShift = 1000.0)
	{
		checked
		{
			int count = points.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM pointM = points[i];
				IntPoint intPoint = new IntPoint((long)Math.Round(pointM.X * precisionShift), (long)Math.Round(pointM.Y * precisionShift));
				intPolygon.Add(intPoint);
			}
			intPolygon.Add(intPolygon.First());
			points.Clear();
		}
	}

	public static Polygon ConvertClipperPointsToPolygon(List<List<IntPoint>> mergedClip, double filterTolerance = double.NaN, double precisionShift = 1000.0)
	{
		if (mergedClip == null || mergedClip.Count == 0)
		{
			return null;
		}
		double scaledPrecisionShift = 1.0 / precisionShift;
		int capacity = 0;
		checked
		{
			foreach (List<IntPoint> mergedClipList in mergedClip)
			{
				if (mergedClipList != null)
				{
					capacity += mergedClipList.Count + 1;
				}
			}
			PointMs pointMs = new PointMs();
			pointMs.Capacity = capacity;
			PointMs pointMs2 = new PointMs();
			List<int> intList = new List<int>();
			foreach (List<IntPoint> mergedClip2 in mergedClip)
			{
				if (mergedClip2.Count != 0)
				{
					pointMs2.Clear();
					if (pointMs2.Capacity < mergedClip2.Count)
					{
						pointMs2.Capacity = mergedClip2.Count;
					}
					int count = mergedClip2.Count - 1;
					for (int i = 0; i <= count; i++)
					{
						IntPoint intPoint = mergedClip2[i];
						pointMs2.Add((double)intPoint.X * scaledPrecisionShift, (double)intPoint.Y * scaledPrecisionShift);
					}
					if (!double.IsNaN(filterTolerance))
					{
						pointMs2.FilterXY(filterTolerance);
					}
					pointMs2.Add(pointMs2.First());
					bool areaLengthTerm = true;
					double area = pointMs2.ComputeArea();
					if (area < 0.0)
					{
						double length = pointMs2.ComputeLength();
						areaLengthTerm = Math.Abs(area / length) > scaledPrecisionShift;
					}
					if (areaLengthTerm)
					{
						intList.Add(pointMs.Count);
						pointMs.AddRangeReverse(pointMs2);
					}
				}
			}
			if (pointMs.Count > 0)
			{
				return new Polygon(pointMs, intList.ToArray());
			}
			return null;
		}
	}

	public static Polygon ConvertClipperPointsToPolygon_MEMORYREUSE(List<List<IntPoint>> mergedClip, double filterTolerance = double.NaN, double precisionShift = 1000.0)
	{
		if (mergedClip.IsNullOrEmpty())
		{
			return null;
		}
		double scaledPrecisionShift = 1.0 / precisionShift;
		int countOffsetSum = 0;
		checked
		{
			foreach (List<IntPoint> mergedClipList in mergedClip)
			{
				if (mergedClipList != null)
				{
					countOffsetSum += mergedClipList.Count + 1;
				}
			}
			List<PointM> pointMsList = new List<PointM>(countOffsetSum);
			List<int> intList = new List<int>();
			PointMs pointMs = new PointMs();
			foreach (List<IntPoint> mergedClip2 in mergedClip)
			{
				if (mergedClip2.Count != 0)
				{
					pointMs.Clear();
					if (pointMs.Capacity < mergedClip2.Count)
					{
						pointMs.Capacity = mergedClip2.Count;
					}
					int count = mergedClip2.Count - 1;
					for (int i = 0; i <= count; i++)
					{
						IntPoint intPoint = mergedClip2[i];
						pointMs.Add((double)intPoint.X * scaledPrecisionShift, (double)intPoint.Y * scaledPrecisionShift);
					}
					if (!double.IsNaN(filterTolerance))
					{
						pointMs.FilterXY(filterTolerance);
					}
					pointMs.Add(pointMs.First());
					bool areaLengthTerm = true;
					double area = pointMs.ComputeArea();
					if (area < 0.0)
					{
						double length = pointMs.ComputeLength();
						areaLengthTerm = Math.Abs(area / length) > scaledPrecisionShift;
					}
					if (areaLengthTerm)
					{
						intList.Add(pointMsList.Count);
						ListExtensionMethods.AddRangeReverse(pointMsList, pointMs);
					}
				}
			}
			if (pointMsList.Count > 0)
			{
				return Polygon.FromBuffersMutable(pointMsList, intList);
			}
			return null;
		}
	}
}
