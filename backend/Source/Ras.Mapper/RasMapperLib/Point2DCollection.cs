using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace RasMapperLib;

[ComVisible(false)]
public class Point2DCollection : List<Point2D>, IComparer<Point2D>
{
	public string XName;

	public string XUnits;

	public string YName;

	public string YUnits;

	public Point2DCollection()
	{
	}

	public Point2DCollection(int capacity)
		: base(capacity)
	{
	}

	public Point2DCollection(IList<Point2D> pts)
	{
		if (pts != null && pts.Count != 0)
		{
			Capacity = pts.Count;
			AddRange(pts);
		}
	}

	public Point2DCollection(float[,] values, int xcol, int ycol)
	{
		AddRange(values, xcol, ycol);
	}

	public Point2DCollection(string XName, string XUnits, string YName, string YUnits, string spaceAndCommaDelimetedData = "")
	{
		this.XName = XName;
		this.XUnits = XUnits;
		this.YName = YName;
		this.YUnits = YUnits;
		AddRange(spaceAndCommaDelimetedData);
	}

	public void AssignNamesUnits(string XName, string XUnits, string YName, string YUnits)
	{
		this.XName = XName;
		this.XUnits = XUnits;
		this.YName = YName;
		this.YUnits = YUnits;
	}

	public List<SegmentM> GetSegmentList()
	{
		List<SegmentM> segmentMs = new List<SegmentM>();
		PointM pA = default;
		PointM pB = default;
		checked
		{
			int count = Count - 2;
			for (int i = 0; i <= count; i++)
			{
				pA.X = base[i].X;
				pA.Y = base[i].Y;
				pB.X = base[i + 1].X;
				pB.Y = base[i + 1].Y;
				segmentMs.Add(new SegmentM(pA, pB));
			}
			pA.X = base[Count - 2].X;
			pA.Y = base[Count - 2].Y;
			pB.X = base[Count - 1].X;
			pB.Y = base[Count - 1].Y;
			segmentMs.Add(new SegmentM(pA, pB));
			return segmentMs;
		}
	}

	public void Add(float x, float y)
	{
		Add(new Point2D(x, y));
	}

	public void Add(double x, double y)
	{
		Add(new Point2D((float)x, (float)y));
	}

	public void AddNew(float x, float y)
	{
		if (Count == 0)
		{
			Add(new Point2D(x, y));
		}
		else if (!this.Last().Equal(x, y))
		{
			Add(new Point2D(x, y));
		}
	}

	public void AddNew(Point2D pt)
	{
		if (Count == 0)
		{
			Add(pt);
		}
		else if (!this.Last().Equal(pt))
		{
			Add(pt);
		}
	}

	public void AddNewSegment(Point2D ptA, Point2D ptB)
	{
		AddNew(ptA);
		AddNew(ptB);
	}

	public void AddRange(string spaceAndCommaDelimetedData)
	{
		if (spaceAndCommaDelimetedData.Length != 0)
		{
			string[] parts = spaceAndCommaDelimetedData.Trim().Split(" ");
			foreach (string commaDelimitedString in parts)
			{
				Point2D commaDelimitedString2 = new Point2D(commaDelimitedString);
				Add(commaDelimitedString2);
			}
		}
	}

	public void AddRange(float[] x, float[] y, int offset, int count)
	{
		int offset2 = offset;
		checked
		{
			int count2 = count - 1;
			for (int i = 0; i <= count2; i++)
			{
				Add(new Point2D(x[i], y[i]));
				offset2++;
			}
		}
	}

	public void AddRange(float[,] values, int xcol, int ycol)
	{
		checked
		{
			if (values != null && values.Rank == 2)
			{
				Capacity = values.GetUpperBound(0) + 1;
				int upperBound = values.GetUpperBound(0);
				for (int i = 0; i <= upperBound; i++)
				{
					Add(values[i, xcol], values[i, ycol]);
				}
			}
		}
	}

	public void RemoveRange(int indexToStartRemoving)
	{
		RemoveRange(indexToStartRemoving, checked(Count - indexToStartRemoving));
	}

	public Point2DCollection CloneP2DCollection()
	{
		Point2DCollection point2DCollection = new Point2DCollection(XName, XUnits, YName, YUnits);
		point2DCollection.Capacity = Count;
		using Enumerator itemEnumerator = GetEnumerator();
		while (itemEnumerator.MoveNext())
		{
			Point2D current = itemEnumerator.Current;
			point2DCollection.Add(current);
		}
		return point2DCollection;
	}

	public void AssignX(int index, float x)
	{
		Point2D point2D = base[index];
		point2D.X = x;
		base[index] = point2D;
	}

	public void AssignY(int index, float y)
	{
		Point2D point2D = base[index];
		point2D.Y = y;
		base[index] = point2D;
	}

	public void AssignXY(int index, float x, float y)
	{
		Point2D point2D = base[index];
		point2D.X = x;
		point2D.Y = y;
		base[index] = point2D;
	}

	public void AddToX(int index, float x)
	{
		Point2D point2D = base[index];
		point2D.X += x;
		base[index] = point2D;
	}

	public void AddToY(int index, float y)
	{
		Point2D point2D = base[index];
		point2D.Y += y;
		base[index] = point2D;
	}

	public void Translate(float dX, float dY)
	{
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Point2D point2D = base[i];
				point2D.X += dX;
				point2D.Y += dY;
				base[i] = point2D;
			}
		}
	}

	public void RatioX(float ratio)
	{
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Point2D point2D = base[i];
				point2D.X *= ratio;
				base[i] = point2D;
			}
		}
	}

	public void RatioY(float ratio)
	{
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Point2D point2D = base[i];
				point2D.Y *= ratio;
				base[i] = point2D;
			}
		}
	}

	public void GetXYVals(ref float[] xVals, ref float[] yVals)
	{
		checked
		{
			xVals = new float[Count - 1 + 1];
			yVals = new float[Count - 1 + 1];
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				xVals[i] = base[i].X;
				yVals[i] = base[i].Y;
			}
		}
	}

	public void CreateEvenIntervalY(float minY, float maxY, int count, float tol)
	{
		float minY2 = minY;
		checked
		{
			float maxYAboveMinYOverCountOffset = (maxY - minY) / (float)(count - 1);
			Clear();
			if ((double)maxYAboveMinYOverCountOffset == 0.0)
			{
				Add(0.0, minY2);
				return;
			}
			if (maxYAboveMinYOverCountOffset < tol)
			{
				Add(0.0, minY2);
				do
				{
					minY2 += tol;
					Add(0.0, minY2);
				}
				while (!(minY2 > maxY));
				return;
			}
			int count2 = count - 1;
			for (int i = 0; i <= count2; i++)
			{
				Add(0.0, minY2);
				minY2 += maxYAboveMinYOverCountOffset;
			}
		}
	}

	public string ExportToCSV()
	{
		return ExportToCSV("", "");
	}

	public string ExportToCSV(string xFormat, string yFormat)
	{
		StringBuilder stringBuilder = new StringBuilder();
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				stringBuilder.Append(base[i].ConvertToCSV(xFormat, yFormat));
				if (i < Count - 1)
				{
					stringBuilder.Append(" ");
				}
			}
			return stringBuilder.ToString();
		}
	}

	public void ExportToArray(int offset, float[,] xy)
	{
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Point2D point2D = base[i];
				xy[0, offset] = point2D.X;
				xy[1, offset] = point2D.Y;
			}
		}
	}

	public void ExportToArrays(ref float[] x, ref float[] y)
	{
		checked
		{
			if (x == null || x.Count() != Count)
			{
				x = new float[Count - 1 + 1];
			}
			if (y == null || y.Count() != Count)
			{
				y = new float[Count - 1 + 1];
			}
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Point2D point2D = base[i];
				x[i] = point2D.X;
				y[i] = point2D.Y;
			}
		}
	}

	public void ExportToArrays(int offset, ref float[] x, ref float[] y)
	{
		checked
		{
			if (x == null || x.Count() < Count + offset)
			{
				x = new float[offset + Count - 1 + 1];
			}
			if (y == null || y.Count() < Count + offset)
			{
				y = new float[offset + Count - 1 + 1];
			}
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Point2D point2D = base[i];
				x[offset + i] = point2D.X;
				y[offset + i] = point2D.Y;
			}
		}
	}

	public float MaxX()
	{
		float minValue = float.MinValue;
		using (Enumerator itemEnumerator = GetEnumerator())
		{
			while (itemEnumerator.MoveNext())
			{
				Point2D current = itemEnumerator.Current;
				if (!current.IsUndefined() && current.X > minValue)
				{
					minValue = current.X;
				}
			}
		}
		if (minValue < float.MaxValue)
		{
			return minValue;
		}
		return float.NaN;
	}

	public float MinX()
	{
		float maxValue = float.MaxValue;
		using (Enumerator itemEnumerator = GetEnumerator())
		{
			while (itemEnumerator.MoveNext())
			{
				Point2D current = itemEnumerator.Current;
				if (!current.IsUndefined() && current.X < maxValue)
				{
					maxValue = current.X;
				}
			}
		}
		if (maxValue < float.MaxValue)
		{
			return maxValue;
		}
		return float.NaN;
	}

	public float MaxY()
	{
		float minValue = float.MinValue;
		using (Enumerator itemEnumerator = GetEnumerator())
		{
			while (itemEnumerator.MoveNext())
			{
				Point2D current = itemEnumerator.Current;
				if (!current.IsUndefined() && current.Y > minValue)
				{
					minValue = current.Y;
				}
			}
		}
		if (minValue < float.MaxValue)
		{
			return minValue;
		}
		return float.NaN;
	}

	public float MinY()
	{
		float maxValue = float.MaxValue;
		using (Enumerator itemEnumerator = GetEnumerator())
		{
			while (itemEnumerator.MoveNext())
			{
				Point2D current = itemEnumerator.Current;
				if (!current.IsUndefined() && current.Y < maxValue)
				{
					maxValue = current.Y;
				}
			}
		}
		if (maxValue < float.MaxValue)
		{
			return maxValue;
		}
		return float.NaN;
	}

	public float Slope(int index)
	{
		return checked((base[index].Y - base[index - 1].Y) / (base[index].X - base[index - 1].X));
	}

	public float InverseSlope(int index)
	{
		return checked((base[index].X - base[index - 1].X) / (base[index].Y - base[index - 1].Y));
	}

	public void SortX()
	{
		Sort(Point2D.CompareX);
	}

	public void SortY()
	{
		Sort(Point2D.CompareY);
	}

	public float Query(float xVal)
	{
		bool beforeStart = false;
		bool exceededFinal = false;
		return Query(xVal, ref beforeStart, ref exceededFinal);
	}

	public float Query(float xVal, ref bool beforeStart, ref bool exceededFinal)
	{
		exceededFinal = false;
		beforeStart = false;
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Point2D secondPt = base[i];
				if (xVal < secondPt.X)
				{
					if (i == 0)
					{
						beforeStart = true;
						return secondPt.Y;
					}
					Point2D firstPt = base[i - 1];
					return YBetweenPoints(xVal, firstPt, secondPt);
				}
			}
			exceededFinal = true;
			return this.Last().Y;
		}
	}

	public static float YBetweenPoints(float station, Point2D firstPt, Point2D secondPt)
	{
		if (station < firstPt.X || station > secondPt.X)
		{
			throw new ArgumentException();
		}
		float secondPtFirstPtTerm = (secondPt.Y - firstPt.Y) / (secondPt.X - firstPt.X);
		float stationFirstPtTerm = station - firstPt.X;
		return firstPt.Y + secondPtFirstPtTerm * stationFirstPtTerm;
	}

	public float QueryWithNodataAwareness(float xVal, ref bool beforeStart, ref bool exceededFinal)
	{
		exceededFinal = false;
		beforeStart = false;
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Point2D point2D = base[i];
				if (xVal < point2D.X)
				{
					if (i == 0)
					{
						beforeStart = true;
						return point2D.Y;
					}
					Point2D point2D2 = base[i - 1];
					if (point2D2.Y == -9999f || point2D.Y == -9999f)
					{
						return -9999f;
					}
					float point2DPoint2D2Term = (point2D.Y - point2D2.Y) / (point2D.X - point2D2.X);
					float xValPoint2D2Term = xVal - point2D2.X;
					return point2D2.Y + point2DPoint2D2Term * xValPoint2D2Term;
				}
			}
			exceededFinal = true;
			return this.Last().Y;
		}
	}

	public float QueryWithLinearExtrapolation(float xInput, float extrapolationFactor)
	{
		bool beforeStart = default;
		bool exceededFinal = default;
		float query = Query(xInput, ref beforeStart, ref exceededFinal);
		if (beforeStart)
		{
			throw new NotImplementedException();
		}
		if (exceededFinal)
		{
			float x = this.Last().X;
			float xInputTerm = xInput - x;
			query += xInputTerm * extrapolationFactor;
		}
		return query;
	}

	public void InvertXY()
	{
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Point2D point2D = base[i];
				base[i] = new Point2D(point2D.Y, point2D.X);
			}
		}
	}

	public Point2DCollection Filter(float filterTol, List<int> indexBuffer = null)
	{
		List<int> indexBufferList = ((indexBuffer != null) ? indexBuffer : new List<int>());
		FilterKeepList(filterTol, indexBufferList);
		Point2DCollection point2DCollection = new Point2DCollection();
		point2DCollection.Capacity = indexBufferList.Count;
		checked
		{
			int count = indexBufferList.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				point2DCollection.Add(base[indexBufferList[i]]);
			}
			return point2DCollection;
		}
	}

	public Point2DCollection Filter_UPSCALEDOUBLE(float filterTol, List<int> indexBuffer = null)
	{
		List<int> indexBufferList = ((indexBuffer != null) ? indexBuffer : new List<int>());
		FilterKeepList_UPSCALEDOUBLE(filterTol, indexBufferList);
		Point2DCollection point2DCollection = new Point2DCollection();
		point2DCollection.Capacity = indexBufferList.Count;
		checked
		{
			int count = indexBufferList.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				point2DCollection.Add(base[indexBufferList[i]]);
			}
			return point2DCollection;
		}
	}

	public void FilterKeepList(float filterTol, List<int> indexes)
	{
		indexes.Clear();
		int ordinal = 0;
		int countOffset = checked(Count - 1);
		if (Count > 0)
		{
			indexes.Add(ordinal);
		}
		if (Count > 1)
		{
			indexes.Add(countOffset);
		}
		FilterDouglasPeucker(ordinal, countOffset, filterTol, indexes);
		indexes.Sort();
	}

	public void FilterKeepList_UPSCALEDOUBLE(float filterTol, List<int> indexes)
	{
		indexes.Clear();
		int ordinal = 0;
		int countOffset = checked(Count - 1);
		if (Count > 0)
		{
			indexes.Add(ordinal);
		}
		if (Count > 1)
		{
			indexes.Add(countOffset);
		}
		FilterDouglasPeucker_UPSCALEDOUBLE(ordinal, countOffset, filterTol, indexes);
		indexes.Sort();
	}

	public List<int> FilterKeepList(float filterTol)
	{
		List<int> intList = new List<int>();
		int ordinal = 0;
		int countOffset = checked(Count - 1);
		if (Count > 0)
		{
			intList.Add(ordinal);
		}
		if (Count > 1)
		{
			intList.Add(countOffset);
		}
		FilterDouglasPeucker(ordinal, countOffset, filterTol, intList);
		intList.Sort();
		return intList;
	}

	private void FilterDouglasPeucker(int first, int last, float Tolerance, List<int> keepIndex)
	{
		checked
		{
			if (last - first <= 1)
			{
				return;
			}
			float computed = 0f;
			int ordinal = 0;
			float distanceTo = base[first].DistanceTo(base[last]);
			int ordinal2 = first + 1;
			int count = last - 1;
			for (int i = ordinal2; i <= count; i++)
			{
				float distanceToTerm = Math.Abs(base[first].X * base[last].Y + base[last].X * base[i].Y + base[i].X * base[first].Y - base[last].X * base[first].Y - base[i].X * base[last].Y - base[first].X * base[i].Y) / distanceTo;
				if (distanceToTerm > computed)
				{
					computed = distanceToTerm;
					ordinal = i;
				}
			}
			if (computed > Tolerance)
			{
				keepIndex.Add(ordinal);
				FilterDouglasPeucker(first, ordinal, Tolerance, keepIndex);
				FilterDouglasPeucker(ordinal, last, Tolerance, keepIndex);
			}
		}
	}

	private void FilterDouglasPeucker_UPSCALEDOUBLE(int first, int last, float Tolerance, List<int> keepIndex)
	{
		checked
		{
			if (last - first <= 1)
			{
				return;
			}
			float computed = 0f;
			int ordinal = 0;
			double computed2 = base[first].X;
			double computed3 = base[first].Y;
			double computed4 = base[last].X;
			double computed5 = base[last].Y;
			PointM pointM = new PointM(computed2, computed3);
			PointM ptM = new PointM(computed4, computed5);
			double distanceTo = pointM.DistanceTo(ptM);
			int ordinal2 = first + 1;
			int count = last - 1;
			for (int i = ordinal2; i <= count; i++)
			{
				double computed6 = base[i].X;
				double computed7 = base[i].Y;
				double distanceToTerm = Math.Abs(computed2 * computed5 + computed4 * computed7 + computed6 * computed3 - computed4 * computed3 - computed6 * computed5 - computed2 * computed7) / distanceTo;
				if (distanceToTerm > (double)computed)
				{
					computed = (float)distanceToTerm;
					ordinal = i;
				}
			}
			if (computed > Tolerance)
			{
				keepIndex.Add(ordinal);
				FilterDouglasPeucker(first, ordinal, Tolerance, keepIndex);
				FilterDouglasPeucker(ordinal, last, Tolerance, keepIndex);
			}
		}
	}

	public void DebugPrint()
	{
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
			}
		}
	}

	public double InterpolateY(double x)
	{
		if (x <= (double)this.First().X)
		{
			return this.First().Y;
		}
		if (x >= (double)this.Last().X)
		{
			return this.Last().Y;
		}
		Point2D point2D = new Point2D((float)x, 0f);
		int binarySearch = BinarySearch(point2D, this);
		if (binarySearch > 0)
		{
			return base[binarySearch].Y;
		}
		binarySearch = ~binarySearch;
		checked
		{
			double binarySearchTerm = ((double)base[binarySearch].X - x) / (double)(base[binarySearch].X - base[binarySearch - 1].X);
			return (double)base[binarySearch - 1].Y * binarySearchTerm + (double)base[binarySearch].Y * (1.0 - binarySearchTerm);
		}
	}

	public List<PointM> GetPointsOfIntersection(Point2D a, Point2D b)
	{
		List<PointM> pointMs = new List<PointM>();
		SegmentM segA = new SegmentM(a, b);
		checked
		{
			int count = Count - 2;
			for (int i = 0; i <= count; i++)
			{
				SegmentM segB = new SegmentM(base[i], base[i + 1]);
				PointM lineLineIntersection = LineM.LineLineIntersection(segA, segB);
				if (!float.IsInfinity((float)lineLineIntersection.X) && !float.IsNaN((float)lineLineIntersection.X) && !float.IsInfinity((float)lineLineIntersection.Y) && !float.IsNaN((float)lineLineIntersection.Y))
				{
					pointMs.Add(lineLineIntersection);
				}
			}
			return pointMs;
		}
	}

	public int Compare(Point2D a, Point2D b)
	{
		if (a.X < b.X)
		{
			return -1;
		}
		if (a.X == b.X)
		{
			return 0;
		}
		return 1;
	}

	int IComparer<Point2D>.Compare(Point2D a, Point2D b)
	{
		//ILSpy generated this explicit interface implementation from .override directive in Compare
		return this.Compare(a, b);
	}

	public float AreaUnderCurveNormalized()
	{
		MinY();
		throw new NotImplementedException();
	}

	public void ToImmediateWindow(string headerline = "")
	{
		string.IsNullOrEmpty(headerline);
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
			}
		}
	}

	public override string ToString()
	{
		return "(" + Count + " Points)";
	}
}
