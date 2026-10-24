using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Utilities;
using Utility.Extensions;
using Utility.Systems;

namespace RasMapperLib;

public class Polyline : MultiPoint, ISegments
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__99_002D0
	{
		public ThreadLocal<List<PointIntersect>> tlPointIntersectList;

		public Polyline polyline;

		public _Closure_0024__99_002D0(_Closure_0024__99_002D0 other)
		{
			if (other != null)
			{
				tlPointIntersectList = other.tlPointIntersectList;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(List<int> bin)
		{
			List<PointIntersect> pointIntersects = tlPointIntersectList.Value;
			checked
			{
				int countOffset = bin.Count - 2;
				int partIndex = default;
				int localSegIdx = default;
				int partIndex2 = default;
				int localSegIdx2 = default;
				double x = default;
				double y = default;
				double t = default;
				double t2 = default;
				for (int i = 0; i <= countOffset; i++)
				{
					int bin2 = bin[i];
					SegmentM segmentM = polyline.Segment(bin2);
					polyline.GetPartialSegmentIndex(bin2, ref partIndex, ref localSegIdx);
					int ordinal = i + 1;
					int countOffset2 = bin.Count - 1;
					for (int j = ordinal; j <= countOffset2; j++)
					{
						int bin3 = bin[j];
						polyline.GetPartialSegmentIndex(bin3, ref partIndex2, ref localSegIdx2);
						if (partIndex != partIndex2 || (localSegIdx2 != localSegIdx && localSegIdx2 != localSegIdx - 1 && localSegIdx2 != localSegIdx + 1))
						{
							SegmentM seg = polyline.Segment(bin3);
							if (segmentM.Intersects(seg, ref x, ref y, ref t, ref t2))
							{
								pointIntersects.Add(new PointIntersect(x, y, bin2, t, bin3, t2));
							}
						}
					}
				}
			}
		}
	}

	public const string sDirectionalArrows = "Directional Arrows";

	public const string sDrawStationingTicks = "Stationing Tick Marks";

	public const string sDrawPoints = "Draw Points";

	public const string sLabelPoints = "Label Points";

	public const string sLabelSegments = "Label Segment Indexes";

	protected IList<int> _partStartIndex;

	private int _segmentCount;

	private int[] _segmentPointAIndex;

	private double _length;

	private Point _centroid;

	private static Polyline _nullPolyline = new Polyline(new PointMs());

	private int[] _partFirstSegmentIndex;

	private List<PointIntersect> _selfIntersections;

	private const int MajorMinorTickRatio = 5;

	[SpecialName]
	private float[] _0024STATIC_0024ComputeBestTickSizes_0024203112814C10D10D_0024TickDistanceBase;

	[SpecialName]
	private StaticLocalInitFlag _0024STATIC_0024ComputeBestTickSizes_0024203112814C10D10D_0024TickDistanceBase_0024Init;

	public override bool UsingQuadTree => false;

	public virtual double Length
	{
		get
		{
			checked
			{
				if (double.IsNaN(_length))
				{
					_length = 0.0;
					int count = SegmentCount() - 1;
					for (int i = 0; i <= count; i++)
					{
						_length += Segment(i).Length;
					}
				}
				return _length;
			}
		}
	}

	public override bool UsesPointSymbol { get; }

	public override bool UsesPen { get; }

	public override bool UsesBrush { get; }

	public override bool UsesSurfaceFill { get; }

	public static Polyline NullPolyline()
	{
		return _nullPolyline;
	}

	public static bool IsValidPolyline(Polyline pline)
	{
		return pline?.IsValid() ?? false;
	}

	public virtual bool IsValid()
	{
		if (Count < 2)
		{
			return false;
		}
		return true;
	}

	public virtual bool HasConsecutiveIdenticalPoints([Optional][DefaultParameterValue(-1)] ref int dupIdx)
	{
		if (!IsValid())
		{
			return false;
		}
		checked
		{
			int count = Count - 1;
			for (int i = 1; i <= count; i++)
			{
				PointM pointM = PointM(i - 1);
				PointM pointM2 = PointM(i);
				if (pointM.EqualXY(pointM2))
				{
					dupIdx = i;
					return true;
				}
			}
			return false;
		}
	}

	public virtual bool HasConsecutiveNearybyPoints(double tol = 1E-05)
	{
		if (!IsValid())
		{
			return false;
		}
		checked
		{
			int count = Count - 1;
			for (int i = 1; i <= count; i++)
			{
				PointM pointM = PointM(i - 1);
				PointM ptM = PointM(i);
				if (pointM.DistanceTo(ptM) < tol)
				{
					return true;
				}
			}
			return false;
		}
	}

	protected Polyline()
	{
		_partStartIndex = null;
		_segmentPointAIndex = null;
		_length = double.NaN;
		_centroid = null;
		UsesPointSymbol = false;
		UsesPen = true;
		UsesBrush = false;
		UsesSurfaceFill = false;
	}

	public Polyline(PointMs pointMs)
		: base(pointMs)
	{
		_partStartIndex = null;
		_segmentPointAIndex = null;
		_length = double.NaN;
		_centroid = null;
		UsesPointSymbol = false;
		UsesPen = true;
		UsesBrush = false;
		UsesSurfaceFill = false;
	}

	public Polyline(PointMs pointMs, IList<int> partStartIndex)
		: base(pointMs)
	{
		_partStartIndex = null;
		_segmentPointAIndex = null;
		_length = double.NaN;
		_centroid = null;
		UsesPointSymbol = false;
		UsesPen = true;
		UsesBrush = false;
		UsesSurfaceFill = false;
		if (partStartIndex == null)
		{
			InitializePartStarts(null);
		}
		else
		{
			InitializePartStarts(partStartIndex.ToArray());
		}
	}

	public Polyline(PointMs pointMs, int[] partStartIndex)
		: base(pointMs)
	{
		_partStartIndex = null;
		_segmentPointAIndex = null;
		_length = double.NaN;
		_centroid = null;
		UsesPointSymbol = false;
		UsesPen = true;
		UsesBrush = false;
		UsesSurfaceFill = false;
		InitializePartStarts(partStartIndex);
	}

	public new static Polyline FromBuffers(IList<PointM> points)
	{
		Polyline polyline = new Polyline(new PointMs());
		polyline.OverrideBuffersAndReinitialize(points);
		polyline._segmentCount = checked(polyline.Count - 1);
		return polyline;
	}

	public List<int> PartStartArrayToList()
	{
		if (_partStartIndex == null)
		{
			return null;
		}
		return _partStartIndex.ToList();
	}

	public int[] PartStartArray()
	{
		if (_partStartIndex == null)
		{
			return null;
		}
		return _partStartIndex.ToArray();
	}

	private void InitializePartStarts(int[] partStartIndex)
	{
		checked
		{
			if (partStartIndex.IsNullOrEmpty())
			{
				_segmentCount = Count - 1;
				return;
			}
			int[] partStartIndexArray = new int[partStartIndex.Count() - 1 + 1];
			Array.Copy(partStartIndex, partStartIndexArray, partStartIndex.Count());
			_partStartIndex = partStartIndexArray;
			InitializeSegmentFirstIndexes();
		}
	}

	private void InitializePartStartsReuseBuff(IList<int> partStartIndex)
	{
		if (partStartIndex.IsNullOrEmpty())
		{
			_segmentCount = checked(Count - 1);
			return;
		}
		_partStartIndex = partStartIndex;
		InitializeSegmentFirstIndexes();
	}

	private void InitializeSegmentFirstIndexes()
	{
		if (_partStartIndex.IsNullOrEmpty())
		{
			throw new Exception("Initialize PartStartIndex first.");
		}
		checked
		{
			_segmentCount = Count - _partStartIndex.Count;
			int count = _partStartIndex.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				if (PointCountInPart(i) == 0)
				{
					_segmentCount++;
					_ = SharedData.IsTestingUtility;
				}
			}
			if (_segmentCount < 0)
			{
				_segmentCount = 0;
			}
			_segmentPointAIndex = new int[_segmentCount - 1 + 1];
			int ordinal = 0;
			int count2 = _partStartIndex.Count - 1;
			for (int j = 0; j <= count2; j++)
			{
				int partStartIndex = _partStartIndex[j];
				int count3 = PointCountInPart(j) - 2;
				for (int k = 0; k <= count3; k++)
				{
					_segmentPointAIndex[ordinal] = partStartIndex + k;
					ordinal++;
				}
			}
		}
	}

	protected override void PointDataChanged()
	{
		base.PointDataChanged();
		_centroid = null;
		ResetLength();
	}

	public static Polyline FromBuffers(IList<PointM> points, IList<int> partStartIndex)
	{
		Polyline polyline = new Polyline(new PointMs());
		polyline.OverrideBuffersAndReinitialize(points);
		return polyline;
	}

	protected void OverrideBuffersAndReinitialize(IList<PointM> points, IList<int> partStartIndexes)
	{
		OverrideBuffersAndReinitialize(points);
		InitializePartStartsReuseBuff(partStartIndexes);
	}

	public void Stream(BinaryWriter bw)
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

	public static Polyline Stream(BinaryReader br)
	{
		double[] readArray = Pipe.ReadArray<double>(br);
		if (readArray.Count() == 0)
		{
			return null;
		}
		checked
		{
			PointM[] pointMs = new PointM[(int)System.Math.Round((double)readArray.Count() / 2.0) - 1 + 1];
			int count = pointMs.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				pointMs[i] = new PointM(readArray[i * 2], readArray[i * 2 + 1]);
			}
			return new Polyline(new PointMs(pointMs));
		}
	}

	protected void Insert(PointM point, int insertIndex, PointMs newPointMs, List<int> newPartStarts)
	{
		checked
		{
			int count = insertIndex - 1;
			for (int i = 0; i <= count; i++)
			{
				newPointMs.Add(_pointM[i]);
			}
			newPointMs.Add(point);
			int count2 = Count - 1;
			for (int j = insertIndex; j <= count2; j++)
			{
				newPointMs.Add(_pointM[j]);
			}
			if (_partStartIndex == null)
			{
				return;
			}
			int count3 = _partStartIndex.Count - 1;
			for (int k = 0; k <= count3; k++)
			{
				if (_partStartIndex[k] > insertIndex)
				{
					newPartStarts.Add(_partStartIndex[k] + 1);
				}
				else
				{
					newPartStarts.Add(_partStartIndex[k]);
				}
			}
		}
	}

	public Polyline Remove(int removeIndex)
	{
		PointMs pointMs = new PointMs();
		List<int> intList = new List<int>();
		Remove(removeIndex, pointMs, intList, 2);
		if (pointMs.Count > 2)
		{
			return new Polyline(pointMs, intList.ToArray());
		}
		return null;
	}

	protected void Remove(int removeIndex, PointMs newPointMs, List<int> newPartStarts, int minPointsPerPart)
	{
		int partIndex = GetPartIndex(removeIndex);
		checked
		{
			int count = PartsCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (i == partIndex)
				{
					if (PointCountInPart(i) <= minPointsPerPart)
					{
						continue;
					}
					newPartStarts.Add(newPointMs.Count);
					int partStartIndex = PartStartIndex(i);
					int partEndIndex = PartEndIndex(i);
					for (int j = partStartIndex; j <= partEndIndex; j++)
					{
						if (removeIndex != j)
						{
							newPointMs.Add(_pointM[j]);
						}
					}
				}
				else
				{
					newPartStarts.Add(newPointMs.Count);
					int partStartIndex2 = PartStartIndex(i);
					int partEndIndex2 = PartEndIndex(i);
					for (int k = partStartIndex2; k <= partEndIndex2; k++)
					{
						newPointMs.Add(_pointM[k]);
					}
				}
			}
		}
	}

	public Polyline Clone()
	{
		PointMs pointMs = new PointMs();
		CopyTo(pointMs);
		return new Polyline(pointMs, _partStartIndex);
	}

	public Polyline DiscretizeLine(float spacing)
	{
		PointMs pointMs = new PointMs(_pointM);
		double maxStep = spacing;
		double lineLength = 0.0;
		return new Polyline(pointMs.DiscretizeLine(maxStep, ref lineLength));
	}

	public Polygon GetOffsetPolygonZ(float width, [Optional][DefaultParameterValue(null)] ref PointMs leftOffset, [Optional][DefaultParameterValue(null)] ref PointMs rightOffset)
	{
		return GetOffsetPolygonInternal(width, ref leftOffset, ref rightOffset, useFiltering: false);
	}

	public Polygon GetOffsetPolygon(float width, [Optional][DefaultParameterValue(null)] ref PointMs leftOffset, [Optional][DefaultParameterValue(null)] ref PointMs rightOffset)
	{
		return GetOffsetPolygonInternal(width, ref leftOffset, ref rightOffset, useFiltering: true);
	}

	private Polygon GetOffsetPolygonInternal(float width, ref PointMs leftOffset, ref PointMs rightOffset, bool useFiltering)
	{
		new OffsetStreamLinesFactory(0.0);
		if (leftOffset == null)
		{
			leftOffset = new PointMs();
		}
		else
		{
			leftOffset.Clear();
		}
		if (rightOffset == null)
		{
			rightOffset = new PointMs();
		}
		else
		{
			rightOffset.Clear();
		}
		OffsetStreamLinesFactory.ComputeSimpleOffset(width, CopyPoints(), leftOffset, rightOffset, useFiltering);
		checked
		{
			PointMs pointMs = new PointMs(leftOffset.Count + rightOffset.Count);
			int count = leftOffset.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				pointMs.Add(leftOffset[i]);
			}
			for (int j = rightOffset.Count - 1; j >= 0; j += -1)
			{
				pointMs.Add(rightOffset[j]);
			}
			return new Polygon(pointMs);
		}
	}

	public Polygon GetOffsetPolygon(float distLeft, float distRight, [Optional][DefaultParameterValue(null)] ref PointMs leftOffset, [Optional][DefaultParameterValue(null)] ref PointMs rightOffset)
	{
		new OffsetStreamLinesFactory(0.0);
		if (leftOffset == null)
		{
			leftOffset = new PointMs();
		}
		else
		{
			leftOffset.Clear();
		}
		if (rightOffset == null)
		{
			rightOffset = new PointMs();
		}
		else
		{
			rightOffset.Clear();
		}
		OffsetStreamLinesFactory.ComputeDirectionalOffset(distLeft, distRight, CopyPoints(), leftOffset, rightOffset);
		checked
		{
			PointMs pointMs = new PointMs(leftOffset.Count + rightOffset.Count);
			int count = leftOffset.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				pointMs.Add(leftOffset[i]);
			}
			for (int j = rightOffset.Count - 1; j >= 0; j += -1)
			{
				pointMs.Add(rightOffset[j]);
			}
			return new Polygon(pointMs);
		}
	}

	public void GetOffsetPolygons(float distLeft, float distRight, ref Polygon leftPolygon, ref Polygon rightPolygon)
	{
		new OffsetStreamLinesFactory(0.0);
		PointMs pointMs = new PointMs();
		PointMs pointMs2 = new PointMs();
		OffsetStreamLinesFactory.ComputeDirectionalOffset(distLeft, distRight, CopyPoints(), pointMs, pointMs2);
		checked
		{
			PointMs pointMs3 = new PointMs(pointMs.Count + Count);
			int count = pointMs.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				pointMs3.Add(pointMs[i]);
			}
			for (int j = Count - 1; j >= 0; j += -1)
			{
				pointMs3.Add(PointM(j));
			}
			leftPolygon = new Polygon(pointMs3);
			pointMs3 = new PointMs(pointMs2.Count + Count);
			int count2 = Count - 1;
			for (int k = 0; k <= count2; k++)
			{
				pointMs3.Add(PointM(k));
			}
			for (int l = pointMs2.Count - 1; l >= 0; l += -1)
			{
				pointMs3.Add(pointMs2[l]);
			}
			rightPolygon = new Polygon(pointMs3);
		}
	}

	public void GetOffsetPolygons(float distLeft, float distRight, float distBufferUpAndDown, ref Polygon leftPolygon, ref Polygon rightPolygon)
	{
		new OffsetStreamLinesFactory(0.0);
		PointMs pointMs = new PointMs();
		PointMs pointMs2 = new PointMs();
		SegmentM segmentM = Segment(0);
		segmentM.ExtendA(segmentM.Length + (double)distBufferUpAndDown);
		checked
		{
			SegmentM segmentM2 = Segment(SegmentCount() - 1);
			segmentM2.ExtendB(segmentM2.Length + (double)distBufferUpAndDown);
			PointMs pointMs3 = new PointMs(PointMCount() + 2);
			pointMs3.Add(segmentM.A);
			CopyTo(pointMs3);
			pointMs3.Add(segmentM2.B);
			OffsetStreamLinesFactory.ComputeDirectionalOffset(distLeft, distRight, pointMs3, pointMs, pointMs2);
			PointMs pointMs4 = new PointMs(pointMs.Count + pointMs3.Count);
			int count = pointMs.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				pointMs4.Add(pointMs[i]);
			}
			for (int j = pointMs3.Count - 1; j >= 0; j += -1)
			{
				pointMs4.Add(pointMs3.PointM(j));
			}
			leftPolygon = new Polygon(pointMs4);
			pointMs4 = new PointMs(pointMs2.Count + pointMs3.Count);
			int count2 = pointMs3.Count - 1;
			for (int k = 0; k <= count2; k++)
			{
				pointMs4.Add(pointMs3.PointM(k));
			}
			for (int l = pointMs2.Count - 1; l >= 0; l += -1)
			{
				pointMs4.Add(pointMs2[l]);
			}
			rightPolygon = new Polygon(pointMs4);
		}
	}

	public Polyline RemoveZM()
	{
		PointMs pointMs = new PointMs();
		CopyTo(pointMs);
		pointMs.RemoveZM();
		return new Polyline(pointMs, _partStartIndex);
	}

	public Polyline SetZ(double z)
	{
		PointMs pointMs = new PointMs();
		CopyTo(pointMs);
		pointMs.SetZ(z);
		return new Polyline(pointMs, _partStartIndex);
	}

	public Polyline SetM(double m)
	{
		PointMs pointMs = new PointMs();
		CopyTo(pointMs);
		pointMs.SetM(m);
		return new Polyline(pointMs, _partStartIndex);
	}

	public Polyline SetZM(double z, double m)
	{
		PointMs pointMs = new PointMs();
		CopyTo(pointMs);
		pointMs.SetZM(z, m);
		return new Polyline(pointMs, _partStartIndex);
	}

	public Polyline SetLinearZ(double startZ, double endZ)
	{
		PointMs pointMs = new PointMs();
		CopyTo(pointMs);
		pointMs.SetLinearZ(startZ, endZ);
		return new Polyline(pointMs, _partStartIndex);
	}

	public Polyline SetLinearM(double startM, double endM)
	{
		PointMs pointMs = new PointMs();
		CopyTo(pointMs);
		pointMs.SetLinearZ(startM, endM);
		return new Polyline(pointMs, _partStartIndex);
	}

	public Polyline SetLinearZM(double startZ, double endZ, double startM, double endM)
	{
		PointMs pointMs = new PointMs();
		CopyTo(pointMs);
		pointMs.SetLinearZM(startZ, endZ, startM, endM);
		return new Polyline(pointMs, _partStartIndex);
	}

	public Polyline Reverse()
	{
		PointMs pointMs = new PointMs();
		CopyTo(pointMs);
		pointMs.Reverse();
		return new Polyline(pointMs, _partStartIndex);
	}

	public Polyline FilterXY(double tolerance)
	{
		PointMs pointMs = new PointMs();
		CopyTo(pointMs);
		pointMs.FilterXY(tolerance);
		return new Polyline(pointMs);
	}

	public Polyline FilterXY_PRESERVEPARTS(double tolerance)
	{
		PointMs pointMs = new PointMs();
		List<int> intList = new List<int>();
		checked
		{
			int count = PartsCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				intList.Add(pointMs.Count);
				PointMs pointMs2 = new PointMs();
				int partStartIndex = PartStartIndex(i);
				int partEndIndex = PartEndIndex(i);
				for (int j = partStartIndex; j <= partEndIndex; j++)
				{
					pointMs2.Add(PointM(j));
				}
				pointMs2.FilterXY(tolerance);
				pointMs.AddRange(pointMs2);
			}
			return new Polyline(pointMs, intList.ToArray());
		}
	}

	public Polyline DedupeXY(float tol = 0.001f)
	{
		PointMs pointMs = new PointMs();
		if (Count == 0)
		{
			return new Polyline(pointMs);
		}
		pointMs.Add(_pointM[0]);
		checked
		{
			int count = _pointM.Count - 1;
			for (int i = 1; i <= count; i++)
			{
				if (!(_pointM[i].DistanceTo(pointMs.Last()) < (double)tol))
				{
					pointMs.Add(_pointM[i]);
				}
			}
			return new Polyline(pointMs);
		}
	}

	public Polyline Densify(double maxSegmentLength)
	{
		PointMs pointMs = new PointMs();
		CopyTo(pointMs);
		pointMs.Densify(maxSegmentLength);
		return new Polyline(pointMs);
	}

	public Polyline MakePolylineZFromProfile(Profile prof, ref List<PointM> injectedPts)
	{
		PointMs pointMs = new PointMs();
		injectedPts = new List<PointM>();
		pointMs.Add(new PointM(this.First().X, this.First().Y, prof.Query(0f)));
		int unity = 1;
		int ordinal = 0;
		double lengthSum = 0.0;
		PointOnPolyline pointOnPolyline = new PointOnPolyline(FirstPointM(), 0, 0.0);
		double runningSum = 0.0;
		checked
		{
			PointM(PointMCount() - 1);
			int segmentCount = SegmentCount();
			while (true)
			{
				float maxValue = ((unity != prof.Count - 2 && unity != prof.Count) ? ((float)((double)prof[unity].X - lengthSum)) : float.MaxValue);
				double length = Segment(ordinal).Length;
				if ((double)maxValue < length)
				{
					pointOnPolyline = PointAtDistance((double)maxValue + lengthSum, pointOnPolyline, ref runningSum);
					PointM pointM = pointOnPolyline.PointM;
					PointM pointM2 = new PointM(pointM.X, pointM.Y, prof[unity].Y);
					pointMs.Add(pointM2);
					injectedPts.Add(pointM2);
					unity++;
					continue;
				}
				PointM b = Segment(ordinal).B;
				pointMs.Add(new PointM(b.X, b.Y, prof.Query((float)(length + lengthSum))));
				lengthSum += length;
				ordinal++;
				if (ordinal == segmentCount)
				{
					break;
				}
			}
			if (System.Math.Abs(lengthSum - (double)prof.Length()) > 0.01 && (double)prof.Length() > lengthSum)
			{
				throw new ArgumentException("Profile length must match polyline length");
			}
			return new Polyline(pointMs);
		}
	}

	public PointMs DiscretizePart(int partIndex, double maxStep)
	{
		PointMs pointMs = new PointMs();
		checked
		{
			if (PointCountInPart(partIndex) > 0)
			{
				pointMs.Add(_pointM[PartStartIndex(partIndex)]);
				double partLength = PartLength(partIndex);
				int systemPartLengthTerm = (int)System.Math.Ceiling(partLength / maxStep);
				double partLengthTerm = partLength / (double)systemPartLengthTerm;
				double lengthSum = 0.0;
				double computed = partLengthTerm;
				int count = PartSegmentCount(partIndex) - 1;
				for (int i = 0; i <= count; i++)
				{
					SegmentM segmentM = PartSegment(partIndex, i);
					double length;
					for (length = segmentM.Length; !(computed > lengthSum + length); computed += partLengthTerm)
					{
						double t = (computed - lengthSum) / length;
						pointMs.Add(segmentM.PointAtFraction(t));
					}
					lengthSum += length;
				}
			}
			return pointMs;
		}
	}

	public void CopyTo(PointMs pointMs, int partIndex)
	{
		int partStartIndex = PartStartIndex(partIndex);
		int partEndIndex = PartEndIndex(partIndex);
		for (int i = partStartIndex; i <= partEndIndex; i = checked(i + 1))
		{
			pointMs.Add(_pointM[i]);
		}
	}

	public Polyline Transform(Transform transformer)
	{
		PointMs pointMs = new PointMs();
		pointMs.AddRange(_pointM);
		pointMs.Transform(transformer);
		if (_partStartIndex == null)
		{
			return new Polyline(pointMs);
		}
		return new Polyline(pointMs, _partStartIndex);
	}

	public int PartsCount()
	{
		if (_partStartIndex == null)
		{
			return 1;
		}
		return _partStartIndex.Count;
	}

	public int PointCountInPart(int partIndex)
	{
		if (_partStartIndex == null)
		{
			return Count;
		}
		checked
		{
			if (partIndex < _partStartIndex.Count - 1)
			{
				return _partStartIndex[partIndex + 1] - _partStartIndex[partIndex];
			}
			return Count - _partStartIndex[partIndex];
		}
	}

	public int PartStartIndex(int partIndex)
	{
		if (_partStartIndex == null)
		{
			return 0;
		}
		return _partStartIndex[partIndex];
	}

	public int PartEndIndex(int partIndex)
	{
		checked
		{
			if (_partStartIndex == null)
			{
				return Count - 1;
			}
			if (partIndex == _partStartIndex.Count - 1)
			{
				return Count - 1;
			}
			return _partStartIndex[partIndex + 1] - 1;
		}
	}

	public int GetPartIndex(int pointIndex)
	{
		if (_partStartIndex == null)
		{
			return 0;
		}
		checked
		{
			int count = _partStartIndex.Count - 1;
			for (int i = 1; i <= count; i++)
			{
				if (_partStartIndex[i] > pointIndex)
				{
					return i - 1;
				}
			}
			return _partStartIndex.Count - 1;
		}
	}

	public int SegmentCount()
	{
		if (Count <= 1)
		{
			return 0;
		}
		if (_segmentPointAIndex == null)
		{
			return checked(Count - 1);
		}
		return _segmentPointAIndex.Count();
	}

	int ISegments.SegmentCount()
	{
		//ILSpy generated this explicit interface implementation from .override directive in SegmentCount
		return this.SegmentCount();
	}

	public SegmentM Segment(int index)
	{
		int segmentPointAIndex = ((_segmentPointAIndex != null) ? _segmentPointAIndex[index] : index);
		SegmentM result = default;
		result.A = _pointM[segmentPointAIndex];
		result.B = _pointM[checked(segmentPointAIndex + 1)];
		result.Index = index;
		return result;
	}

	SegmentM ISegments.Segment(int index)
	{
		//ILSpy generated this explicit interface implementation from .override directive in Segment
		return this.Segment(index);
	}

	public int PartSegmentCount(int partIndex)
	{
		checked
		{
			if (_partStartIndex == null)
			{
				return Count - 1;
			}
			return PointCountInPart(partIndex) - 1;
		}
	}

	protected void ResetLength()
	{
		_length = double.NaN;
	}

	public double DistanceAlong(PointOnPolyline ptOnPoly)
	{
		double segmentLengthSum = 0.0;
		checked
		{
			int count = ptOnPoly.SegmentIndex - 1;
			for (int i = 0; i <= count; i++)
			{
				segmentLengthSum += Segment(i).Length;
			}
			return segmentLengthSum + Segment(ptOnPoly.SegmentIndex).Length * ptOnPoly.Fraction;
		}
	}

	public double DistanceAlongPart(PointOnPolyline ptOnPoly)
	{
		int partIndex = default;
		int localSegIdx = default;
		GetPartialSegmentIndex(ptOnPoly.SegmentIndex, ref partIndex, ref localSegIdx);
		double partSegmentLengthSum = 0.0;
		checked
		{
			int count = localSegIdx - 1;
			for (int i = 0; i <= count; i++)
			{
				partSegmentLengthSum += PartSegment(partIndex, i).Length;
			}
			return partSegmentLengthSum + Segment(ptOnPoly.SegmentIndex).Length * ptOnPoly.Fraction;
		}
	}

	public float[] DistancesAlongSorted(List<PointOnPolyline> pts)
	{
		checked
		{
			float[] floatValues = new float[pts.Count - 1 + 1];
			if (pts.IsNullOrEmpty())
			{
				return floatValues;
			}
			double lengthSum = 0.0;
			int ordinal = 0;
			PointOnPolyline pointOnPolyline = pts.First();
			int count = SegmentCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				double length = Segment(i).Length;
				while (pointOnPolyline.SegmentIndex == i)
				{
					double fraction = pointOnPolyline.Fraction;
					floatValues[ordinal] = (float)(lengthSum + fraction * length);
					ordinal++;
					if (ordinal == pts.Count)
					{
						goto end_IL_0097;
					}
					pointOnPolyline = pts[ordinal];
				}
				lengthSum += length;
				continue;
				end_IL_0097:
				break;
			}
			return floatValues;
		}
	}

	public double LengthAlong(PointOnPolyline startPtOnPoly, PointOnPolyline stopPtOnPoly)
	{
		double distanceAlong = DistanceAlong(startPtOnPoly);
		return DistanceAlong(stopPtOnPoly) - distanceAlong;
	}

	public double PartLength(int partIndex)
	{
		double partSegmentLengthSum = 0.0;
		checked
		{
			int count = PartSegmentCount(partIndex) - 1;
			for (int i = 0; i <= count; i++)
			{
				partSegmentLengthSum += PartSegment(partIndex, i).Length;
			}
			return partSegmentLengthSum;
		}
	}

	public double[] CumulativeSegmentLengths()
	{
		checked
		{
			double[] doubleValues = new double[SegmentCount() - 1 + 1];
			double segmentTerm = 0.0;
			int count = doubleValues.Length - 1;
			for (int i = 0; i <= count; i++)
			{
				segmentTerm = (doubleValues[i] = segmentTerm + Segment(i).Length);
			}
			return doubleValues;
		}
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
			cumulativeSegmentLengthSum += cumulativeSegmentLengths[checked(ptOnPoly.SegmentIndex - 1)];
		}
		return cumulativeSegmentLengthSum + Segment(ptOnPoly.SegmentIndex).Length * ptOnPoly.Fraction;
	}

	public override PointM Centroid()
	{
		if (_centroid == null)
		{
			_centroid = new Point(PointAtFraction(0.5).PointM);
		}
		return _centroid.PointM();
	}

	public SegmentM PartSegment(int partIndex, int localSegmentIdx)
	{
		checked
		{
			int localSegmentIdx2 = ((_partStartIndex != null) ? (_partStartIndex[partIndex] + localSegmentIdx) : localSegmentIdx);
			SegmentM result = default;
			result.A = _pointM[localSegmentIdx2];
			result.B = _pointM[localSegmentIdx2 + 1];
			result.Index = localSegmentIdx2;
			return result;
		}
	}

	private void EnsureSegmentPartStarts()
	{
		checked
		{
			if (_partFirstSegmentIndex == null && _partStartIndex != null)
			{
				_partFirstSegmentIndex = new int[_partStartIndex.Count - 1 + 1];
				int partSegmentCountSum = 0;
				int count = PartsCount() - 1;
				for (int i = 0; i <= count; i++)
				{
					_partFirstSegmentIndex[i] = partSegmentCountSum;
					partSegmentCountSum += PartSegmentCount(i);
				}
			}
		}
	}

	public void GetPartialSegmentIndex(int globalSegIdx, ref int partIndex, ref int localSegIdx)
	{
		EnsureSegmentPartStarts();
		if (_partFirstSegmentIndex == null || _partFirstSegmentIndex.Count() <= 1)
		{
			partIndex = 0;
			localSegIdx = globalSegIdx;
			return;
		}
		int partIndex2 = Array.BinarySearch(_partFirstSegmentIndex, globalSegIdx);
		checked
		{
			if (partIndex2 < 0)
			{
				partIndex2 = ~partIndex2 - 1;
			}
			partIndex = partIndex2;
			localSegIdx = globalSegIdx - _partFirstSegmentIndex[partIndex2];
		}
	}

	public SegmentM SegmentFirstToLast()
	{
		SegmentM result = default;
		result.A = _pointM[0];
		result.B = _pointM[checked(Count - 1)];
		return result;
	}

	public SegmentM SegmentFirstToLast(int partIndex)
	{
		SegmentM result = default;
		result.A = _pointM[PartStartIndex(partIndex)];
		result.B = _pointM[PartEndIndex(partIndex)];
		return result;
	}

	public List<SegmentM> Segments(int partIndex)
	{
		List<SegmentM> segmentMs = new List<SegmentM>();
		if (PointCountInPart(partIndex) == 0)
		{
			return segmentMs;
		}
		int partStartIndex = PartStartIndex(partIndex);
		checked
		{
			int count = PartEndIndex(partIndex) - 1;
			for (int i = partStartIndex; i <= count; i++)
			{
				segmentMs.Add(new SegmentM(_pointM[i], _pointM[i + 1]));
			}
			return segmentMs;
		}
	}

	public virtual Polyline PolylineSubsection(PointOnPolyline startPoint, PointOnPolyline endPoint)
	{
		PointMs pointMs = new PointMs();
		PointSubsection(startPoint, endPoint, pointMs);
		return new Polyline(pointMs);
	}

	public Polyline PolylineSubsection(float startSta, float endSta)
	{
		PointMs pointMs = new PointMs();
		PointSubsection(PointAtDistance(startSta), PointAtDistance(endSta), pointMs);
		return new Polyline(pointMs);
	}

	public PointMs PointSubsection(PointOnPolyline startPoint, PointOnPolyline endPoint)
	{
		PointMs pointMs = new PointMs();
		pointMs.Add(startPoint.PointM);
		checked
		{
			int segmentIndexOffset = startPoint.SegmentIndex + 1;
			int segmentIndex = endPoint.SegmentIndex;
			for (int i = segmentIndexOffset; i <= segmentIndex; i++)
			{
				pointMs.Add(_pointM[i]);
			}
			if (endPoint.Fraction > 0.0)
			{
				pointMs.Add(endPoint.PointM);
			}
			return pointMs;
		}
	}

	public void PointSubsection(PointOnPolyline startPoint, PointOnPolyline endPoint, PointMs pointMs)
	{
		pointMs.Clear();
		pointMs.Add(startPoint.PointM);
		checked
		{
			int segmentIndexOffset = startPoint.SegmentIndex + 1;
			int segmentIndex = endPoint.SegmentIndex;
			for (int i = segmentIndexOffset; i <= segmentIndex; i++)
			{
				pointMs.Add(_pointM[i]);
			}
			if (endPoint.Fraction > 0.0)
			{
				pointMs.Add(endPoint.PointM);
			}
		}
	}

	public void PointsBeforePoP(PointOnPolyline point, PointMs pointMs)
	{
		pointMs.Clear();
		int segmentIndex = point.SegmentIndex;
		for (int i = 0; i <= segmentIndex; i = checked(i + 1))
		{
			pointMs.Add(_pointM[i]);
		}
		if (point.Fraction > 0.0)
		{
			pointMs.Add(point.PointM);
		}
	}

	public void PointsAfterPoP(PointOnPolyline point, PointMs pointMs)
	{
		pointMs.Clear();
		pointMs.Add(point.PointM);
		checked
		{
			int segmentIndexOffset = point.SegmentIndex + 1;
			int count = Count - 1;
			for (int i = segmentIndexOffset; i <= count; i++)
			{
				pointMs.Add(_pointM[i]);
			}
		}
	}

	public int NearestPointIndex(PointOnPolyline ptOnPoly, ref double distance)
	{
		int segmentIndex = 0;
		segmentIndex = ((!(ptOnPoly.Fraction < 0.5)) ? checked(ptOnPoly.SegmentIndex + 1) : ptOnPoly.SegmentIndex);
		distance = _pointM[segmentIndex].DistanceTo(ptOnPoly.PointM);
		return segmentIndex;
	}

	public SpatialIndex<int> GenerateSegmentSpatialIndex()
	{
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(this, SpatialIndex.ReasonableSegmentBinCount(SegmentCount()));
		checked
		{
			int count = SegmentCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				spatialIndex.Add(Segment(i), i);
			}
			return spatialIndex;
		}
	}

	public bool Intersects(Polyline polyline)
	{
		return GetLineIntersection(polyline) != null;
	}

	public PointIntersect GetLineIntersection(Polyline polyline)
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
				SegmentM segmentM = Segment(i);
				int count2 = polyline.SegmentCount() - 1;
				for (int j = 0; j <= count2; j++)
				{
					if (segmentM.Intersects(polyline.Segment(j), ref x, ref y, ref t, ref t2))
					{
						return new PointIntersect(x, y, i, t, j, t2);
					}
				}
			}
			return null;
		}
	}

	public List<PointIntersect> GetLineIntersectionMulti(Polyline polyline, [Optional][DefaultParameterValue(null)] ref SpatialIndex<int> segmentSpIdx)
	{
		List<PointIntersect> pointIntersects = new List<PointIntersect>();
		if (segmentSpIdx == null)
		{
			segmentSpIdx = GenerateSegmentSpatialIndex();
		}
		checked
		{
			int count = polyline.SegmentCount() - 1;
			double x = default;
			double y = default;
			double t = default;
			double t2 = default;
			for (int i = 0; i <= count; i++)
			{
				SegmentM segmentM = polyline.Segment(i);
				List<int> elements = segmentSpIdx.GetElements(segmentM);
				int count2 = elements.Count - 1;
				for (int j = 0; j <= count2; j++)
				{
					int element = elements[j];
					if (!Segment(element).Intersects(segmentM, ref x, ref y, ref t, ref t2))
					{
						continue;
					}
					if (pointIntersects.Count != 0)
					{
						PointIntersect pointIntersect = pointIntersects.Last();
						if (pointIntersect.Point.X == x && pointIntersect.Point.Y == y)
						{
							continue;
						}
					}
					pointIntersects.Add(new PointIntersect(x, y, element, t, i, t2));
				}
			}
			return pointIntersects;
		}
	}

	public List<PointIntersect> GetAllSelfIntersections()
	{
		if (_selfIntersections != null)
		{
			return _selfIntersections;
		}
		_selfIntersections = new List<PointIntersect>();
		SpatialIndex<int> spatialIndex = GenerateSegmentSpatialIndex();
		HashSet<Tuple<int, int>> valueTupleSet = new HashSet<Tuple<int, int>>();
		_Closure_0024__99_002D0 capturedLocals = default;
		_Closure_0024__99_002D0 capturedLocals2 = new _Closure_0024__99_002D0(capturedLocals);
		capturedLocals2.polyline = this;
		capturedLocals2.tlPointIntersectList = new ThreadLocal<List<PointIntersect>>([SpecialName] () => new List<PointIntersect>(), trackAllValues: true);
		EnsureSegmentPartStarts();
		Parallel.ForEach(spatialIndex.Bins, checked([SpecialName] (List<int> bin) =>
		{
			List<PointIntersect> pointIntersects = capturedLocals2.tlPointIntersectList.Value;
			int count = bin.Count - 2;
			int partIndex = default;
			int localSegIdx = default;
			int partIndex2 = default;
			int localSegIdx2 = default;
			double x = default;
			double y = default;
			double t = default;
			double t2 = default;
			for (int i = 0; i <= count; i++)
			{
				int bin2 = bin[i];
				SegmentM segmentM = capturedLocals2.polyline.Segment(bin2);
				capturedLocals2.polyline.GetPartialSegmentIndex(bin2, ref partIndex, ref localSegIdx);
				int ordinal = i + 1;
				int count2 = bin.Count - 1;
				for (int j = ordinal; j <= count2; j++)
				{
					int bin3 = bin[j];
					capturedLocals2.polyline.GetPartialSegmentIndex(bin3, ref partIndex2, ref localSegIdx2);
					if (partIndex != partIndex2 || (localSegIdx2 != localSegIdx && localSegIdx2 != localSegIdx - 1 && localSegIdx2 != localSegIdx + 1))
					{
						SegmentM seg = capturedLocals2.polyline.Segment(bin3);
						if (segmentM.Intersects(seg, ref x, ref y, ref t, ref t2))
						{
							pointIntersects.Add(new PointIntersect(x, y, bin2, t, bin3, t2));
						}
					}
				}
			}
		}));
		foreach (List<PointIntersect> pointIntersectsList in capturedLocals2.tlPointIntersectList.Values)
		{
			foreach (PointIntersect pointIntersect in pointIntersectsList)
			{
				int aSegmentIndex = pointIntersect.ASegmentIndex;
				int bSegmentIndex = pointIntersect.BSegmentIndex;
				Tuple<int, int> aSegmentIndexBSegmentIndexTerm = ((aSegmentIndex >= bSegmentIndex) ? new Tuple<int, int>(bSegmentIndex, aSegmentIndex) : new Tuple<int, int>(aSegmentIndex, bSegmentIndex));
				if (valueTupleSet.Add(aSegmentIndexBSegmentIndexTerm))
				{
					_selfIntersections.Add(pointIntersect);
				}
			}
		}
		return _selfIntersections;
	}

	public bool Intersects(SegmentM seg)
	{
		return GetLineIntersection(seg) != null;
	}

	public PointIntersect GetLineIntersection(SegmentM segment)
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
					return new PointIntersect(x, y, i, t, -1, t2);
				}
			}
			return null;
		}
	}

	public List<PointIntersect> GetLineIntersectionMulti(SegmentM segment)
	{
		List<PointIntersect> pointIntersects = new List<PointIntersect>();
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
					pointIntersects.Add(new PointIntersect(x, y, i, t, -1, t2));
				}
			}
			return pointIntersects;
		}
	}

	public bool Intersects(Polyline polyline, double nearDistance)
	{
		checked
		{
			int count = polyline.SegmentCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (Intersects(polyline.Segment(i), nearDistance))
				{
					return true;
				}
			}
			return false;
		}
	}

	public bool Intersects(SegmentM seg, double nearDistance)
	{
		checked
		{
			int count = SegmentCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				SegmentM segmentM = Segment(i);
				if (segmentM.Intersects(seg))
				{
					return true;
				}
				PointM a = seg.A;
				PointM b = seg.B;
				PointM a2 = segmentM.A;
				PointM b2 = segmentM.B;
				SegmentM.RelativePosition relativePosition = SegmentM.RelativePosition.Undefined;
				if (segmentM.DistanceToPoint(a, ref relativePosition) < nearDistance)
				{
					return true;
				}
				relativePosition = SegmentM.RelativePosition.Undefined;
				if (segmentM.DistanceToPoint(b, ref relativePosition) < nearDistance)
				{
					return true;
				}
				relativePosition = SegmentM.RelativePosition.Undefined;
				if (seg.DistanceToPoint(a2, ref relativePosition) < nearDistance)
				{
					return true;
				}
				relativePosition = SegmentM.RelativePosition.Undefined;
				if (seg.DistanceToPoint(b2, ref relativePosition) < nearDistance)
				{
					return true;
				}
			}
			return false;
		}
	}

	public bool RoughlyFollows(Polyline plineOther, double nearDistance)
	{
		double actualDistance = default;
		PointOnPolyline pointOnPolyline = plineOther.NearestPointOnPolyline(FirstPointM(), ref actualDistance);
		if (actualDistance > nearDistance)
		{
			return false;
		}
		checked
		{
			int count = PointMCount() - 2;
			for (int i = 1; i <= count; i++)
			{
				PointM ptM = PointM(i);
				plineOther.NearestPointOnPolyline(ptM, ref actualDistance);
				if (actualDistance > nearDistance)
				{
					return false;
				}
			}
			PointOnPolyline pointOnPolyline2 = plineOther.NearestPointOnPolyline(LastPointM(), ref actualDistance);
			if (actualDistance > nearDistance)
			{
				return false;
			}
			int segmentIndexOffset = pointOnPolyline.SegmentIndex + 1;
			int segmentIndex = pointOnPolyline2.SegmentIndex;
			int segmentIndex2 = segmentIndex;
			for (int j = segmentIndexOffset; j <= segmentIndex2; j++)
			{
				PointM ptM2 = plineOther.PointM(j);
				NearestPointOnPolyline(ptM2, ref actualDistance);
				if (actualDistance > nearDistance)
				{
					return false;
				}
			}
			return true;
		}
	}

	public bool RoughlyFollowsOrIsOutside(Polygon pgon, double tol, DevelopmentTin devTin = null)
	{
		if (devTin == null)
		{
			devTin = PolygonFeatureLayer.ComputeDevelopmentTIN(pgon);
		}
		checked
		{
			int count = PointMCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (devTin.ContainsPointBufferedFavorOutOfBounds(PointM(i), tol))
				{
					return false;
				}
			}
			double actualDistance = default;
			PointOnPolyline pointOnPolyline = pgon.NearestPointOnPolyline(FirstPointM(), ref actualDistance);
			PointOnPolyline pointOnPolyline2 = pgon.NearestPointOnPolyline(LastPointM(), ref actualDistance);
			int segmentIndexOffset = pointOnPolyline.SegmentIndex + 1;
			int segmentIndex = pointOnPolyline2.SegmentIndex;
			int segmentIndex2 = segmentIndex;
			for (int j = segmentIndexOffset; j <= segmentIndex2; j++)
			{
				PointM ptM = pgon.PointM(j);
				PointOnPolyline pointOnPolyline3 = NearestPointOnPolyline(ptM, ref actualDistance);
				if (actualDistance > tol && devTin.ContainsPointBufferedFavorOutOfBounds(pointOnPolyline3.PointM, tol))
				{
					return false;
				}
			}
			return true;
		}
	}

	public bool RoughlyFollowsOrIsInside(Polygon pgon, double tol, DevelopmentTin devTin = null)
	{
		if (devTin == null)
		{
			devTin = PolygonFeatureLayer.ComputeDevelopmentTIN(pgon);
		}
		checked
		{
			int count = PointMCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (!devTin.ContainsPointBufferedFavorInBounds(PointM(i), tol))
				{
					return false;
				}
			}
			double actualDistance = default;
			PointOnPolyline pointOnPolyline = pgon.NearestPointOnPolyline(FirstPointM(), ref actualDistance);
			PointOnPolyline pointOnPolyline2 = pgon.NearestPointOnPolyline(LastPointM(), ref actualDistance);
			int segmentIndexOffset = pointOnPolyline.SegmentIndex + 1;
			int segmentIndex = pointOnPolyline2.SegmentIndex;
			int segmentIndex2 = segmentIndex;
			for (int j = segmentIndexOffset; j <= segmentIndex2; j++)
			{
				PointM ptM = pgon.PointM(j);
				PointOnPolyline pointOnPolyline3 = NearestPointOnPolyline(ptM, ref actualDistance);
				if (actualDistance > tol && devTin.ContainsPointBufferedFavorOutOfBounds(pointOnPolyline3.PointM, tol))
				{
					return false;
				}
			}
			throw new NotImplementedException();
		}
	}

	public bool IsStrictlyInside(Polygon pgon, double nearDistance, DevelopmentTin devTin = null)
	{
		if (devTin == null)
		{
			devTin = PolygonFeatureLayer.ComputeDevelopmentTIN(pgon);
		}
		checked
		{
			int count = PointMCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (!devTin.ContainsPointBufferedFavorOutOfBounds(PointM(i), nearDistance))
				{
					return false;
				}
			}
			double actualDistance = default;
			PointOnPolyline pointOnPolyline = pgon.NearestPointOnPolyline(FirstPointM(), ref actualDistance);
			PointOnPolyline pointOnPolyline2 = pgon.NearestPointOnPolyline(LastPointM(), ref actualDistance);
			int segmentIndexOffset = pointOnPolyline.SegmentIndex + 1;
			int segmentIndex = pointOnPolyline2.SegmentIndex;
			int segmentIndex2 = segmentIndex;
			for (int j = segmentIndexOffset; j <= segmentIndex2; j++)
			{
				PointM ptM = pgon.PointM(j);
				PointOnPolyline pointOnPolyline3 = NearestPointOnPolyline(ptM, ref actualDistance);
				if (!devTin.ContainsPointBufferedFavorOutOfBounds(pointOnPolyline3.PointM, nearDistance))
				{
					return false;
				}
			}
			return true;
		}
	}

	public double DistanceToPoint(PointM ptM)
	{
		double maxValue = double.MaxValue;
		checked
		{
			int count = SegmentCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				SegmentM segmentM = Segment(i);
				SegmentM.RelativePosition relativePosition = SegmentM.RelativePosition.Undefined;
				double distanceToPoint = segmentM.DistanceToPoint(ptM, ref relativePosition);
				if (distanceToPoint < maxValue)
				{
					maxValue = distanceToPoint;
				}
			}
			return maxValue;
		}
	}

	public List<PointOnPolyline> CopyPointsAsPointOnPolyline()
	{
		List<PointOnPolyline> pointOnPolylines = new List<PointOnPolyline>();
		pointOnPolylines.Add(new PointOnPolyline(PointM(0), 0, 0.0));
		checked
		{
			int count = PointMCount() - 1;
			for (int i = 1; i <= count; i++)
			{
				pointOnPolylines.Add(new PointOnPolyline(PointM(i), i - 1, 1.0));
			}
			return pointOnPolylines;
		}
	}

	public PointOnPolyline NearestPointOnPolyline(PointM ptm, PointOnPolyline lastPop, [Optional][DefaultParameterValue(0.0)] ref double actualDistance)
	{
		int segStartIdx = 0;
		if (lastPop != null)
		{
			segStartIdx = lastPop.SegmentIndex;
		}
		return NearestPointOnPolylineInternal(ptm, ref actualDistance, segStartIdx);
	}

	public PointOnPolyline NearestPointOnPolyline(PointM ptM, [Optional][DefaultParameterValue(0.0)] ref double actualDistance)
	{
		return NearestPointOnPolylineInternal(ptM, ref actualDistance);
	}

	private PointOnPolyline NearestPointOnPolylineInternal(PointM ptm, [Optional][DefaultParameterValue(0.0)] ref double actualDistance, int segStartIdx = 0)
	{
		if (Count == 0)
		{
			return null;
		}
		if (Count == 1)
		{
			return FirstPointOnPolyline();
		}
		actualDistance = double.MaxValue;
		bool condition = false;
		checked
		{
			int count = SegmentCount() - 1;
			int ordinal = default;
			for (int i = segStartIdx; i <= count; i++)
			{
				SegmentM segmentM = Segment(i);
				SegmentM.RelativePosition relativePosition = SegmentM.RelativePosition.Undefined;
				double actualDistance2 = segmentM.DistanceToPoint(ptm, ref relativePosition);
				if (actualDistance2 < actualDistance)
				{
					actualDistance = actualDistance2;
					ordinal = i;
					condition = true;
				}
			}
			if (!condition)
			{
				Console.WriteLine();
			}
			double t = default;
			PointM pointM = Segment(ordinal).NearestPoint(ptm, ref t);
			return new PointOnPolyline(pointM, ordinal, t);
		}
	}

	public PointOnPolyline NearestPointOnPolyline(PointM ptM, PointM vector, double tolerance = 1E-06)
	{
		double factor = System.Math.Pow(System.Math.Pow(MaxX - MinX, 2.0) + System.Math.Pow(MaxY - MinY, 2.0), 0.5);
		PointM delta = vector.UnitVector().Ratio(factor);
		PointM pB = ptM.Translate(delta);
		SegmentM seg = new SegmentM(ptM, pB);
		PointOnPolyline result = null;
		double maxValue = double.MaxValue;
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
				if (segmentM.Intersects(seg, ref x, ref y, ref t, ref t2, tolerance) && t2 < maxValue)
				{
					result = new PointOnPolyline(segmentM.PointAtFraction(t), i, t);
					maxValue = t2;
				}
			}
			if (maxValue == double.MaxValue)
			{
				PointM ptM2 = ptM;
				double actualDistance = 0.0;
				return NearestPointOnPolyline(ptM2, ref actualDistance);
			}
			return result;
		}
	}

	public override bool Intersects(PointM PointM, double nearDistance)
	{
		checked
		{
			int count = SegmentCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (Segment(i).Intersects(PointM, nearDistance))
				{
					return true;
				}
			}
			return false;
		}
	}

	public override bool Intersects(Extent extent)
	{
		checked
		{
			int count = SegmentCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (Segment(i).Intersects(extent))
				{
					return true;
				}
			}
			return false;
		}
	}

	public PointOnPolyline FirstPointOnPolyline()
	{
		if (Count == 0)
		{
			return null;
		}
		return new PointOnPolyline(_pointM.First(), 0, 0.0);
	}

	public PointOnPolyline LastPointOnPolyline()
	{
		if (Count == 0)
		{
			return null;
		}
		return new PointOnPolyline(_pointM.Last(), checked(SegmentCount() - 1), 1.0);
	}

	public PointOnPolyline Midpoint()
	{
		return PointAtFraction(0.5);
	}

	public PointOnPolyline PointAtFraction(double fraction)
	{
		if (fraction <= 0.0)
		{
			return FirstPointOnPolyline();
		}
		if (fraction >= 1.0)
		{
			return LastPointOnPolyline();
		}
		double lengthSum = 0.0;
		fraction = RasMapperLib.Utilities.Math.GetInRange(fraction, 0.0, 1.0);
		double lengthTimesFraction = Length * fraction;
		checked
		{
			int count = SegmentCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				double lengthSum2 = lengthSum;
				double length = Segment(i).Length;
				lengthSum += length;
				if (lengthSum >= lengthTimesFraction)
				{
					double lengthTimesFractionAboveLengthSum2OverLength = (lengthTimesFraction - lengthSum2) / length;
					return new PointOnPolyline(Segment(i).PointAtFraction(lengthTimesFractionAboveLengthSum2OverLength), i, lengthTimesFractionAboveLengthSum2OverLength);
				}
			}
			return LastPointOnPolyline();
		}
	}

	public PointOnPolyline PointAtDistance(double distance)
	{
		double sum = 0.0;
		return PointAtDistanceInternal(distance, ref sum);
	}

	public PointOnPolyline PointAtDistance(double distance, PointOnPolyline lastPop, ref double runningSum)
	{
		return PointAtDistanceInternal(distance, ref runningSum, lastPop.SegmentIndex);
	}

	private PointOnPolyline PointAtDistanceInternal(double distance, [Optional][DefaultParameterValue(0.0)] ref double sum, int segStartIdx = 0)
	{
		if (distance <= 0.0)
		{
			return FirstPointOnPolyline();
		}
		if (distance >= Length)
		{
			return LastPointOnPolyline();
		}
		double sum2 = sum;
		checked
		{
			int count = SegmentCount() - 1;
			for (int i = segStartIdx; i <= count; i++)
			{
				sum2 = sum;
				double length = Segment(i).Length;
				sum += length;
				if (sum >= distance)
				{
					double distanceAboveSum2OverLength = (distance - sum2) / length;
					sum = sum2;
					return new PointOnPolyline(Segment(i).PointAtFraction(distanceAboveSum2OverLength), i, distanceAboveSum2OverLength);
				}
			}
			return LastPointOnPolyline();
		}
	}

	public List<PointOnPolyline> PointsAtDistancesSorted(List<double> distances)
	{
		List<PointOnPolyline> pointOnPolylines = new List<PointOnPolyline>(distances.Count);
		if (distances.IsNullOrEmpty())
		{
			return pointOnPolylines;
		}
		double lengthSum = 0.0;
		double lengthSum2 = 0.0;
		int ordinal = 0;
		double distancesTerm = distances.First();
		checked
		{
			int count = SegmentCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				lengthSum2 = lengthSum;
				SegmentM segmentM = Segment(i);
				double length = segmentM.Length;
				lengthSum += length;
				while (lengthSum >= distancesTerm)
				{
					double lengthSum2LengthTerm = (distancesTerm - lengthSum2) / length;
					pointOnPolylines.Add(new PointOnPolyline(segmentM.PointAtFraction(lengthSum2LengthTerm), i, lengthSum2LengthTerm));
					ordinal++;
					if (ordinal == distances.Count)
					{
						goto end_IL_00ab;
					}
					distancesTerm = distances[ordinal];
				}
				continue;
				end_IL_00ab:
				break;
			}
			PointOnPolyline lastPointOnPolyline = LastPointOnPolyline();
			while (distances.Count > pointOnPolylines.Count)
			{
				pointOnPolylines.Add(lastPointOnPolyline);
			}
			return pointOnPolylines;
		}
	}

	public List<PointOnPolyline> SplitLineEvenly(double approxSplitLength, [Optional][DefaultParameterValue(0.0)] ref double actualSplitLength)
	{
		checked
		{
			int systemApproxSplitLengthTerm = (int)System.Math.Round(Length / approxSplitLength);
			if (systemApproxSplitLengthTerm <= 1)
			{
				actualSplitLength = Length;
				return new PointOnPolyline[2]
				{
					FirstPointOnPolyline(),
					LastPointOnPolyline()
				}.ToList();
			}
			actualSplitLength = Length / (double)systemApproxSplitLengthTerm;
			List<PointOnPolyline> splitLineExactly = SplitLineExactly(actualSplitLength);
			if (splitLineExactly.Count != systemApproxSplitLengthTerm + 1)
			{
				splitLineExactly.Add(LastPointOnPolyline());
			}
			return splitLineExactly;
		}
	}

	public List<PointOnPolyline> SplitLineExactly(double splitLength, bool addLastPoint = false)
	{
		if (splitLength <= 0.0)
		{
			return new List<PointOnPolyline>();
		}
		double length = Length;
		checked
		{
			int capacity = (int)System.Math.Ceiling(length / splitLength) + 1;
			List<PointOnPolyline> pointOnPolylines = new List<PointOnPolyline>(capacity);
			pointOnPolylines.Add(FirstPointOnPolyline());
			double length2Sum = 0.0;
			double length2Sum2 = 0.0;
			double splitLength2 = splitLength;
			int count = SegmentCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				length2Sum2 = length2Sum;
				SegmentM segmentM = Segment(i);
				double length2 = segmentM.Length;
				length2Sum += length2;
				while (length2Sum >= splitLength2)
				{
					double splitLength2AboveLength2Sum2OverLength2 = (splitLength2 - length2Sum2) / length2;
					pointOnPolylines.Add(new PointOnPolyline(segmentM.PointAtFraction(splitLength2AboveLength2Sum2OverLength2), i, splitLength2AboveLength2Sum2OverLength2));
					splitLength2 += splitLength;
					if (splitLength2 > length)
					{
						goto end_IL_00be;
					}
				}
				continue;
				end_IL_00be:
				break;
			}
			if (addLastPoint && pointOnPolylines.Last().PointM.DistanceTo(LastPointOnPolyline().PointM) > 0.0001)
			{
				pointOnPolylines.Add(LastPointOnPolyline());
			}
			return pointOnPolylines;
		}
	}

	public List<PointOnPolyline> SplitLineTargetValues(double splitLengthDivisor)
	{
		if (checked((int)System.Math.Ceiling(Length / splitLengthDivisor)) <= 1)
		{
			return new PointOnPolyline[2]
			{
				FirstPointOnPolyline(),
				LastPointOnPolyline()
			}.ToList();
		}
		return SplitLineExactly(splitLengthDivisor, addLastPoint: true);
	}

	public PointM PointMOnLine(int segIndex, double fraction)
	{
		if (fraction == 0.0)
		{
			return _pointM[segIndex];
		}
		if (fraction == 1.0)
		{
			return _pointM[checked(segIndex + 1)];
		}
		return Segment(segIndex).PointAtFraction(fraction);
	}

	public override string ToString()
	{
		return "Polyline" + PointLabel(HasZ, HasM);
	}

	protected override string ToSString(string separatorCharacter, string pointSeparatorCharacter, string xyFormat, string zmFormat)
	{
		StringBuilder stringBuilder = new StringBuilder();
		checked
		{
			int count = PartsCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (i > 0)
				{
					stringBuilder.Append(pointSeparatorCharacter + pointSeparatorCharacter);
				}
				int partStartIndex = PartStartIndex(i);
				int partEndIndex = PartEndIndex(i);
				for (int j = partStartIndex; j <= partEndIndex; j++)
				{
					stringBuilder.Append(_pointM[j].ToSString(separatorCharacter, xyFormat, zmFormat));
					if (j < PartEndIndex(i))
					{
						stringBuilder.Append(pointSeparatorCharacter);
					}
				}
			}
			return stringBuilder.ToString();
		}
	}

	private void ComputeBestTickSizes(RasterM raster, ref double majorTickDistance, ref double minorTickDistance)
	{
		_ = Length;
		double scaledCellSize = 100.0 * raster.CellSize;
		double systemTerm = System.Math.Pow(10.0, System.Math.Floor(System.Math.Log10(scaledCellSize)));
		double computed = scaledCellSize / systemTerm;
		if (_0024STATIC_0024ComputeBestTickSizes_0024203112814C10D10D_0024TickDistanceBase_0024Init == null)
		{
			Interlocked.CompareExchange(ref _0024STATIC_0024ComputeBestTickSizes_0024203112814C10D10D_0024TickDistanceBase_0024Init, new StaticLocalInitFlag(), null);
		}
		bool lockTaken = false;
		try
		{
			Monitor.Enter(_0024STATIC_0024ComputeBestTickSizes_0024203112814C10D10D_0024TickDistanceBase_0024Init, ref lockTaken);
			if (_0024STATIC_0024ComputeBestTickSizes_0024203112814C10D10D_0024TickDistanceBase_0024Init.State == 0)
			{
				_0024STATIC_0024ComputeBestTickSizes_0024203112814C10D10D_0024TickDistanceBase_0024Init.State = 2;
				_0024STATIC_0024ComputeBestTickSizes_0024203112814C10D10D_0024TickDistanceBase = new float[4] { 1f, 2.5f, 5f, 10f };
			}
			else if (_0024STATIC_0024ComputeBestTickSizes_0024203112814C10D10D_0024TickDistanceBase_0024Init.State == 2)
			{
				throw new IncompleteInitialization();
			}
		}
		finally
		{
			_0024STATIC_0024ComputeBestTickSizes_0024203112814C10D10D_0024TickDistanceBase_0024Init.State = 1;
			if (lockTaken)
			{
				Monitor.Exit(_0024STATIC_0024ComputeBestTickSizes_0024203112814C10D10D_0024TickDistanceBase_0024Init);
			}
		}
		float maxValue = float.MaxValue;
		float computed2 = 0f;
		checked
		{
			int count = _0024STATIC_0024ComputeBestTickSizes_0024203112814C10D10D_0024TickDistanceBase.Length - 1;
			for (int i = 0; i <= count; i++)
			{
				float computed3 = _0024STATIC_0024ComputeBestTickSizes_0024203112814C10D10D_0024TickDistanceBase[i];
				float maxValue2 = (float)System.Math.Abs(computed - (double)computed3);
				if (maxValue2 < maxValue)
				{
					maxValue = maxValue2;
					computed2 = computed3;
				}
			}
			majorTickDistance = (double)computed2 * systemTerm;
			minorTickDistance = majorTickDistance / 5.0;
		}
	}

	internal static int ComputeBestRoundingDisplay(double majorTickDist)
	{
		if (majorTickDist > 1000.0)
		{
			return 0;
		}
		if (majorTickDist > 100.0)
		{
			return 1;
		}
		if (majorTickDist > 10.0)
		{
			return 2;
		}
		if (majorTickDist > 1.0)
		{
			return 3;
		}
		if (majorTickDist > 0.1)
		{
			return 4;
		}
		if (majorTickDist > 0.01)
		{
			return 5;
		}
		return 6;
	}

	public override List<PlotOption> AvailablePlotOptions(bool debugMode)
	{
		return GetAvailablePlotOptions(debugMode);
	}

	public new static List<PlotOption> GetAvailablePlotOptions(bool debugMode)
	{
		return new List<PlotOption>(new PlotOption[5]
		{
			new PlotOption("Directional Arrows"),
			new PlotOption("Stationing Tick Marks"),
			new PlotOption("Draw Points"),
			new PlotOption("Label Points"),
			new PlotOption("Label Segment Indexes")
		});
	}

	public override IEditableFeature GetMutableView()
	{
		return new PolylineEditable(this);
	}
}
