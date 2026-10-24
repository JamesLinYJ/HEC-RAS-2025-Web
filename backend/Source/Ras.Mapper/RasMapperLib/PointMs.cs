using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Geospatial.GDALAssist;
using RasMapperLib.Utilities;

namespace RasMapperLib;

[ComVisible(false)]
public class PointMs : List<PointM>, IPoints, ISegments
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__44_002D0
	{
		public int sigFigs;

		public Dictionary<double, int> dic;

		public _Closure_0024__44_002D0(_Closure_0024__44_002D0 other)
		{
			if (other != null)
			{
				sigFigs = other.sigFigs;
				dic = other.dic;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(IList<PointM> pts)
		{
			if (pts.Count <= 1)
			{
				return;
			}
			checked
			{
				int countOffset = pts.Count - 1;
				for (int i = 0; i <= countOffset; i++)
				{
					PointM pointM = pts[i];
					double maxValue = double.MaxValue;
					int countOffset2 = pts.Count - 1;
					for (int j = 0; j <= countOffset2; j++)
					{
						if (i != j)
						{
							PointM ptM = pts[j];
							double distanceTo = pointM.DistanceTo(ptM);
							maxValue = System.Math.Min(maxValue, distanceTo);
						}
					}
					double key = RoundToSignificantDigits(maxValue, sigFigs);
					if (dic.ContainsKey(key))
					{
						dic[key] += 1;
					}
					else
					{
						dic[key] = 1;
					}
				}
			}
		}
	}

	public PointMs()
	{
	}

	public PointMs(IList<PointM> elements)
	{
		AddRange(elements);
	}

	public PointMs(IEnumerable<PointM> elements)
	{
		AddRange(elements);
	}

	public PointMs(IPoints elements)
	{
		AddRange(elements);
	}

	public PointMs(double[] x, double[] y)
		: base(x.Count())
	{
		checked
		{
			int count = x.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				Add(new PointM(x[i], y[i]));
			}
		}
	}

	public PointMs(SegmentM segment)
		: base(2)
	{
		Add(segment.A);
		Add(segment.B);
	}

	public PointMs(int startCount)
		: base(startCount)
	{
	}

	public void Add(double x, double y)
	{
		PointM pointM = default;
		pointM.Assign(x, y);
		Add(pointM);
	}

	public void Add(double x, double y, double z)
	{
		PointM pointM = default;
		pointM.Assign(x, y, z);
		Add(pointM);
	}

	public void Add(double x, double y, double z, double m)
	{
		PointM pointM = default;
		pointM.Assign(x, y, z, m);
		Add(pointM);
	}

	public void Add(SegmentM segment)
	{
		Add(segment.A);
		Add(segment.B);
	}

	public void Add(PointM point, double colinearTolerance)
	{
		if (Count <= 1)
		{
			Add(point);
			return;
		}
		checked
		{
			if (base[Count - 1].CloseTo(point, colinearTolerance))
			{
				base[Count - 1] = point;
				return;
			}
			SegmentM segmentM = new SegmentM(base[Count - 2], point);
			PointM pt = this.Last();
			SegmentM.RelativePosition relativePosition = SegmentM.RelativePosition.Undefined;
			if (segmentM.DistanceToPoint(pt, ref relativePosition) < colinearTolerance)
			{
				base[Count - 1] = point;
			}
			else
			{
				Add(point);
			}
		}
	}

	public void AddNew(PointM pointM)
	{
		if (Count == 0)
		{
			Add(pointM);
		}
		else if (!this.Last().Equal(pointM))
		{
			Add(pointM);
		}
	}

	public void AddRange(PointM[] pointM)
	{
		checked
		{
			if (Capacity < Count + pointM.Count())
			{
				Capacity = Count + pointM.Count();
			}
			foreach (PointM pointM2 in pointM)
			{
				Add(pointM2);
			}
		}
	}

	public void AddRange(PointMs points)
	{
		checked
		{
			if (Capacity < Count + points.Count)
			{
				Capacity = Count + points.Count;
			}
			foreach (PointM point in points)
			{
				Add(point);
			}
		}
	}

	public void AddRange(PointMs points, double nearTolerance)
	{
		checked
		{
			if (Capacity < Count + points.Count)
			{
				Capacity = Count + points.Count;
			}
			foreach (PointM point in points)
			{
				if (Count == 0)
				{
					Add(point);
				}
				else if (!this.Last().CloseTo(point, nearTolerance))
				{
					Add(point);
				}
			}
		}
	}

	public void AddRange(PointMs points, PointOnPolyline startPoint, PointOnPolyline endPoint)
	{
		Add(startPoint.PointM);
		checked
		{
			int segmentIndexOffset = startPoint.SegmentIndex + 1;
			int segmentIndex = endPoint.SegmentIndex;
			for (int i = segmentIndexOffset; i <= segmentIndex; i++)
			{
				Add(points[i]);
			}
			if (endPoint.Fraction > 0.0)
			{
				Add(endPoint.PointM);
			}
		}
	}

	public void AddRange(params PointMs[] pointMsParamArray)
	{
		foreach (PointMs points in pointMsParamArray)
		{
			AddRange(points);
		}
	}

	public void AddRange(int count, double[] x, double[] y)
	{
		checked
		{
			int count2 = count - 1;
			PointM pointM = default;
			for (int i = 0; i <= count2; i++)
			{
				pointM.Assign(x[i], y[i]);
				Add(pointM);
			}
		}
	}

	public void AddRange(int startIndex, int count, double[] x, double[] y)
	{
		checked
		{
			int count2 = startIndex + count - 1;
			PointM pointM = default;
			for (int i = startIndex; i <= count2; i++)
			{
				pointM.Assign(x[i], y[i]);
				Add(pointM);
			}
		}
	}

	public void AddRange(string CommaAndSpaceDelimtedData)
	{
		string[] parts = CommaAndSpaceDelimtedData.Trim().Split(' ');
		checked
		{
			if (Capacity < Count + parts.Count())
			{
				Capacity = Count + parts.Count();
			}
			int count = parts.Count() - 1;
			PointM point = default;
			for (int i = 0; i <= count; i++)
			{
				if (RasMapperLib.PointM.TryParse(parts[i], ref point))
				{
					Add(point);
				}
			}
		}
	}

	public void AddRange(int dimension, string posList)
	{
		string[] parts = posList.Split(' ');
		int ordinal = 0;
		PointM pointM = default;
		pointM.SetToNaN();
		checked
		{
			int count = parts.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				switch (ordinal)
				{
				case 0:
					double.TryParse(parts[i], out pointM.X);
					break;
				case 1:
					double.TryParse(parts[i], out pointM.Y);
					break;
				case 2:
					double.TryParse(parts[i], out pointM.Z);
					break;
				case 3:
					double.TryParse(parts[i], out pointM.M);
					break;
				}
				ordinal++;
				if (ordinal == dimension)
				{
					Add(pointM);
					ordinal = 0;
				}
			}
		}
	}

	public void AddRange(IPoints points)
	{
		checked
		{
			if (Capacity < Count + points.PointMCount())
			{
				Capacity = Count + points.PointMCount();
			}
			int count = points.PointMCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Add(points.PointM(i));
			}
		}
	}

	public void AddRange(IPoints points, bool excludeFirst, bool excludeLast)
	{
		int unity = 0;
		checked
		{
			int pointMCountPointsTerm = points.PointMCount() - 1;
			if (excludeFirst)
			{
				unity = 1;
			}
			if (excludeLast)
			{
				pointMCountPointsTerm--;
			}
			int ordinal = pointMCountPointsTerm - unity + 1;
			if (ordinal > 0)
			{
				if (Capacity < Count + points.PointMCount())
				{
					Capacity = Count + ordinal;
				}
				int ordinal2 = unity;
				int count = pointMCountPointsTerm;
				for (int i = ordinal2; i <= count; i++)
				{
					Add(points.PointM(i));
				}
			}
		}
	}

	public void AddRangeNew(IPoints points)
	{
		checked
		{
			int count = points.PointMCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				AddNew(points.PointM(i));
			}
		}
	}

	public void AddRangeReverse(PointMs ptMs)
	{
		checked
		{
			if (Capacity < Count + ptMs.Count)
			{
				Capacity = Count + ptMs.Count;
			}
			for (int i = ptMs.Count - 1; i >= 0; i += -1)
			{
				Add(ptMs[i]);
			}
		}
	}

	public void AddRangeReverse(List<PointM> pms)
	{
		checked
		{
			if (Capacity < Count + pms.Count)
			{
				Capacity = Count + pms.Count;
			}
			for (int i = pms.Count - 1; i >= 0; i += -1)
			{
				Add(pms[i]);
			}
		}
	}

	public void AddRangeReverse(PointMs points, PointOnPolyline startPoint, PointOnPolyline endPoint)
	{
		Add(endPoint.PointM);
		int segmentIndex = endPoint.SegmentIndex;
		checked
		{
			int segmentIndexOffset = startPoint.SegmentIndex + 1;
			for (int i = segmentIndex; i >= segmentIndexOffset; i += -1)
			{
				Add(points[i]);
			}
			Add(startPoint.PointM);
		}
	}

	public void AddRangeReverse(IPoints points)
	{
		checked
		{
			if (Capacity < Count + points.PointMCount())
			{
				Capacity = Count + points.PointMCount();
			}
			for (int i = points.PointMCount() - 1; i >= 0; i += -1)
			{
				Add(points.PointM(i));
			}
		}
	}

	public void RemoveRange(int indexToStartRemoving)
	{
		RemoveRange(indexToStartRemoving, checked(Count - indexToStartRemoving));
	}

	public void RemovePoints(bool[] remove)
	{
		int ordinal = 0;
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				if (remove[i])
				{
					ordinal++;
				}
				else if (i != ordinal)
				{
					base[i - ordinal] = base[i];
				}
			}
			while (ordinal != 0)
			{
				RemoveAt(Count - 1);
				ordinal--;
			}
		}
	}

	public void CopyTo(PointMs target)
	{
		target.Clear();
		if (target.Capacity < Count)
		{
			target.Capacity = Count;
		}
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				target.Add(base[i]);
			}
		}
	}

	public void CopyTo(IList<PointM> target)
	{
		target.Clear();
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				target.Add(base[i]);
			}
		}
	}

	public void CopyTo(ref double[] x, ref double[] y)
	{
		checked
		{
			if (Count > 0)
			{
				x = new double[Count - 1 + 1];
				y = new double[Count - 1 + 1];
				int count = Count - 1;
				for (int i = 0; i <= count; i++)
				{
					PointM pointM = base[i];
					x[i] = pointM.X;
					y[i] = pointM.Y;
				}
			}
			else
			{
				x = null;
				y = null;
			}
		}
	}

	private void AssignZ(int index, double z)
	{
		PointM pointM = base[index];
		pointM.Z = z;
		base[index] = pointM;
	}

	private void AssignM(int index, double m)
	{
		PointM pointM = base[index];
		pointM.M = m;
		base[index] = pointM;
	}

	private void AssignZM(int index, double z, double m)
	{
		PointM pointM = base[index];
		pointM.Z = z;
		pointM.M = m;
		base[index] = pointM;
	}

	public bool Equals(PointMs pts)
	{
		if (Count != pts.Count)
		{
			return false;
		}
		checked
		{
			int count = pts.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				if (!PointM(i).EqualXYZMWithNaN(pts.PointM(i)))
				{
					return false;
				}
			}
			return true;
		}
	}

	public Extent ComputeExtent()
	{
		int maxX = default;
		int minX = default;
		int maxY = default;
		int minY = default;
		int maxZ = default;
		int minZ = default;
		int maxM = default;
		int minM = default;
		MaxMinIndexes(ref maxX, ref minX, ref maxY, ref minY, ref maxZ, ref minZ, ref maxM, ref minM);
		if (maxZ >= 0 && maxM >= 0)
		{
			return new Extent(base[maxX].X, base[minX].X, base[maxY].Y, base[minY].Y, base[maxZ].Z, base[minZ].Z, base[maxM].M, base[minM].M);
		}
		if (maxZ >= 0)
		{
			return new Extent(base[maxX].X, base[minX].X, base[maxY].Y, base[minY].Y, base[maxZ].Z, base[minZ].Z);
		}
		return new Extent(base[maxX].X, base[minX].X, base[maxY].Y, base[minY].Y);
	}

	Extent IPoints.ComputeExtent()
	{
		//ILSpy generated this explicit interface implementation from .override directive in ComputeExtent
		return this.ComputeExtent();
	}

	public void MaxMinXY(ref double maxX, ref double minX, ref double maxY, ref double minY)
	{
		if (Count != 0)
		{
			int maxX2 = default;
			int minX2 = default;
			int maxY2 = default;
			int minY2 = default;
			int maxZ = default;
			int minZ = default;
			int maxM = default;
			int minM = default;
			MaxMinIndexes(ref maxX2, ref minX2, ref maxY2, ref minY2, ref maxZ, ref minZ, ref maxM, ref minM);
			maxX = base[maxX2].X;
			minX = base[minX2].X;
			maxY = base[maxY2].Y;
			minY = base[minY2].Y;
		}
	}

	public void MaxMinIndexes(ref int maxX, ref int minX, ref int maxY, ref int minY, ref int maxZ, ref int minZ, ref int maxM, ref int minM)
	{
		maxX = -1;
		minX = -1;
		maxY = -1;
		minY = -1;
		maxZ = -1;
		minZ = -1;
		maxM = -1;
		minM = -1;
		double maxValue = double.MinValue;
		double minValue = double.MaxValue;
		double maxValue2 = double.MinValue;
		double minValue2 = double.MaxValue;
		double maxValue3 = double.MinValue;
		double minValue3 = double.MaxValue;
		double maxValue4 = double.MinValue;
		double minValue4 = double.MaxValue;
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM pointM = base[i];
				MaxMinValue(i, pointM.X, ref maxX, ref maxValue, ref minX, ref minValue);
				MaxMinValue(i, pointM.Y, ref maxY, ref maxValue2, ref minY, ref minValue2);
				MaxMinValue(i, pointM.Z, ref maxZ, ref maxValue3, ref minZ, ref minValue3);
				MaxMinValue(i, pointM.M, ref maxM, ref maxValue4, ref minM, ref minValue4);
			}
		}
	}

	public static void MaxMinIndexes(IList<PointM> vals, ref int maxX, ref int minX, ref int maxY, ref int minY, ref int maxZ, ref int minZ, ref int maxM, ref int minM)
	{
		maxX = -1;
		minX = -1;
		maxY = -1;
		minY = -1;
		maxZ = -1;
		minZ = -1;
		maxM = -1;
		minM = -1;
		double maxValue = double.MinValue;
		double minValue = double.MaxValue;
		double maxValue2 = double.MinValue;
		double minValue2 = double.MaxValue;
		double maxValue3 = double.MinValue;
		double minValue3 = double.MaxValue;
		double maxValue4 = double.MinValue;
		double minValue4 = double.MaxValue;
		checked
		{
			int count = vals.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM pointM = vals[i];
				MaxMinValue(i, pointM.X, ref maxX, ref maxValue, ref minX, ref minValue);
				MaxMinValue(i, pointM.Y, ref maxY, ref maxValue2, ref minY, ref minValue2);
				MaxMinValue(i, pointM.Z, ref maxZ, ref maxValue3, ref minZ, ref minValue3);
				MaxMinValue(i, pointM.M, ref maxM, ref maxValue4, ref minM, ref minValue4);
			}
		}
	}

	private static void MaxMinValue(int i, double v, ref int maxIndex, ref double maxValue, ref int minIndex, ref double minValue)
	{
		if (double.IsNaN(v))
		{
			maxIndex = -1;
			minIndex = -1;
			maxValue = double.NaN;
			minValue = double.NaN;
		}
		else if (v != -9999.0 && !double.IsNaN(maxValue))
		{
			if ((maxIndex == -1) | (v > maxValue))
			{
				maxIndex = i;
				maxValue = v;
			}
			if ((minIndex == -1) | (v < minValue))
			{
				minIndex = i;
				minValue = v;
			}
		}
	}

	public double NearestNeighborRepresentativeAverageDistance()
	{
		checked
		{
			Func<IList<PointM>, double> func = [SpecialName] (IList<PointM> pts) =>
			{
				if (pts.Count <= 1)
				{
					return double.NaN;
				}
				double maxValueSum = 0.0;
				int ordinal = 0;
				int count = pts.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					PointM pointM = pts[i];
					double maxValue = double.MaxValue;
					int count2 = pts.Count - 1;
					for (int j = 0; j <= count2; j++)
					{
						if (i != j)
						{
							PointM ptM = pts[j];
							double distanceTo = pointM.DistanceTo(ptM);
							maxValue = System.Math.Min(maxValue, distanceTo);
						}
					}
					maxValueSum += maxValue;
					ordinal++;
				}
				return maxValueSum / (double)ordinal;
			};
			if (Count <= 50)
			{
				return func(this);
			}
			Extent extent = ComputeExtent();
			SpatialIndex<int> spatialIndex = new SpatialIndex<int>(extent, System.Math.Min((int)System.Math.Round((double)Count / 50.0), 4096));
			int count3 = Count - 1;
			for (int ordinal2 = 0; ordinal2 <= count3; ordinal2++)
			{
				spatialIndex.Add(PointM(ordinal2), ordinal2);
			}
			double funcSum = 0.0;
			int ordinal3 = 0;
			int count4 = spatialIndex.Bins.Count() - 1;
			for (int intervalIndex = 0; intervalIndex <= count4; intervalIndex++)
			{
				double func2 = func(spatialIndex.Bins[intervalIndex].Select([SpecialName] (int idx) => PointM(idx)).ToList());
				if (!double.IsNaN(func2))
				{
					funcSum += func2;
					ordinal3++;
				}
			}
			if (ordinal3 == 0)
			{
				return double.NaN;
			}
			return funcSum / (double)ordinal3;
		}
	}

	public double NearestNeighborMostCommonDistance([Optional][DefaultParameterValue(3)] int sigFigs, [Optional][DefaultParameterValue(null)] ref Dictionary<double, int> mapping)
	{
		_Closure_0024__44_002D0 capturedLocals = default;
		_Closure_0024__44_002D0 capturedLocals2 = new _Closure_0024__44_002D0(capturedLocals);
		capturedLocals2.sigFigs = sigFigs;
		if (capturedLocals2.sigFigs <= 0)
		{
			throw new ArgumentException("sigFigs");
		}
		mapping = new Dictionary<double, int>();
		capturedLocals2.dic = mapping;
		checked
		{
			Action<IList<PointM>> action = [SpecialName] (IList<PointM> pts) =>
			{
				if (pts.Count > 1)
				{
					int count = pts.Count - 1;
					for (int i = 0; i <= count; i++)
					{
						PointM pointM = pts[i];
						double maxValue = double.MaxValue;
						int count2 = pts.Count - 1;
						for (int j = 0; j <= count2; j++)
						{
							if (i != j)
							{
								PointM ptM = pts[j];
								double distanceTo = pointM.DistanceTo(ptM);
								maxValue = System.Math.Min(maxValue, distanceTo);
							}
						}
						double key = RoundToSignificantDigits(maxValue, capturedLocals2.sigFigs);
						if (capturedLocals2.dic.ContainsKey(key))
						{
							capturedLocals2.dic[key] = capturedLocals2.dic[key] + 1;
						}
						else
						{
							capturedLocals2.dic[key] = 1;
						}
					}
				}
			};
			if (Count <= 50)
			{
				action(this);
			}
			else
			{
				Extent extent = ComputeExtent();
				SpatialIndex<int> spatialIndex = new SpatialIndex<int>(extent, System.Math.Min((int)System.Math.Round((double)Count / 50.0), 4096));
				int count3 = Count - 1;
				for (int ordinal = 0; ordinal <= count3; ordinal++)
				{
					spatialIndex.Add(PointM(ordinal), ordinal);
				}
				int count4 = spatialIndex.Bins.Count() - 1;
				for (int intervalIndex = 0; intervalIndex <= count4; intervalIndex++)
				{
					action(spatialIndex.Bins[intervalIndex].Select([SpecialName] (int idx) => PointM(idx)).ToList());
				}
			}
			int ordinal2 = 0;
			double result = double.NaN;
			foreach (KeyValuePair<double, int> localValue in capturedLocals2.dic)
			{
				if (localValue.Value > ordinal2)
				{
					ordinal2 = localValue.Value;
					result = localValue.Key;
				}
			}
			return result;
		}
	}

	public static double RoundToSignificantDigits(double d, int digits)
	{
		if (d == 0.0)
		{
			return 0.0;
		}
		double systemTerm = System.Math.Pow(10.0, System.Math.Floor(System.Math.Log10(System.Math.Abs(d))) + 1.0);
		return systemTerm * System.Math.Round(d / systemTerm, digits);
	}

	public bool HasZ()
	{
		using (Enumerator itemEnumerator = GetEnumerator())
		{
			while (itemEnumerator.MoveNext())
			{
				if (itemEnumerator.Current.HasZ())
				{
					return true;
				}
			}
		}
		return false;
	}

	public bool HasM()
	{
		using (Enumerator itemEnumerator = GetEnumerator())
		{
			while (itemEnumerator.MoveNext())
			{
				if (itemEnumerator.Current.HasM())
				{
					return true;
				}
			}
		}
		return false;
	}

	public double ComputeLength()
	{
		double distanceToSum = 0.0;
		checked
		{
			int count = Count - 1;
			for (int i = 1; i <= count; i++)
			{
				distanceToSum += base[i].DistanceTo(base[i - 1]);
			}
			return distanceToSum;
		}
	}

	public double ComputeArea()
	{
		double area = 0.0;
		int segmentCount = SegmentCount();
		checked
		{
			SegmentM segmentM = default;
			for (int i = 0; i <= segmentCount; i++)
			{
				if (i == 0)
				{
					segmentM.Assign(base[i], base[Count - 1]);
				}
				else
				{
					segmentM.Assign(base[i], base[i - 1]);
				}
				double deltaX = segmentM.DeltaX();
				double y = segmentM.A.Y;
				double y2 = segmentM.B.Y;
				area += 0.5 * (y + y2) * deltaX;
			}
			return area;
		}
	}

	public int PointMCount()
	{
		return Count;
	}

	int IPoints.PointMCount()
	{
		//ILSpy generated this explicit interface implementation from .override directive in PointMCount
		return this.PointMCount();
	}

	public PointM PointM(int index)
	{
		return base[index];
	}

	PointM IPoints.PointM(int index)
	{
		//ILSpy generated this explicit interface implementation from .override directive in PointM
		return this.PointM(index);
	}

	public PointM FirstPointM()
	{
		return this.First();
	}

	PointM IPoints.FirstPointM()
	{
		//ILSpy generated this explicit interface implementation from .override directive in FirstPointM
		return this.FirstPointM();
	}

	public PointM LastPointM()
	{
		return this.Last();
	}

	PointM IPoints.LastPointM()
	{
		//ILSpy generated this explicit interface implementation from .override directive in LastPointM
		return this.LastPointM();
	}

	public SegmentM Segment(int index)
	{
		SegmentM result = default;
		result.A = base[index];
		result.B = base[checked(index + 1)];
		result.Index = index;
		return result;
	}

	SegmentM ISegments.Segment(int index)
	{
		//ILSpy generated this explicit interface implementation from .override directive in Segment
		return this.Segment(index);
	}

	public int SegmentCount()
	{
		return checked(Count - 1);
	}

	int ISegments.SegmentCount()
	{
		//ILSpy generated this explicit interface implementation from .override directive in SegmentCount
		return this.SegmentCount();
	}

	public SegmentM SegmentFirst()
	{
		SegmentM result = default;
		result.A = base[0];
		result.B = base[1];
		result.Index = 0;
		return result;
	}

	public SegmentM SegmentLast()
	{
		checked
		{
			int countOffset = Count - 1;
			SegmentM result = default;
			result.A = base[countOffset - 1];
			result.B = base[countOffset];
			result.Index = countOffset - 1;
			return result;
		}
	}

	public PointMs DiscretizeLine(double maxStep, [Optional][DefaultParameterValue(0.0)] ref double lineLength)
	{
		checked
		{
			if (Count > 1)
			{
				PointMs pointMs = new PointMs();
				lineLength = ComputeLength();
				int systemLineLengthTerm = (int)System.Math.Ceiling(lineLength / maxStep);
				double lineLengthTerm = lineLength / (double)systemLineLengthTerm;
				if (lineLength == 0.0)
				{
					return null;
				}
				pointMs.Add(this.First());
				double lengthSum = 0.0;
				double computed = lineLengthTerm;
				int count = SegmentCount() - 1;
				for (int i = 0; i <= count; i++)
				{
					SegmentM segmentM = Segment(i);
					double length = segmentM.Length;
					if (length != 0.0)
					{
						for (; !(computed > lengthSum + length); computed += lineLengthTerm)
						{
							double t = (computed - lengthSum) / length;
							pointMs.Add(segmentM.PointAtFraction(t));
						}
						lengthSum += length;
					}
				}
				if (this.Last().DistanceTo(pointMs.Last()) > lineLengthTerm * 0.01)
				{
					pointMs.Add(this.Last());
				}
				return pointMs;
			}
			return null;
		}
	}

	public double[] GetZ()
	{
		checked
		{
			double[] doubleValues = new double[Count - 1 + 1];
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				doubleValues[i] = base[i].Z;
			}
			return doubleValues;
		}
	}

	public double[] GetM()
	{
		checked
		{
			double[] doubleValues = new double[Count - 1 + 1];
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				doubleValues[i] = base[i].M;
			}
			return doubleValues;
		}
	}

	public void RemoveZM()
	{
		SetZM(double.NaN, double.NaN);
	}

	public void SetZ(double z)
	{
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM pointM = base[i];
				pointM.Z = z;
				base[i] = pointM;
			}
		}
	}

	public void SetZ(double[] z)
	{
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM pointM = base[i];
				pointM.Z = z[i];
				base[i] = pointM;
			}
		}
	}

	public void SetM(double m)
	{
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM pointM = base[i];
				pointM.M = m;
				base[i] = pointM;
			}
		}
	}

	public void SetM(double[] m)
	{
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM pointM = base[i];
				pointM.M = m[i];
				base[i] = pointM;
			}
		}
	}

	public void SetZM(double z, double m)
	{
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM pointM = base[i];
				pointM.Z = z;
				pointM.M = m;
				base[i] = pointM;
			}
		}
	}

	public void SetZM(double[] z, double[] m)
	{
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM pointM = base[i];
				pointM.Z = z[i];
				pointM.M = m[i];
				base[i] = pointM;
			}
		}
	}

	public void SwapZM()
	{
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM pointM = base[i];
				pointM.SwapZM();
				base[i] = pointM;
			}
		}
	}

	public void SetLinearZ(double startZ, double endZ)
	{
		AssignZ(0, startZ);
		double distanceToSum = 0.0;
		double length = ComputeLength();
		checked
		{
			int count = Count - 2;
			for (int i = 1; i <= count; i++)
			{
				distanceToSum += base[i].DistanceTo(base[i - 1]);
				double distanceToSumOverLength = distanceToSum / length;
				double z = startZ * (1.0 - distanceToSumOverLength) + endZ * distanceToSumOverLength;
				AssignZ(i, z);
			}
			AssignZ(Count - 1, endZ);
		}
	}

	public void SetLinearM(double startM, double endM)
	{
		AssignM(0, startM);
		double distanceToSum = 0.0;
		double length = ComputeLength();
		checked
		{
			int count = Count - 1;
			for (int i = 1; i <= count; i++)
			{
				distanceToSum += base[i].DistanceTo(base[i - 1]);
				double distanceToSumOverLength = distanceToSum / length;
				double m = startM * (1.0 - distanceToSumOverLength) + endM * distanceToSumOverLength;
				AssignM(i, m);
			}
			AssignZ(Count - 1, endM);
		}
	}

	public void SetLinearZM(double startZ, double endZ, double startM, double endM)
	{
		AssignZM(0, startZ, startM);
		double distanceToSum = 0.0;
		double length = ComputeLength();
		checked
		{
			int count = Count - 1;
			for (int i = 1; i <= count; i++)
			{
				distanceToSum += base[i].DistanceTo(base[i - 1]);
				double distanceToSumOverLength = distanceToSum / length;
				double z = startZ * (1.0 - distanceToSumOverLength) + endZ * distanceToSumOverLength;
				double m = startM * (1.0 - distanceToSumOverLength) + endM * distanceToSumOverLength;
				AssignZM(i, z, m);
			}
			AssignZM(Count - 1, endZ, endM);
		}
	}

	public PointM PointMAtFraction(double fraction)
	{
		int segIndex = default;
		double segFraction = default;
		return PointOnPolylineAtFraction(fraction, ref segIndex, ref segFraction);
	}

	public PointOnPolyline PointAtFraction(double fraction)
	{
		int segIndex = default;
		double segFraction = default;
		PointM pointM = PointOnPolylineAtFraction(fraction, ref segIndex, ref segFraction);
		return new PointOnPolyline(pointM, segIndex, segFraction);
	}

	public PointM PointOnPolylineAtFraction(double fraction, ref int segIndex, ref double segFraction)
	{
		if (fraction <= 0.0)
		{
			segIndex = 0;
			segFraction = 0.0;
			return this.First();
		}
		checked
		{
			if (fraction >= 1.0)
			{
				segIndex = SegmentCount() - 1;
				segFraction = 1.0;
				return this.Last();
			}
			fraction = RasMapperLib.Utilities.Math.GetInRange(fraction, 0.0, 1.0);
			double computeLengthFractionTerm = ComputeLength() * fraction;
			if (computeLengthFractionTerm == 0.0)
			{
				segIndex = 0;
				segFraction = 0.0;
				return this.First();
			}
			double lengthSum = 0.0;
			int count = SegmentCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				double lengthSum2 = lengthSum;
				double length = Segment(i).Length;
				lengthSum += length;
				if (lengthSum >= computeLengthFractionTerm)
				{
					segIndex = i;
					segFraction = (computeLengthFractionTerm - lengthSum2) / length;
					return Segment(i).PointAtFraction(segFraction);
				}
			}
			segIndex = SegmentCount() - 1;
			segFraction = 1.0;
			return this.Last();
		}
	}

	public PointOnPolyline PointAtDistance(double distance)
	{
		double lengthSum = 0.0;
		checked
		{
			int count = SegmentCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				SegmentM segmentM = Segment(i);
				double lengthSum2 = lengthSum;
				double length = segmentM.Length;
				lengthSum += length;
				if (lengthSum >= distance)
				{
					double distanceAboveLengthSum2OverLength = (distance - lengthSum2) / length;
					return new PointOnPolyline(segmentM.PointAtFraction(distanceAboveLengthSum2OverLength), i, distanceAboveLengthSum2OverLength);
				}
			}
			return new PointOnPolyline(this.Last(), SegmentCount() - 1, 1.0);
		}
	}

	public bool CLeftOfAB(int a, int b, int c)
	{
		double computed = base[b].X - base[a].X;
		double computed2 = base[b].Y - base[a].Y;
		double computed3 = base[c].X - base[a].X;
		double computed4 = base[c].Y - base[a].Y;
		return computed * computed4 - computed2 * computed3 > 0.0;
	}

	public bool PointInTriangle(PointM pt, int p0, int p1, int p2)
	{
		double p1P0Term = base[p1].X - base[p0].X;
		double p1P0Term2 = base[p1].Y - base[p0].Y;
		double p2P0Term = base[p2].X - base[p0].X;
		double p2P0Term2 = base[p2].Y - base[p0].Y;
		double ptP0Term = pt.X - base[p0].X;
		double ptP0Term2 = pt.Y - base[p0].Y;
		double computed = p1P0Term * p1P0Term + p1P0Term2 * p1P0Term2;
		double computed2 = p1P0Term * p2P0Term + p1P0Term2 * p2P0Term2;
		double computed3 = p1P0Term * ptP0Term + p1P0Term2 * ptP0Term2;
		double computed4 = p2P0Term * p2P0Term + p2P0Term2 * p2P0Term2;
		double computed5 = p2P0Term * ptP0Term + p2P0Term2 * ptP0Term2;
		double computed6 = 1.0 / (computed * computed4 - computed2 * computed2);
		double computed7 = (computed4 * computed3 - computed2 * computed5) * computed6;
		double computed8 = (computed * computed5 - computed2 * computed3) * computed6;
		if (computed7 > 0.0 && computed8 > 0.0)
		{
			return computed7 + computed8 < 1.0;
		}
		return false;
	}

	public LineM StartingAverageLine(double distanceFromStart)
	{
		LineM result = default;
		result.Assign(this.First(), PointAtDistance(distanceFromStart).PointM);
		return result;
	}

	public LineM EndingAverageLine(double distanceFromEnd)
	{
		double distance = ComputeLength() - distanceFromEnd;
		LineM result = default;
		result.Assign(this.Last(), PointAtDistance(distance).PointM);
		return result;
	}

	public new void Reverse()
	{
		if (Count > 1)
		{
			int count = checked(Count - 1) / 2;
			checked
			{
				for (int i = 0; i <= count; i++)
				{
					int index = Count - 1 - i;
					PointM pointM = base[i];
					base[i] = base[index];
					base[index] = pointM;
				}
			}
		}
	}

	public override string ToString()
	{
		return ToString("", "");
	}

	public string ToString(string xyFormat, string zmFormat)
	{
		StringBuilder stringBuilder = new StringBuilder();
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				stringBuilder.Append(base[i].ToCSString(xyFormat, zmFormat));
				if (i < Count - 1)
				{
					stringBuilder.Append(" ");
				}
			}
			return stringBuilder.ToString();
		}
	}

	public string ToSSString()
	{
		return ToString("", "");
	}

	public string ToSSString(string xyFormat, string zmFormat)
	{
		StringBuilder stringBuilder = new StringBuilder();
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				stringBuilder.Append(base[i].ToSSString(xyFormat, zmFormat));
				if (i < Count - 1)
				{
					stringBuilder.Append(" ");
				}
			}
			return stringBuilder.ToString();
		}
	}

	public void WriteToBinaryXY(BinaryWriter bw)
	{
		bw.Write(Count);
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				bw.Write(base[i].X);
				bw.Write(base[i].Y);
			}
		}
	}

	public void ReadFromBinaryXY(BinaryReader br)
	{
		int capacity = br.ReadInt32();
		Clear();
		if (capacity > Capacity)
		{
			Capacity = capacity;
		}
		int readInt32 = capacity;
		for (int i = 1; i <= readInt32; i = checked(i + 1))
		{
			double x = br.ReadDouble();
			double y = br.ReadDouble();
			Add(x, y);
		}
	}

	public void ToImmediateWindow(string headerline = "")
	{
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
			}
		}
	}

	public void WriteToImmediateWindowAsPointMsCode(string name)
	{
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
			}
		}
	}

	public void FilterXY(double tolerance)
	{
		FilterXY(tolerance, 0, checked(Count - 1));
	}

	public void FilterXY(double tolerance, int first, int last)
	{
		checked
		{
			if (double.IsNaN(tolerance) || last - first + 1 <= 2)
			{
				return;
			}
			List<int> intList = new List<int>();
			for (int i = 0; i <= first; i++)
			{
				intList.Add(i);
			}
			intList.Add(last);
			for (int j = last; j >= first; j += -1)
			{
				if (!base[first].EqualXY(base[j]))
				{
					last = j;
					break;
				}
			}
			FilterDouglasPeucker(first, last, tolerance, intList);
			int ordinal = last + 1;
			int count = Count - 1;
			for (int k = ordinal; k <= count; k++)
			{
				intList.Add(k);
			}
			if (intList.Count >= Count)
			{
				return;
			}
			intList.Sort();
			int count2 = intList.Count - 1;
			for (int l = 0; l <= count2; l++)
			{
				if (l != intList[l])
				{
					base[l] = base[intList[l]];
				}
			}
			RemoveRange(intList.Count);
		}
	}

	private void FilterDouglasPeucker(int first, int last, double tolerance, List<int> keepIndex)
	{
		checked
		{
			if (last - first <= 1)
			{
				return;
			}
			double computed = 0.0;
			int ordinal = 0;
			double computed2 = base[last].X - base[first].X;
			double computed3 = base[last].Y - base[first].Y;
			int ordinal2 = first + 1;
			int count = last - 1;
			for (int i = ordinal2; i <= count; i++)
			{
				double systemTerm = System.Math.Abs(computed2 * (base[i].Y - base[first].Y) - computed3 * (base[i].X - base[first].X));
				if (systemTerm > computed)
				{
					computed = systemTerm;
					ordinal = i;
				}
			}
			double systemTerm2 = System.Math.Sqrt(System.Math.Pow(computed2, 2.0) + System.Math.Pow(computed3, 2.0));
			if (computed / systemTerm2 > tolerance)
			{
				keepIndex.Add(ordinal);
				FilterDouglasPeucker(first, ordinal, tolerance, keepIndex);
				FilterDouglasPeucker(ordinal, last, tolerance, keepIndex);
			}
		}
	}

	public PointMs Clone()
	{
		PointMs pointMs = new PointMs(Count);
		CopyTo(pointMs);
		return pointMs;
	}

	public void FilterXYIterative(int iterationCount, int targetPointCount)
	{
		if (Count <= targetPointCount)
		{
			return;
		}
		PointMs pointMs = Clone();
		double unity = 1.0;
		PointMs pointMs2 = null;
		pointMs.FilterXY(unity);
		if (pointMs.PointMCount() == targetPointCount)
		{
			Clear();
			pointMs.CopyTo(this);
			return;
		}
		checked
		{
			double computed;
			double computed2;
			if (pointMs.PointMCount() < targetPointCount)
			{
				computed = unity;
				pointMs2 = pointMs;
				double computed3 = unity;
				int ordinal = 0;
				while (true)
				{
					computed3 /= 2.0;
					PointMs pointMs3 = Clone();
					pointMs3.FilterXY(computed3);
					if (pointMs3.PointMCount() == targetPointCount)
					{
						Clear();
						pointMs3.CopyTo(this);
						return;
					}
					if (pointMs3.PointMCount() >= targetPointCount)
					{
						break;
					}
					computed = computed3;
					pointMs2 = pointMs3;
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
				PointMs pointMs4;
				while (true)
				{
					computed4 *= 2.0;
					pointMs4 = Clone();
					pointMs4.FilterXY(computed4);
					if (pointMs4.PointMCount() == targetPointCount)
					{
						Clear();
						pointMs4.CopyTo(this);
						return;
					}
					if (pointMs4.PointMCount() <= targetPointCount)
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
				pointMs2 = pointMs4;
			}
			int count = iterationCount - 1;
			for (int i = 0; i <= count; i++)
			{
				double computed5 = (computed2 + computed) / 2.0;
				PointMs pointMs5 = Clone();
				pointMs5.FilterXY(computed5);
				if (pointMs5.PointMCount() == targetPointCount)
				{
					Clear();
					pointMs5.CopyTo(this);
					return;
				}
				if (pointMs5.PointMCount() > targetPointCount)
				{
					computed2 = computed5;
					continue;
				}
				computed = computed5;
				pointMs2 = pointMs5;
			}
			Clear();
			pointMs2.CopyTo(this);
		}
	}

	public void Resample(double approximateSpacing, PointMs resampled)
	{
		resampled.Clear();
		if (Count == 0)
		{
			return;
		}
		if (Count == 1)
		{
			resampled.Add(base[0]);
			return;
		}
		double length2 = ComputeLength();
		if (length2 == 0.0)
		{
			resampled.Add(base[0]);
			return;
		}
		checked
		{
			int systemLength2Term = (int)System.Math.Ceiling(length2 / approximateSpacing);
			double length2Term = length2 / (double)systemLength2Term;
			resampled.Add(this.First());
			double computed = length2Term;
			double lengthSum = 0.0;
			int count = SegmentCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				SegmentM segmentM = Segment(i);
				double length;
				for (length = segmentM.Length; !(computed > lengthSum + length); computed += length2Term)
				{
					double t = (computed - lengthSum) / length;
					if (resampled.Count < systemLength2Term)
					{
						resampled.Add(segmentM.PointAtFraction(t));
					}
				}
				lengthSum += length;
			}
			resampled.Add(this.Last());
		}
	}

	public void Densify(double maxSegmentLength)
	{
		checked
		{
			if (Count > 0)
			{
				PointMs pointMs = new PointMs();
				pointMs.Add(this.First());
				int count = SegmentCount() - 1;
				for (int i = 0; i <= count; i++)
				{
					SegmentM segmentM = Segment(i);
					pointMs.AddRange(segmentM.AddDensifyPoints(maxSegmentLength));
					pointMs.Add(segmentM.B);
				}
				if (pointMs.Count > Count)
				{
					Clear();
					AddRange(pointMs);
				}
			}
		}
	}

	public void Transform(Transform transformer)
	{
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM PointM = base[i];
				transformer.TransformPointM(ref PointM);
				base[i] = PointM;
			}
		}
	}

	public void TransformAffine(PointM newStart, PointM newEnd)
	{
		SegmentM curSegment = new SegmentM(this.First(), this.Last());
		SegmentM transSegment = new SegmentM(newStart, newEnd);
		Affine transformer = new Affine(curSegment, transSegment);
		Transform(transformer);
	}

	public void Project(Projection fromProjection, Projection toProjection)
	{
		if (Count <= 0)
		{
			return;
		}
		checked
		{
			double[] doubleValues = new double[Count - 1 + 1];
			double[] doubleValuesArray = new double[Count - 1 + 1];
			double[] doubleValues2 = new double[Count - 1 + 1];
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM pointM = base[i];
				doubleValues[i] = pointM.X;
				doubleValuesArray[i] = pointM.Y;
				if (double.IsNaN(pointM.Z))
				{
					doubleValues2[i] = 0.0;
				}
				else
				{
					doubleValues2[i] = pointM.Z;
				}
			}
			Projector.TransformPoints(fromProjection, toProjection, doubleValues, doubleValuesArray, doubleValues2);
			int count2 = Count - 1;
			for (int j = 0; j <= count2; j++)
			{
				PointM pointM2 = base[j];
				pointM2.X = doubleValues[j];
				pointM2.Y = doubleValuesArray[j];
				if (!double.IsNaN(pointM2.Z))
				{
					pointM2.Z = doubleValues2[j];
				}
				base[j] = pointM2;
			}
		}
	}

	public PointOnPolyline NearestPoint(PointM PointM)
	{
		double maxValue = double.MaxValue;
		checked
		{
			int count = SegmentCount() - 1;
			int ordinal = default;
			for (int i = 0; i <= count; i++)
			{
				SegmentM segmentM = Segment(i);
				SegmentM.RelativePosition relativePosition = SegmentM.RelativePosition.Undefined;
				double distanceToPoint = segmentM.DistanceToPoint(PointM, ref relativePosition);
				if (distanceToPoint < maxValue)
				{
					maxValue = distanceToPoint;
					ordinal = i;
				}
			}
			double t = default;
			PointM pointM = Segment(ordinal).NearestPoint(PointM, ref t);
			return new PointOnPolyline(pointM, ordinal, t);
		}
	}

	public void TrimBeginning(PointOnPolyline point)
	{
		if (Count <= 1)
		{
			return;
		}
		if (point.SegmentIndex == 0)
		{
			if (point.Fraction > 0.0)
			{
				base[0] = point.PointM;
			}
		}
		else
		{
			base[0] = point.PointM;
			RemoveRange(1, point.SegmentIndex);
		}
	}

	public void TrimEnd(PointOnPolyline point)
	{
		checked
		{
			if (point.SegmentIndex == SegmentCount() - 1)
			{
				if (point.Fraction < 1.0)
				{
					base[Count - 1] = point.PointM;
				}
			}
			else
			{
				RemoveRange(point.SegmentIndex + 1);
				Add(point.PointM);
			}
		}
	}

	public void TrimSelfIntersections()
	{
		if (Count <= 3)
		{
			return;
		}
		int i = 0;
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(ComputeExtent(), SpatialIndex.ReasonableSegmentBinCount(SegmentCount()));
		checked
		{
			int count = SegmentCount() - 1;
			for (int j = 2; j <= count; j++)
			{
				spatialIndex.Add(Segment(j), j);
			}
			int ordinal = 0;
			double x = default;
			double y = default;
			double t = default;
			double t2 = default;
			PointM pointM = default;
			for (; i < SegmentCount() - 1; i++)
			{
				SegmentM segment = Segment(i);
				List<int> elements = spatialIndex.GetElements(segment);
				elements.Sort();
				for (int k = elements.Count - 1; k >= 0; k += -1)
				{
					int elementsTerm = elements[k] - ordinal;
					if (elementsTerm == -1 || elementsTerm < i + 2)
					{
						continue;
					}
					SegmentM seg = Segment(elementsTerm);
					if (segment.Intersects(seg, ref x, ref y, ref t, ref t2))
					{
						pointM.Assign(x, y);
						if (segment.HasZ() && Segment(elementsTerm).HasZ())
						{
							pointM.Z = (segment.PointAtFraction(t).Z + Segment(elementsTerm).PointAtFraction(t2).Z) * 0.5;
						}
						if (segment.HasM() && Segment(elementsTerm).HasM())
						{
							pointM.M = (segment.PointAtFraction(t).M + Segment(elementsTerm).PointAtFraction(t2).M) * 0.5;
						}
						base[i + 1] = pointM;
						int ordinal2 = elementsTerm - (i + 1);
						int index = i + 2;
						RemoveRange(index, ordinal2);
						ordinal += ordinal2;
						break;
					}
				}
			}
		}
	}

	public void Intersections(PointMs pts, List<PointIntersect> intersections)
	{
		intersections.Clear();
		checked
		{
			int count = SegmentCount() - 1;
			double x = default;
			double y = default;
			double t = default;
			double t2 = default;
			for (int i = 0; i <= count; i++)
			{
				SegmentM segmentM = Segment(i);
				int count2 = pts.SegmentCount() - 1;
				for (int j = 0; j <= count2; j++)
				{
					if (segmentM.Intersects(pts.Segment(j), ref x, ref y, ref t, ref t2))
					{
						intersections.Add(new PointIntersect(x, y, i, t, j, t2));
					}
				}
			}
		}
	}

	public PointIntersect Intersects(SegmentM segment)
	{
		checked
		{
			int count = SegmentCount() - 1;
			double x = default;
			double y = default;
			double t = default;
			double t2 = default;
			for (int i = 0; i <= count; i++)
			{
				if (Segment(i).Intersects(segment, ref x, ref y, ref t, ref t2))
				{
					return new PointIntersect(x, y, i, t, 0, t2);
				}
			}
			return null;
		}
	}

	public static int CompareFirstM(PointMs a, PointMs b)
	{
		double naN = double.NaN;
		if (a != null && a.Count > 0)
		{
			naN = a[0].M;
		}
		double naN2 = double.NaN;
		if (b != null && b.Count > 0)
		{
			naN2 = b[0].M;
		}
		if (naN == naN2)
		{
			return 0;
		}
		if (naN < naN2)
		{
			return -1;
		}
		return 1;
	}
}
