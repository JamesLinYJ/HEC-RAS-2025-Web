using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Utility.Systems;

namespace RasMapperLib;

[ComVisible(false)]
public class Profile : Point2DCollection
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__26_002D0
	{
		public int thisIdx;

		public Profile other;

		public int otherIdx;

		public Profile newPf;

		public byte mostRecentInjectionState;

		public Func<SegmentM, Point2D, bool> pointOnCorrectSide;

		public Action findInjectMRUIntersection;

		public Func<float, float, bool> swapLTGT;

		public Profile profile;

		public _Closure_0024__26_002D0(_Closure_0024__26_002D0 other)
		{
			if (other != null)
			{
				thisIdx = other.thisIdx;
				this.other = other.other;
				otherIdx = other.otherIdx;
				newPf = other.newPf;
				mostRecentInjectionState = other.mostRecentInjectionState;
				pointOnCorrectSide = other.pointOnCorrectSide;
				findInjectMRUIntersection = other.findInjectMRUIntersection;
				swapLTGT = other.swapLTGT;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__4()
		{
			checked
			{
				SegmentM segmentM = new SegmentM(profile[thisIdx - 1], profile[thisIdx]);
				SegmentM seg = new SegmentM(other[otherIdx - 1], other[otherIdx]);
				double x = default;
				double y = default;
				double t = default;
				double t2 = default;
				if (segmentM.IntersectPoint(seg, ref x, ref y, ref t, ref t2))
				{
					newPf.Add(new Point2D((float)x, (float)y));
				}
			}
		}

		[SpecialName]
		internal void _Lambda_0024__5(Profile aProfile, Profile bProfile, int aIndex, int bIndex, byte aState)
		{
			Point2D point2D = aProfile[aIndex];
			if (bIndex == 0)
			{
				newPf.Add(point2D);
				mostRecentInjectionState = aState;
				return;
			}
			Point2D pB = bProfile[bIndex];
			checked
			{
				Point2D pA = bProfile[bIndex - 1];
				SegmentM segmentM2 = new SegmentM(pA, pB);
				if (aIndex == 0)
				{
					if (pointOnCorrectSide(segmentM2, point2D))
					{
						newPf.Add(point2D);
						mostRecentInjectionState = aState;
					}
					return;
				}
				Point2D point2D2 = aProfile[aIndex - 1];
				SegmentM segmentM = new SegmentM(point2D2, point2D);
				newPf.Last();
				if (mostRecentInjectionState == aState)
				{
					if (pointOnCorrectSide(segmentM2, point2D))
					{
						newPf.Add(point2D);
						mostRecentInjectionState = aState;
					}
					else
					{
						findInjectMRUIntersection();
						mostRecentInjectionState = 2;
					}
				}
				else if (mostRecentInjectionState == 2)
				{
					if (pointOnCorrectSide(segmentM2, point2D))
					{
						if (!pointOnCorrectSide(segmentM2, point2D2))
						{
							findInjectMRUIntersection();
						}
						newPf.Add(point2D);
						mostRecentInjectionState = aState;
					}
				}
				else if (mostRecentInjectionState == 3)
				{
					double slope = segmentM.Slope();
					double slope2 = segmentM2.Slope();
					if (swapLTGT((float)slope, (float)slope2))
					{
						newPf.Add(point2D);
						mostRecentInjectionState = aState;
					}
					else
					{
						mostRecentInjectionState = 2;
					}
				}
				else if (pointOnCorrectSide(segmentM2, point2D))
				{
					findInjectMRUIntersection();
					newPf.Add(point2D);
					mostRecentInjectionState = aState;
				}
			}
		}
	}

	public Profile(string XUnits, string YUnits)
		: base("Station", XUnits, "Elevation", YUnits)
	{
	}

	public Profile(string XUnits, string YUnits, string spaceAndCommaDelimetedData)
		: base("Station", XUnits, "Elevation", YUnits, spaceAndCommaDelimetedData)
	{
	}

	public Profile()
	{
	}

	public Profile(int capacity)
		: base(capacity)
	{
	}

	public float MinElevation()
	{
		return MinY();
	}

	public float MaxElevation()
	{
		return MaxY();
	}

	public float Length()
	{
		if (Count == 0)
		{
			return 0f;
		}
		return LastStation() - FirstStation();
	}

	public float FirstStation()
	{
		return base[0].X;
	}

	public float LastStation()
	{
		return base[checked(Count - 1)].X;
	}

	public bool CopyRange(float minX, float maxX, Profile profile)
	{
		int count = profile.Count;
		checked
		{
			if (Count > 0)
			{
				if (base[0].X >= minX)
				{
					profile.Add(base[0]);
				}
				int count2 = Count - 1;
				for (int i = 1; i <= count2; i++)
				{
					if (base[i - 1].X < minX && base[i].X > minX)
					{
						float computed = base[i].Y - base[i - 1].Y;
						float computed2 = base[i].X - base[i - 1].X;
						float y = base[i - 1].Y + computed / computed2 * (minX - base[i - 1].X);
						profile.Add(minX, y);
					}
					else if (base[i].X >= minX && base[i].X <= maxX)
					{
						profile.Add(base[i]);
					}
					else if (base[i].X > maxX)
					{
						if (base[i - 1].X < maxX)
						{
							float computed3 = base[i].Y - base[i - 1].Y;
							float computed4 = base[i].X - base[i - 1].X;
							float y2 = base[i - 1].Y + computed3 / computed4 * (maxX - base[i - 1].X);
							profile.Add(maxX, y2);
						}
						break;
					}
				}
			}
			return profile.Count > count;
		}
	}

	public Profile Clone()
	{
		Profile profile = new Profile(XUnits, YUnits);
		profile.Capacity = Count;
		using Enumerator itemEnumerator = GetEnumerator();
		while (itemEnumerator.MoveNext())
		{
			Point2D current = itemEnumerator.Current;
			profile.Add(current);
		}
		return profile;
	}

	public Profile CloneDedupe(double minDXDY = 1E-06)
	{
		Profile profile = new Profile(XUnits, YUnits);
		profile.Capacity = Count;
		Point2D point2D = new Point2D(float.NegativeInfinity, float.NegativeInfinity);
		using Enumerator itemEnumerator = GetEnumerator();
		while (itemEnumerator.MoveNext())
		{
			Point2D current = itemEnumerator.Current;
			if ((double)Math.Abs(current.X - point2D.X) > minDXDY || (double)Math.Abs(current.Y - point2D.Y) > minDXDY)
			{
				profile.Add(current);
			}
			point2D = current;
		}
		return profile;
	}

	public Polyline AsPolyline()
	{
		PointMs pointMs = new PointMs();
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				pointMs.Add(base[i].ToPointM());
			}
			return new Polyline(pointMs);
		}
	}

	public void Stream(BinaryWriter bw)
	{
		checked
		{
			float[] floatValues = new float[Count * 2 - 1 + 1];
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				floatValues[i * 2] = base[i].X;
				floatValues[i * 2 + 1] = base[i].Y;
			}
			Pipe.WriteArray(bw, floatValues);
		}
	}

	public static Profile Stream(BinaryReader br)
	{
		float[] readArray = Pipe.ReadArray<float>(br);
		if (readArray.Count() == 0)
		{
			return null;
		}
		checked
		{
			Point2D[] point2Ds = new Point2D[(int)Math.Round((double)readArray.Count() / 2.0) - 1 + 1];
			int count = point2Ds.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				point2Ds[i] = new Point2D(readArray[i * 2], readArray[i * 2 + 1]);
			}
			Profile profile = new Profile(point2Ds.Count());
			profile.AddRange(point2Ds);
			return profile;
		}
	}

	public void SortByStation()
	{
		SortX();
	}

	public void AdjustStationing(float dx)
	{
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				base[i] = new Point2D(base[i].X + dx, base[i].Y);
			}
		}
	}

	public void AdjustToZeroStationing()
	{
		float x = this.First().X;
		if (x != 0f)
		{
			AdjustStationing(-1f * x);
		}
	}

	public float FillArea(float elevation)
	{
		float computed = 0f;
		checked
		{
			int count = Count - 1;
			for (int i = 1; i <= count; i++)
			{
				Point2D point2D = base[i - 1];
				Point2D point2D2 = base[i];
				if (!point2D.IsUndefined() && !point2D2.IsUndefined())
				{
					float point2D2Point2DTerm = point2D2.X - point2D.X;
					float elevationPoint2DTerm = elevation - point2D.Y;
					float elevationPoint2D2Term = elevation - point2D2.Y;
					if ((double)elevationPoint2DTerm > 0.0 && (double)elevationPoint2D2Term > 0.0)
					{
						computed += point2D2Point2DTerm * 0.5f * (elevationPoint2DTerm + elevationPoint2D2Term);
					}
					else if ((double)elevationPoint2DTerm > 0.0)
					{
						computed += elevationPoint2DTerm * 0.5f * point2D2Point2DTerm * elevationPoint2DTerm / (point2D2.Y - point2D.Y);
					}
					else if ((double)elevationPoint2D2Term > 0.0)
					{
						computed += elevationPoint2D2Term * 0.5f * point2D2Point2DTerm * elevationPoint2D2Term / (point2D.Y - point2D2.Y);
					}
				}
			}
			return computed;
		}
	}

	public float WettedPerimeter(float elevation)
	{
		double segmentMElevationTerm = 0.0;
		checked
		{
			int count = Count - 1;
			SegmentM segmentM = default;
			for (int i = 1; i <= count; i++)
			{
				if (base[i - 1].IsUndefined() || base[i].IsUndefined())
				{
					continue;
				}
				segmentM.A.Assign(base[i - 1].X, base[i - 1].Y);
				segmentM.B.Assign(base[i].X, base[i].Y);
				if (!(segmentM.MinY >= (double)elevation))
				{
					if (segmentM.MaxY < (double)elevation)
					{
						segmentMElevationTerm += segmentM.Length;
						continue;
					}
					double px = segmentM.InterpolatedX(elevation);
					segmentMElevationTerm = ((!(segmentM.A.Y < (double)elevation)) ? (segmentMElevationTerm + segmentM.B.DistanceTo(px, elevation)) : (segmentMElevationTerm + segmentM.A.DistanceTo(px, elevation)));
				}
			}
			return (float)segmentMElevationTerm;
		}
	}

	public Profile GetSubsectionProfileRespectBounds(float startStation, float endStation)
	{
		Profile profile = new Profile();
		if (startStation < FirstStation())
		{
			profile.Add(this.First());
		}
		else
		{
			profile.Add(new Point2D(startStation, Query(startStation)));
		}
		checked
		{
			int count = Count - 2;
			for (int i = 1; i <= count; i++)
			{
				if (base[i].X >= startStation && base[i].X <= endStation)
				{
					profile.Add(base[i]);
				}
			}
			if (endStation > LastStation())
			{
				profile.Add(this.Last());
			}
			else
			{
				profile.Add(new Point2D(endStation, Query(endStation)));
			}
			return profile;
		}
	}

	public float GetHighestElevInSubsection(float startStation, float endStation)
	{
		float minValue = float.MinValue;
		if (startStation >= FirstStation())
		{
			float query = Query(startStation);
			if (query > minValue)
			{
				minValue = query;
			}
		}
		if (endStation <= LastStation())
		{
			float query2 = Query(endStation);
			if (query2 > minValue)
			{
				minValue = query2;
			}
		}
		checked
		{
			int count = Count - 2;
			for (int i = 1; i <= count; i++)
			{
				if (base[i].X >= startStation && base[i].X <= endStation)
				{
					float y = base[i].Y;
					if (y > minValue)
					{
						minValue = y;
					}
				}
			}
			return minValue;
		}
	}

	public float GetLowestElevInSubsection(float startStation, float endStation)
	{
		float maxValue = float.MaxValue;
		if (startStation >= FirstStation())
		{
			float query = Query(startStation);
			if (query < maxValue)
			{
				maxValue = query;
			}
		}
		if (endStation <= LastStation())
		{
			float query2 = Query(endStation);
			if (query2 < maxValue)
			{
				maxValue = query2;
			}
		}
		checked
		{
			int count = Count - 2;
			for (int i = 1; i <= count; i++)
			{
				if (base[i].X >= startStation && base[i].X <= endStation)
				{
					float y = base[i].Y;
					if (y < maxValue)
					{
						maxValue = y;
					}
				}
			}
			return maxValue;
		}
	}

	public ProfileHydraulicPropertiesTable[,] ComputeSubSectionProperties(float[] elevations, float[] stations)
	{
		checked
		{
			ProfileHydraulicPropertiesTable[,] profileHydraulicPropertiesTables = new ProfileHydraulicPropertiesTable[elevations.Count() - 1 + 1, stations.Count() - 2 + 1];
			Profile profile = new Profile("", "");
			int count = stations.Count() - 2;
			for (int i = 0; i <= count; i++)
			{
				float station = stations[i];
				float station2 = stations[i + 1];
				profile.Clear();
				int count2 = Count - 2;
				for (int j = 0; j <= count2; j++)
				{
					Point2D point2D = base[j];
					Point2D point2D2 = base[j + 1];
					if (point2D.X > station2 || point2D2.X < station)
					{
						continue;
					}
					if (point2D.X == point2D2.X)
					{
						if (point2D.X == station)
						{
							if (point2D.Y > point2D2.Y)
							{
								profile.AddNewSegment(point2D, point2D2);
							}
						}
						else if (point2D.X == station2)
						{
							if (point2D.Y < point2D2.Y)
							{
								profile.AddNewSegment(point2D, point2D2);
							}
						}
						else
						{
							profile.AddNewSegment(point2D, point2D2);
						}
					}
					else if (point2D.X >= station && point2D2.X <= station2)
					{
						profile.AddNewSegment(point2D, point2D2);
					}
					else
					{
						float point2D2Point2DTerm = (point2D2.Y - point2D.Y) / (point2D2.X - point2D.X);
						if (point2D.X < station)
						{
							float y = point2D.Y + point2D2Point2DTerm * (station - point2D.X);
							profile.AddNew(station, y);
						}
						else
						{
							profile.AddNew(point2D);
						}
						if (point2D2.X > station2)
						{
							float y2 = point2D.Y + point2D2Point2DTerm * (station2 - point2D.X);
							profile.AddNew(station2, y2);
						}
						else
						{
							profile.AddNew(point2D2);
						}
					}
				}
				List<ProfileHydraulicPropertiesTable> profileHydraulicPropertiesTablesList = null;
				int count3 = elevations.Count() - 1;
				for (int k = 0; k <= count3; k++)
				{
					profileHydraulicPropertiesTables[k, i] = profileHydraulicPropertiesTablesList[k];
				}
			}
			return profileHydraulicPropertiesTables;
		}
	}

	public Profile MergeTakeHigher(Profile other)
	{
		return MergeTakeHL(other, takeHigher: true);
	}

	public Profile MergeTakeLower(Profile other)
	{
		return MergeTakeHL(other, takeHigher: false);
	}

	private Profile MergeTakeHL(Profile other, bool takeHigher)
	{
		_Closure_0024__26_002D0 capturedLocals = default;
		_Closure_0024__26_002D0 capturedLocals2 = new _Closure_0024__26_002D0(capturedLocals);
		capturedLocals2.profile = this;
		capturedLocals2.other = other;
		if (capturedLocals2.other == null)
		{
			return Clone();
		}
		if (capturedLocals2.other.Count < 2)
		{
			return Clone();
		}
		if (Count < 2)
		{
			return capturedLocals2.other.Clone();
		}
		capturedLocals2.swapLTGT = null;
		capturedLocals2.pointOnCorrectSide = null;
		if (takeHigher)
		{
			capturedLocals2.swapLTGT = [SpecialName] (float argA, float argB) => argA > argB;
			capturedLocals2.pointOnCorrectSide = [SpecialName] (SegmentM seg, Point2D pt) => seg.PointIsLeft(pt);
		}
		else
		{
			capturedLocals2.swapLTGT = [SpecialName] (float argA, float argB) => argA < argB;
			capturedLocals2.pointOnCorrectSide = [SpecialName] (SegmentM seg, Point2D pt) => seg.PointIsRight(pt);
		}
		capturedLocals2.newPf = new Profile();
		capturedLocals2.thisIdx = 0;
		capturedLocals2.otherIdx = 0;
		checked
		{
			if (this.First().X == capturedLocals2.other.First().X)
			{
				if (this.First().Y == capturedLocals2.other.First().Y)
				{
					capturedLocals2.newPf.Add(this.First());
					capturedLocals2.mostRecentInjectionState = 3;
				}
				else if (capturedLocals2.swapLTGT(this.First().Y, capturedLocals2.other.First().Y))
				{
					capturedLocals2.newPf.Add(this.First());
					capturedLocals2.mostRecentInjectionState = 0;
				}
				else
				{
					capturedLocals2.newPf.Add(capturedLocals2.other.First());
					capturedLocals2.mostRecentInjectionState = 1;
				}
				capturedLocals2.thisIdx++;
				capturedLocals2.otherIdx++;
			}
			else if (this.First().X < capturedLocals2.other.First().X)
			{
				capturedLocals2.newPf.Add(this.First());
				capturedLocals2.mostRecentInjectionState = 0;
				capturedLocals2.thisIdx++;
			}
			else
			{
				capturedLocals2.newPf.Add(capturedLocals2.other.First());
				capturedLocals2.mostRecentInjectionState = 1;
				capturedLocals2.otherIdx++;
			}
			capturedLocals2.findInjectMRUIntersection = [SpecialName] () =>
			{
				SegmentM segmentM = new SegmentM(capturedLocals2.profile[capturedLocals2.thisIdx - 1], capturedLocals2.profile[capturedLocals2.thisIdx]);
				SegmentM seg = new SegmentM(capturedLocals2.other[capturedLocals2.otherIdx - 1], capturedLocals2.other[capturedLocals2.otherIdx]);
				double x = default;
				double y = default;
				double t = default;
				double t2 = default;
				if (segmentM.IntersectPoint(seg, ref x, ref y, ref t, ref t2))
				{
					capturedLocals2.newPf.Add(new Point2D((float)x, (float)y));
				}
			};
			Action<Profile, Profile, int, int, byte> action = [SpecialName] (Profile aProfile, Profile bProfile, int aIndex, int bIndex, byte aState) =>
			{
				Point2D point2D2 = aProfile[aIndex];
				if (bIndex == 0)
				{
					capturedLocals2.newPf.Add(point2D2);
					capturedLocals2.mostRecentInjectionState = aState;
				}
				else
				{
					Point2D pB = bProfile[bIndex];
					Point2D pA = bProfile[bIndex - 1];
					SegmentM segmentM2 = new SegmentM(pA, pB);
					if (aIndex == 0)
					{
						if (capturedLocals2.pointOnCorrectSide(segmentM2, point2D2))
						{
							capturedLocals2.newPf.Add(point2D2);
							capturedLocals2.mostRecentInjectionState = aState;
						}
					}
					else
					{
						Point2D point2D3 = aProfile[aIndex - 1];
						SegmentM segmentM = new SegmentM(point2D3, point2D2);
						capturedLocals2.newPf.Last();
						if (capturedLocals2.mostRecentInjectionState == aState)
						{
							if (capturedLocals2.pointOnCorrectSide(segmentM2, point2D2))
							{
								capturedLocals2.newPf.Add(point2D2);
								capturedLocals2.mostRecentInjectionState = aState;
							}
							else
							{
								capturedLocals2.findInjectMRUIntersection();
								capturedLocals2.mostRecentInjectionState = 2;
							}
						}
						else if (capturedLocals2.mostRecentInjectionState == 2)
						{
							if (capturedLocals2.pointOnCorrectSide(segmentM2, point2D2))
							{
								if (!capturedLocals2.pointOnCorrectSide(segmentM2, point2D3))
								{
									capturedLocals2.findInjectMRUIntersection();
								}
								capturedLocals2.newPf.Add(point2D2);
								capturedLocals2.mostRecentInjectionState = aState;
							}
						}
						else if (capturedLocals2.mostRecentInjectionState == 3)
						{
							double slope = segmentM.Slope();
							double slope2 = segmentM2.Slope();
							if (capturedLocals2.swapLTGT((float)slope, (float)slope2))
							{
								capturedLocals2.newPf.Add(point2D2);
								capturedLocals2.mostRecentInjectionState = aState;
							}
							else
							{
								capturedLocals2.mostRecentInjectionState = 2;
							}
						}
						else if (capturedLocals2.pointOnCorrectSide(segmentM2, point2D2))
						{
							capturedLocals2.findInjectMRUIntersection();
							capturedLocals2.newPf.Add(point2D2);
							capturedLocals2.mostRecentInjectionState = aState;
						}
					}
				}
			};
			while (true)
			{
				if (capturedLocals2.thisIdx >= Count)
				{
					capturedLocals2.newPf.AddRange(capturedLocals2.other.GetRange(capturedLocals2.otherIdx, capturedLocals2.other.Count - capturedLocals2.otherIdx));
					break;
				}
				if (capturedLocals2.otherIdx >= capturedLocals2.other.Count)
				{
					capturedLocals2.newPf.AddRange(GetRange(capturedLocals2.thisIdx, Count - capturedLocals2.thisIdx));
					break;
				}
				Point2D point2D4 = base[capturedLocals2.thisIdx];
				Point2D point2D = capturedLocals2.other[capturedLocals2.otherIdx];
				if (point2D4.X == point2D.X)
				{
					if (point2D4.Y == point2D.Y)
					{
						capturedLocals2.mostRecentInjectionState = 3;
						capturedLocals2.newPf.Add(point2D4);
					}
					else if (capturedLocals2.swapLTGT(point2D4.Y, point2D.Y))
					{
						action(this, capturedLocals2.other, capturedLocals2.thisIdx, capturedLocals2.otherIdx, 0);
					}
					else
					{
						action(capturedLocals2.other, this, capturedLocals2.otherIdx, capturedLocals2.thisIdx, 1);
					}
					capturedLocals2.thisIdx++;
					capturedLocals2.otherIdx++;
				}
				else if (point2D4.X < point2D.X)
				{
					action(this, capturedLocals2.other, capturedLocals2.thisIdx, capturedLocals2.otherIdx, 0);
					capturedLocals2.thisIdx++;
				}
				else
				{
					action(capturedLocals2.other, this, capturedLocals2.otherIdx, capturedLocals2.thisIdx, 1);
					capturedLocals2.otherIdx++;
				}
			}
			return capturedLocals2.newPf;
		}
	}

	public Profile MergeOverrideWith(Profile other)
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
		Profile profile = new Profile();
		checked
		{
			int i;
			for (i = 0; base[i].X < other.First().X; i++)
			{
				profile.Add(base[i]);
			}
			profile.AddRange(other);
			int ordinal = i;
			int count = Count - 1;
			for (int j = ordinal; j <= count; j++)
			{
				Point2D point2D = base[j];
				if (point2D.X > other.Last().X)
				{
					profile.Add(point2D);
				}
			}
			return profile;
		}
	}

	public Profile MergeAdd(Profile other)
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
		Profile profile = new Profile();
		int i = 0;
		int ordinal = 0;
		checked
		{
			for (; base[i].X < other.First().X; i++)
			{
				profile.Add(base[i]);
			}
			bool beforeStart = default;
			bool exceededFinal = default;
			bool beforeStart2 = default;
			bool exceededFinal2 = default;
			while (i != Count)
			{
				Point2D point2D3 = base[i];
				if (ordinal == other.Count)
				{
					Point2D point2D = other[ordinal - 1];
					if (point2D.X + 0.01f < point2D3.X && i != 0)
					{
						float point2DTerm = point2D.X + 0.01f;
						profile.Add(new Point2D(point2DTerm, Query(point2DTerm)));
					}
					int ordinal2 = i;
					int count = Count - 1;
					for (int j = ordinal2; j <= count; j++)
					{
						profile.Add(base[j]);
					}
					break;
				}
				Point2D point2D2 = other[ordinal];
				if (point2D3.X == point2D2.X)
				{
					if (ordinal == 0 && i != 0)
					{
						float point2D3Term = Math.Min(0.01f, (point2D3.X - base[i - 1].X) / 2f);
						profile.Add(new Point2D(point2D3.X - point2D3Term, point2D3.Y));
					}
					profile.Add(new Point2D(point2D3.X, point2D3.Y + point2D2.Y));
					i++;
					ordinal++;
					continue;
				}
				if (point2D3.X < point2D2.X)
				{
					float query = other.Query(point2D3.X, ref beforeStart, ref exceededFinal);
					if (beforeStart || exceededFinal)
					{
						profile.Add(point2D3);
					}
					else
					{
						profile.Add(new Point2D(point2D3.X, point2D3.Y + query));
					}
					i++;
					continue;
				}
				if (ordinal == 0 && i != 0)
				{
					float point2D3Term2 = Math.Min(0.01f, (point2D3.X - base[i - 1].X) / 2f);
					profile.Add(new Point2D(point2D2.X - point2D3Term2, point2D3.Y));
				}
				float query2 = Query(point2D2.X, ref beforeStart2, ref exceededFinal2);
				if (!beforeStart2 && !exceededFinal2)
				{
					profile.Add(new Point2D(point2D2.X, point2D2.Y + query2));
				}
				ordinal++;
			}
			return profile;
		}
	}

	public void InjectPointsBetweenStations(float startStation, float endStation, bool removeCoveredPoints)
	{
		float computed = Length() / 1000f;
		if (startStation > endStation)
		{
			float startStation2 = startStation;
			startStation = endStation;
			endStation = startStation2;
		}
		int count = Count;
		int count2 = Count;
		checked
		{
			int count3 = Count - 1;
			for (int i = 0; i <= count3; i++)
			{
				Point2D point2D = base[i];
				if (count == Count && point2D.X >= startStation)
				{
					count = i;
					if (endStation == Length())
					{
						break;
					}
				}
				if (point2D.X >= endStation)
				{
					count2 = i;
					break;
				}
			}
			float naN = float.NaN;
			if (count != 0)
			{
				naN = Query(startStation - computed);
			}
			float naN2 = float.NaN;
			if (count2 != Count)
			{
				naN2 = Query(endStation + computed);
			}
			if (removeCoveredPoints && count2 > count)
			{
				RemoveRange(count, count2 - count);
			}
			if (!float.IsNaN(naN))
			{
				Add(new Point2D(startStation - computed, naN));
			}
			if (!float.IsNaN(naN2))
			{
				Add(new Point2D(endStation + computed, naN2));
			}
			SortByStation();
		}
	}

	public static List<Profile> FromInfoValues(float[,] ptsXY, int[] starts, int[] counts)
	{
		if (ptsXY == null || ptsXY.GetLength(1) < 2 || starts == null || counts == null || starts.Length != counts.Length)
		{
			throw new ArgumentException();
		}
		int startLength = starts.Length;
		List<Profile> profiles = new List<Profile>(startLength);
		checked
		{
			int count = startLength - 1;
			for (int i = 0; i <= count; i++)
			{
				int start = starts[i];
				int count2 = counts[i];
				if (start < 0 || count2 <= 0)
				{
					profiles.Add(new Profile());
					continue;
				}
				Profile profile = new Profile(count2);
				int count3 = start + count2 - 1;
				for (int j = start; j <= count3; j++)
				{
					profile.Add(new Point2D(ptsXY[j, 0], ptsXY[j, 1]));
				}
				profiles.Add(profile);
			}
			return profiles;
		}
	}

	public static void ToInfoValues(IList<Profile> profiles, ref int[] starts, ref int[] counts, ref float[,] ptsXY)
	{
		int sum = profiles.Sum([SpecialName] (Profile pf) => pf?.Count ?? 0);
		checked
		{
			ptsXY = new float[sum - 1 + 1, 2];
			starts = new int[profiles.Count - 1 + 1];
			counts = new int[profiles.Count - 1 + 1];
			int profileCountSum = 0;
			int count = profiles.Count - 1;
			for (int intervalIndex = 0; intervalIndex <= count; intervalIndex++)
			{
				Profile profile = profiles[intervalIndex];
				if (profile == null)
				{
					starts[intervalIndex] = profileCountSum;
					counts[intervalIndex] = 0;
					continue;
				}
				starts[intervalIndex] = profileCountSum;
				counts[intervalIndex] = profile.Count;
				int count2 = profile.Count - 1;
				for (int intervalIndex2 = 0; intervalIndex2 <= count2; intervalIndex2++)
				{
					ptsXY[intervalIndex2 + profileCountSum, 0] = profile[intervalIndex2].X;
					ptsXY[intervalIndex2 + profileCountSum, 1] = profile[intervalIndex2].Y;
				}
				profileCountSum += profile.Count;
			}
		}
	}

	public static void AppendInfoValues(Profile profile, List<int> starts, List<int> counts, List<float[,]> ptList)
	{
		checked
		{
			starts.Add(starts.LastOrDefault() + counts.LastOrDefault());
			if (profile == null || profile.Count == 0)
			{
				counts.Add(0);
				float[,] elements = new float[0, 2];
				ptList.Add(elements);
				return;
			}
			counts.Add(profile.Count);
			float[,] floatValues = new float[profile.Count - 1 + 1, 2];
			int count = profile.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Point2D point2D = profile[i];
				floatValues[i, 0] = point2D.X;
				floatValues[i, 1] = point2D.Y;
			}
			ptList.Add(floatValues);
		}
	}

	public static float[,] MergePointList(List<float[,]> ptList)
	{
		checked
		{
			float[,] floatValues = new float[ptList.Sum([SpecialName] (float[,] buf) => buf.GetLength(0)) - 1 + 1, 2];
			int scaledLengthSum = 0;
			int count = ptList.Count - 1;
			for (int intervalIndex = 0; intervalIndex <= count; intervalIndex++)
			{
				float[,] ptList2 = ptList[intervalIndex];
				if (ptList2.GetLength(0) != 0)
				{
					int scaledLength = ptList2.Length * 4;
					Buffer.BlockCopy(ptList2, 0, floatValues, scaledLengthSum, scaledLength);
					scaledLengthSum += scaledLength;
				}
			}
			return floatValues;
		}
	}

	public bool ValidateData(double invalidDeltaX = 0.0)
	{
		if (Count == 0)
		{
			return true;
		}
		double minValue = double.MinValue;
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Point2D point2D = base[i];
				if (double.IsNaN(point2D.X) || double.IsNaN(point2D.Y))
				{
					return false;
				}
				if ((double)point2D.X <= minValue + invalidDeltaX)
				{
					return false;
				}
				minValue = point2D.X;
			}
			return true;
		}
	}

	public new Profile Filter(float filterTol, List<int> indexBuffer = null)
	{
		List<int> indexBufferList = ((indexBuffer != null) ? indexBuffer : new List<int>());
		FilterKeepList(filterTol, indexBufferList);
		Profile profile = new Profile(XUnits, YUnits);
		profile.Capacity = indexBufferList.Count;
		checked
		{
			int count = indexBufferList.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				profile.Add(base[indexBufferList[i]]);
			}
			return profile;
		}
	}

	public Profile FilterXYIterative(int iterationCount, int targetPointCount, double minDXDY = 1E-06)
	{
		Profile profile = CloneDedupe(minDXDY);
		if (profile.Count <= targetPointCount)
		{
			return profile;
		}
		float unity = 1f;
		Profile profile2 = null;
		Profile profile3 = Filter(unity);
		if (profile3.Count == targetPointCount)
		{
			return profile3;
		}
		checked
		{
			float computed;
			float computed2;
			if (profile3.Count < targetPointCount)
			{
				computed = unity;
				profile2 = profile3;
				float computed3 = unity;
				int ordinal = 0;
				while (true)
				{
					computed3 /= 2f;
					Profile profile4 = profile.Filter(computed3);
					if (profile4.Count == targetPointCount)
					{
						return profile4;
					}
					if (profile4.Count >= targetPointCount)
					{
						break;
					}
					computed = computed3;
					profile2 = profile4;
					ordinal++;
					if (ordinal == 100)
					{
						return profile;
					}
				}
				computed2 = computed3;
			}
			else
			{
				computed2 = unity;
				float computed4 = unity;
				int ordinal2 = 0;
				Profile profile5;
				while (true)
				{
					computed4 *= 2f;
					profile5 = profile.Filter(computed4);
					if (profile5.Count == targetPointCount)
					{
						return profile5;
					}
					if (profile5.Count <= targetPointCount)
					{
						break;
					}
					computed2 = computed4;
					ordinal2++;
					if (ordinal2 == 100)
					{
						return profile.Clone();
					}
				}
				computed = computed4;
				profile2 = profile5;
			}
			int count = iterationCount - 1;
			for (int i = 0; i <= count; i++)
			{
				float computed5 = (computed2 + computed) / 2f;
				Profile profile6 = profile.Filter(computed5);
				if (profile6.Count == targetPointCount)
				{
					return profile6;
				}
				if (profile6.Count > targetPointCount)
				{
					computed2 = computed5;
					continue;
				}
				computed = computed5;
				profile2 = profile6;
			}
			return profile2;
		}
	}

	public Profile FilterByArea(int maxPoints, double minDXDY = 1E-06, List<int> keepPoints = null)
	{
		if (keepPoints == null)
		{
			keepPoints = new List<int>();
		}
		Profile profile = CloneDedupe(minDXDY);
		List<double> doubleList = new List<double>();
		checked
		{
			int count = profile.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				double area = GetArea(i, profile, keepPoints);
				doubleList.Add(area);
			}
			while (profile.Count > maxPoints)
			{
				int intervalIndex = 0;
				int count2 = profile.Count - 1;
				for (int j = 1; j <= count2; j++)
				{
					if (doubleList[j] < doubleList[intervalIndex])
					{
						intervalIndex = j;
					}
				}
				profile.RemoveAt(intervalIndex);
				doubleList.RemoveAt(intervalIndex);
				if (doubleList[intervalIndex - 1] < double.MaxValue)
				{
					doubleList[intervalIndex - 1] = GetArea(intervalIndex - 1, profile);
				}
				if (doubleList[intervalIndex] < double.MaxValue)
				{
					doubleList[intervalIndex] = GetArea(intervalIndex, profile);
				}
			}
			return profile;
		}
	}

	private double GetArea(int i, Profile pf, List<int> keepPoints = null)
	{
		if (keepPoints == null)
		{
			keepPoints = new List<int>();
		}
		checked
		{
			if (i == 0 || i == pf.Count - 1)
			{
				return double.MaxValue;
			}
			if (keepPoints.Contains(i))
			{
				return double.MaxValue;
			}
			return Math.Abs((pf[i - 1].Y * pf[i].X + pf[i].Y * pf[i + 1].X + pf[i + 1].Y * pf[i - 1].X - (pf[i - 1].X * pf[i].Y + pf[i].X * pf[i + 1].Y + pf[i + 1].X * pf[i - 1].Y)) / 2f);
		}
	}

	public Profile AccumulateCDF()
	{
		Profile profile = new Profile(Count);
		double computed = 0.0;
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				computed += (double)base[i].Y;
				profile.Add(base[i].X, computed);
			}
			return profile;
		}
	}

	public Profile RoundData(int xDP, int yDP)
	{
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				AssignX(i, (float)Math.Round(base[i].X, xDP));
				AssignY(i, (float)Math.Round(base[i].Y, yDP));
			}
			return this;
		}
	}
}
