using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.VisualBasic.CompilerServices;
using Utility;
using Utility.Extensions;
using Utility.Systems;

namespace RasMapperLib;

public class Polygon : Polyline, IAreaGeometry
{
	public class PseudoPriorityQueue
	{
		[CompilerGenerated]
		internal sealed class _Closure_0024__5_002D0
		{
			public double capturedValue;

			public int idx;

			public PseudoPriorityQueue pseudoPriorityQueue;

			public _Closure_0024__5_002D0(_Closure_0024__5_002D0 other)
			{
				if (other != null)
				{
					capturedValue = other.capturedValue;
					idx = other.idx;
				}
			}

			[SpecialName]
			internal void _Lambda_0024__0(int start, int last)
			{
				for (int i = start; i <= last; i = checked(i + 1))
				{
					double computed = pseudoPriorityQueue.Values[i];
					if (capturedValue < computed)
					{
						pseudoPriorityQueue.InsertAndPushBack(i, idx, capturedValue);
						break;
					}
				}
			}
		}

		public int LastValidIndex;

		public int[] Indexes;

		public double[] Values;

		public PseudoPriorityQueue(IList<double> data = null, int sz = 100)
		{
			LastValidIndex = -1;
			if (sz < 16)
			{
				sz = 16;
			}
			checked
			{
				Indexes = new int[sz - 1 + 1];
				Values = new double[sz - 1 + 1];
				Regenerate(data);
			}
		}

		public void Regenerate(IList<double> data = null)
		{
			LastValidIndex = -1;
			checked
			{
				int count = Indexes.Length - 1;
				for (int i = 0; i <= count; i++)
				{
					Indexes[i] = -1;
					Values[i] = double.MaxValue;
				}
				if (data != null)
				{
					int count2 = data.Count - 1;
					for (int j = 0; j <= count2; j++)
					{
						TryAdd(j, data[j]);
					}
				}
			}
		}

		public void TryAdd(int idx, double doubleValue)
		{
			_Closure_0024__5_002D0 capturedLocals = default;
			_Closure_0024__5_002D0 capturedLocals2 = new _Closure_0024__5_002D0(capturedLocals);
			capturedLocals2.pseudoPriorityQueue = this;
			capturedLocals2.idx = idx;
			capturedLocals2.capturedValue = doubleValue;
			if (LastValidIndex == -1)
			{
				Indexes[0] = capturedLocals2.idx;
				Values[0] = capturedLocals2.capturedValue;
				LastValidIndex = 0;
				return;
			}
			Action<int, int> action;
			checked
			{
				if (capturedLocals2.capturedValue > Values[LastValidIndex])
				{
					if (LastValidIndex != Values.Count() - 1)
					{
						LastValidIndex++;
						Indexes[LastValidIndex] = capturedLocals2.idx;
						Values[LastValidIndex] = capturedLocals2.capturedValue;
					}
					return;
				}
				action = [SpecialName] (int start, int last) =>
				{
					for (int i = start; i <= last; i++)
					{
						double capturedLocals3 = capturedLocals2.pseudoPriorityQueue.Values[i];
						if (capturedLocals2.capturedValue < capturedLocals3)
						{
							capturedLocals2.pseudoPriorityQueue.InsertAndPushBack(i, capturedLocals2.idx, capturedLocals2.capturedValue);
							break;
						}
					}
				};
				if (LastValidIndex < 8)
				{
					action(0, LastValidIndex);
					return;
				}
			}
			int scaledLastValidIndex = LastValidIndex / 2;
			int ordinal = scaledLastValidIndex / 2;
			checked
			{
				while (true)
				{
					if (scaledLastValidIndex <= 0 || ordinal <= 4)
					{
						if (scaledLastValidIndex < 0)
						{
							scaledLastValidIndex = 0;
						}
						int ordinal2 = ordinal * 2 + 1;
						int ordinal3 = scaledLastValidIndex - ordinal2;
						if (ordinal3 < 0)
						{
							ordinal3 = 0;
						}
						int lastValidIndexTerm = scaledLastValidIndex + ordinal2;
						if (lastValidIndexTerm > LastValidIndex)
						{
							lastValidIndexTerm = LastValidIndex;
						}
						action(ordinal3, lastValidIndexTerm);
						return;
					}
					double computed = Values[scaledLastValidIndex];
					if (capturedLocals2.capturedValue == computed)
					{
						InsertAndPushBack(scaledLastValidIndex, capturedLocals2.idx, capturedLocals2.capturedValue);
						return;
					}
					if (capturedLocals2.capturedValue < computed)
					{
						double computed2 = Values[scaledLastValidIndex - 1];
						if (capturedLocals2.capturedValue >= computed2)
						{
							break;
						}
						scaledLastValidIndex -= ordinal;
						ordinal = unchecked(ordinal / 2);
					}
					else
					{
						scaledLastValidIndex += ordinal;
						ordinal = unchecked(ordinal / 2);
					}
				}
				InsertAndPushBack(scaledLastValidIndex, capturedLocals2.idx, capturedLocals2.capturedValue);
			}
		}

		public int TakeMin()
		{
			if (LastValidIndex == -1)
			{
				return -1;
			}
			int index = Indexes[0];
			int lastValidIndex = LastValidIndex;
			checked
			{
				for (int i = 1; i <= lastValidIndex; i++)
				{
					int index2 = Indexes[i];
					if (index2 > index)
					{
						index2--;
					}
					Indexes[i - 1] = index2;
					Values[i - 1] = Values[i];
				}
				LastValidIndex--;
				return index;
			}
		}

		public void NotifyValueChanged(int idx, double newValue)
		{
			bool condition = false;
			int lastValidIndex = LastValidIndex;
			for (int i = 0; i <= lastValidIndex; i = checked(i + 1))
			{
				if (Indexes[i] == idx)
				{
					RemoveAndPull(idx);
					condition = true;
					return;
				}
			}
			if (condition && LastValidIndex > 0)
			{
				double lastValidIndexTerm = Values[LastValidIndex];
				if (newValue < lastValidIndexTerm)
				{
					TryAdd(idx, newValue);
				}
			}
		}

		private void RemoveAndPull(int localIdx)
		{
			checked
			{
				int localIdxOffset = localIdx + 1;
				int lastValidIndex = LastValidIndex;
				for (int i = localIdxOffset; i <= lastValidIndex; i++)
				{
					Indexes[i - 1] = Indexes[i];
					Values[i - 1] = Values[i];
				}
				LastValidIndex--;
			}
		}

		private void InsertAndPushBack(int localIdx, int pushIdx, double computed)
		{
			int pushIdx2 = pushIdx;
			double computed2 = computed;
			checked
			{
				int lastValidIndexOffset = LastValidIndex + 1;
				int localIdx2 = localIdx;
				while (true)
				{
					if (localIdx2 <= lastValidIndexOffset)
					{
						if (localIdx2 < Indexes.Length)
						{
							int index = Indexes[localIdx2];
							double localIdx2Term = Values[localIdx2];
							Indexes[localIdx2] = pushIdx2;
							Values[localIdx2] = computed2;
							pushIdx2 = index;
							computed2 = localIdx2Term;
							localIdx2++;
							continue;
						}
						break;
					}
					LastValidIndex++;
					break;
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__51_002D0
	{
		public Polyline pline;

		public _Closure_0024__51_002D0(_Closure_0024__51_002D0 other)
		{
			if (other != null)
			{
				pline = other.pline;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__1(PointOnPolyline pop)
		{
			return pop.PointM.EqualXY(pline.FirstPointM());
		}

		[SpecialName]
		internal bool _Lambda_0024__2(PointOnPolyline pop)
		{
			return pop.PointM.EqualXY(pline.LastPointM());
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__52_002D0
	{
		public Polyline polyline;

		public _Closure_0024__52_002D0(_Closure_0024__52_002D0 other)
		{
			if (other != null)
			{
				polyline = other.polyline;
			}
		}

		[SpecialName]
		internal double _Lambda_0024__0(PointIntersect pi)
		{
			return polyline.DistanceAlong(pi.BPointOnPolyline);
		}
	}

	private double _area;

	private double[] _partArea;

	private Point _centroid;

	private DistinctSpatialIndex _partSpIndex;

	private static Polygon _nullPolygon = new Polygon(new PointMs());

	public override bool UsingQuadTree => false;

	public double Area
	{
		get
		{
			checked
			{
				if (double.IsNaN(_area))
				{
					_area = 0.0;
					double minY = MinY;
					int count = PartsCount() - 1;
					for (int i = 0; i <= count; i++)
					{
						double computed = 0.0;
						int count2 = SegmentCount() - 1;
						for (int j = 0; j <= count2; j++)
						{
							SegmentM segmentM = Segment(j);
							double deltaX = segmentM.DeltaX();
							double segmentMMinYTerm = segmentM.A.Y - minY;
							double segmentMMinYTerm2 = segmentM.B.Y - minY;
							computed += (segmentMMinYTerm + segmentMMinYTerm2) * deltaX;
						}
						computed *= 0.5;
						_area += computed;
					}
				}
				return _area;
			}
		}
	}

	public override bool UsesPointSymbol { get; }

	public override bool UsesPen { get; }

	public override bool UsesBrush { get; }

	public override bool UsesSurfaceFill { get; }

	public static Polygon NullPolygon()
	{
		return _nullPolygon;
	}

	protected Polygon()
	{
		_area = double.NaN;
		_centroid = null;
		_partSpIndex = null;
		UsesPointSymbol = false;
		UsesPen = true;
		UsesBrush = true;
		UsesSurfaceFill = false;
	}

	public Polygon(PointMs pointMs)
		: this(pointMs, null)
	{
	}

	protected internal Polygon(PointMs pointMs, IList<int> partStartIndex)
		: base(EnsureFirstPointEqualsLast(pointMs, partStartIndex), partStartIndex)
	{
		_area = double.NaN;
		_centroid = null;
		_partSpIndex = null;
		UsesPointSymbol = false;
		UsesPen = true;
		UsesBrush = true;
		UsesSurfaceFill = false;
		Initialize();
	}

	public Polygon(PointMs pointMs, int[] partStartIndex)
		: base(EnsureFirstPointEqualsLast(pointMs, partStartIndex), partStartIndex)
	{
		_area = double.NaN;
		_centroid = null;
		_partSpIndex = null;
		UsesPointSymbol = false;
		UsesPen = true;
		UsesBrush = true;
		UsesSurfaceFill = false;
		Initialize();
	}

	public new void Stream(BinaryWriter bw)
	{
		checked
		{
			double[] doubleValues = new double[_pointM.Count * 2 - 1 + 1];
			int count = _pointM.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				doubleValues[i * 2] = _pointM[i].X;
				doubleValues[i * 2 + 1] = _pointM[i].Y;
			}
			Pipe.WriteArray(bw, doubleValues);
		}
	}

	public new static Polygon Stream(BinaryReader br)
	{
		double[] readArray = Pipe.ReadArray<double>(br);
		if (readArray.Count() == 0)
		{
			return null;
		}
		checked
		{
			PointM[] pointMs = new PointM[(int)Math.Round((double)readArray.Count() / 2.0) - 1 + 1];
			int count = pointMs.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				pointMs[i] = new PointM(readArray[i * 2], readArray[i * 2 + 1]);
			}
			return new Polygon(new PointMs(pointMs));
		}
	}

	public new static List<PlotOption> GetAvailablePlotOptions(bool debugMode)
	{
		return new List<PlotOption>(new PlotOption[3]
		{
			new PlotOption("Label Segment Indexes"),
			new PlotOption("Label Points"),
			new PlotOption("Draw Points")
		});
	}

	protected override void PointDataChanged()
	{
		base.PointDataChanged();
		_area = double.NaN;
		_centroid = null;
		_partSpIndex = null;
	}

	public static Polygon FromExtent(IExtent ext)
	{
		PointMs pointMs = new PointMs();
		pointMs.Add(ext.MaxX, ext.MaxY);
		pointMs.Add(ext.MinX, ext.MaxY);
		pointMs.Add(ext.MinX, ext.MinY);
		pointMs.Add(ext.MaxX, ext.MinY);
		return new Polygon(pointMs);
	}

	public new static Polygon FromBuffers(IList<PointM> points)
	{
		Polygon polygon = new Polygon(new PointMs());
		polygon.OverrideBuffersAndReinitialize(points);
		return polygon;
	}

	private void Initialize()
	{
		Array.Resize(ref _partArea, PartsCount());
		Buffers.ArraySetAllVB(_partArea, double.NaN);
		if (PartsCount() <= 1)
		{
			_ = Count;
			_ = 1;
		}
	}

	protected static IList<PointM> EnsureFirstPointEqualsLast(IList<PointM> pms, IList<int> partStartIndex = null)
	{
		if (pms.IsNullOrEmpty())
		{
			return pms;
		}
		if (partStartIndex == null || partStartIndex.Count <= 1)
		{
			PointM pointM2 = pms.First();
			PointM pointM = pms.Last();
			if (!pointM2.EqualXY(pointM))
			{
				pms = pms.ToList();
				pms.Add(pointM2);
			}
		}
		return pms;
	}

	protected static void MergePointMs(List<PointMs> ptMsList, ref PointMs mergedPtms, ref IList<int> partStartIndex)
	{
		int ptMsTerm = 0;
		foreach (PointMs ptMs in ptMsList)
		{
			if (ptMs.Count > 2)
			{
				if (!ptMs.First().Equal(ptMs.Last()))
				{
					ptMs.Add(ptMs.First());
				}
				ptMsTerm = checked(ptMsTerm + ptMs.Count);
			}
		}
		if (ptMsTerm == 0)
		{
			return;
		}
		mergedPtms = new PointMs(ptMsTerm);
		partStartIndex = new List<int>();
		foreach (PointMs ptMs2 in ptMsList)
		{
			partStartIndex.Add(mergedPtms.Count);
			mergedPtms.AddRange(ptMs2);
		}
	}

	protected static PointMs EnsureFirstPointEqualsLast(PointMs pms, IList<int> partStartIndex = null)
	{
		if (pms.IsNullOrEmpty())
		{
			return pms;
		}
		if (partStartIndex == null || partStartIndex.Count <= 1)
		{
			PointM pointM2 = pms.First();
			PointM pointM = pms.Last();
			if (!pointM2.EqualXY(pointM))
			{
				pms = pms.Clone();
				pms.Add(pointM2);
			}
		}
		return pms;
	}

	protected void EnsureFirstPointEqualsLast()
	{
		checked
		{
			if (_pointM.Count > 0 && !_pointM.First().EqualXY(_pointM.Last()))
			{
				if (_pointM is PointM[])
				{
					PointM[] arySrc = (PointM[])_pointM;
					arySrc = (PointM[])Utils.CopyArray(arySrc, new PointM[_pointM.Count + 1]);
					arySrc[arySrc.Count() - 1] = arySrc[0];
					_pointM = arySrc;
				}
				else
				{
					_pointM.Add(_pointM[0]);
				}
			}
		}
	}

	public static bool IsValidPolygon(Polygon pgon)
	{
		return pgon?.IsValid() ?? false;
	}

	public override bool IsValid()
	{
		if (Count == 0)
		{
			return false;
		}
		if (_pointM.First().EqualXY(_pointM.Last()))
		{
			if (Count < 4)
			{
				return false;
			}
		}
		else if (Count < 3)
		{
			return false;
		}
		return true;
	}

	public static Polygon FromBuffersMutable(IList<PointM> points, IList<int> partStartIndexes)
	{
		Polygon polygon = new Polygon(new PointMs());
		polygon.OverrideBuffersAndReinitialize(points, partStartIndexes);
		return polygon;
	}

	protected new void OverrideBuffersAndReinitialize(IList<PointM> points, IList<int> partStartIndexes)
	{
		base.OverrideBuffersAndReinitialize(EnsureFirstPointEqualsLast(points, partStartIndexes), partStartIndexes);
		Initialize();
	}

	public override PointM Centroid()
	{
		checked
		{
			if (_centroid == null)
			{
				double xComponent = 0.0;
				double yComponent = 0.0;
				if (_pointM.Count == 0)
				{
					return new PointM(double.NaN, double.NaN);
				}
				if (_pointM.Count <= 2)
				{
					return _pointM.First();
				}
				int count = PartsCount() - 1;
				double computed = default;
				for (int i = 0; i <= count; i++)
				{
					int partStartIndexTerm = PartStartIndex(i) + 1;
					int count2 = PartEndIndex(i) - 1;
					for (int j = partStartIndexTerm; j <= count2; j++)
					{
						double pointMPartStartIndexTerm = _pointM[PartStartIndex(i)].X + _pointM[j].X + _pointM[j + 1].X;
						double pointMPartStartIndexTerm2 = _pointM[PartStartIndex(i)].Y + _pointM[j].Y + _pointM[j + 1].Y;
						double pointMPartStartIndexTerm3 = (_pointM[j].X - _pointM[PartStartIndex(i)].X) * (_pointM[j + 1].Y - _pointM[PartStartIndex(i)].Y) - (_pointM[j + 1].X - _pointM[PartStartIndex(i)].X) * (_pointM[j].Y - _pointM[PartStartIndex(i)].Y);
						xComponent += pointMPartStartIndexTerm3 * pointMPartStartIndexTerm;
						yComponent += pointMPartStartIndexTerm3 * pointMPartStartIndexTerm2;
						computed += pointMPartStartIndexTerm3;
					}
					xComponent /= 3.0 * computed;
					yComponent /= 3.0 * computed;
				}
				_centroid = new Point(xComponent, yComponent);
			}
			return _centroid.PointM();
		}
	}

	public Polygon Add(PointM point)
	{
		if (Count == 0)
		{
			return Insert(point, 0);
		}
		if (Count == 1)
		{
			return Insert(point, 1);
		}
		return Insert(point, checked(Count - 1));
	}

	public Polygon Insert(PointM point, int insertIndex)
	{
		PointMs pointMs = new PointMs();
		List<int> intList = new List<int>();
		Insert(point, insertIndex, pointMs, intList);
		return new Polygon(pointMs, intList.ToArray());
	}

	public new Polygon Remove(int removeIndex)
	{
		PointMs pointMs = new PointMs();
		List<int> intList = new List<int>();
		Remove(removeIndex, pointMs, intList, 3);
		if (pointMs.Count > 3)
		{
			return new Polygon(pointMs, intList.ToArray());
		}
		return null;
	}

	public bool Contains(PointM PointM)
	{
		return Contains(PointM.X, PointM.Y);
	}

	bool IAreaGeometry.Contains(PointM PointM)
	{
		//ILSpy generated this explicit interface implementation from .override directive in Contains
		return this.Contains(PointM);
	}

	public bool ContainsAny(IPoints pts)
	{
		checked
		{
			int count = pts.PointMCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (Contains(pts.PointM(i)))
				{
					return true;
				}
			}
			return false;
		}
	}

	public bool ContainsAll(IPoints pts)
	{
		checked
		{
			int count = pts.PointMCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (!Contains(pts.PointM(i)))
				{
					return false;
				}
			}
			return true;
		}
	}

	public bool ContainsAny(IPoints pts, double tol)
	{
		checked
		{
			int count = pts.PointMCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM pointM = pts.PointM(i);
				if (ContainsFuzzy(pointM.X, pointM.Y, tol))
				{
					return true;
				}
			}
			return false;
		}
	}

	public bool ContainsAll(IPoints pts, double tol)
	{
		checked
		{
			int count = pts.PointMCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM pointM = pts.PointM(i);
				if (!ContainsFuzzy(pointM.X, pointM.Y, tol))
				{
					return false;
				}
			}
			return true;
		}
	}

	public bool Contains(double px, double py)
	{
		if (px < MinX || px > MaxX || py < MinY || py > MaxY)
		{
			return false;
		}
		int ordinal = 0;
		checked
		{
			int count = SegmentCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				SegmentM segmentM = Segment(i);
				if (((segmentM.A.X <= px && segmentM.B.X > px) || (segmentM.B.X <= px && segmentM.A.X > px)) && py > segmentM.InterpolatedY(px))
				{
					ordinal++;
				}
			}
		}
		return ordinal % 2 > 0;
	}

	public bool ContainsFuzzy(PointM ptm, double tol = 1E-05)
	{
		return ContainsFuzzy(ptm.X, ptm.Y, tol);
	}

	public bool ContainsFuzzy(double px, double py, double tol = 1E-05)
	{
		if (px + tol < MinX || px - tol > MaxX || py + tol < MinY || py - tol > MaxY)
		{
			return false;
		}
		int ordinal = 0;
		checked
		{
			int count = SegmentCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				SegmentM segmentM = Segment(i);
				if ((!(segmentM.A.X - tol <= px) || !(segmentM.B.X + tol > px)) && (!(segmentM.B.X - tol <= px) || !(segmentM.A.X + tol > px)))
				{
					continue;
				}
				double interpolatedYSegmentMTerm = segmentM.InterpolatedY(px) - py;
				if (Math.Abs(interpolatedYSegmentMTerm) < tol)
				{
					return true;
				}
				if (Math.Abs(segmentM.A.X - px) < tol && Math.Abs(segmentM.A.X - segmentM.B.X) < tol)
				{
					double y;
					double y2;
					if (segmentM.A.Y > segmentM.B.Y)
					{
						y = segmentM.A.Y;
						y2 = segmentM.B.Y;
					}
					else
					{
						y = segmentM.B.Y;
						y2 = segmentM.A.Y;
					}
					if (py - tol < y && py + tol > y2)
					{
						return true;
					}
				}
				if (interpolatedYSegmentMTerm < 0.0)
				{
					ordinal++;
				}
			}
		}
		return ordinal % 2 > 0;
	}

	public bool Contains(Polygon pgon)
	{
		return Contains((Polyline)pgon);
	}

	public bool Contains(Polyline pline)
	{
		if (!ContainsAll(pline))
		{
			return false;
		}
		if (Intersects(pline))
		{
			return false;
		}
		return true;
	}

	bool IAreaGeometry.Contains(Polyline pline)
	{
		//ILSpy generated this explicit interface implementation from .override directive in Contains
		return this.Contains(pline);
	}

	public bool ContainsFuzzy(Polyline pline, double tol = 1E-05)
	{
		checked
		{
			int count = pline.PointMCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM ptm = pline.PointM(i);
				if (!ContainsFuzzy(ptm, tol))
				{
					return false;
				}
			}
			if (Intersects(pline))
			{
				return false;
			}
			return true;
		}
	}

	public bool Contains(Extent ext)
	{
		return Contains(ext.ToPolygon());
	}

	public bool AreaIntersects(Polygon pgon)
	{
		if (ContainsAny(pgon))
		{
			return true;
		}
		if (Intersects(pgon))
		{
			return true;
		}
		if (pgon.ContainsAny(this))
		{
			return true;
		}
		if (pgon.Intersects(this))
		{
			return true;
		}
		return false;
	}

	public bool AreaIntersects(Extent ext)
	{
		return AreaIntersects(ext.ToPolygon());
	}

	public bool AreaIntersectsBuffered(Polygon pgon, double buffer)
	{
		if (ContainsAny(pgon, buffer))
		{
			return true;
		}
		if (Intersects(pgon, buffer))
		{
			return true;
		}
		if (pgon.ContainsAny(this, buffer))
		{
			return true;
		}
		if (pgon.Intersects(this, buffer))
		{
			return true;
		}
		return false;
	}

	public bool ContainsOrIntersects(Polyline pline)
	{
		if (!GetExtent().MBROverlaps(pline))
		{
			return false;
		}
		if (ContainsAny(pline) || Intersects(pline))
		{
			return true;
		}
		return false;
	}

	public List<Polyline> ClipPolyline(Polyline pline)
	{
		if (!Polyline.IsValidPolyline(pline))
		{
			return null;
		}
		SpatialIndex<int> segmentSpIdx = null;
		List<PointIntersect> lineIntersectionMulti = GetLineIntersectionMulti(pline, ref segmentSpIdx);
		if (lineIntersectionMulti.IsNullOrEmpty())
		{
			if (Contains(pline.FirstPointM()))
			{
				return new Polyline[1] { pline.Clone() }.ToList();
			}
			return null;
		}
		List<Polyline> polylines = new List<Polyline>();
		lineIntersectionMulti = (from ptint in lineIntersectionMulti
			orderby ptint.BSegmentIndex, ptint.BFraction
			select ptint).ToList();
		bool firstPointMPlineTerm = !Contains(pline.FirstPointM());
		PointOnPolyline pointOnPolyline = pline.FirstPointOnPolyline();
		int count = lineIntersectionMulti.Count;
		checked
		{
			for (int intervalIndex = 0; intervalIndex <= count; intervalIndex++)
			{
				PointOnPolyline pointOnPolyline2 = ((intervalIndex != lineIntersectionMulti.Count) ? lineIntersectionMulti[intervalIndex].BPointOnPolyline : pline.LastPointOnPolyline());
				if (firstPointMPlineTerm)
				{
					pointOnPolyline = pointOnPolyline2;
					firstPointMPlineTerm = false;
					continue;
				}
				PointMs pointMs = new PointMs();
				pointMs.Add(pointOnPolyline.PointM);
				int segmentIndexOffset = pointOnPolyline.SegmentIndex + 1;
				if (pointOnPolyline.Fraction > 0.9999)
				{
					segmentIndexOffset++;
				}
				int segmentIndex = pointOnPolyline2.SegmentIndex;
				if (pointOnPolyline2.Fraction < 0.0001)
				{
					segmentIndex--;
				}
				int segmentIndexOffset2 = segmentIndexOffset;
				int segmentIndex2 = segmentIndex;
				for (int segmentIndexOffset3 = segmentIndexOffset2; segmentIndexOffset3 <= segmentIndex2; segmentIndexOffset3++)
				{
					pointMs.Add(pline.PointM(segmentIndexOffset3));
				}
				pointMs.Add(pointOnPolyline2.PointM);
				polylines.Add(new Polyline(pointMs));
				firstPointMPlineTerm = true;
			}
			return polylines;
		}
	}

	public List<Tuple<PointOnPolyline, PointOnPolyline>> EntryExitPoints(Polyline pline)
	{
		_Closure_0024__51_002D0 capturedLocals = default;
		_Closure_0024__51_002D0 capturedLocals2 = new _Closure_0024__51_002D0(capturedLocals);
		capturedLocals2.pline = pline;
		Polyline polyline = capturedLocals2.pline;
		SpatialIndex<int> segmentSpIdx = null;
		List<PointOnPolyline> pointOnPolylines = (from pointIntersect in polyline.GetLineIntersectionMulti(this, ref segmentSpIdx)
			select pointIntersect.APointOnPolyline).ToList();
		PointM pointM = capturedLocals2.pline.FirstPointM();
		PointM pointM2 = capturedLocals2.pline.LastPointM();
		if (pointOnPolylines.Where([SpecialName] (PointOnPolyline pop) => pop.PointM.EqualXY(capturedLocals2.pline.FirstPointM())).Count() != 1 && Contains(pointM))
		{
			pointOnPolylines.Insert(0, capturedLocals2.pline.FirstPointOnPolyline());
		}
		if (pointOnPolylines.Where([SpecialName] (PointOnPolyline pop) => pop.PointM.EqualXY(capturedLocals2.pline.LastPointM())).Count() != 1 && Contains(pointM2))
		{
			pointOnPolylines.Add(capturedLocals2.pline.LastPointOnPolyline());
		}
		pointOnPolylines = pointOnPolylines.OrderBy([SpecialName] (PointOnPolyline pop) => pop.Fraction).ToList();
		List<Tuple<PointOnPolyline, PointOnPolyline>> valueTupleList = new List<Tuple<PointOnPolyline, PointOnPolyline>>();
		checked
		{
			int count = pointOnPolylines.Count - 2;
			for (int intervalIndex = 0; intervalIndex <= count; intervalIndex += 2)
			{
				PointOnPolyline pointOnPolyline = pointOnPolylines[intervalIndex];
				PointOnPolyline pointOnPolyline2 = pointOnPolylines[intervalIndex + 1];
				valueTupleList.Add(new Tuple<PointOnPolyline, PointOnPolyline>(pointOnPolyline, pointOnPolyline2));
			}
			return valueTupleList;
		}
	}

	public List<Tuple<double, double>> GetContainedSubsegments(Polyline polyline, ref SpatialIndex<int> segmentSpatialIdx)
	{
		_Closure_0024__52_002D0 capturedLocals = default;
		_Closure_0024__52_002D0 capturedLocals2 = new _Closure_0024__52_002D0(capturedLocals);
		capturedLocals2.polyline = polyline;
		List<Tuple<double, double>> containedSubsegments = new List<Tuple<double, double>>();
		if (!GetExtent().MBROverlaps(capturedLocals2.polyline))
		{
			return containedSubsegments;
		}
		if (!Polyline.IsValidPolyline(capturedLocals2.polyline))
		{
			return containedSubsegments;
		}
		if (capturedLocals2.polyline.PartsCount() != 1)
		{
			throw new NotImplementedException();
		}
		if (segmentSpatialIdx == null)
		{
			segmentSpatialIdx = GenerateSegmentSpatialIndex();
		}
		List<PointIntersect> lineIntersectionMulti = GetLineIntersectionMulti(capturedLocals2.polyline, ref segmentSpatialIdx);
		lineIntersectionMulti = lineIntersectionMulti.OrderBy([SpecialName] (PointIntersect pi) => capturedLocals2.polyline.DistanceAlong(pi.BPointOnPolyline)).ToList();
		bool condition = true;
		SegmentM segmentM = capturedLocals2.polyline.Segment(0);
		PointM pointM = segmentM.A;
		double tolerance = 1E-05;
		if (PointCloseToPerimeter(pointM, tolerance, segmentSpatialIdx))
		{
			pointM = segmentM.PointAtDistance(tolerance);
			if (lineIntersectionMulti.Count > 0)
			{
				PointIntersect pointIntersect = lineIntersectionMulti.First();
				if (pointIntersect.BSegmentIndex == 0 && pointIntersect.BFraction * segmentM.Length < tolerance)
				{
					lineIntersectionMulti.RemoveAt(0);
				}
			}
		}
		if (Contains(pointM))
		{
			if (lineIntersectionMulti.Count == 0)
			{
				containedSubsegments.Add(new Tuple<double, double>(0.0, capturedLocals2.polyline.Length));
				return containedSubsegments;
			}
			condition = true;
		}
		else
		{
			if (lineIntersectionMulti.Count == 0)
			{
				return containedSubsegments;
			}
			condition = false;
		}
		double[] cumulativeSegmentLengths = capturedLocals2.polyline.CumulativeSegmentLengths();
		double cumulativeSegmentLengthSum = 0.0;
		checked
		{
			int count = lineIntersectionMulti.Count - 1;
			for (int intervalIndex = 0; intervalIndex <= count; intervalIndex++)
			{
				PointIntersect pointIntersect2 = lineIntersectionMulti[intervalIndex];
				int segmentIndex = pointIntersect2.BPointOnPolyline.SegmentIndex;
				double bFraction = pointIntersect2.BFraction;
				double cumulativeSegmentLengthSum2 = capturedLocals2.polyline.Segment(segmentIndex).Length * bFraction;
				if (segmentIndex != 0)
				{
					cumulativeSegmentLengthSum2 += cumulativeSegmentLengths[segmentIndex - 1];
				}
				if (condition)
				{
					if (cumulativeSegmentLengthSum != cumulativeSegmentLengthSum2)
					{
						containedSubsegments.Add(new Tuple<double, double>(cumulativeSegmentLengthSum, cumulativeSegmentLengthSum2));
					}
					condition = false;
				}
				else
				{
					cumulativeSegmentLengthSum = cumulativeSegmentLengthSum2;
					condition = true;
				}
			}
			if (condition && cumulativeSegmentLengthSum != capturedLocals2.polyline.Length)
			{
				containedSubsegments.Add(new Tuple<double, double>(cumulativeSegmentLengthSum, capturedLocals2.polyline.Length));
			}
			return containedSubsegments;
		}
	}

	public List<Tuple<double, double>> GetContainedSubsegments(Polyline polyline)
	{
		SpatialIndex<int> segmentSpatialIdx = null;
		return GetContainedSubsegments(polyline, ref segmentSpatialIdx);
	}

	List<Tuple<double, double>> IAreaGeometry.GetContainedSubsegments(Polyline polyline)
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetContainedSubsegments
		return this.GetContainedSubsegments(polyline);
	}

	public DistinctSpatialIndex GeneratePartSpatialIndex()
	{
		DistinctSpatialIndex distinctSpatialIndex = new DistinctSpatialIndex(this, PartsCount());
		checked
		{
			int count = PartsCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				int pointCountInPart = PointCountInPart(i);
				if (pointCountInPart != 0)
				{
					List<Point2Double> point2Doubles = new List<Point2Double>(pointCountInPart);
					int partStartIndex = PartStartIndex(i);
					int partEndIndex = PartEndIndex(i);
					for (int j = partStartIndex; j <= partEndIndex; j++)
					{
						point2Doubles.Add(new Point2Double(_pointM[j]));
					}
					Extent extent = new Extent(point2Doubles);
					distinctSpatialIndex.Add(extent, i);
				}
			}
			return distinctSpatialIndex;
		}
	}

	public DistinctSpatialIndex GetPartSpatialIndex()
	{
		if (_partSpIndex != null)
		{
			return _partSpIndex;
		}
		_partSpIndex = GeneratePartSpatialIndex();
		return _partSpIndex;
	}

	public bool PointCloseToPerimeter(PointM point, double nearDistance)
	{
		checked
		{
			int count = SegmentCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				SegmentM segmentM = Segment(i);
				SegmentM.RelativePosition relativePosition = SegmentM.RelativePosition.Undefined;
				if (segmentM.DistanceToPoint(point, ref relativePosition) < nearDistance)
				{
					return true;
				}
			}
			return false;
		}
	}

	public bool PointCloseToPerimeter(PointM point, double nearDistance, SpatialIndex<int> segmentSpatialIndex)
	{
		if (segmentSpatialIndex == null)
		{
			segmentSpatialIndex = GenerateSegmentSpatialIndex();
		}
		RowColRangeM rowColRangeM = segmentSpatialIndex.Raster.RowColRange(point, nearDistance, limitToRaster: true);
		int minRow = rowColRangeM.MinRow;
		int maxRow = rowColRangeM.MaxRow;
		checked
		{
			for (int i = minRow; i <= maxRow; i++)
			{
				int minCol = rowColRangeM.MinCol;
				int maxCol = rowColRangeM.MaxCol;
				for (int j = minCol; j <= maxCol; j++)
				{
					List<int> get_Bin = segmentSpatialIndex.get_Bin(i, j);
					if (get_Bin == null)
					{
						continue;
					}
					foreach (int get_Bin2 in get_Bin)
					{
						SegmentM segmentM = Segment(get_Bin2);
						SegmentM.RelativePosition relativePosition = SegmentM.RelativePosition.Undefined;
						if (segmentM.DistanceToPoint(point, ref relativePosition) < nearDistance)
						{
							return true;
						}
					}
				}
			}
			return false;
		}
	}

	public int GetTravelDirection(PointOnPolyline startPoP, PointOnPolyline midPoP, PointOnPolyline endPoP)
	{
		if (IsPointBetweenCCW(startPoP, midPoP, endPoP))
		{
			return 1;
		}
		return -1;
	}

	public int GetTravelDirectionShortestArc(PointOnPolyline startPoP, PointOnPolyline endPoP)
	{
		double distanceAlong = DistanceAlong(startPoP);
		double lengthSum = DistanceAlong(endPoP) - distanceAlong;
		if (lengthSum < 0.0)
		{
			lengthSum += Length;
		}
		if (lengthSum <= 0.5 * Length)
		{
			return 1;
		}
		return -1;
	}

	public bool IsPointBetweenCCW(PointOnPolyline startPoP, PointOnPolyline midPoP, PointOnPolyline endPoP)
	{
		if (startPoP.SegmentIndex == endPoP.SegmentIndex)
		{
			if (startPoP.Fraction == endPoP.Fraction)
			{
				if (midPoP.SegmentIndex == startPoP.SegmentIndex && midPoP.Fraction == startPoP.Fraction)
				{
					return true;
				}
				return false;
			}
			if (startPoP.Fraction < endPoP.Fraction)
			{
				if (midPoP.SegmentIndex == startPoP.SegmentIndex && midPoP.Fraction > startPoP.Fraction && midPoP.Fraction < endPoP.Fraction)
				{
					return true;
				}
				return false;
			}
			if (midPoP.SegmentIndex == startPoP.SegmentIndex && midPoP.Fraction > endPoP.Fraction && midPoP.Fraction < startPoP.Fraction)
			{
				return false;
			}
			return true;
		}
		int segmentIndex = startPoP.SegmentIndex;
		while (true)
		{
			if (segmentIndex == SegmentCount())
			{
				segmentIndex = 0;
			}
			if (segmentIndex == midPoP.SegmentIndex)
			{
				if (midPoP.SegmentIndex == startPoP.SegmentIndex)
				{
					if (midPoP.Fraction > startPoP.Fraction)
					{
						return true;
					}
					return false;
				}
				if (midPoP.SegmentIndex == endPoP.SegmentIndex)
				{
					if (midPoP.Fraction < endPoP.Fraction)
					{
						return true;
					}
					return false;
				}
				return true;
			}
			if (segmentIndex == endPoP.SegmentIndex)
			{
				break;
			}
			segmentIndex = checked(segmentIndex + 1);
		}
		return false;
	}

	public bool Rasterize(RasterM raster, bool[] mask)
	{
		bool result = false;
		if (raster.Overlaps(this))
		{
			new RasterizePolygon(raster).ComputeCells(this, [SpecialName] (int n) =>
			{
				mask[n] = true;
				result = true;
			});
		}
		return result;
	}

	public bool RasterizeOnto<T>(RasterM raster, T[] mask, T setValue) where T : struct
	{
		bool result = false;
		if (raster.Overlaps(this))
		{
			new RasterizePolygon(raster).ComputeCells(this, [SpecialName] (int n) =>
			{
				mask[n] = setValue;
				result = true;
			});
		}
		return result;
	}

	public double PartArea(int partIndex)
	{
		checked
		{
			if (double.IsNaN(_partArea[partIndex]))
			{
				_partArea[partIndex] = 0.0;
				int partStartIndexPartIndexTerm = PartStartIndex(partIndex) + 1;
				int partEndIndex = PartEndIndex(partIndex);
				SegmentM segmentM = default;
				for (int i = partStartIndexPartIndexTerm; i <= partEndIndex; i++)
				{
					segmentM.A = PointM(i - 1);
					segmentM.B = PointM(i);
					double deltaX = segmentM.DeltaX();
					double segmentMMinYTerm = segmentM.A.Y - MinY;
					double segmentMMinYTerm2 = segmentM.B.Y - MinY;
					_partArea[partIndex] += 0.5 * (segmentMMinYTerm + segmentMMinYTerm2) * deltaX;
				}
			}
			return _partArea[partIndex];
		}
	}

	public PointMs PartPoints(int partIndex)
	{
		PointMs pointMs = new PointMs();
		int partStartIndex = PartStartIndex(partIndex);
		int partEndIndex = PartEndIndex(partIndex);
		for (int i = partStartIndex; i <= partEndIndex; i = checked(i + 1))
		{
			pointMs.Add(PointM(i));
		}
		return pointMs;
	}

	public bool PartIsInterior(int partIndex)
	{
		return PartArea(partIndex) < 0.0;
	}

	public bool PartIsExterior(int partIndex)
	{
		return PartArea(partIndex) > 0.0;
	}

	public static Polygon MergeToMultipartPolygon(IList<Polygon> polygons)
	{
		polygons = polygons.Where([SpecialName] (Polygon pg) => pg != null && pg.Count > 0).ToList();
		if (polygons.Count == 0)
		{
			return null;
		}
		int sum = polygons.Sum([SpecialName] (Polygon pg) => pg.PartsCount());
		PointMs pointMs = new PointMs();
		checked
		{
			int[] intValues = new int[sum - 1 + 1];
			int ordinal = 0;
			int count = polygons.Count - 1;
			for (int intervalIndex = 0; intervalIndex <= count; intervalIndex++)
			{
				Polygon polygon = polygons[intervalIndex];
				int count2 = polygon.PartsCount() - 1;
				for (int ordinal2 = 0; ordinal2 <= count2; ordinal2++)
				{
					intValues[ordinal] = pointMs.Count;
					polygon.CopyTo(pointMs, ordinal2);
					ordinal++;
				}
			}
			return new Polygon(pointMs, intValues);
		}
	}

	public List<Polygon> ExplodeMultipartPolygon(bool forcePositiveArea = false)
	{
		List<Polygon> polygons = new List<Polygon>();
		if (PartsCount() == 1)
		{
			if (forcePositiveArea && Area < 0.0)
			{
				polygons.Add(Reverse());
			}
			else
			{
				polygons.Add((Polygon)EnsureImmutable());
			}
			return polygons;
		}
		checked
		{
			int count = PartsCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Polygon polygon = GetPartAsPolygon(i);
				if (forcePositiveArea && polygon.Area < 0.0)
				{
					polygon = polygon.Reverse();
				}
				polygons.Add(polygon);
			}
			return polygons;
		}
	}

	public PointMs GetPartPoints(int partIdx)
	{
		PointMs pointMs = new PointMs();
		int partStartIndex = PartStartIndex(partIdx);
		int partEndIndex = PartEndIndex(partIdx);
		checked
		{
			int count = partEndIndex - 1;
			for (int i = partStartIndex; i <= count; i++)
			{
				pointMs.Add(PointM(i));
			}
			return pointMs;
		}
	}

	public Polygon GetPartAsPolygon(int partIdx)
	{
		return new Polygon(GetPartPoints(partIdx));
	}

	public List<Polygon> ExplodeMultipartPolygonMaintainHoles()
	{
		List<Polygon> polygons = new List<Polygon>();
		if (PartsCount() == 1)
		{
			polygons.Add((Polygon)EnsureImmutable());
			return polygons;
		}
		List<Tuple<int, double>> partsCount = new List<Tuple<int, double>>(PartsCount());
		checked
		{
			int count = PartsCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				double partArea = PartArea(i);
				partsCount.Add(new Tuple<int, double>(i, partArea));
			}
			partsCount = partsCount.OrderBy([SpecialName] (Tuple<int, double> kvp) => kvp.Item2).ToList();
			bool[] boolValues = new bool[PartsCount() - 1 + 1];
			int count2 = partsCount.Count - 1;
			for (int intervalIndex = 0; intervalIndex <= count2; intervalIndex++)
			{
				int partsCountTerm = partsCount[intervalIndex].Item1;
				if (boolValues[partsCountTerm])
				{
					continue;
				}
				PointMs partPoints = GetPartPoints(partsCountTerm);
				List<int> intList = new List<int>();
				intList.Add(0);
				Polygon polygon = new Polygon(partPoints);
				int ordinal = intervalIndex + 1;
				int count3 = partsCount.Count - 1;
				for (int ordinal2 = ordinal; ordinal2 <= count3; ordinal2++)
				{
					int partsCountTerm2 = partsCount[ordinal2].Item1;
					if (!boolValues[partsCountTerm2])
					{
						PointMs partPoints2 = GetPartPoints(partsCountTerm);
						Polygon pgon = new Polygon(partPoints2);
						if (polygon.Contains(pgon))
						{
							intList.Add(partPoints.Count);
							partPoints.AddRange(partPoints2);
							boolValues[partsCountTerm2] = true;
						}
					}
				}
				polygons.Add(new Polygon(partPoints, intList.ToArray()));
				boolValues[partsCountTerm] = true;
			}
			return polygons;
		}
	}

	public override PointM LabelPoint(FeatureLayer layer)
	{
		return Centroid();
	}

	public new Polygon Reverse()
	{
		PointMs pointMs = new PointMs();
		pointMs.Capacity = Count;
		checked
		{
			for (int i = Count - 1; i >= 0; i += -1)
			{
				pointMs.Add(PointM(i));
			}
			return new Polygon(pointMs);
		}
	}

	public Polygon Rotate(double angleRotationDegrees, PointM centerPt = default(PointM))
	{
		if (centerPt.Equals(default(PointM)))
		{
			centerPt = GetExtent().CenterPointM();
		}
		PointMs pointMs = AffineFeature.RotatePointsAroundCenterPoint(CopyPoints(), centerPt, angleRotationDegrees);
		return new Polygon(pointMs);
	}

	public new Polygon FilterXY(double tolerance)
	{
		if (PartsCount() == 1)
		{
			PointMs pointMs = new PointMs();
			CopyTo(pointMs);
			pointMs.FilterXY(tolerance);
			return new Polygon(pointMs);
		}
		List<PointMs> pointMsList = new List<PointMs>();
		checked
		{
			int count = PartsCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				PointMs pointMs2 = PartPoints(i);
				pointMs2.FilterXY(tolerance);
				if (pointMs2.Count > 2)
				{
					pointMsList.Add(pointMs2);
				}
			}
			PointMs mergedPtms = null;
			IList<int> partStartIndex = null;
			MergePointMs(pointMsList, ref mergedPtms, ref partStartIndex);
			return new Polygon(mergedPtms, partStartIndex);
		}
	}

	public Polygon FilterXYIterative(int iterationCount, int targetPointCount)
	{
		PointMs pointMs = new PointMs();
		CopyTo(pointMs);
		if (PointMCount() <= targetPointCount)
		{
			return new Polygon(pointMs);
		}
		double unity = 1.0;
		Polygon polygon = null;
		Polygon polygon2 = FilterXY(unity);
		if (polygon2.PointMCount() == targetPointCount)
		{
			return polygon2;
		}
		checked
		{
			double computed;
			double computed2;
			if (polygon2.PointMCount() < targetPointCount)
			{
				computed = unity;
				polygon = polygon2;
				double computed3 = unity;
				int ordinal = 0;
				while (true)
				{
					computed3 /= 2.0;
					Polygon polygon3 = FilterXY(computed3);
					if (polygon3.PointMCount() == targetPointCount)
					{
						return polygon3;
					}
					if (polygon3.PointMCount() >= targetPointCount)
					{
						break;
					}
					computed = computed3;
					polygon = polygon3;
					ordinal++;
					if (ordinal == 100)
					{
						return new Polygon(pointMs);
					}
				}
				computed2 = computed3;
			}
			else
			{
				computed2 = unity;
				double computed4 = unity;
				int ordinal2 = 0;
				Polygon polygon4;
				while (true)
				{
					computed4 *= 2.0;
					polygon4 = FilterXY(computed4);
					if (polygon4.PointMCount() == targetPointCount)
					{
						return polygon4;
					}
					if (polygon4.PointMCount() <= targetPointCount)
					{
						break;
					}
					computed2 = computed4;
					ordinal2++;
					if (ordinal2 == 100)
					{
						return new Polygon(pointMs);
					}
				}
				computed = computed4;
				polygon = polygon4;
			}
			int count = iterationCount - 1;
			for (int i = 0; i <= count; i++)
			{
				double computed5 = (computed2 + computed) / 2.0;
				Polygon polygon5 = FilterXY(computed5);
				if (polygon5.PointMCount() == targetPointCount)
				{
					return polygon5;
				}
				if (polygon5.PointMCount() > targetPointCount)
				{
					computed2 = computed5;
					continue;
				}
				computed = computed5;
				polygon = polygon5;
			}
			return polygon;
		}
	}

	public Polygon FilterByArea(int maxPoints, bool usePseudoPQueue = true, int pQueueSize = 1000)
	{
		if (maxPoints < 3)
		{
			maxPoints = 3;
		}
		PointMs pointMs = new PointMs(maxPoints);
		CopyTo(pointMs);
		checked
		{
			if (usePseudoPQueue)
			{
				PseudoPriorityQueue pseudoPriorityQueue = new PseudoPriorityQueue(null, pQueueSize);
				int count = pointMs.PointMCount() - 1;
				for (int i = 0; i <= count; i++)
				{
					double area = GetArea(i, pointMs);
					pseudoPriorityQueue.TryAdd(i, area);
				}
				while (pointMs.Count > maxPoints)
				{
					int takeMin = pseudoPriorityQueue.TakeMin();
					if (takeMin == -1)
					{
						pseudoPriorityQueue.Regenerate();
						int count2 = pointMs.PointMCount() - 1;
						for (int j = 0; j <= count2; j++)
						{
							double area2 = GetArea(j, pointMs);
							pseudoPriorityQueue.TryAdd(j, area2);
						}
						takeMin = pseudoPriorityQueue.TakeMin();
					}
					pointMs.RemoveAt(takeMin);
					pseudoPriorityQueue.NotifyValueChanged(takeMin - 1, GetArea(takeMin - 1, pointMs));
					pseudoPriorityQueue.NotifyValueChanged(takeMin, GetArea(takeMin, pointMs));
				}
			}
			else
			{
				List<double> doubleList = new List<double>();
				int count3 = PointMCount() - 1;
				for (int k = 0; k <= count3; k++)
				{
					double area3 = GetArea(k, pointMs);
					doubleList.Add(area3);
				}
				while (pointMs.Count > maxPoints)
				{
					int intervalIndex = 0;
					int count4 = pointMs.Count - 1;
					for (int l = 1; l <= count4; l++)
					{
						if (doubleList[l] < doubleList[intervalIndex])
						{
							intervalIndex = l;
						}
					}
					pointMs.RemoveAt(intervalIndex);
					doubleList.RemoveAt(intervalIndex);
					if (doubleList[intervalIndex - 1] < double.MaxValue)
					{
						doubleList[intervalIndex - 1] = GetArea(intervalIndex - 1, pointMs);
					}
					if (doubleList[intervalIndex] < double.MaxValue)
					{
						doubleList[intervalIndex] = GetArea(intervalIndex, pointMs);
					}
				}
			}
			return new Polygon(pointMs);
		}
	}

	private double GetArea(int i, PointMs pms)
	{
		checked
		{
			if (i == 0 || i == pms.Count - 1)
			{
				return double.MaxValue;
			}
			return Math.Abs((pms[i - 1].Y * pms[i].X + pms[i].Y * pms[i + 1].X + pms[i + 1].Y * pms[i - 1].X - (pms[i - 1].X * pms[i].Y + pms[i].X * pms[i + 1].Y + pms[i + 1].X * pms[i - 1].Y)) / 2.0);
		}
	}

	public new Polygon Densify(double maxSegmentLength)
	{
		PointMs pointMs = new PointMs();
		CopyTo(pointMs);
		pointMs.Densify(maxSegmentLength);
		return new Polygon(pointMs);
	}

	public bool IsVertexVerticallyBetweenNeighbors(int partIndex, int partPointIndex)
	{
		double y = _pointM[partPointIndex].Y;
		int nextPointOnPart = GetNextPointOnPart(partIndex, partPointIndex);
		int previousPointOnPart = GetPreviousPointOnPart(partIndex, partPointIndex);
		double pointMNextPointOnPartTerm = _pointM[nextPointOnPart].Y - y;
		double pointMPreviousPointOnPartTerm = _pointM[previousPointOnPart].Y - y;
		if (pointMNextPointOnPartTerm == 0.0 && pointMPreviousPointOnPartTerm == 0.0)
		{
			return false;
		}
		if (pointMNextPointOnPartTerm == 0.0)
		{
			return pointMPreviousPointOnPartTerm > 0.0;
		}
		if (pointMPreviousPointOnPartTerm == 0.0)
		{
			return pointMNextPointOnPartTerm > 0.0;
		}
		return Math.Sign(pointMNextPointOnPartTerm) != Math.Sign(pointMPreviousPointOnPartTerm);
	}

	public bool IsVertexVerticallyBetweenNeighborsPrev(int partIndex, int partPointIndex)
	{
		double y = _pointM[partPointIndex].Y;
		int partPointIndex2 = partPointIndex;
		do
		{
			partPointIndex2 = GetNextPointOnPart(partIndex, partPointIndex2);
		}
		while (_pointM[partPointIndex2].Y == y);
		int partPointIndex3 = partPointIndex;
		do
		{
			partPointIndex3 = GetPreviousPointOnPart(partIndex, partPointIndex3);
		}
		while (_pointM[partPointIndex3].Y == y);
		double pointMPartPointIndex2Term = _pointM[partPointIndex2].Y - y;
		double pointMPartPointIndex3Term = y - PointM(partPointIndex3).Y;
		return Math.Sign(pointMPartPointIndex2Term) == Math.Sign(pointMPartPointIndex3Term);
	}

	public int GetNextPointOnPart(int partIndex, int pointIndex)
	{
		int pointIndexOffset = checked(pointIndex + 1);
		if (pointIndexOffset == PointCountInPart(partIndex))
		{
			return 1;
		}
		return pointIndexOffset;
	}

	public int GetPreviousPointOnPart(int partIndex, int pointIndex)
	{
		checked
		{
			int pointIndexOffset = pointIndex - 1;
			if (pointIndexOffset < PartStartIndex(partIndex))
			{
				return PartEndIndex(partIndex) - 1;
			}
			return pointIndexOffset;
		}
	}

	public override Polyline PolylineSubsection(PointOnPolyline startPoint, PointOnPolyline endPoint)
	{
		PointMs pointMs = new PointMs();
		PointsBetweenInclusive(startPoint, endPoint, pointMs);
		return new Polyline(pointMs);
	}

	public void PointsBetweenInclusive(PointOnPolyline startPoint, PointOnPolyline endPoint, PointMs pointMs)
	{
		pointMs.Add(startPoint.PointM);
		PointsBetweenExclusive(startPoint, endPoint, pointMs);
		if (endPoint.Fraction > 0.0)
		{
			pointMs.Add(endPoint.PointM);
		}
	}

	public void PointsBetweenExclusive(PointOnPolyline startPoint, PointOnPolyline endPoint, PointMs pointMs)
	{
		int partIndex = default;
		int localSegIdx = default;
		GetPartialSegmentIndex(startPoint.SegmentIndex, ref partIndex, ref localSegIdx);
		int partIndex2 = default;
		int localSegIdx2 = default;
		GetPartialSegmentIndex(endPoint.SegmentIndex, ref partIndex2, ref localSegIdx2);
		if (partIndex != partIndex2)
		{
			return;
		}
		int partIndex3 = partIndex;
		checked
		{
			if (startPoint.LessThan(endPoint))
			{
				int partStartIndexPartIndex3Term = PartStartIndex(partIndex3) + localSegIdx + 1;
				int count = PartStartIndex(partIndex3) + localSegIdx2;
				for (int i = partStartIndexPartIndex3Term; i <= count; i++)
				{
					pointMs.Add(_pointM[i]);
				}
				return;
			}
			int partStartIndexPartIndex3Term2 = PartStartIndex(partIndex3) + localSegIdx + 1;
			int count2 = PartEndIndex(partIndex3) - 1;
			for (int j = partStartIndexPartIndex3Term2; j <= count2; j++)
			{
				pointMs.Add(_pointM[j]);
			}
			int partStartIndex = PartStartIndex(partIndex3);
			int count3 = PartStartIndex(partIndex3) + localSegIdx2;
			for (int k = partStartIndex; k <= count3; k++)
			{
				pointMs.Add(_pointM[k]);
			}
		}
	}

	public bool SelfIntersects()
	{
		checked
		{
			int count = PartsCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (SelfIntersects(i))
				{
					return true;
				}
			}
			return false;
		}
	}

	public bool SelfIntersects(int partIndex)
	{
		List<SegmentM> segmentMs = new List<SegmentM>();
		checked
		{
			int count = PartSegmentCount(partIndex) - 1;
			for (int i = 0; i <= count; i++)
			{
				segmentMs.Add(PartSegment(partIndex, i));
			}
			int count2 = segmentMs.Count - 1;
			for (int j = 0; j <= count2; j++)
			{
				int countOffset = j - 1;
				int ordinal = j + 1;
				if (countOffset < 0)
				{
					countOffset = segmentMs.Count - 1;
				}
				if (ordinal > segmentMs.Count - 1)
				{
					ordinal = 0;
				}
				int count3 = segmentMs.Count - 1;
				for (int k = 0; k <= count3; k++)
				{
					if (k != countOffset && k != j && k != ordinal && segmentMs[j].Intersects(segmentMs[k]))
					{
						return true;
					}
				}
			}
			return false;
		}
	}

	public bool ClipPolygon(IExtent extent, int startIndex, int endIndex, ref int nc, ref double[] cx, ref double[] cy)
	{
		nc = 0;
		checked
		{
			int endIndexStartIndexTerm = (endIndex - startIndex + 1) * 3;
			if (cx == null || cx.Count() < endIndexStartIndexTerm)
			{
				cx = new double[endIndexStartIndexTerm - 1 + 1];
			}
			if (cy == null || cy.Count() < endIndexStartIndexTerm)
			{
				cy = new double[endIndexStartIndexTerm - 1 + 1];
			}
			if (_pointM.Count <= 2)
			{
				return false;
			}
			for (int i = startIndex; i <= endIndex; i++)
			{
				double pointMTerm;
				double pointMTerm2;
				if (i < endIndex)
				{
					pointMTerm = _pointM[i + 1].X - _pointM[i].X;
					pointMTerm2 = _pointM[i + 1].Y - _pointM[i].Y;
				}
				else
				{
					pointMTerm = _pointM[startIndex].X - _pointM[i].X;
					pointMTerm2 = _pointM[startIndex].Y - _pointM[i].Y;
				}
				double minXExtentTerm;
				double maxXExtentTerm;
				if ((pointMTerm > 0.0) | ((pointMTerm == 0.0) & (_pointM[i].X > extent.MaxX)))
				{
					minXExtentTerm = extent.MinX;
					maxXExtentTerm = extent.MaxX;
				}
				else
				{
					minXExtentTerm = extent.MaxX;
					maxXExtentTerm = extent.MinX;
				}
				double minYExtentTerm;
				double maxYExtentTerm;
				if ((pointMTerm2 > 0.0) | ((pointMTerm2 == 0.0) & (_pointM[i].Y > extent.MaxY)))
				{
					minYExtentTerm = extent.MinY;
					maxYExtentTerm = extent.MaxY;
				}
				else
				{
					minYExtentTerm = extent.MaxY;
					maxYExtentTerm = extent.MinY;
				}
				double minValue = ((pointMTerm == 0.0) ? double.MinValue : ((minXExtentTerm - _pointM[i].X) / pointMTerm));
				double minValue2 = ((pointMTerm2 == 0.0) ? double.MinValue : ((minYExtentTerm - _pointM[i].Y) / pointMTerm2));
				double minValue3;
				double minValue4;
				if (minValue < minValue2)
				{
					minValue3 = minValue;
					minValue4 = minValue2;
				}
				else
				{
					minValue3 = minValue2;
					minValue4 = minValue;
				}
				if (!(1.0 >= minValue3))
				{
					continue;
				}
				if (0.0 < minValue3)
				{
					ClipPolygonAdd(minXExtentTerm, minYExtentTerm, ref nc, ref cx, ref cy);
				}
				if (!(1.0 >= minValue4))
				{
					continue;
				}
				double pointMTerm3;
				if (pointMTerm != 0.0)
				{
					pointMTerm3 = (maxXExtentTerm - _pointM[i].X) / pointMTerm;
				}
				else
				{
					pointMTerm3 = ((!((extent.MinX <= _pointM[i].X) & (_pointM[i].X <= extent.MaxX))) ? double.MinValue : double.MaxValue);
				}
				double pointMTerm4;
				if (pointMTerm2 != 0.0)
				{
					pointMTerm4 = (maxYExtentTerm - _pointM[i].Y) / pointMTerm2;
				}
				else
				{
					pointMTerm4 = ((!((extent.MinY <= _pointM[i].Y) & (_pointM[i].Y <= extent.MaxY))) ? double.MinValue : double.MaxValue);
				}
				double computed = ((!(pointMTerm3 < pointMTerm4)) ? pointMTerm4 : pointMTerm3);
				if (!((0.0 < minValue4) | (0.0 < computed)))
				{
					continue;
				}
				if (minValue4 <= computed)
				{
					if (0.0 < minValue4)
					{
						if (minValue > minValue2)
						{
							ClipPolygonAdd(minXExtentTerm, _pointM[i].Y + minValue * pointMTerm2, ref nc, ref cx, ref cy);
						}
						else
						{
							ClipPolygonAdd(_pointM[i].X + minValue2 * pointMTerm, minYExtentTerm, ref nc, ref cx, ref cy);
						}
					}
					if (1.0 > computed)
					{
						if (pointMTerm3 < pointMTerm4)
						{
							ClipPolygonAdd(maxXExtentTerm, _pointM[i].Y + pointMTerm3 * pointMTerm2, ref nc, ref cx, ref cy);
						}
						else
						{
							ClipPolygonAdd(_pointM[i].X + pointMTerm4 * pointMTerm, maxYExtentTerm, ref nc, ref cx, ref cy);
						}
					}
					else if (i < endIndex)
					{
						ClipPolygonAdd(_pointM[i + 1].X, _pointM[i + 1].Y, ref nc, ref cx, ref cy);
					}
					else
					{
						ClipPolygonAdd(_pointM[startIndex].X, _pointM[startIndex].Y, ref nc, ref cx, ref cy);
					}
				}
				else if (minValue > minValue2)
				{
					ClipPolygonAdd(minXExtentTerm, maxYExtentTerm, ref nc, ref cx, ref cy);
				}
				else
				{
					ClipPolygonAdd(maxXExtentTerm, minYExtentTerm, ref nc, ref cx, ref cy);
				}
			}
			return nc > 2;
		}
	}

	private void ClipPolygonAdd(double x, double y, ref int nc, ref double[] cx, ref double[] cy)
	{
		cx[nc] = x;
		cy[nc] = y;
		checked
		{
			nc++;
		}
	}

	public override bool Intersects(PointM PointM, double nearDistance)
	{
		return Contains(PointM);
	}

	public override bool Intersects(Extent extent)
	{
		if (extent.MBROverlaps(this))
		{
			if (!base.Intersects(extent))
			{
				return Contains(extent.UpperLeft);
			}
			return true;
		}
		return false;
	}

	public override string ToString()
	{
		return "Polygon" + PointLabel(HasZ, HasM);
	}

	public override IEditableFeature GetMutableView()
	{
		return new PolygonEditable(this);
	}
}
