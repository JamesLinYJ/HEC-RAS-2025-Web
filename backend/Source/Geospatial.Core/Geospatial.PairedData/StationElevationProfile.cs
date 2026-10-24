using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Geospatial.Vectors;
using Utility;

namespace Geospatial.PairedData;

[DebuggerDisplay("{Count} points, Max Station={MaxStation}")]
public class StationElevationProfile : StationValueProfile<double>
{
	public bool IsReadOnly => false;

	public Point this[int index]
	{
		get
		{
			return new Point(_stations[index], _values[index]);
		}
		set
		{
			_stations[index] = value.X;
			_values[index] = value.Y;
		}
	}

	public IReadOnlyList<double> Elevations => _values;

	public StationElevationProfile()
	{
	}

	public StationElevationProfile(int capacity)
		: base(capacity)
	{
	}

	public StationElevationProfile(IList<Point> points)
		: base(points.Select((Point pt) => pt.X), points.Select((Point pt) => pt.Y))
	{
	}

	public StationElevationProfile(IEnumerable<double> stations, IEnumerable<double> elevations)
		: base(stations, elevations)
	{
	}

	public static StationElevationProfile FromPoints(Memory<Point> points)
	{
		Span<Point> pointsSpan = points.Span;
		StationElevationProfile stationElevationProfile = new StationElevationProfile(points.Length);
		for (int i = 0; i < points.Length; i++)
		{
			stationElevationProfile.Add(pointsSpan[i]);
		}
		return stationElevationProfile;
	}

	public bool CopyRange(double minStation, double maxStation, StationElevationProfile profile)
	{
		int count = profile.Count;
		if (Count > 0)
		{
			if (this[0].X >= minStation)
			{
				profile.Add(this[0]);
			}
			for (int i = 1; i < Count; i++)
			{
				if (this[i - 1].X < minStation && this[i].X > minStation)
				{
					double computed = this[i].Y - this[i - 1].Y;
					double computed2 = this[i].X - this[i - 1].X;
					double minStationTerm = this[i - 1].Y + computed / computed2 * (minStation - this[i - 1].X);
					profile.Add(minStation, minStationTerm);
				}
				else if (this[i].X >= minStation && this[i].X <= maxStation)
				{
					profile.Add(this[i]);
				}
				else if (this[i].X > maxStation)
				{
					if (this[i - 1].X < maxStation)
					{
						double computed3 = this[i].Y - this[i - 1].Y;
						double computed4 = this[i].X - this[i - 1].X;
						double maxStationTerm = this[i - 1].Y + computed3 / computed4 * (maxStation - this[i - 1].X);
						profile.Add(maxStation, maxStationTerm);
					}
					break;
				}
			}
		}
		return profile.Count > count;
	}

	public StationElevationProfile CloneFiltered(double tolerance)
	{
		return CloneDedupe(tolerance);
	}

	public new StationElevationProfile Clone()
	{
		return new StationElevationProfile(_stations, _values);
	}

	public StationElevationProfile CloneDedupe(double minDXDY = 1E-06)
	{
		StationElevationProfile stationElevationProfile = new StationElevationProfile(Count);
		double minValue = double.MinValue;
		double minValue2 = double.MinValue;
		for (int i = 0; i < Count; i++)
		{
			double station = _stations[i];
			double minValue22 = _values[i];
			if (station - minValue > minDXDY || Math.Abs(minValue22 - minValue2) > minDXDY)
			{
				stationElevationProfile.Add(station, minValue22);
				minValue = station;
				minValue2 = minValue22;
			}
		}
		return stationElevationProfile;
	}

	public static StationElevationProfile FromConstantStationing(Memory<double> data, double stationDelta = 1.0)
	{
		double[] stations = (from i in Enumerable.Range(0, data.Length)
			select (double)i * stationDelta).ToArray();
		StationElevationProfile stationElevationProfile = new StationElevationProfile(data.Length);
		stationElevationProfile.AddRange(stations, data.ToArray());
		return stationElevationProfile;
	}

	public double FillArea(double elevation)
	{
		double computed = 0.0;
		for (int i = 1; i < Count; i++)
		{
			Point point = this[i - 1];
			Point point2 = this[i];
			if (!point.IsUndefined() && !point2.IsUndefined())
			{
				double point2PointTerm = point2.X - point.X;
				double elevationPointTerm = elevation - point.Y;
				double elevationPoint2Term = elevation - point2.Y;
				if (elevationPointTerm > 0.0 && elevationPoint2Term > 0.0)
				{
					computed += point2PointTerm * 0.5 * (elevationPointTerm + elevationPoint2Term);
				}
				else if (elevationPointTerm > 0.0)
				{
					computed += elevationPointTerm * 0.5 * point2PointTerm * elevationPointTerm / (point2.Y - point.Y);
				}
				else if (elevationPoint2Term > 0.0)
				{
					computed += elevationPoint2Term * 0.5 * point2PointTerm * elevationPoint2Term / (point.Y - point2.Y);
				}
			}
		}
		return computed;
	}

	public double WettedPerimeter(double elevation)
	{
		double segmentElevationTerm = 0.0;
		Segment segment = default;
		for (int i = 1; i < Count; i++)
		{
			if (this[i - 1].IsUndefined() || this[i].IsUndefined())
			{
				continue;
			}
			segment.A = new Point(this[i - 1].X, this[i - 1].Y);
			segment.B = new Point(this[i].X, this[i].Y);
			if (!(segment.MinY >= elevation))
			{
				if (segment.MaxY < elevation)
				{
					segmentElevationTerm += segment.Length;
					continue;
				}
				double x = segment.InterpolatedX(elevation);
				segmentElevationTerm = ((!(segment.A.Y < elevation)) ? (segmentElevationTerm + segment.B.DistanceTo(x, elevation)) : (segmentElevationTerm + segment.A.DistanceTo(x, elevation)));
			}
		}
		return segmentElevationTerm;
	}

	public void MinMaxElevation(out double min, out double max)
	{
		Buffers.MaxMin(CollectionsMarshal.AsSpan(_values), out max, out min);
	}

	public StationElevationProfile MergeTakeHigher(StationElevationProfile other)
	{
		return MergeTakeHL(other, takeHigher: true);
	}

	public StationElevationProfile MergeTakeLower(StationElevationProfile other)
	{
		return MergeTakeHL(other, takeHigher: false);
	}

	private StationElevationProfile MergeTakeHL(StationElevationProfile other, bool takeHigher)
	{
		if (other == null)
		{
			return Clone();
		}
		if (other.Count < 2)
		{
			return Clone();
		}
		if (Count < 2)
		{
			return other.Clone();
		}
		Func<double, double, bool> swapLTGT;
		Func<Segment, Point, bool> PointOnCorrectSide;
		if (takeHigher)
		{
			swapLTGT = (double argA, double argB) => argA > argB;
			PointOnCorrectSide = (Segment seg, Point p) => seg.PointIsLeft(p);
		}
		else
		{
			swapLTGT = (double argA, double argB) => argA < argB;
			PointOnCorrectSide = (Segment seg, Point p) => seg.PointIsRight(p);
		}
		StationElevationProfile newPf = new StationElevationProfile();
		int thisIdx = 0;
		int otherIdx = 0;
		byte mostRecentInjectionState;
		if (MinStation == other.MinStation)
		{
			if (_values[0] == other._values[0])
			{
				newPf.Add(_stations[0], _values[0]);
				mostRecentInjectionState = 3;
			}
			else if (swapLTGT(_values[0], other._values[0]))
			{
				newPf.Add(this[0]);
				mostRecentInjectionState = 0;
			}
			else
			{
				newPf.Add(other[0]);
				mostRecentInjectionState = 1;
			}
			thisIdx++;
			otherIdx++;
		}
		else if (MinStation < other.MinStation)
		{
			newPf.Add(this[0]);
			mostRecentInjectionState = 0;
			thisIdx++;
		}
		else
		{
			newPf.Add(other[0]);
			mostRecentInjectionState = 1;
			otherIdx++;
		}
		while (true)
		{
			if (thisIdx >= Count)
			{
				other.GetRange(otherIdx, other.Count - otherIdx, out var stations, out var values);
				newPf.AddRange(stations, values);
				break;
			}
			if (otherIdx >= other.Count)
			{
				GetRange(thisIdx, Count - thisIdx, out var stations2, out var values2);
				newPf.AddRange(stations2, values2);
				break;
			}
			Point pt = this[thisIdx];
			Point point = other[otherIdx];
			if (pt.X == point.X)
			{
				if (pt.Y == point.Y)
				{
					mostRecentInjectionState = 3;
					newPf.Add(pt);
				}
				else if (swapLTGT(pt.Y, point.Y))
				{
					Advancer(this, other, thisIdx, otherIdx, 0);
				}
				else
				{
					Advancer(other, this, otherIdx, thisIdx, 1);
				}
				thisIdx++;
				otherIdx++;
			}
			else if (pt.X < point.X)
			{
				Advancer(this, other, thisIdx, otherIdx, 0);
				thisIdx++;
			}
			else
			{
				Advancer(other, this, otherIdx, thisIdx, 1);
				otherIdx++;
			}
		}
		return newPf;
		void Advancer(StationElevationProfile aProfile, StationElevationProfile bProfile, int aIndex, int bIndex, byte aState)
		{
			Point point2 = aProfile[aIndex];
			if (bIndex == 0)
			{
				newPf.Add(point2);
				mostRecentInjectionState = aState;
			}
			else
			{
				Point ptB = bProfile[bIndex];
				Point ptA = bProfile[bIndex - 1];
				Segment segment2 = new Segment(ptA, ptB);
				if (aIndex == 0)
				{
					if (PointOnCorrectSide(segment2, point2))
					{
						newPf.Add(point2);
						mostRecentInjectionState = aState;
					}
				}
				else
				{
					Point point3 = aProfile[aIndex - 1];
					Segment segment = new Segment(point3, point2);
					if (mostRecentInjectionState == aState)
					{
						if (PointOnCorrectSide(segment2, point2))
						{
							newPf.Add(point2);
							mostRecentInjectionState = aState;
						}
						else
						{
							FindInjectMRUIntersection();
							mostRecentInjectionState = 2;
						}
					}
					else if (mostRecentInjectionState == 2)
					{
						if (PointOnCorrectSide(segment2, point2))
						{
							if (!PointOnCorrectSide(segment2, point3))
							{
								FindInjectMRUIntersection();
							}
							newPf.Add(point2);
							mostRecentInjectionState = aState;
						}
					}
					else if (mostRecentInjectionState == 3)
					{
						double slope = segment.Slope;
						double slope2 = segment2.Slope;
						if (swapLTGT(slope, slope2))
						{
							newPf.Add(point2);
							mostRecentInjectionState = aState;
						}
						else
						{
							mostRecentInjectionState = 2;
						}
					}
					else if (PointOnCorrectSide(segment2, point2))
					{
						FindInjectMRUIntersection();
						newPf.Add(point2);
						mostRecentInjectionState = aState;
					}
				}
			}
		}
		void FindInjectMRUIntersection()
		{
			Segment segment = new Segment(this[thisIdx - 1], this[thisIdx]);
			Segment seg = new Segment(other[otherIdx - 1], other[otherIdx]);
			if (segment.IntersectPoint(seg, out var x, out var y, out var _, out var _))
			{
				newPf.Add(x, y);
			}
		}
	}

	public StationElevationProfile MergeReplaceWith(StationElevationProfile other, bool injectVerticalWall)
	{
		if (other == null)
		{
			throw new ArgumentNullException("other");
		}
		if (other.Count < 2)
		{
			return Clone();
		}
		if (Count < 2)
		{
			return other.Clone();
		}
		StationElevationProfile stationElevationProfile = new StationElevationProfile();
		double station = other._stations[0];
		List<double> stations = other._stations;
		double station2 = stations[stations.Count - 1];
		double station3 = _stations[0];
		List<double> stations2 = _stations;
		double stations22 = stations2[stations2.Count - 1];
		if (station > stations22)
		{
			stationElevationProfile.AddRange(this);
			stationElevationProfile.AddRange(other);
			return stationElevationProfile;
		}
		if (station == stations22)
		{
			if (injectVerticalWall)
			{
				stationElevationProfile.AddRange(this);
				stationElevationProfile.AddRange(other);
			}
			else
			{
				stationElevationProfile.AddRange(this, 0, Count - 1);
				stationElevationProfile.AddRange(other);
			}
			return stationElevationProfile;
		}
		if (station2 < station3)
		{
			stationElevationProfile.AddRange(other);
			stationElevationProfile.AddRange(this);
			return stationElevationProfile;
		}
		if (station2 == station3)
		{
			if (injectVerticalWall)
			{
				stationElevationProfile.AddRange(other);
				stationElevationProfile.AddRange(this);
			}
			else
			{
				stationElevationProfile.AddRange(other, 0, other.Count - 1);
				stationElevationProfile.AddRange(this);
			}
			return stationElevationProfile;
		}
		int i;
		for (i = 0; i < Count; i++)
		{
			double station4 = _stations[i];
			if (station4 < station)
			{
				stationElevationProfile.Add(this[i]);
				continue;
			}
			if (!((station4 == station) & injectVerticalWall))
			{
				break;
			}
			stationElevationProfile.Add(this[i]);
		}
		if ((i == 0 || _stations[i - 1] != station) & injectVerticalWall)
		{
			double sample = Sample(station);
			stationElevationProfile.Add(station, sample);
		}
		stationElevationProfile.AddRange(other._stations, other._values);
		if (station2 > stations22)
		{
			return stationElevationProfile;
		}
		if (station2 == stations22)
		{
			if (injectVerticalWall)
			{
				List<double> values = _values;
				stationElevationProfile.Add(stations22, values[values.Count - 1]);
			}
			return stationElevationProfile;
		}
		for (; i < Count && !(_stations[i] >= station2); i++)
		{
		}
		if ((_stations[i - 1] != station2) & injectVerticalWall)
		{
			double sample2 = Sample(station2);
			stationElevationProfile.Add(station2, sample2);
		}
		for (; i < Count; i++)
		{
			stationElevationProfile.Add(_stations[i], _values[i]);
		}
		return stationElevationProfile;
	}

	public StationElevationProfile MergeAdd(StationElevationProfile other, double dupeTolerance = 0.01)
	{
		if (other == null)
		{
			return Clone();
		}
		if (other.Count < 2)
		{
			return Clone();
		}
		if (Count < 2)
		{
			return null;
		}
		StationElevationProfile stationElevationProfile = new StationElevationProfile();
		int i = 0;
		int ordinal = 0;
		for (; _stations[i] < other._stations[0]; i++)
		{
			stationElevationProfile.Add(this[i]);
		}
		while (i != Count)
		{
			Point pt = this[i];
			if (ordinal == other.Count)
			{
				Point point = other[ordinal - 1];
				if (point.X + dupeTolerance < pt.X && i != 0)
				{
					double xComponent = point.X + dupeTolerance;
					stationElevationProfile.Add(new Point(xComponent, Query(xComponent)));
				}
				for (int j = i; j < Count; j++)
				{
					stationElevationProfile.Add(this[j]);
				}
				break;
			}
			Point point2 = other[ordinal];
			if (pt.X == point2.X)
			{
				if (ordinal == 0 && i != 0)
				{
					double dupeTolerancePtTerm = Math.Min(dupeTolerance, (pt.X - this[i - 1].X) / 2.0);
					stationElevationProfile.Add(new Point(pt.X - dupeTolerancePtTerm, pt.Y));
				}
				stationElevationProfile.Add(new Point(pt.X, pt.Y + point2.Y));
				i++;
				ordinal++;
				continue;
			}
			if (pt.X < point2.X)
			{
				double query = other.Query(pt.X, out var beforeStart, out var exceededFinal);
				if (beforeStart | exceededFinal)
				{
					stationElevationProfile.Add(pt);
				}
				else
				{
					stationElevationProfile.Add(new Point(pt.X, pt.Y + query));
				}
				i++;
				continue;
			}
			if (ordinal == 0 && i != 0)
			{
				double dupeTolerancePtTerm2 = Math.Min(dupeTolerance, (pt.X - this[i - 1].X) / 2.0);
				stationElevationProfile.Add(new Point(pt.X - dupeTolerancePtTerm2, pt.Y));
			}
			double query2 = Query(point2.X, out var beforeStart2, out var exceededFinal2);
			if (!(beforeStart2 | exceededFinal2))
			{
				stationElevationProfile.Add(new Point(point2.X, point2.Y + query2));
			}
			ordinal++;
		}
		return stationElevationProfile;
	}

	public bool ValidateData(double invalidDeltaX = 0.0)
	{
		if (Count == 0)
		{
			return true;
		}
		double minValue = double.MinValue;
		for (int i = 0; i <= Count - 1; i++)
		{
			Point point = this[i];
			if (double.IsNaN(point.X) || double.IsNaN(point.Y))
			{
				return false;
			}
			if (point.X <= minValue + invalidDeltaX)
			{
				return false;
			}
			minValue = point.X;
		}
		return true;
	}

	public StationElevationProfile FilterByArea(int maxPoints, double minDXDY = 1E-06, List<int> forceKeepPointIndexes = null)
	{
		if (forceKeepPointIndexes == null)
		{
			forceKeepPointIndexes = new List<int>();
		}
		StationElevationProfile stationElevationProfile = CloneDedupe(minDXDY);
		List<double> doubleList = new List<double>();
		for (int i = 0; i < stationElevationProfile.Count; i++)
		{
			double triangleArea = stationElevationProfile.GetTriangleArea(i, forceKeepPointIndexes);
			doubleList.Add(triangleArea);
		}
		while (stationElevationProfile.Count > maxPoints)
		{
			int intervalIndex = 0;
			for (int j = 1; j < stationElevationProfile.Count; j++)
			{
				if (doubleList[j] < doubleList[intervalIndex])
				{
					intervalIndex = j;
				}
			}
			stationElevationProfile.Remove(intervalIndex);
			doubleList.RemoveAt(intervalIndex);
			if (doubleList[intervalIndex - 1] < double.MaxValue)
			{
				doubleList[intervalIndex - 1] = stationElevationProfile.GetTriangleArea(intervalIndex - 1);
			}
			if (doubleList[intervalIndex] < double.MaxValue)
			{
				doubleList[intervalIndex] = stationElevationProfile.GetTriangleArea(intervalIndex);
			}
		}
		return stationElevationProfile;
	}

	private double GetTriangleArea(int idx, List<int> keepPoints = null)
	{
		if (idx == 0 || idx == Count - 1)
		{
			return double.MaxValue;
		}
		if (keepPoints != null && keepPoints.Contains(idx))
		{
			return double.MaxValue;
		}
		return Math.Abs(Vector.CrossProduct(this[idx - 1], this[idx], this[idx + 1]) / 2.0);
	}

	public StationElevationProfile RoundData(int xDP, int yDP)
	{
		for (int i = 0; i < Count; i++)
		{
			Point point = this[i];
			this[i] = new Point(Math.Round(point.X, xDP), Math.Round(point.Y, yDP));
		}
		return this;
	}

	public void Add(Point pt)
	{
		Add(pt.X, pt.Y);
	}

	public double Slope(int idx)
	{
		return (_values[idx] - _values[idx - 1]) / (_stations[idx] - _stations[idx - 1]);
	}

	public double InverseSlope(int idx)
	{
		return (_stations[idx] - _stations[idx - 1]) / (_values[idx] - _values[idx - 1]);
	}

	public double Query(double xVal)
	{
		bool beforeStart;
		bool exceededFinal;
		return Query(xVal, out beforeStart, out exceededFinal);
	}

	public double Query(double xVal, out bool beforeStart, out bool exceededFinal)
	{
		exceededFinal = false;
		beforeStart = false;
		for (int i = 0; i < Count; i++)
		{
			Point ptA = this[i];
			if (xVal < ptA.X)
			{
				if (i == 0)
				{
					beforeStart = true;
					return ptA.Y;
				}
				Point ptB = this[i - 1];
				double slope = new Segment(ptA, ptB).Slope;
				double xValPtBTerm = xVal - ptB.X;
				return ptB.Y + slope * xValPtBTerm;
			}
		}
		exceededFinal = true;
		List<double> values = _values;
		return values[values.Count - 1];
	}

	public double QueryWithLinearExtrapolation(double xInput, double extrapolationFactor)
	{
		double query = Query(xInput, out var beforeStart, out var exceededFinal);
		if (beforeStart)
		{
			throw new NotImplementedException();
		}
		if (exceededFinal)
		{
			double stationsTerm = Stations.Last();
			double xInputTerm = xInput - stationsTerm;
			query += xInputTerm * extrapolationFactor;
		}
		return query;
	}

	public override double Sample(double station)
	{
		int binarySearch = MathHelpers.BinarySearch(_stations, station);
		if (binarySearch >= 0)
		{
			return _values[binarySearch];
		}
		binarySearch = ~binarySearch;
		if (binarySearch == 0 || binarySearch == Count)
		{
			if (binarySearch == Count)
			{
				binarySearch = Count - 1;
			}
			if (ExtrapolationBehavior == ExtrapolationBehavior.ReturnNoData)
			{
				return 0.0;
			}
			if (ExtrapolationBehavior == ExtrapolationBehavior.ReturnZero)
			{
				return 0.0;
			}
			if (ExtrapolationBehavior == ExtrapolationBehavior.NearestValue)
			{
				return _values[binarySearch];
			}
			if (ExtrapolationBehavior == ExtrapolationBehavior.ExtrapolateNearestSegment)
			{
				return _values[binarySearch];
			}
			if (ExtrapolationBehavior == ExtrapolationBehavior.ThrowException)
			{
				throw new ArgumentException("Station was out of range.");
			}
			throw new NotImplementedException();
		}
		Point ptB = this[binarySearch - 1];
		Point ptA = this[binarySearch];
		double slope = new Segment(ptA, ptB).Slope;
		double stationPtBTerm = station - ptB.X;
		return ptB.Y + slope * stationPtBTerm;
	}

	public string DebugPrint(int rowCh = 40, int colCh = 180)
	{
		MinMaxElevation(out var min, out var max);
		string min2 = min.ToString("G6");
		string max2 = max.ToString("G6");
		double minStation = MinStation;
		double maxStation = MaxStation;
		string minStation2 = minStation.ToString("G6");
		string maxStation2 = maxStation.ToString("G6");
		int min2Max2Term = Math.Max(min2.Length, max2.Length) + 1;
		char[,] charValues = new char[rowCh, colCh];
		for (int i = 0; i < rowCh; i++)
		{
			for (int j = 0; j < colCh; j++)
			{
				charValues[i, j] = ' ';
			}
		}
		int ordinal = min2Max2Term + 1;
		int count = rowCh - 2;
		int countOffset = count + 1;
		int ordinal2 = ordinal;
		for (int k = ordinal; k < colCh; k++)
		{
			charValues[count, k] = '-';
		}
		for (int l = 0; l < count; l++)
		{
			charValues[l, ordinal] = '|';
		}
		for (int m = 0; m < min2.Length; m++)
		{
			charValues[count, m] = min2[m];
		}
		for (int n = 0; n < max2.Length; n++)
		{
			charValues[0, n] = max2[n];
		}
		for (int intervalIndex = 0; intervalIndex < minStation2.Length; intervalIndex++)
		{
			charValues[countOffset, intervalIndex + ordinal2] = minStation2[intervalIndex];
		}
		for (int intervalIndex2 = 0; intervalIndex2 < maxStation2.Length; intervalIndex2++)
		{
			charValues[countOffset, intervalIndex2 + (colCh - maxStation2.Length)] = maxStation2[intervalIndex2];
		}
		int ordinal3 = ordinal + 1;
		int count2 = colCh - 1;
		int countOffset2 = count - 1;
		for (int ordinal4 = ordinal3; ordinal4 <= count2; ordinal4++)
		{
			double submergenceRatio = (double)(ordinal4 - ordinal3) / (double)(count2 - ordinal3);
			double station = minStation + submergenceRatio * (maxStation - minStation);
			double submergenceRatio2 = (Sample(station) - min) / (max - min);
			int ordinal5 = (int)((1.0 - submergenceRatio2) * (double)countOffset2);
			charValues[ordinal5, ordinal4] = 'o';
		}
		StringBuilder stringBuilder = new StringBuilder();
		for (int intervalIndex3 = 0; intervalIndex3 < rowCh; intervalIndex3++)
		{
			for (int intervalIndex4 = 0; intervalIndex4 < colCh; intervalIndex4++)
			{
				stringBuilder.Append(charValues[intervalIndex3, intervalIndex4]);
			}
			stringBuilder.AppendLine();
		}
		return stringBuilder.ToString();
	}
}
