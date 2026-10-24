using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Geospatial.DataStructures;
using Geospatial.Transformers;
using Geospatial.Vectors.Intersections;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.DataStructures;
using Utility.Extensions;
using Utility.Hashing;
using Utility.Logging;
using Utility.Systems;
using Utility.Unsafe;

namespace Geospatial.Vectors;

[DebuggerDisplay("{Count} Points")]
public class PointCollection : IPointCollection, IVectorCollection<Point>, IReadOnlyVectorCollection<Point>, IEnumerable<Point>, IEnumerable, ISpatialVersion, IExtent, IReadOnlyList<Point>, IReadOnlyCollection<Point>, IReadOnlyPointCollection, ISpatiallyIndexedPointCollection, IList<Point>, ICollection<Point>, IMutableSpatialVersion, IDataHash, IStreamableObject
{
	public delegate void OnDataModifiedEvent();

	private class TrackList<T> : IList<T>, ICollection<T>, IEnumerable<T>, IEnumerable
	{
		private int _version;

		private readonly List<T> _track;

		public T this[int index]
		{
			get
			{
				return _track[index];
			}
			set
			{
				_track[index] = value;
				Interlocked.Increment(ref _version);
			}
		}

		public int Version => _version;

		public int Count => _track.Count;

		public bool IsReadOnly => false;

		public TrackList(List<T> track)
		{
			_track = track;
		}

		public void Add(T localValue)
		{
			throw new NotSupportedException();
		}

		public void Clear()
		{
			throw new NotSupportedException();
		}

		public bool Contains(T localValue)
		{
			throw new NotSupportedException();
		}

		public void CopyTo(T[] track, int arrayIndex)
		{
			_track.CopyTo(track, arrayIndex);
		}

		public IEnumerator<T> GetEnumerator()
		{
			return _track.GetEnumerator();
		}

		public int IndexOf(T localValue)
		{
			throw new NotSupportedException();
		}

		public void Insert(int index, T localValue)
		{
			throw new NotSupportedException();
		}

		public bool Remove(T localValue)
		{
			throw new NotSupportedException();
		}

		public void RemoveAt(int index)
		{
			throw new NotSupportedException();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return _track.GetEnumerator();
		}
	}

	public const int DefaultStartingCapacity = 0;

	public const double DefaultFillValue = double.NaN;

	protected static readonly ILogger _logger = LogHelper.GetLogger("PointCollection");

	private long _xyVersion = 1L;

	protected List<Point> _points;

	private Versioned<Extent> _extCache;

	private Versioned<SpatialIndex<int>> _pointSpIdxCache;

	protected List<double> _z;

	private IList<double> _zView;

	protected List<double> _m;

	private IList<double> _mView;

	private IReadOnlyList<Point3D> _p3d;

	private IReadOnlyList<Point4D> _p4d;

	public long VersionXY => _xyVersion;

	private long VersionZM
	{
		get
		{
			long versionSum = 0L;
			if (HasZ)
			{
				versionSum += (Z as TrackList<double>).Version;
			}
			if (HasM)
			{
				versionSum += (M as TrackList<double>).Version;
			}
			return versionSum;
		}
	}

	public long FullVersion => VersionXY + VersionZM;

	public int PointCount => _points.Count;

	public int Count => _points.Count;

	public IList<Point> Points => this;

	public int Capacity
	{
		get
		{
			return _points.Capacity;
		}
		set
		{
			_points.Capacity = value;
			if (HasZ)
			{
				_z.Capacity = value;
			}
			if (HasM)
			{
				_m.Capacity = value;
			}
		}
	}

	public Extent Extent => LazyThreadsafe<Versioned<Extent>>.AllocateOnce(ref _extCache, this, () => new Versioned<Extent>()).Get(VersionXY, this, (PointCollection pc) => pc.ComputeExtent());

	public SpatialIndex<int> PointSpatialIndex => LazyThreadsafe<Versioned<SpatialIndex<int>>>.AllocateOnce(ref _pointSpIdxCache, this, () => new Versioned<SpatialIndex<int>>()).Get(VersionXY, this, ComputePointSpatialIndex);

	public IList<double> Z => LazyThreadsafe<IList<double>>.AllocateOnce(ref _zView, this, this, (PointCollection pc) => pc.CreateZView());

	public bool HasZ { get; private set; }

	public double ZFillValue { get; private set; } = double.NaN;

	public IList<double> M => LazyThreadsafe<IList<double>>.AllocateOnce(ref _mView, this, this, (PointCollection pc) => pc.CreateMView());

	public bool HasM { get; private set; }

	public double MFillValue { get; private set; } = double.NaN;

	public bool IsReadOnly => false;

	public Point this[int index]
	{
		get
		{
			return _points[index];
		}
		set
		{
			_points[index] = value;
			PointDataModifiedThreadsafe();
		}
	}

	public IReadOnlyList<Point3D> Point3D
	{
		get
		{
			return LazyThreadsafe<IReadOnlyList<Geospatial.Vectors.Point3D>>.AllocateOnce(ref _p3d, this, this, (PointCollection t) => new VirtualPoint3DCollection(t));
		}
		private set
		{
			_p3d = value;
		}
	}

	public IReadOnlyList<Point4D> Point4D
	{
		get
		{
			return LazyThreadsafe<IReadOnlyList<Geospatial.Vectors.Point4D>>.AllocateOnce(ref _p4d, this, this, (PointCollection t) => new VirtualPoint4DCollection(t));
		}
		private set
		{
			_p4d = value;
		}
	}

	public ReaderWriterLock Lock
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public event OnDataModifiedEvent OnDataModified;

	public void FreezeOne()
	{
	}

	public void ThawOne()
	{
	}

	public static PointCollection NullPointCollection()
	{
		return new PointCollection();
	}

	public Memory<Point> GetPointBufferUnsafe()
	{
		return UnsafeHelpers.GetListBackingBuffer(_points);
	}

	public void SetPointBufferUnsafe(Point[] points)
	{
		UnsafeHelpers.SetListBackingBuffer(_points, points);
		PointDataModifiedThreadsafe();
	}

	public Memory<double> GetZBufferUnsafe()
	{
		if (!HasZ)
		{
			return Memory<double>.Empty;
		}
		return UnsafeHelpers.GetListBackingBuffer(_z);
	}

	public Memory<double> GetMBufferUnsafe()
	{
		if (!HasM)
		{
			return Memory<double>.Empty;
		}
		return UnsafeHelpers.GetListBackingBuffer(_m);
	}

	public Point First()
	{
		return _points[0];
	}

	public Point Last()
	{
		return _points[PointCount - 1];
	}

	public PointCollection(bool hasZ = false, bool hasM = false)
		: this(0, hasZ, hasM)
	{
	}

	public PointCollection(int capacity, bool hasZ = false, bool hasM = false)
	{
		if (capacity < 0)
		{
			capacity = 0;
		}
		_points = new List<Point>(capacity);
		if (hasZ)
		{
			_z = new List<double>(capacity);
		}
		if (hasM)
		{
			_m = new List<double>(capacity);
		}
		InitializeZM();
	}

	public PointCollection()
	{
		_points = new List<Point>(0);
		InitializeZM();
	}

	public PointCollection(IEnumerable<Point> points)
	{
		_points = points?.ToList();
		InitializeZM();
	}

	public PointCollection(Point[] points)
		: this((IEnumerable<Point>)points)
	{
	}

	public PointCollection(Memory<Point> points)
	{
		_points = new List<Point>();
		UnsafeHelpers.SetListBackingBuffer(_points, points.ToArray());
		InitializeZM();
	}

	public PointCollection(Memory<Point> points, Memory<double> z)
	{
		_points = new List<Point>();
		UnsafeHelpers.SetListBackingBuffer(_points, points.ToArray());
		_z = new List<double>();
		UnsafeHelpers.SetListBackingBuffer(_z, z.ToArray());
		InitializeZM();
	}

	public PointCollection(Memory<Point> points, Memory<double> z, Memory<double> m)
	{
		_points = new List<Point>();
		UnsafeHelpers.SetListBackingBuffer(_points, points.ToArray());
		if (!z.IsEmpty)
		{
			_z = new List<double>();
			UnsafeHelpers.SetListBackingBuffer(_z, z.ToArray());
		}
		if (!m.IsEmpty)
		{
			_m = new List<double>();
			UnsafeHelpers.SetListBackingBuffer(_m, m.ToArray());
		}
		InitializeZM();
	}

	public PointCollection(List<Point> points)
	{
		_points = points.ToList();
		InitializeZM();
	}

	public PointCollection(List<Point> points, List<double> z)
	{
		_points = points.ToList();
		_z = z.ToList();
		InitializeZM();
	}

	public PointCollection(List<Point> points, List<double> z, List<double> m)
	{
		_points = points.ToList();
		_z = z.ToList();
		_m = m.ToList();
		InitializeZM();
	}

	public PointCollection(IList<Point3D> points)
	{
		_points = new List<Point>(points.Count);
		_z = new List<double>(points.Count);
		for (int i = 0; i < points.Count; i++)
		{
			_points.Add(points[i].AsPoint2D());
			_z.Add(points[i].Z);
		}
		InitializeZM();
	}

	public PointCollection(IEnumerable<Point3D> points)
	{
		_points = new List<Point>();
		_z = new List<double>();
		foreach (Point3D point in points)
		{
			_points.Add(point.AsPoint2D());
			_z.Add(point.Z);
		}
		InitializeZM();
	}

	public PointCollection(PointCollection pointCollection)
	{
		_points = pointCollection._points.ToList();
		_z = pointCollection._z?.ToList();
		_m = pointCollection._m?.ToList();
		InitializeZM();
	}

	public PointCollection(IReadOnlyList<double> xs, IReadOnlyList<double> ys)
	{
		_points = new List<Point>(xs.Count);
		for (int i = 0; i < xs.Count; i++)
		{
			_points.Add(new Point(xs[i], ys[i]));
		}
		InitializeZM();
	}

	private void InitializeZM()
	{
		if (_z != null && _z.Count != PointCount)
		{
			throw new ArgumentException("Points/Z input buffers cannot be different lengths.");
		}
		if (_m != null && _m.Count != PointCount)
		{
			throw new ArgumentException("Points/M input buffers cannot be different lengths.");
		}
		HasZ = _z != null;
		HasM = _m != null;
		_zView = null;
		_mView = null;
	}

	private IList<double> CreateZView()
	{
		if (!HasZ)
		{
			return new ConstReadOnlyList<PointCollection, double>(this, (PointCollection line) => line.ZFillValue, (PointCollection line) => line.Count);
		}
		return new TrackList<double>(_z);
	}

	private IList<double> CreateMView()
	{
		if (!HasM)
		{
			return new ConstReadOnlyList<PointCollection, double>(this, (PointCollection line) => line.MFillValue, (PointCollection line) => line.Count);
		}
		return new TrackList<double>(_m);
	}

	public void TrackZ()
	{
		if (!HasZ)
		{
			_z = new List<double>();
			_z.AddRange(Enumerable.Repeat(ZFillValue, PointCount));
			InitializeZM();
		}
	}

	public void UntrackZ()
	{
		if (HasZ)
		{
			_z = null;
			InitializeZM();
		}
	}

	public void TrackZ(IEnumerable<double> zValues)
	{
		if (zValues == null || zValues.Count() != Count)
		{
			throw new Exception("Incorrect z values buffer.");
		}
		_z = zValues.ToList();
		InitializeZM();
	}

	public void TrackZ(ReadOnlySpan<double> zValues)
	{
		if (zValues.Length != Count)
		{
			throw new Exception("Incorrect sized z values buffer.");
		}
		_z = new List<double>(Count);
		for (int i = 0; i < Count; i++)
		{
			_z.Add(zValues[i]);
		}
		InitializeZM();
	}

	public void TrackM()
	{
		if (!HasM)
		{
			_m = new List<double>();
			_m.AddRange(Enumerable.Repeat(MFillValue, PointCount));
			InitializeZM();
		}
	}

	public void UntrackM()
	{
		if (HasM)
		{
			_m = null;
			InitializeZM();
		}
	}

	public void TrackM(IEnumerable<double> mValues)
	{
		if (mValues == null || mValues.Count() != Count)
		{
			throw new Exception("Incorrect m values buffer.");
		}
		_m = mValues.ToList();
		InitializeZM();
	}

	public void TrackM(ReadOnlySpan<double> mValues)
	{
		if (mValues.Length != Count)
		{
			throw new Exception("Incorrect sized m values buffer.");
		}
		_m = new List<double>(Count);
		for (int i = 0; i < Count; i++)
		{
			_m.Add(mValues[i]);
		}
		InitializeZM();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	protected void PointDataModified()
	{
		_xyVersion++;
		OnDataModified?.Invoke();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	protected void PointDataModifiedThreadsafe()
	{
		Interlocked.Increment(ref _xyVersion);
		OnDataModified?.Invoke();
	}

	public void ForceSetVersion(long amount)
	{
		_xyVersion = amount;
	}

	public void ForceIncrementVersion()
	{
		_xyVersion++;
	}

	protected void AssignBuffers(List<Point> pts, List<double> z, List<double> m)
	{
		_points = pts;
		_z = z;
		_m = m;
		InitializeZM();
	}

	protected void AssignBuffers(Point[] pts, double[] z, double[] m)
	{
		if (_points == null)
		{
			_points = new List<Point>();
			Debugger.Break();
		}
		UnsafeHelpers.SetListBackingBuffer(_points, pts);
		if (z != null)
		{
			if (_z == null)
			{
				_z = new List<double>();
			}
			UnsafeHelpers.SetListBackingBuffer(_z, z);
		}
		if (m != null)
		{
			if (_m == null)
			{
				_m = new List<double>();
			}
			UnsafeHelpers.SetListBackingBuffer(_m, m);
		}
		InitializeZM();
	}

	protected void AssignBuffers(PointCollection pc)
	{
		AssignBuffers(pc._points, pc._z, pc._m);
	}

	public static PointCollection CreateWithBuffer(List<Point> points)
	{
		PointCollection pointCollection = new PointCollection(0);
		pointCollection.AssignBuffers(points, null, null);
		return pointCollection;
	}

	public static PointCollection CreateWithBuffer(Point[] points, double[] z = null, double[] m = null)
	{
		PointCollection pointCollection = new PointCollection(0);
		pointCollection.AssignBuffers(points, z, m);
		return pointCollection;
	}

	public static PointCollection CreateWithBuffer(PointCollection points)
	{
		PointCollection pointCollection = new PointCollection(0);
		pointCollection.AssignBuffers(points);
		return pointCollection;
	}

	protected virtual Extent ComputeExtent()
	{
		double maxValue = double.MaxValue;
		double minValue = double.MinValue;
		double maxValue2 = double.MaxValue;
		double minValue2 = double.MinValue;
		for (int i = 0; i < Count; i++)
		{
			Point point = this[i];
			if (point.X < maxValue)
			{
				maxValue = point.X;
			}
			if (point.X > minValue)
			{
				minValue = point.X;
			}
			if (point.Y < maxValue2)
			{
				maxValue2 = point.Y;
			}
			if (point.Y > minValue2)
			{
				minValue2 = point.Y;
			}
		}
		return new Extent(maxValue, minValue, maxValue2, minValue2);
	}

	private SpatialIndex<int> ComputePointSpatialIndex()
	{
		return ComputePointSpatialIndex(this);
	}

	private static SpatialIndex<int> ComputePointSpatialIndex(PointCollection pc)
	{
		return SpatialIndex.FromPoints(pc._points);
	}

	public virtual Point ComputeCentroid()
	{
		double computed = 0.0;
		double computed2 = 0.0;
		for (int i = 0; i < Count; i++)
		{
			Point point = _points[i];
			computed += point.X;
			computed2 += point.Y;
		}
		return new Point(computed / (double)Count, computed2 / (double)Count);
	}

	public static Extent ComputeExtent(Point[] points)
	{
		return ComputeExtent(points.AsSpan());
	}

	public static Extent ComputeExtent(Memory<Point> points)
	{
		return ComputeExtent(points.Span);
	}

	public static Extent ComputeExtent(List<Point> points)
	{
		return ComputeExtent(CollectionsMarshal.AsSpan(points));
	}

	public static Extent ComputeExtent(IList<Point> points)
	{
		if (points.Count == 0)
		{
			return Extent.NullExtent;
		}
		double maxValue = double.MaxValue;
		double minValue = double.MinValue;
		double maxValue2 = double.MaxValue;
		double minValue2 = double.MinValue;
		for (int i = 0; i < points.Count; i++)
		{
			Point point = points[i];
			if (point.X < maxValue)
			{
				maxValue = point.X;
			}
			if (point.X > minValue)
			{
				minValue = point.X;
			}
			if (point.Y < maxValue2)
			{
				maxValue2 = point.Y;
			}
			if (point.Y > minValue2)
			{
				minValue2 = point.Y;
			}
		}
		return new Extent(maxValue, minValue, maxValue2, minValue2);
	}

	public static Extent ComputeExtent<T>(IList<T> points, Func<T, Point> getPoint)
	{
		double maxValue = double.MaxValue;
		double minValue = double.MinValue;
		double maxValue2 = double.MaxValue;
		double minValue2 = double.MinValue;
		for (int i = 0; i < points.Count; i++)
		{
			Point point = getPoint(points[i]);
			if (point.X < maxValue)
			{
				maxValue = point.X;
			}
			if (point.X > minValue)
			{
				minValue = point.X;
			}
			if (point.Y < maxValue2)
			{
				maxValue2 = point.Y;
			}
			if (point.Y > minValue2)
			{
				minValue2 = point.Y;
			}
		}
		return new Extent(maxValue, minValue, maxValue2, minValue2);
	}

	public static Extent ComputeExtent(Span<Point> ptSpan)
	{
		double maxValue = double.MaxValue;
		double minValue = double.MinValue;
		double maxValue2 = double.MaxValue;
		double minValue2 = double.MinValue;
		for (int i = 0; i < ptSpan.Length; i++)
		{
			Point point = ptSpan[i];
			if (point.X < maxValue)
			{
				maxValue = point.X;
			}
			if (point.X > minValue)
			{
				minValue = point.X;
			}
			if (point.Y < maxValue2)
			{
				maxValue2 = point.Y;
			}
			if (point.Y > minValue2)
			{
				minValue2 = point.Y;
			}
		}
		return new Extent(maxValue, minValue, maxValue2, minValue2);
	}

	public static void BuildExtent(Span<Point> points, ref double minX, ref double maxX, ref double minY, ref double maxY)
	{
		for (int i = 0; i < points.Length; i++)
		{
			Point point = points[i];
			minX = Math.Min(minX, point.X);
			maxX = Math.Max(maxX, point.X);
			minY = Math.Min(minY, point.Y);
			maxY = Math.Max(maxY, point.Y);
		}
	}

	public static Extent ComputeValidExtent(Memory<Point> points)
	{
		double maxValue = double.MaxValue;
		double minValue = double.MinValue;
		double maxValue2 = double.MaxValue;
		double minValue2 = double.MinValue;
		Span<Point> pointsSpan = points.Span;
		for (int i = 0; i < pointsSpan.Length; i++)
		{
			Point point = pointsSpan[i];
			double x = point.X;
			double y = point.Y;
			if (!double.IsNaN(x) && x != double.MaxValue && x != double.MinValue && !double.IsInfinity(x) && !double.IsNaN(y) && y != double.MaxValue && y != double.MinValue && !double.IsInfinity(y))
			{
				if (x < maxValue)
				{
					maxValue = x;
				}
				if (x > minValue)
				{
					minValue = x;
				}
				if (y < maxValue2)
				{
					maxValue2 = y;
				}
				if (y > minValue2)
				{
					minValue2 = y;
				}
			}
		}
		if (maxValue == double.MaxValue)
		{
			return Extent.NullExtent;
		}
		return new Extent(maxValue, minValue, maxValue2, minValue2);
	}

	public void MD5HashAppend(Hash hash)
	{
		double[] doubleValues = new double[_points.Count * 2];
		for (int i = 0; i < _points.Count; i++)
		{
			doubleValues[i * 2] = _points[i].X;
			doubleValues[i * 2 + 1] = _points[i].Y;
		}
		hash.Add(doubleValues);
		if (HasZ)
		{
			hash.Add(_z.ToArray());
		}
		if (HasM)
		{
			hash.Add(_m.ToArray());
		}
	}

	public void Add(double x, double y)
	{
		Add(new Point(x, y));
	}

	public void Add(Point point)
	{
		AddInternal(point);
		PointDataModified();
	}

	private void AddInternal(Point point)
	{
		_points.Add(point);
		if (HasZ)
		{
			_z.Add(ZFillValue);
		}
		if (HasM)
		{
			_m.Add(MFillValue);
		}
	}

	private void AddInternal(double x, double y)
	{
		_points.Add(new Point(x, y));
		if (HasZ)
		{
			_z.Add(ZFillValue);
		}
		if (HasM)
		{
			_m.Add(MFillValue);
		}
	}

	public unsafe void AddRangeXY(double* arr, int start, int pointCount)
	{
		for (int i = start; i < start + pointCount; i++)
		{
			_points.Add(new Point(arr[i * 2], arr[i * 2 + 1]));
		}
		if (HasZ && HasM)
		{
			for (int j = 0; j < pointCount; j++)
			{
				_z.Add(ZFillValue);
				_m.Add(MFillValue);
			}
		}
		else if (HasZ)
		{
			for (int k = 0; k < pointCount; k++)
			{
				_z.Add(ZFillValue);
			}
		}
		else if (HasM)
		{
			for (int l = 0; l < pointCount; l++)
			{
				_m.Add(MFillValue);
			}
		}
		PointDataModified();
	}

	public void Add(double x, double y, double z)
	{
		_points.Add(new Point(x, y));
		if (HasZ)
		{
			_z.Add(z);
		}
		if (HasM)
		{
			_m.Add(MFillValue);
		}
		PointDataModified();
	}

	public void Add(Point3D point3D)
	{
		_points.Add(point3D.AsPoint2D());
		if (HasZ)
		{
			_z.Add(point3D.Z);
		}
		if (HasM)
		{
			_m.Add(MFillValue);
		}
		PointDataModified();
	}

	public void Add(Point pt, double z)
	{
		_points.Add(pt);
		if (HasZ)
		{
			_z.Add(z);
		}
		if (HasM)
		{
			_m.Add(MFillValue);
		}
		PointDataModified();
	}

	public void Add(double x, double y, double z, double m)
	{
		_points.Add(new Point(x, y));
		if (HasZ)
		{
			_z.Add(z);
		}
		if (HasM)
		{
			_m.Add(m);
		}
		PointDataModified();
	}

	public void Add(Point4D point4D)
	{
		_points.Add(point4D.AsPoint2D());
		if (HasZ)
		{
			_z.Add(point4D.Z);
		}
		if (HasM)
		{
			_m.Add(point4D.M);
		}
		PointDataModified();
	}

	public void Add(Point pt, double z, double m)
	{
		_points.Add(pt);
		if (HasZ)
		{
			_z.Add(z);
		}
		if (HasM)
		{
			_m.Add(m);
		}
		PointDataModified();
	}

	public void AddWithinTolerance(Point point, double colinearTolerance)
	{
		if (Count <= 1)
		{
			AddInternal(point);
		}
		else if (this[Count - 1].CloseToXY(point, colinearTolerance))
		{
			this[Count - 1] = point;
		}
		else if (new Segment(this[Count - 2], point).DistanceToPoint(Last()) < colinearTolerance)
		{
			this[Count - 1] = point;
		}
		else
		{
			AddInternal(point);
		}
		PointDataModified();
	}

	public void AddNew(Point Point)
	{
		if (Count == 0)
		{
			Add(Point);
		}
		else if (!Last().EqualXY(in Point))
		{
			Add(Point);
		}
	}

	public void AddRange(Point[] points)
	{
		AddRange(points.AsSpan());
	}

	public void AddRange(List<Point> points)
	{
		AddRange((IList<Point>)points);
	}

	public void AddRange(IList<Point> points)
	{
		if (points == null || points.Count == 0)
		{
			return;
		}
		_points.AddRange(points);
		if (HasZ)
		{
			double zFillValue = ZFillValue;
			for (int i = 0; i < points.Count; i++)
			{
				_z.Add(zFillValue);
			}
		}
		if (HasM)
		{
			double mFillValue = MFillValue;
			for (int j = 0; j < points.Count; j++)
			{
				_m.Add(mFillValue);
			}
		}
		PointDataModified();
	}

	public void AddRange(Span<Point> points)
	{
		if (points.Length == 0)
		{
			return;
		}
		_points.AddRange(points);
		if (HasZ)
		{
			double zFillValue = ZFillValue;
			for (int i = 0; i < points.Length; i++)
			{
				_z.Add(zFillValue);
			}
		}
		if (HasM)
		{
			double mFillValue = MFillValue;
			for (int j = 0; j < points.Length; j++)
			{
				_m.Add(mFillValue);
			}
		}
		PointDataModified();
	}

	public void AddRange(PointCollection pc)
	{
		if (pc.HasZ && !HasZ)
		{
			TrackZ();
		}
		if (pc.HasM && !HasM)
		{
			TrackM();
		}
		_points.AddRange(pc.Points);
		if (HasZ)
		{
			if (pc.HasZ)
			{
				_z.AddRange(pc._z);
			}
			else
			{
				double zFillValue = ZFillValue;
				for (int i = 0; i < pc.Count; i++)
				{
					_z.Add(zFillValue);
				}
			}
		}
		if (HasM)
		{
			if (pc.HasM)
			{
				_m.AddRange(pc._m);
			}
			else
			{
				double mFillValue = MFillValue;
				for (int j = 0; j < pc.Count; j++)
				{
					_m.Add(mFillValue);
				}
			}
		}
		PointDataModified();
	}

	public void AddRange(int startIndex, int count, PointCollection pc)
	{
		if (pc.HasZ && !HasZ)
		{
			TrackZ();
		}
		if (pc.HasM && !HasM)
		{
			TrackM();
		}
		for (int i = startIndex; i < startIndex + count; i++)
		{
			_points.Add(pc.Points[i]);
		}
		if (HasZ)
		{
			if (pc.HasZ)
			{
				for (int j = startIndex; j < startIndex + count; j++)
				{
					_z.Add(pc._z[j]);
				}
			}
			else
			{
				double zFillValue = ZFillValue;
				for (int k = startIndex; k < startIndex + count; k++)
				{
					_z.Add(zFillValue);
				}
			}
		}
		if (HasM)
		{
			if (pc.HasM)
			{
				for (int l = startIndex; l < startIndex + count; l++)
				{
					_m.Add(pc._m[l]);
				}
			}
			else
			{
				double mFillValue = MFillValue;
				for (int m = startIndex; m < startIndex + count; m++)
				{
					_m.Add(mFillValue);
				}
			}
		}
		PointDataModified();
	}

	public void AddRange(IReadOnlyList<Point> points)
	{
		_points.AddRange(points);
		if (HasZ)
		{
			double zFillValue = ZFillValue;
			for (int i = 0; i < points.Count; i++)
			{
				_z.Add(zFillValue);
			}
		}
		if (HasM)
		{
			double mFillValue = MFillValue;
			for (int j = 0; j < points.Count; j++)
			{
				_m.Add(mFillValue);
			}
		}
		PointDataModified();
	}

	public void AddRange(IList<Point> points, double[] z)
	{
		_points.AddRange(points);
		if (HasZ)
		{
			_z.AddRange(z);
		}
		if (HasM)
		{
			double mFillValue = MFillValue;
			for (int i = 0; i < points.Count; i++)
			{
				_m.Add(mFillValue);
			}
		}
		PointDataModified();
	}

	public void AddRange(IList<Point> points, double[] z, double[] m)
	{
		_points.AddRange(points);
		if (HasZ)
		{
			_z.AddRange(z);
		}
		if (HasM)
		{
			_m.AddRange(m);
		}
		PointDataModified();
	}

	public void AddRange(IArray<Point> points)
	{
		_points.AddRange(points);
		if (HasZ)
		{
			double zFillValue = ZFillValue;
			for (int i = 0; i < points.Count; i++)
			{
				_z.Add(zFillValue);
			}
		}
		if (HasM)
		{
			double mFillValue = MFillValue;
			for (int j = 0; j < points.Count; j++)
			{
				_m.Add(mFillValue);
			}
		}
		PointDataModified();
	}

	public void AddRange(IList<Point> points, double nearTolerance)
	{
		if (Capacity < Count + points.Count)
		{
			Capacity = Count + points.Count;
		}
		bool condition = false;
		for (int i = 0; i < points.Count; i++)
		{
			Point point = points[i];
			if (Count == 0)
			{
				AddInternal(point);
				condition = true;
			}
			else if (!Last().CloseToXY(point, nearTolerance))
			{
				AddInternal(point);
				condition = true;
			}
		}
		if (condition)
		{
			PointDataModified();
		}
	}

	public void AddRange(IList<Point> points, PointOnPolyline startPoint, PointOnPolyline endPoint)
	{
		AddInternal(startPoint.Point);
		for (int i = startPoint.SegmentIndex + 1; i <= endPoint.SegmentIndex; i++)
		{
			AddInternal(points[i]);
		}
		if (endPoint.Fraction > 0.0)
		{
			AddInternal(endPoint.Point);
		}
		PointDataModified();
	}

	public void AddRange(double[] x, double[] y)
	{
		for (int i = 0; i < x.Length; i++)
		{
			AddInternal(x[i], y[i]);
		}
		PointDataModified();
	}

	public void AddRange(int startIndex, int count, double[] x, double[] y)
	{
		for (int i = startIndex; i < startIndex + count; i++)
		{
			AddInternal(x[i], y[i]);
		}
		PointDataModified();
	}

	public void AddRangeReverse(IList<Point> ptMs)
	{
		if (Capacity < Count + ptMs.Count)
		{
			Capacity = Count + ptMs.Count;
		}
		for (int i = ptMs.Count - 1; i >= 0; i += -1)
		{
			AddInternal(ptMs[i]);
		}
		PointDataModified();
	}

	public void AddRangeReverse(Span<Point> ptMs)
	{
		if (Capacity < Count + ptMs.Length)
		{
			Capacity = Count + ptMs.Length;
		}
		for (int i = ptMs.Length - 1; i >= 0; i += -1)
		{
			AddInternal(ptMs[i]);
		}
		PointDataModified();
	}

	public void InsertRange(int offset, IList<Point> ptMs, int start, int mCount)
	{
		int count = Count;
		if (Capacity < Count + mCount)
		{
			Capacity = Count + mCount;
		}
		int countAboveOffset = count - offset;
		if (mCount > countAboveOffset)
		{
			for (int i = start + countAboveOffset; i < start + mCount; i++)
			{
				AddInternal(ptMs[i]);
			}
			for (int j = offset; j < count; j++)
			{
				AddInternal(this[j]);
			}
			for (int k = start; k < start + countAboveOffset; k++)
			{
				this[offset + k - start] = ptMs[k];
			}
			return;
		}
		for (int l = count - mCount; l < count; l++)
		{
			AddInternal(this[l]);
		}
		for (int countMCountTerm = count - mCount - 1; countMCountTerm >= offset; countMCountTerm--)
		{
			this[countMCountTerm + mCount] = this[countMCountTerm];
		}
		for (int m = start; m < start + mCount; m++)
		{
			this[offset + m - start] = ptMs[m];
		}
	}

	public bool Remove(Point point)
	{
		throw new NotSupportedException();
	}

	public void RemoveAt(int index)
	{
		RemoveAtInternal(index);
		PointDataModified();
	}

	private void RemoveAtInternal(int index)
	{
		_points.RemoveAt(index);
		if (HasZ)
		{
			_z.RemoveAt(index);
		}
		if (HasM)
		{
			_m.RemoveAt(index);
		}
	}

	public void RemovePoints(bool[] remove)
	{
		if (remove.IsNullOrEmpty() || remove.Length != Count)
		{
			throw new ArgumentException("Incorrectly allocated 'remove' byte array");
		}
		int ordinal = 0;
		for (int i = 0; i < Count; i++)
		{
			if (remove[i])
			{
				ordinal++;
			}
			else if (i != ordinal)
			{
				this[i - ordinal] = this[i];
			}
		}
		if (ordinal != 0)
		{
			while (ordinal != 0)
			{
				RemoveAtInternal(Count - 1);
				ordinal--;
			}
			PointDataModified();
		}
	}

	public void RemoveRange(int startIndex)
	{
		RemoveRange(startIndex, Count - startIndex);
	}

	public void RemoveRange(int startIndex, int count)
	{
		_points.RemoveRange(startIndex, count);
		if (HasZ)
		{
			_z.RemoveRange(startIndex, count);
		}
		if (HasM)
		{
			_m.RemoveRange(startIndex, count);
		}
		PointDataModified();
	}

	public void CompressList(List<int> keepIndex, bool keepListIsSorted = false)
	{
		if (!keepListIsSorted)
		{
			keepIndex.Sort();
		}
		for (int i = 0; i < keepIndex.Count; i++)
		{
			if (i != keepIndex[i])
			{
				MovePoint(keepIndex[i], i);
			}
		}
		RemoveRange(keepIndex.Count);
	}

	public void MovePoint(int fromIndex, int toIndex)
	{
		_points[toIndex] = _points[fromIndex];
		if (HasZ)
		{
			_z[toIndex] = _z[fromIndex];
		}
		if (HasM)
		{
			_m[toIndex] = _m[fromIndex];
		}
		PointDataModified();
	}

	public void Translate(Vector vec)
	{
		Translate(vec.DeltaX, vec.DeltaY);
	}

	public void Translate(double x, double y)
	{
		for (int i = 0; i < Count; i++)
		{
			Point point = this[i];
			point.X += x;
			point.Y += y;
			this[i] = point;
		}
		PointDataModified();
	}

	public void Ratio(double x, double y)
	{
		for (int i = 0; i < Count; i++)
		{
			Point point = this[i];
			point.X *= x;
			point.Y *= y;
			this[i] = point;
		}
		PointDataModified();
	}

	public void RatioX(double x)
	{
		for (int i = 0; i < Count; i++)
		{
			Point point = this[i];
			point.X *= x;
			this[i] = point;
		}
		PointDataModified();
	}

	public void RatioY(double y)
	{
		for (int i = 0; i < Count; i++)
		{
			Point point = this[i];
			point.Y *= y;
			this[i] = point;
		}
		PointDataModified();
	}

	public void Transform(ISpatialTransformer transformer)
	{
		Memory<Point> listBackingBuffer = UnsafeHelpers.GetListBackingBuffer(_points);
		transformer.Transform(listBackingBuffer, listBackingBuffer);
		InitializeZM();
		PointDataModified();
	}

	public void CopyTo(PointCollection target)
	{
		if (HasZ && HasM)
		{
			for (int i = 0; i < Count; i++)
			{
				target.Add(Point4D[i]);
			}
		}
		else if (HasZ)
		{
			for (int j = 0; j < Count; j++)
			{
				target.Add(Point3D[j]);
			}
		}
		else
		{
			for (int k = 0; k < Count; k++)
			{
				target.Add(this[k]);
			}
		}
	}

	public void CopyTo(IList<Point> target)
	{
		for (int i = 0; i < Count; i++)
		{
			target.Add(this[i]);
		}
	}

	public void CopyTo(ref double[] x, ref double[] y)
	{
		if (Count > 0)
		{
			x = new double[Count];
			y = new double[Count];
			for (int i = 0; i < Count; i++)
			{
				Point point = this[i];
				x[i] = point.X;
				y[i] = point.Y;
			}
		}
		else
		{
			x = null;
			y = null;
		}
	}

	public void CopyTo(Point[] points, int arrayIndex)
	{
		_points.CopyTo(points, arrayIndex);
	}

	public void CopyTo(Point3D[] point3Ds, int arrayIndex)
	{
		if (HasZ)
		{
			for (int i = 0; i < Count; i++)
			{
				point3Ds[arrayIndex + i] = new Point3D(_points[i], _z[i]);
			}
			return;
		}
		double zFillValue = ZFillValue;
		for (int j = 0; j < Count; j++)
		{
			point3Ds[arrayIndex + j] = new Point3D(_points[j], zFillValue);
		}
	}

	public void CopyTo(Point4D[] point4Ds, int arrayIndex)
	{
		if (HasZ && HasM)
		{
			for (int i = 0; i < Count; i++)
			{
				point4Ds[arrayIndex + i] = new Point4D(_points[i], _z[i], _m[i]);
			}
			return;
		}
		if (HasZ)
		{
			double mFillValue = MFillValue;
			for (int j = 0; j < Count; j++)
			{
				point4Ds[arrayIndex + j] = new Point4D(_points[j], _z[j], mFillValue);
			}
			return;
		}
		if (HasM)
		{
			double zFillValue = ZFillValue;
			for (int k = 0; k < Count; k++)
			{
				point4Ds[arrayIndex + k] = new Point4D(_points[k], zFillValue, _m[k]);
			}
			return;
		}
		double zFillValue2 = ZFillValue;
		double mFillValue2 = MFillValue;
		for (int l = 0; l < Count; l++)
		{
			point4Ds[arrayIndex + l] = new Point4D(_points[l], zFillValue2, mFillValue2);
		}
	}

	public void MaxMinIndexes(out int minX, out int maxX, out int minY, out int maxY)
	{
		MaxMinIndexes(this, out minX, out maxX, out minY, out maxY);
	}

	public static void MaxMinIndexes(IList<Point> vals, out int minX, out int maxX, out int minY, out int maxY)
	{
		maxX = -1;
		minX = -1;
		maxY = -1;
		minY = -1;
		double maxValue = double.MinValue;
		double minValue = double.MaxValue;
		double maxValue2 = double.MinValue;
		double minValue2 = double.MaxValue;
		for (int i = 0; i < vals.Count; i++)
		{
			Point point = vals[i];
			MaxMinValue(i, point.X, ref maxX, ref maxValue, ref minX, ref minValue);
			MaxMinValue(i, point.Y, ref maxY, ref maxValue2, ref minY, ref minValue2);
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
			if (maxIndex == -1 || v > maxValue)
			{
				maxIndex = i;
				maxValue = v;
			}
			if (minIndex == -1 || v < minValue)
			{
				minIndex = i;
				minValue = v;
			}
		}
	}

	public bool EqualsXY(PointCollection other)
	{
		if (other == null)
		{
			return false;
		}
		if (other.Count != Count)
		{
			return false;
		}
		Memory<Point> pointBufferUnsafe = GetPointBufferUnsafe();
		Span<Point> pointBufferUnsafeSpan = pointBufferUnsafe.Span;
		pointBufferUnsafe = other.GetPointBufferUnsafe();
		return Buffers.Compare(pointBufferUnsafeSpan, pointBufferUnsafe.Span);
	}

	public bool EqualsXYZM(PointCollection other)
	{
		if (other == null)
		{
			return false;
		}
		if (other.Count != Count)
		{
			return false;
		}
		if (HasZ != other.HasZ || HasM != other.HasM)
		{
			return false;
		}
		Memory<Point> pointBufferUnsafe = GetPointBufferUnsafe();
		Span<Point> pointBufferUnsafeSpan = pointBufferUnsafe.Span;
		pointBufferUnsafe = other.GetPointBufferUnsafe();
		if (!Buffers.Compare(pointBufferUnsafeSpan, pointBufferUnsafe.Span))
		{
			return false;
		}
		Memory<double> doubleSpan;
		if (HasZ)
		{
			doubleSpan = GetZBufferUnsafe();
			Span<double> doubleSpan2 = doubleSpan.Span;
			doubleSpan = other.GetZBufferUnsafe();
			if (!Buffers.Compare(doubleSpan2, doubleSpan.Span))
			{
				return false;
			}
		}
		if (HasM)
		{
			doubleSpan = GetMBufferUnsafe();
			Span<double> doubleSpan3 = doubleSpan.Span;
			doubleSpan = other.GetMBufferUnsafe();
			if (!Buffers.Compare(doubleSpan3, doubleSpan.Span))
			{
				return false;
			}
		}
		return true;
	}

	public static List<PointIntersection> GetAllCoincidentPoints<T>(IList<T> pts, Func<T, Point> getPt, double distTolerance = 1.401298464324817E-45, SpatialIndex<int> cachedSpIdx = null)
	{
		if (pts.Count == 0)
		{
			return new List<PointIntersection>();
		}
		SpatialIndex<int> spatialIndex = cachedSpIdx ?? SpatialIndex.FromPoints(pts, getPt);
		ThreadLocal<List<PointIntersection>> intersectionListTl = new ThreadLocal<List<PointIntersection>>(() => new List<PointIntersection>(), trackAllValues: true);
		double distTolSq = distTolerance * distTolerance;
		System.Threading.Tasks.Parallel.ForEach(spatialIndex.Bins, (List<int> bin) =>
		{
			if (bin.Count != 0)
			{
				List<PointIntersection> pointIntersections = intersectionListTl.Value;
				for (int i = 0; i < bin.Count; i++)
				{
					int bin2 = bin[i];
					T pts2 = pts[bin2];
					Point point = getPt(pts2);
					for (int j = i + 1; j < bin.Count; j++)
					{
						int bin3 = bin[j];
						T pts3 = pts[bin3];
						Point other = getPt(pts3);
						if (point.DistanceSquaredTo(other) < distTolSq)
						{
							PointIntersection pointIntersection = new PointIntersection
							{
								APointIndex = bin2,
								BPointIndex = bin3
							};
							pointIntersections.Add(pointIntersection);
						}
					}
				}
			}
		});
		HashSet<PointIntersection> pointIntersectionsSet = new HashSet<PointIntersection>();
		List<PointIntersection> pointIntersectionsList = new List<PointIntersection>();
		foreach (List<PointIntersection> pointIntersections2 in intersectionListTl.Values)
		{
			foreach (PointIntersection pointIntersection2 in pointIntersections2)
			{
				if (!pointIntersectionsSet.Contains(pointIntersection2))
				{
					pointIntersectionsSet.Add(pointIntersection2);
					pointIntersectionsList.Add(pointIntersection2);
				}
			}
		}
		return pointIntersectionsList;
	}

	public virtual void StreamOut(BinaryWriter bw)
	{
		bw.WriteBuffer(GetPointBufferUnsafe());
		bw.Write(HasZ);
		bw.Write(HasM);
		if (HasZ)
		{
			bw.WriteBuffer(GetZBufferUnsafe());
		}
		if (HasM)
		{
			bw.WriteBuffer(GetMBufferUnsafe());
		}
	}

	public static PointCollection Stream(BinaryReader br)
	{
		double[] readArray = Pipe.ReadArray<double>(br);
		if (readArray.Count() == 0)
		{
			return null;
		}
		int count = readArray.Length / 2;
		PointCollection pointCollection = new PointCollection(count);
		for (int i = 0; i < count; i++)
		{
			pointCollection.Add(new Point(readArray[i * 2], readArray[i * 2 + 1]));
		}
		return pointCollection;
	}

	public virtual void StreamIn(BinaryReader br)
	{
		Clear();
		UntrackZ();
		UntrackM();
		Point[] points = br.ReadBuffer<Point>();
		AddRange(points);
		bool readBoolean = br.ReadBoolean();
		bool readBoolean2 = br.ReadBoolean();
		if (readBoolean)
		{
			double[] readBuffer = br.ReadBuffer<double>();
			TrackZ(readBuffer);
		}
		if (readBoolean2)
		{
			double[] readBufferArray = br.ReadBuffer<double>();
			TrackM(readBufferArray);
		}
	}

	public int IndexOf(Point point)
	{
		throw new NotSupportedException();
	}

	public void Insert(int index, Point point)
	{
		_points.Insert(index, point);
		if (HasZ)
		{
			_z.Insert(index, ZFillValue);
		}
		if (HasM)
		{
			_m.Insert(index, MFillValue);
		}
		PointDataModified();
	}

	public List<Point> GetRange(int index, int count)
	{
		return _points.GetRange(index, count);
	}

	public virtual void Clear()
	{
		_points.Clear();
		if (HasZ)
		{
			_z.Clear();
		}
		if (HasM)
		{
			_m.Clear();
		}
		PointDataModified();
	}

	public virtual void Reverse()
	{
		_points.Reverse();
		if (HasZ)
		{
			_z.Reverse();
		}
		if (HasM)
		{
			_m.Reverse();
		}
		PointDataModified();
	}

	public bool Contains(Point point)
	{
		return _points.Contains(point);
	}

	public virtual int GetDataHash()
	{
		int combine = 0;
		for (int i = 0; i < Count; i++)
		{
			combine = StableHash.Combine(combine, Points[i].GetHashCode());
		}
		if (HasZ)
		{
			for (int j = 0; j < Count; j++)
			{
				combine = StableHash.Combine(combine, Z[j].GetHashCode());
			}
		}
		if (HasM)
		{
			for (int k = 0; k < Count; k++)
			{
				combine = StableHash.Combine(combine, M[k].GetHashCode());
			}
		}
		return combine;
	}

	public IEnumerator<Point> GetEnumerator()
	{
		return _points.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return _points.GetEnumerator();
	}

	public PointCollection Clone()
	{
		return new PointCollection(this);
	}
}
