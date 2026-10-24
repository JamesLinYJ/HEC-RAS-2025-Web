using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using RasMapperLib.TinHelpers;
using RasMapperLib.Utilities;
using Utility;

namespace RasMapperLib;

public class Tin : MultiPoint
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__103_002D0
	{
		public IPoints subPts;

		public _Closure_0024__103_002D0(_Closure_0024__103_002D0 other)
		{
			if (other != null)
			{
				subPts = other.subPts;
			}
		}

		[SpecialName]
		internal PointM _Lambda_0024__0(int idx)
		{
			return subPts.PointM(idx);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__104_002D0
	{
		public IList<Polyline> polylines;

		public RasterizeSegment siRasterizeSegment;

		public double tolerance;

		public Profile[] profiles;

		public Tin tin;

		public _Closure_0024__104_002D0(_Closure_0024__104_002D0 other)
		{
			if (other != null)
			{
				polylines = other.polylines;
				siRasterizeSegment = other.siRasterizeSegment;
				tolerance = other.tolerance;
				profiles = other.profiles;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int fin)
		{
			checked
			{
				_Closure_0024__104_002D2 localValue = default;
				_Closure_0024__104_002D1 localValue2 = default;
				for (int i = start; i <= fin; i++)
				{
					localValue = new _Closure_0024__104_002D2(localValue);
					localValue.outerClosure = this;
					Polyline polyline = polylines[i];
					if (!Polyline.IsValidPolyline(polyline) || !polyline.MBROverlaps(tin))
					{
						continue;
					}
					localValue.segmentProfile = new Profile();
					localValue.segStartStation = 0.0;
					int count = polyline.SegmentCount() - 1;
					for (int j = 0; j <= count; j++)
					{
						localValue2 = new _Closure_0024__104_002D1(localValue2);
						localValue2.outerClosure = localValue;
						localValue2.segment = polyline.Segment(j);
						if (localValue2.segment.MBROverlaps(tin))
						{
							siRasterizeSegment.ComputeCells(localValue2.segment, localValue2._Lambda_0024__1);
						}
						localValue2.outerClosure.segStartStation = localValue2.outerClosure.segStartStation + localValue2.segment.Length;
					}
					if (localValue.segmentProfile.Count <= 0)
					{
						continue;
					}
					localValue.segmentProfile.SortByStation();
					Profile profile = localValue.segmentProfile.Filter((float)tolerance);
					if (profile.Count > 0)
					{
						if (profiles[i] == null)
						{
							profiles[i] = profile;
							continue;
						}
						profiles[i].AddRange(profile);
						profiles[i].SortByStation();
					}
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__104_002D1
	{
		public SegmentM segment;

		public _Closure_0024__104_002D2 outerClosure;

		public _Closure_0024__104_002D1(_Closure_0024__104_002D1 other)
		{
			if (other != null)
			{
				segment = other.segment;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int n)
		{
			List<int> bin = outerClosure.outerClosure.tin._triangleSpatialIndex.Bins[n];
			if (bin == null)
			{
				return;
			}
			double z = default;
			double z2 = default;
			double x = default;
			double y = default;
			double t = default;
			double t2 = default;
			foreach (int bin2 in bin)
			{
				TriangleShape triangleShape = outerClosure.outerClosure.tin.TriangleShape(bin2);
				if (triangleShape.ComputeZ(segment.A, ref z))
				{
					outerClosure.segmentProfile.Add(outerClosure.segStartStation, z);
				}
				if (triangleShape.ComputeZ(segment.B, ref z2))
				{
					outerClosure.segmentProfile.Add(outerClosure.segStartStation + segment.Length, z2);
				}
				int ordinal = 0;
				do
				{
					SegmentM seg = outerClosure.outerClosure.tin.TriangleEdgeSegment(bin2, ordinal);
					if (segment.Intersects(seg, ref x, ref y, ref t, ref t2))
					{
						double x2 = outerClosure.segStartStation + segment.A.DistanceTo(x, y);
						double z3 = seg.PointAtFraction(t2).Z;
						outerClosure.segmentProfile.Add(x2, z3);
					}
					ordinal = checked(ordinal + 1);
				}
				while (ordinal <= 2);
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__104_002D2
	{
		public Profile segmentProfile;

		public double segStartStation;

		public _Closure_0024__104_002D0 outerClosure;

		public _Closure_0024__104_002D2(_Closure_0024__104_002D2 other)
		{
			if (other != null)
			{
				segmentProfile = other.segmentProfile;
				segStartStation = other.segStartStation;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__105_002D0
	{
		public IList<Polyline> polylines;

		public RasterizeSegment siRasterizeSegment;

		public double tolerance;

		public Profile[] profiles;

		public Tin tin;

		public _Closure_0024__105_002D0(_Closure_0024__105_002D0 other)
		{
			if (other != null)
			{
				polylines = other.polylines;
				siRasterizeSegment = other.siRasterizeSegment;
				tolerance = other.tolerance;
				profiles = other.profiles;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int fin)
		{
			checked
			{
				_Closure_0024__105_002D2 localValue = default;
				_Closure_0024__105_002D1 localValue2 = default;
				for (int i = start; i <= fin; i++)
				{
					localValue = new _Closure_0024__105_002D2(localValue);
					localValue.outerClosure = this;
					Polyline polyline = polylines[i];
					if (!Polyline.IsValidPolyline(polyline) || !polyline.MBROverlaps(tin))
					{
						continue;
					}
					localValue.segmentProfile = new Profile();
					localValue.segStartStation = 0.0;
					int count = polyline.SegmentCount() - 1;
					for (int j = 0; j <= count; j++)
					{
						localValue2 = new _Closure_0024__105_002D1(localValue2);
						localValue2.outerClosure = localValue;
						localValue2.segment = polyline.Segment(j);
						if (localValue2.segment.MBROverlaps(tin))
						{
							siRasterizeSegment.ComputeCells(localValue2.segment, localValue2._Lambda_0024__1);
						}
						localValue2.outerClosure.segStartStation = localValue2.outerClosure.segStartStation + localValue2.segment.Length;
					}
					if (localValue.segmentProfile.Count <= 0)
					{
						continue;
					}
					localValue.segmentProfile.SortByStation();
					Profile profile = localValue.segmentProfile.Filter((float)tolerance);
					if (profile.Count > 0)
					{
						if (profiles[i] == null)
						{
							profiles[i] = profile;
							continue;
						}
						profiles[i].AddRange(profile);
						profiles[i].SortByStation();
					}
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__105_002D1
	{
		public SegmentM segment;

		public _Closure_0024__105_002D2 outerClosure;

		public _Closure_0024__105_002D1(_Closure_0024__105_002D1 other)
		{
			if (other != null)
			{
				segment = other.segment;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int n)
		{
			List<int> bin = outerClosure.outerClosure.tin._triangleSpatialIndex.Bins[n];
			if (bin == null)
			{
				return;
			}
			double z = default;
			double z2 = default;
			double x = default;
			double y = default;
			double t = default;
			double t2 = default;
			foreach (int bin2 in bin)
			{
				TriangleShape triangleShape = outerClosure.outerClosure.tin.TriangleShape(bin2);
				if (triangleShape.ComputeZ(segment.A, ref z))
				{
					outerClosure.segmentProfile.Add(outerClosure.segStartStation, z);
				}
				if (triangleShape.ComputeZ(segment.B, ref z2))
				{
					outerClosure.segmentProfile.Add(outerClosure.segStartStation + segment.Length, z2);
				}
				int ordinal = 0;
				do
				{
					SegmentM seg = outerClosure.outerClosure.tin.TriangleEdgeSegment(bin2, ordinal);
					if (segment.Intersects(seg, ref x, ref y, ref t, ref t2))
					{
						double x2 = outerClosure.segStartStation + segment.A.DistanceTo(x, y);
						double z3 = seg.PointAtFraction(t2).Z;
						outerClosure.segmentProfile.Add(x2, z3);
					}
					ordinal = checked(ordinal + 1);
				}
				while (ordinal <= 2);
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__105_002D2
	{
		public Profile segmentProfile;

		public double segStartStation;

		public _Closure_0024__105_002D0 outerClosure;

		public _Closure_0024__105_002D2(_Closure_0024__105_002D2 other)
		{
			if (other != null)
			{
				segmentProfile = other.segmentProfile;
				segStartStation = other.segStartStation;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__97_002D0
	{
		public float[] floatValues;

		public bool cellsSet;

		public _Closure_0024__97_002D0(_Closure_0024__97_002D0 other)
		{
			if (other != null)
			{
				floatValues = other.floatValues;
				cellsSet = other.cellsSet;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int n, float z)
		{
			floatValues[n] = z;
			cellsSet = true;
		}

		[SpecialName]
		internal void _Lambda_0024__1(int n, float z)
		{
			floatValues[n] = z;
			cellsSet = true;
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__98_002D0
	{
		public bool[] mask;

		public bool cellsSet;

		public _Closure_0024__98_002D0(_Closure_0024__98_002D0 other)
		{
			if (other != null)
			{
				mask = other.mask;
				cellsSet = other.cellsSet;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int n)
		{
			mask[n] = true;
			cellsSet = true;
		}
	}

	public const string sTINPoints = "TIN Points";

	public const string sTINEdges = "TIN Edges";

	public const string sTINTriangleNumbers = "TIN Triangle Numbers";

	public const string sTINPointNumbers = "TIN Point Numbers";

	public const string sTINPlotM = "TIN Plot M Surface";

	private static readonly PlotOption _poTinPoints;

	private static readonly PlotOption _poTinEdges;

	private static readonly PlotOption _poTinTriNumbers;

	private static readonly PlotOption _poPointNumbers;

	private static readonly PlotOption _poPlotM;

	private static readonly List<PlotOption> _debugPlotOptions;

	private static readonly List<PlotOption> _plotOptions;

	private EdgeStruct[] _edge;

	public TriangleStruct[] Triangles;

	private SurfaceStatistics _surfaceStatisticsZ;

	private SurfaceStatistics _surfaceStatisticsM;

	private SparseSolver.enumConvergenceResult _laplaceConvergence;

	private double _area;

	private SpatialIndex<int> _triangleSpatialIndex;

	public SurfaceStatistics SurfaceStatisticsZ
	{
		get
		{
			checked
			{
				if ((_surfaceStatisticsZ == null) & HasZ)
				{
					Histogram histogram = new Histogram(MinZ, MaxZ, 1024);
					int count = Triangles.Count() - 1;
					for (int i = 0; i <= count; i++)
					{
						double area = TriangleArea(i);
						PointM pointM = _pointM[Triangles[i].p0];
						PointM pointM2 = _pointM[Triangles[i].p1];
						PointM pointM3 = _pointM[Triangles[i].p2];
						histogram.AddTriangle(area, pointM.Z, pointM2.Z, pointM3.Z);
					}
					_surfaceStatisticsZ = histogram.ComputeSurfaceStatistics();
				}
				return _surfaceStatisticsZ;
			}
		}
	}

	public SurfaceStatistics SurfaceStatisticsM
	{
		get
		{
			checked
			{
				if ((_surfaceStatisticsM == null) & HasM)
				{
					Histogram histogram = new Histogram(MinM, MaxM, 1024);
					int count = Triangles.Count() - 1;
					for (int i = 0; i <= count; i++)
					{
						double area = TriangleArea(i);
						PointM pointM = _pointM[Triangles[i].p0];
						PointM pointM2 = _pointM[Triangles[i].p1];
						PointM pointM3 = _pointM[Triangles[i].p2];
						histogram.AddTriangle(area, pointM.M, pointM2.M, pointM3.M);
					}
					_surfaceStatisticsM = histogram.ComputeSurfaceStatistics();
				}
				return _surfaceStatisticsM;
			}
		}
	}

	public double Area
	{
		get
		{
			checked
			{
				if (double.IsNaN(_area))
				{
					_area = 0.0;
					int count = Triangles.Count() - 1;
					for (int i = 0; i <= count; i++)
					{
						_area += TriangleArea(i);
					}
				}
				return _area;
			}
		}
	}

	public override bool UsesPointSymbol => true;

	public override bool UsesPen => true;

	public override bool UsesBrush => !HasZ;

	public override bool UsesSurfaceFill => HasZ;

	public SparseSolver.enumConvergenceResult LaplaceConvergence => _laplaceConvergence;

	static Tin()
	{
		_poTinPoints = new PlotOption("TIN Points");
		_poTinEdges = new PlotOption("TIN Edges");
		_poTinTriNumbers = new PlotOption("TIN Triangle Numbers");
		_poPointNumbers = new PlotOption("TIN Point Numbers");
		_poPlotM = new PlotOption("TIN Plot M Surface");
		_debugPlotOptions = new List<PlotOption>();
		_plotOptions = new List<PlotOption>();
		_debugPlotOptions.Add(_poTinPoints);
		_debugPlotOptions.Add(_poTinEdges);
		_debugPlotOptions.Add(_poTinTriNumbers);
		_debugPlotOptions.Add(_poPointNumbers);
		_debugPlotOptions.Add(_poPlotM);
		_plotOptions.AddRange(_debugPlotOptions.Where([SpecialName] (PlotOption po) => !po.IsDebugOption));
	}

	public override List<PlotOption> AvailablePlotOptions(bool debugMode)
	{
		return GetAvailablePlotOptions(debugMode);
	}

	public new static List<PlotOption> GetAvailablePlotOptions(bool debugMode)
	{
		if (debugMode)
		{
			return _debugPlotOptions;
		}
		return _plotOptions;
	}

	public Tin(PointMs PointMs, TriangleStruct[] triangle)
		: base(PointMs)
	{
		_edge = null;
		Triangles = null;
		_surfaceStatisticsZ = null;
		_surfaceStatisticsM = null;
		_area = double.NaN;
		Triangles = new TriangleStruct[checked(triangle.Count() - 1 + 1)];
		Array.Copy(triangle, Triangles, triangle.Count());
	}

	public void ComputeEdgesAndAdjacentTriangles()
	{
		if (!((_pointM.Count > 0) & (Triangles.Count() > 0)))
		{
			return;
		}
		int ordinal = 0;
		checked
		{
			_edge = new EdgeStruct[Triangles.Count() * 3 + 1];
			List<int>[] intListArray = new List<int>[_pointM.Count - 1 + 1];
			List<int>[] intListArrayList = new List<int>[_pointM.Count - 1 + 1];
			int count = _pointM.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				intListArray[i] = new List<int>();
				intListArrayList[i] = new List<int>();
			}
			int count2 = Triangles.Count() - 1;
			for (int j = 0; j <= count2; j++)
			{
				Triangles[j].at0 = -1;
				Triangles[j].at1 = -1;
				Triangles[j].at2 = -1;
			}
			int count3 = Triangles.Count() - 1;
			for (int k = 0; k <= count3; k++)
			{
				int ordinal2 = 0;
				do
				{
					int p0 = Triangles[k].EdgePoint0(ordinal2);
					int p1 = Triangles[k].EdgePoint1(ordinal2);
					int indexOf = intListArray[p0].IndexOf(p1);
					if (indexOf == -1)
					{
						_edge[ordinal].p0 = p0;
						_edge[ordinal].p1 = p1;
						ordinal++;
						intListArray[p0].Add(p1);
						intListArray[p1].Add(p0);
						intListArrayList[p0].Add(k);
						intListArrayList[p1].Add(k);
					}
					else
					{
						int p0IndexOfTerm = intListArrayList[p0][indexOf];
						Triangles[k].set_AdjacentTriangle(p0, p1, p0IndexOfTerm);
						Triangles[p0IndexOfTerm].set_AdjacentTriangle(p0, p1, k);
					}
					ordinal2++;
				}
				while (ordinal2 <= 2);
			}
			Array.Resize(ref _edge, ordinal);
		}
	}

	public bool IsEdgePerimeterAndUncoincident(int triangleIdx, int p0, int p1)
	{
		if (Triangle(triangleIdx).get_AdjacentTriangle(p0, p1) != -1)
		{
			return false;
		}
		if ((PointM(p0).Z == 0.0 && PointM(p1).Z == 0.0) || (PointM(p0).Z == 1.0 && PointM(p1).Z == 1.0))
		{
			return false;
		}
		return true;
	}

	public Tin Clone()
	{
		return Copy();
	}

	public Tin Copy()
	{
		PointMs pointMs = new PointMs();
		CopyTo(pointMs);
		return new Tin(pointMs, Triangles);
	}

	public void Stream(BinaryWriter bw)
	{
		RasMapperLib.PointM.Stream(bw, _pointM);
		TriangleStruct.Stream(bw, Triangles);
	}

	public static Tin Stream(BinaryReader br)
	{
		PointM[] elements = RasMapperLib.PointM.Stream(br);
		TriangleStruct[] triangle = TriangleStruct.Stream(br);
		return new Tin(new PointMs(elements), triangle);
	}

	private bool PointInTriangle(PointM PointM, int t)
	{
		return PointInTriangle(PointM.X, PointM.Y, Triangles[t].p0, Triangles[t].p1, Triangles[t].p2);
	}

	private bool PointInTriangle(PointM PointM, int p0, int p1, int p2)
	{
		return PointInTriangle(PointM.X, PointM.Y, p0, p1, p2);
	}

	private bool PointInTriangle(double ptx, double pty, int p0, int p1, int p2)
	{
		TriangleShape triangleShape = new TriangleShape(_pointM[p0], _pointM[p1], _pointM[p2]);
		double lambda = 0.0;
		double lambda2 = 0.0;
		double lambda3 = 0.0;
		return triangleShape.Contains(ptx, pty, ref lambda, ref lambda2, ref lambda3);
	}

	public void TriangleBarycentricCoordinates(double px, double py, int t, ref double lambda0, ref double lambda1, ref double lambda2)
	{
		new TriangleShape(_pointM[Triangles[t].p0], _pointM[Triangles[t].p1], _pointM[Triangles[t].p2]).InterpolationWeights(px, py, ref lambda0, ref lambda1, ref lambda2);
	}

	public int TriangleCount()
	{
		return Triangles.Count();
	}

	public void TrianglePointMs(int t, ref PointM p0, ref PointM p1, ref PointM p2)
	{
		p0 = _pointM[Triangles[t].p0];
		p1 = _pointM[Triangles[t].p1];
		p2 = _pointM[Triangles[t].p2];
	}

	public void TrianglePoints(int t, ref int p0, ref int p1, ref int p2)
	{
		p0 = Triangles[t].p0;
		p1 = Triangles[t].p1;
		p2 = Triangles[t].p2;
	}

	public TriangleShape TriangleShape(int t)
	{
		TriangleShape result = default;
		result.pt0 = _pointM[Triangles[t].p0];
		result.pt1 = _pointM[Triangles[t].p1];
		result.pt2 = _pointM[Triangles[t].p2];
		return result;
	}

	public int TriangleThirdPoint(int t, int firstPoint, int secondPoint)
	{
		return Triangles[t].ThirdPoint(firstPoint, secondPoint);
	}

	public bool TriangleMBRContains(int t, PointM pt)
	{
		PointM p = default;
		PointM p2 = default;
		PointM p3 = default;
		TrianglePointMs(t, ref p, ref p2, ref p3);
		double max = default;
		double min = default;
		RasMapperLib.Utilities.Math.MaxMin3(p.X, p2.X, p3.X, ref max, ref min);
		if (pt.X < min || pt.X > max)
		{
			return false;
		}
		double max2 = default;
		double min2 = default;
		RasMapperLib.Utilities.Math.MaxMin3(p.Y, p2.Y, p3.Y, ref max2, ref min2);
		if (pt.Y < min2 || pt.Y > max2)
		{
			return false;
		}
		return true;
	}

	public void TriangleMBR(int t, ref double minX, ref double maxX, ref double minY, ref double maxY)
	{
		PointM p = default;
		PointM p2 = default;
		PointM p3 = default;
		TrianglePointMs(t, ref p, ref p2, ref p3);
		RasMapperLib.Utilities.Math.MaxMin3(p.X, p2.X, p3.X, ref maxX, ref minX);
		RasMapperLib.Utilities.Math.MaxMin3(p.Y, p2.Y, p3.Y, ref maxY, ref minY);
	}

	public double TriangleMaxX(int t)
	{
		PointM p = default;
		PointM p2 = default;
		PointM p3 = default;
		TrianglePointMs(t, ref p, ref p2, ref p3);
		return RasMapperLib.Utilities.Math.Max3(p.X, p2.X, p3.X);
	}

	public double TriangleMinX(int t)
	{
		PointM p = default;
		PointM p2 = default;
		PointM p3 = default;
		TrianglePointMs(t, ref p, ref p2, ref p3);
		return RasMapperLib.Utilities.Math.Min3(p.X, p2.X, p3.X);
	}

	public double TriangleMaxY(int t)
	{
		PointM p = default;
		PointM p2 = default;
		PointM p3 = default;
		TrianglePointMs(t, ref p, ref p2, ref p3);
		return RasMapperLib.Utilities.Math.Max3(p.Y, p2.Y, p3.Y);
	}

	public double TriangleMinY(int t)
	{
		PointM p = default;
		PointM p2 = default;
		PointM p3 = default;
		TrianglePointMs(t, ref p, ref p2, ref p3);
		return RasMapperLib.Utilities.Math.Min3(p.Y, p2.Y, p3.Y);
	}

	public double TriangleMaxZ(int t)
	{
		PointM p = default;
		PointM p2 = default;
		PointM p3 = default;
		TrianglePointMs(t, ref p, ref p2, ref p3);
		return RasMapperLib.Utilities.Math.Max3(p.Z, p2.Z, p3.Z);
	}

	public double TriangleMinZ(int t)
	{
		PointM p = default;
		PointM p2 = default;
		PointM p3 = default;
		TrianglePointMs(t, ref p, ref p2, ref p3);
		return RasMapperLib.Utilities.Math.Min3(p.Z, p2.Z, p3.Z);
	}

	public double TriangleMidZ(int t)
	{
		PointM p = default;
		PointM p2 = default;
		PointM p3 = default;
		TrianglePointMs(t, ref p, ref p2, ref p3);
		return RasMapperLib.Utilities.Math.Mid3(p.Z, p2.Z, p3.Z);
	}

	public void TriangleMinMidMaxZ(int t, ref double min, ref double mid, ref double max)
	{
		min = _pointM[Triangles[t].p0].Z;
		mid = _pointM[Triangles[t].p1].Z;
		max = _pointM[Triangles[t].p2].Z;
		RasMapperLib.Utilities.Math.Sort3(ref min, ref mid, ref max);
	}

	public void TriangleMinMidMaxM(int t, ref double min, ref double mid, ref double max)
	{
		min = _pointM[Triangles[t].p0].M;
		mid = _pointM[Triangles[t].p1].M;
		max = _pointM[Triangles[t].p2].M;
		RasMapperLib.Utilities.Math.Sort3(ref min, ref mid, ref max);
	}

	public PlaneM TrianglePlane(int t)
	{
		return Triangles[t].PlaneCoeffients(this);
	}

	public SegmentM TriangleEdgeSegment(int t, int e012)
	{
		int index = Triangles[t].EdgePoint0(e012);
		int index2 = Triangles[t].EdgePoint1(e012);
		SegmentM result = default;
		result.Assign(_pointM[index], _pointM[index2]);
		return result;
	}

	public void TriangleXYs(int t, ref double x0, ref double y0, ref double x1, ref double y1, ref double x2, ref double y2)
	{
		x0 = _pointM[Triangles[t].p0].X;
		y0 = _pointM[Triangles[t].p0].Y;
		x1 = _pointM[Triangles[t].p1].X;
		y1 = _pointM[Triangles[t].p1].Y;
		x2 = _pointM[Triangles[t].p2].X;
		y2 = _pointM[Triangles[t].p2].Y;
	}

	public Extent TriangleExtent(int t)
	{
		double x = _pointM[Triangles[t].p0].X;
		double y = _pointM[Triangles[t].p0].Y;
		double x2 = _pointM[Triangles[t].p1].X;
		double y2 = _pointM[Triangles[t].p1].Y;
		double x3 = _pointM[Triangles[t].p2].X;
		double y3 = _pointM[Triangles[t].p2].Y;
		double max = default;
		double min = default;
		RasMapperLib.Utilities.Math.MaxMin3(x, x2, x3, ref max, ref min);
		double max2 = default;
		double min2 = default;
		RasMapperLib.Utilities.Math.MaxMin3(y, y2, y3, ref max2, ref min2);
		return new Extent(max, min, max2, min2);
	}

	public void TriangleAdjacentIndexes(int t, ref int at0, ref int at1, ref int at2)
	{
		if (_edge == null)
		{
			ComputeEdgesAndAdjacentTriangles();
		}
		at0 = Triangles[t].at0;
		at1 = Triangles[t].at1;
		at2 = Triangles[t].at2;
	}

	public int TriangleAdjacent(int t, int e012)
	{
		return e012 switch
		{
			0 => Triangles[t].at0, 
			1 => Triangles[t].at1, 
			_ => Triangles[t].at2, 
		};
	}

	public int TriangleAdjacentToEdgeBeforePoint(int t, int p)
	{
		if (Triangles[t].p0 == p)
		{
			return Triangles[t].at2;
		}
		if (Triangles[t].p1 == p)
		{
			return Triangles[t].at0;
		}
		return Triangles[t].at1;
	}

	public int TriangleAdjacentToEdgeAfterPoint(int t, int p)
	{
		if (Triangles[t].p0 == p)
		{
			return Triangles[t].at0;
		}
		if (Triangles[t].p1 == p)
		{
			return Triangles[t].at1;
		}
		return Triangles[t].at2;
	}

	public int TrianglePointBeforePoint(int t, int p)
	{
		if (Triangles[t].p0 == p)
		{
			return Triangles[t].p2;
		}
		if (Triangles[t].p1 == p)
		{
			return Triangles[t].p0;
		}
		return Triangles[t].p1;
	}

	public int TrianglePointAfterPoint(int t, int p)
	{
		if (Triangles[t].p0 == p)
		{
			return Triangles[t].p1;
		}
		if (Triangles[t].p1 == p)
		{
			return Triangles[t].p2;
		}
		return Triangles[t].p0;
	}

	public bool TriangleIsPerimeter(int t)
	{
		if (Triangles[t].at0 == -1 || Triangles[t].at1 == -1 || Triangles[t].at2 == -1)
		{
			return true;
		}
		return false;
	}

	public bool TriangleEdgeIsPerimeter(int t)
	{
		if (Triangles[t].at0 == -1 || Triangles[t].at1 == -1 || Triangles[t].at2 == -1)
		{
			return true;
		}
		return false;
	}

	public int[] CreatePoint2TriangleMap()
	{
		checked
		{
			int[] point2TriangleMap = new int[Count - 1 + 1];
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				point2TriangleMap[i] = -1;
			}
			int count2 = Triangles.Count() - 1;
			for (int j = 0; j <= count2; j++)
			{
				point2TriangleMap[Triangles[j].p0] = j;
				point2TriangleMap[Triangles[j].p1] = j;
				point2TriangleMap[Triangles[j].p2] = j;
			}
			return point2TriangleMap;
		}
	}

	public void PointTriangles(int p, int[] point2TriangleMap, List<int> triangles, ref bool pIsExternal)
	{
		triangles.Clear();
		int point2TriangleMap2 = point2TriangleMap[p];
		if (point2TriangleMap2 > -1)
		{
			triangles.Add(point2TriangleMap2);
			int point2TriangleMap3 = point2TriangleMap2;
			do
			{
				point2TriangleMap3 = TriangleAdjacentToEdgeBeforePoint(point2TriangleMap3, p);
				if ((point2TriangleMap3 != point2TriangleMap2) & (point2TriangleMap3 != -1))
				{
					triangles.Add(point2TriangleMap3);
				}
			}
			while (!((point2TriangleMap3 == point2TriangleMap2) | (point2TriangleMap3 == -1)));
			if (point2TriangleMap3 == -1)
			{
				pIsExternal = true;
				point2TriangleMap3 = point2TriangleMap2;
				do
				{
					point2TriangleMap3 = TriangleAdjacentToEdgeAfterPoint(point2TriangleMap3, p);
					if (point2TriangleMap3 != -1)
					{
						triangles.Insert(0, point2TriangleMap3);
					}
				}
				while (point2TriangleMap3 != -1);
			}
			else
			{
				pIsExternal = false;
			}
		}
		else
		{
			pIsExternal = true;
		}
	}

	public double TriangleArea(int t)
	{
		int p = Triangles[t].p0;
		int p2 = Triangles[t].p1;
		int p3 = Triangles[t].p2;
		return TriangleArea(_pointM[p].X, _pointM[p].Y, _pointM[p2].X, _pointM[p2].Y, _pointM[p3].X, _pointM[p3].Y);
	}

	public static double TriangleArea(double ax, double ay, double bx, double by, double cx, double cy)
	{
		return System.Math.Abs((bx - ax) * (cy - ay) - (by - ay) * (cx - ax)) * 0.5;
	}

	public double TriangleContourArea(int t, double maxZ, double minZ, ref int ptsCount, PointM[] pts)
	{
		ptsCount = 0;
		int ordinal = 0;
		checked
		{
			double t2 = default;
			double t3 = default;
			SegmentM segmentM = default;
			do
			{
				int index = Triangles[t].EdgePoint0(ordinal);
				int index2 = Triangles[t].EdgePoint1(ordinal);
				if (RasMapperLib.Utilities.Math.ParametricFractions(_pointM[index].Z, _pointM[index2].Z, maxZ, minZ, ref t2, ref t3))
				{
					segmentM.A.X = _pointM[index].X;
					segmentM.A.Y = _pointM[index].Y;
					segmentM.B.X = _pointM[index2].X;
					segmentM.B.Y = _pointM[index2].Y;
					pts[ptsCount] = segmentM.PointAtFraction(t2);
					ptsCount++;
					pts[ptsCount] = segmentM.PointAtFraction(t3);
					ptsCount++;
				}
				ordinal++;
			}
			while (ordinal <= 2);
			double computed = 0.0;
			double scaledMinY = MinY * 2.0;
			int count = ptsCount - 1;
			for (int i = 0; i <= count; i++)
			{
				int ptsCountOffset = ((i != 0) ? (i - 1) : (ptsCount - 1));
				double ptsTerm = pts[i].X - pts[ptsCountOffset].X;
				if (ptsTerm != 0.0)
				{
					computed += (pts[i].Y + pts[ptsCountOffset].Y - scaledMinY) * ptsTerm;
				}
			}
			return System.Math.Abs(computed * 0.5);
		}
	}

	public void TriangleContourZ(int t, double minZ, double maxZ, PointMs pts)
	{
		TriangleContour(t, minZ, maxZ, [SpecialName] (PointM ptm) => ptm.Z, pts);
	}

	public void TriangleContourM(int t, double minM, double maxM, PointMs pts)
	{
		TriangleContour(t, minM, maxM, [SpecialName] (PointM ptm) => ptm.M, pts);
	}

	public void TriangleContour(int t, double minV, double maxV, Func<PointM, double> V, PointMs pts)
	{
		pts.Clear();
		int ordinal = 0;
		double t2 = default;
		double t3 = default;
		SegmentM segmentM = default;
		do
		{
			int index = Triangles[t].EdgePoint0(ordinal);
			int index2 = Triangles[t].EdgePoint1(ordinal);
			if (RasMapperLib.Utilities.Math.ParametricFractions(V(_pointM[index]), V(_pointM[index2]), maxV, minV, ref t2, ref t3))
			{
				segmentM.A.X = _pointM[index].X;
				segmentM.A.Y = _pointM[index].Y;
				segmentM.B.X = _pointM[index2].X;
				segmentM.B.Y = _pointM[index2].Y;
				pts.AddNew(segmentM.PointAtFraction(t2));
				pts.AddNew(segmentM.PointAtFraction(t3));
			}
			ordinal = checked(ordinal + 1);
		}
		while (ordinal <= 2);
	}

	public PointM TriangleCentroid(int t)
	{
		PointM p = default;
		PointM p2 = default;
		PointM p3 = default;
		TrianglePointMs(t, ref p, ref p2, ref p3);
		return TriangleCentroid(p, p2, p3);
	}

	public static PointM TriangleCentroid(PointM pt0, PointM pt1, PointM pt2)
	{
		SegmentM segmentM = new SegmentM(pt0, pt1.MidPoint(pt2));
		SegmentM seg = new SegmentM(pt1, pt0.MidPoint(pt2));
		PointM result = default;
		segmentM.Intersects(seg, ref result.X, ref result.Y);
		return result;
	}

	public PointM TriangleCircumcircle(int t)
	{
		PointM p = default;
		PointM p2 = default;
		PointM p3 = default;
		TrianglePointMs(t, ref p, ref p2, ref p3);
		double p2Term = p2.X - p.X;
		double p2Term2 = p2.Y - p.Y;
		double computed = p2Term * p2Term + p2Term2 * p2Term2;
		double p3Term = p3.X - p.X;
		double p3Term2 = p3.Y - p.Y;
		double computed2 = p3Term * p3Term + p3Term2 * p3Term2;
		double computed3 = 2.0 * (p2Term * p3Term2 - p2Term2 * p3Term);
		PointM result = default;
		result.X = p.X + (p3Term2 * computed - p2Term2 * computed2) / computed3;
		result.Y = p.Y + (p2Term * computed2 - p3Term * computed) / computed3;
		return result;
	}

	public TriangleStruct Triangle(int t)
	{
		return Triangles[t];
	}

	public double TriangleAspectRatio(int t)
	{
		PointM p = default;
		PointM p2 = default;
		PointM p3 = default;
		TrianglePointMs(t, ref p, ref p2, ref p3);
		return TriangleAspectRatio(p, p2, p3);
	}

	public double TriangleAspectRatio(PointM p0, PointM p1, PointM p2)
	{
		double distanceTo = p0.DistanceTo(p1);
		double distanceTo2 = p1.DistanceTo(p2);
		double distanceTo3 = p2.DistanceTo(p0);
		PointM pointM;
		SegmentM segmentM = default;
		if (distanceTo > distanceTo2 && distanceTo > distanceTo3)
		{
			pointM = p2;
			segmentM.Assign(p0, p1);
		}
		else if (distanceTo2 > distanceTo && distanceTo2 > distanceTo3)
		{
			pointM = p0;
			segmentM.Assign(p1, p2);
		}
		else
		{
			pointM = p1;
			segmentM.Assign(p2, p0);
		}
		PointM pt = pointM;
		SegmentM.RelativePosition relativePosition = SegmentM.RelativePosition.Undefined;
		double distanceToPoint = segmentM.DistanceToPoint(pt, ref relativePosition);
		if (distanceToPoint == 0.0)
		{
			return double.MaxValue;
		}
		return segmentM.Length / distanceToPoint;
	}

	public int EdgeCount()
	{
		if (_edge == null)
		{
			ComputeEdgesAndAdjacentTriangles();
		}
		if (_edge == null)
		{
			return 0;
		}
		return _edge.Count();
	}

	public int EdgePoint0(int e)
	{
		if (_edge == null)
		{
			ComputeEdgesAndAdjacentTriangles();
		}
		return _edge[e].p0;
	}

	public int EdgePoint1(int e)
	{
		if (_edge == null)
		{
			ComputeEdgesAndAdjacentTriangles();
		}
		return _edge[e].p1;
	}

	public SegmentM Edge(int e)
	{
		if (_edge == null)
		{
			ComputeEdgesAndAdjacentTriangles();
		}
		SegmentM result = default;
		result.A = _pointM[_edge[e].p0];
		result.B = _pointM[_edge[e].p1];
		return result;
	}

	public SegmentM Edge(int t, int e012)
	{
		PointM p = default;
		PointM p2 = default;
		PointM p3 = default;
		TrianglePointMs(t, ref p, ref p2, ref p3);
		SegmentM result = default;
		switch (e012)
		{
		case 0:
			result.A = p;
			result.B = p2;
			break;
		case 1:
			result.A = p2;
			result.B = p3;
			break;
		default:
			result.A = p3;
			result.B = p;
			break;
		}
		return result;
	}

	public void EdgesToImmediateWindow()
	{
		if (_edge == null)
		{
			ComputeEdgesAndAdjacentTriangles();
		}
		checked
		{
			int count = _edge.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
			}
		}
	}

	public override bool Intersects(PointM PointM, double nearDistance)
	{
		checked
		{
			int count = Triangles.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (PointInTriangle(PointM, i))
				{
					return true;
				}
			}
			return false;
		}
	}

	public override bool Intersects(Extent extent)
	{
		return base.Intersects(extent);
	}

	public override string ToString()
	{
		return "TIN" + PointLabel(HasZ, HasM);
	}

	public bool Rasterize(RasterM raster, float[] values)
	{
		_Closure_0024__97_002D0 capturedLocals = default;
		_Closure_0024__97_002D0 capturedLocals2 = new _Closure_0024__97_002D0(capturedLocals);
		capturedLocals2.floatValues = values;
		capturedLocals2.cellsSet = false;
		checked
		{
			if (raster.Overlaps(this))
			{
				RasterizeTriangle rasterizeTriangle = new RasterizeTriangle(raster);
				int count = Triangles.Count() - 1;
				PointM p = default;
				PointM p2 = default;
				PointM p3 = default;
				for (int i = 0; i <= count; i++)
				{
					TrianglePointMs(i, ref p, ref p2, ref p3);
					rasterizeTriangle.ComputeCellsZNew(p, p2, p3, [SpecialName] (int n, float z) =>
					{
						capturedLocals2.floatValues[n] = z;
						capturedLocals2.cellsSet = true;
					});
				}
			}
			return capturedLocals2.cellsSet;
		}
	}

	public bool Rasterize(RasterM raster, bool[] mask)
	{
		_Closure_0024__98_002D0 capturedLocals = default;
		_Closure_0024__98_002D0 capturedLocals2 = new _Closure_0024__98_002D0(capturedLocals);
		capturedLocals2.mask = mask;
		capturedLocals2.cellsSet = false;
		checked
		{
			if (raster.Overlaps(this))
			{
				RasterizeTriangle rasterizeTriangle = new RasterizeTriangle(raster);
				int count = Triangles.Count() - 1;
				PointM p = default;
				PointM p2 = default;
				PointM p3 = default;
				for (int i = 0; i <= count; i++)
				{
					TrianglePointMs(i, ref p, ref p2, ref p3);
					rasterizeTriangle.ComputeCells(p, p2, p3, [SpecialName] (int n) =>
					{
						capturedLocals2.mask[n] = true;
						capturedLocals2.cellsSet = true;
					});
				}
			}
			return capturedLocals2.cellsSet;
		}
	}

	private bool ClipTriangle(int t, IExtent extent, ref int nc, ref double[] cx, ref double[] cy)
	{
		nc = 0;
		int ordinal = 0;
		do
		{
			double x;
			double y;
			double x2;
			double y2;
			switch (ordinal)
			{
			case 0:
				x = _pointM[Triangles[t].p0].X;
				y = _pointM[Triangles[t].p0].Y;
				x2 = _pointM[Triangles[t].p1].X;
				y2 = _pointM[Triangles[t].p1].Y;
				break;
			case 1:
				x = _pointM[Triangles[t].p1].X;
				y = _pointM[Triangles[t].p1].Y;
				x2 = _pointM[Triangles[t].p2].X;
				y2 = _pointM[Triangles[t].p2].Y;
				break;
			default:
				x = _pointM[Triangles[t].p2].X;
				y = _pointM[Triangles[t].p2].Y;
				x2 = _pointM[Triangles[t].p0].X;
				y2 = _pointM[Triangles[t].p0].Y;
				break;
			}
			double x2Term = x2 - x;
			double y2Term = y2 - y;
			double minXExtentTerm;
			double maxXExtentTerm;
			if ((x2Term > 0.0) | ((x2Term == 0.0) & (x > extent.MaxX)))
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
			if ((y2Term > 0.0) | ((y2Term == 0.0) & (y > extent.MaxY)))
			{
				minYExtentTerm = extent.MinY;
				maxYExtentTerm = extent.MaxY;
			}
			else
			{
				minYExtentTerm = extent.MaxY;
				maxYExtentTerm = extent.MinY;
			}
			double minValue = ((x2Term == 0.0) ? double.MinValue : ((minXExtentTerm - x) / x2Term));
			double minValue2 = ((y2Term == 0.0) ? double.MinValue : ((minYExtentTerm - y) / y2Term));
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
			if (1.0 >= minValue3)
			{
				if (0.0 < minValue3)
				{
					ClipPolygonAdd(minXExtentTerm, minYExtentTerm, ref nc, ref cx, ref cy);
				}
				if (1.0 >= minValue4)
				{
					double computed;
					if (x2Term != 0.0)
					{
						computed = (maxXExtentTerm - x) / x2Term;
					}
					else
					{
						computed = ((!((extent.MinX <= x) & (x <= extent.MaxX))) ? double.MinValue : double.MaxValue);
					}
					double computed2;
					if (y2Term != 0.0)
					{
						computed2 = (maxYExtentTerm - y) / y2Term;
					}
					else
					{
						computed2 = ((!((extent.MinY <= y) & (y <= extent.MaxY))) ? double.MinValue : double.MaxValue);
					}
					double computed3 = ((!(computed < computed2)) ? computed2 : computed);
					if ((0.0 < minValue4) | (0.0 < computed3))
					{
						if (minValue4 <= computed3)
						{
							if (0.0 < minValue4)
							{
								if (minValue > minValue2)
								{
									ClipPolygonAdd(minXExtentTerm, y + minValue * y2Term, ref nc, ref cx, ref cy);
								}
								else
								{
									ClipPolygonAdd(x + minValue2 * x2Term, minYExtentTerm, ref nc, ref cx, ref cy);
								}
							}
							if (1.0 > computed3)
							{
								if (computed < computed2)
								{
									ClipPolygonAdd(maxXExtentTerm, y + computed * y2Term, ref nc, ref cx, ref cy);
								}
								else
								{
									ClipPolygonAdd(x + computed2 * x2Term, maxYExtentTerm, ref nc, ref cx, ref cy);
								}
							}
							else
							{
								ClipPolygonAdd(x2, y2, ref nc, ref cx, ref cy);
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
				}
			}
			ordinal = checked(ordinal + 1);
		}
		while (ordinal <= 2);
		return nc > 0;
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

	public void ComputePointElevationsIfNoData(IPoints points, float[] z)
	{
		PointMs pointMs = new PointMs();
		List<int> intList = new List<int>();
		checked
		{
			int count = points.PointMCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (z[i] == -9999f)
				{
					pointMs.Add(points.PointM(i));
					intList.Add(i);
				}
			}
			ComputePointElevationsInternal(pointMs, intList, z);
		}
	}

	public void ComputePointElevations(IPoints points, float[] z)
	{
		ComputePointElevationsInternal(points, Enumerable.Range(0, points.PointMCount()).ToList(), z);
	}

	private void ComputePointElevationsInternal(IPoints subPts, List<int> subPtsIdxs, float[] z)
	{
		_Closure_0024__103_002D0 capturedLocals = default;
		_Closure_0024__103_002D0 capturedLocals2 = new _Closure_0024__103_002D0(capturedLocals);
		capturedLocals2.subPts = subPts;
		if (capturedLocals2.subPts == null || TriangleCount() == 0)
		{
			return;
		}
		EnsureTriangleSpatialIndex();
		if (capturedLocals2.subPts.PointMCount() == 0)
		{
			return;
		}
		checked
		{
			DistinctSpatialIndex distinctSpatialIndex = new DistinctSpatialIndex(capturedLocals2.subPts.ComputeExtent(), capturedLocals2.subPts.PointMCount() - 1);
			distinctSpatialIndex.AddRange(capturedLocals2.subPts);
			int count = distinctSpatialIndex.Bins.Count() - 1;
			double z2 = default;
			for (int i = 0; i <= count; i++)
			{
				List<int> bin = distinctSpatialIndex.Bins[i];
				if (bin.Count == 0)
				{
					continue;
				}
				PointMs pts = new PointMs(bin.Select([SpecialName] (int idx) => capturedLocals2.subPts.PointM(idx)));
				List<int> elementsAtPoints = _triangleSpatialIndex.GetElementsAtPoints(pts);
				bool[] boolValues = new bool[bin.Count - 1 + 1];
				foreach (int elementsAtPoint in elementsAtPoints)
				{
					int count2 = bin.Count - 1;
					for (int intervalIndex = 0; intervalIndex <= count2; intervalIndex++)
					{
						if (!boolValues[intervalIndex])
						{
							int index = bin[intervalIndex];
							Extent extent = TriangleExtent(elementsAtPoint);
							PointM pointM = capturedLocals2.subPts.PointM(index);
							if (extent.Contains(pointM) && TriangleShape(elementsAtPoint).ComputeZ(pointM, ref z2))
							{
								z[subPtsIdxs[index]] = (float)z2;
								boolValues[intervalIndex] = true;
							}
						}
					}
				}
			}
		}
	}

	public void ComputeProfiles(IList<Polyline> polylines, Profile[] profiles, double tolerance = 0.0)
	{
		_Closure_0024__104_002D0 capturedLocals = default;
		_Closure_0024__104_002D0 capturedLocals2 = new _Closure_0024__104_002D0(capturedLocals);
		capturedLocals2.tin = this;
		capturedLocals2.polylines = polylines;
		capturedLocals2.profiles = profiles;
		capturedLocals2.tolerance = tolerance;
		if (capturedLocals2.polylines == null || TriangleCount() == 0)
		{
			return;
		}
		EnsureTriangleSpatialIndex();
		capturedLocals2.siRasterizeSegment = new RasterizeSegment(_triangleSpatialIndex.Raster);
		Parallel.SmartFor(capturedLocals2.polylines.Count, checked([SpecialName] (int start, int fin) =>
		{
			_Closure_0024__104_002D2 capturedLocals3 = default;
			_Closure_0024__104_002D1 capturedLocals4 = default;
			for (int i = start; i <= fin; i++)
			{
				capturedLocals3 = new _Closure_0024__104_002D2(capturedLocals3);
				capturedLocals3.outerClosure = capturedLocals2;
				Polyline polyline = capturedLocals2.polylines[i];
				if (Polyline.IsValidPolyline(polyline) && polyline.MBROverlaps(capturedLocals2.tin))
				{
					capturedLocals3.segmentProfile = new Profile();
					capturedLocals3.segStartStation = 0.0;
					int count = polyline.SegmentCount() - 1;
					for (int j = 0; j <= count; j++)
					{
						capturedLocals4 = new _Closure_0024__104_002D1(capturedLocals4);
						capturedLocals4.outerClosure = capturedLocals3;
						capturedLocals4.segment = polyline.Segment(j);
						if (capturedLocals4.segment.MBROverlaps(capturedLocals2.tin))
						{
							capturedLocals2.siRasterizeSegment.ComputeCells(capturedLocals4.segment, capturedLocals4._Lambda_0024__1);
						}
						capturedLocals4.outerClosure.segStartStation = capturedLocals4.outerClosure.segStartStation + capturedLocals4.segment.Length;
					}
					if (capturedLocals3.segmentProfile.Count > 0)
					{
						capturedLocals3.segmentProfile.SortByStation();
						Profile profile = capturedLocals3.segmentProfile.Filter((float)capturedLocals2.tolerance);
						if (profile.Count > 0)
						{
							if (capturedLocals2.profiles[i] == null)
							{
								capturedLocals2.profiles[i] = profile;
							}
							else
							{
								capturedLocals2.profiles[i].AddRange(profile);
								capturedLocals2.profiles[i].SortByStation();
							}
						}
					}
				}
			}
		}), 256);
	}

	public void ComputeProfileSubsections(IList<Polyline> polylines, Profile[] profiles, List<Tuple<double, double>> subsections, double tolerance = 0.0)
	{
		_Closure_0024__105_002D0 capturedLocals = default;
		_Closure_0024__105_002D0 capturedLocals2 = new _Closure_0024__105_002D0(capturedLocals);
		capturedLocals2.tin = this;
		capturedLocals2.polylines = polylines;
		capturedLocals2.profiles = profiles;
		capturedLocals2.tolerance = tolerance;
		if (capturedLocals2.polylines == null || TriangleCount() == 0)
		{
			return;
		}
		EnsureTriangleSpatialIndex();
		capturedLocals2.siRasterizeSegment = new RasterizeSegment(_triangleSpatialIndex.Raster);
		Parallel.SmartFor(capturedLocals2.polylines.Count, checked([SpecialName] (int start, int fin) =>
		{
			_Closure_0024__105_002D2 capturedLocals3 = default;
			_Closure_0024__105_002D1 capturedLocals4 = default;
			for (int i = start; i <= fin; i++)
			{
				capturedLocals3 = new _Closure_0024__105_002D2(capturedLocals3);
				capturedLocals3.outerClosure = capturedLocals2;
				Polyline polyline = capturedLocals2.polylines[i];
				if (Polyline.IsValidPolyline(polyline) && polyline.MBROverlaps(capturedLocals2.tin))
				{
					capturedLocals3.segmentProfile = new Profile();
					capturedLocals3.segStartStation = 0.0;
					int count = polyline.SegmentCount() - 1;
					for (int j = 0; j <= count; j++)
					{
						capturedLocals4 = new _Closure_0024__105_002D1(capturedLocals4);
						capturedLocals4.outerClosure = capturedLocals3;
						capturedLocals4.segment = polyline.Segment(j);
						if (capturedLocals4.segment.MBROverlaps(capturedLocals2.tin))
						{
							capturedLocals2.siRasterizeSegment.ComputeCells(capturedLocals4.segment, capturedLocals4._Lambda_0024__1);
						}
						capturedLocals4.outerClosure.segStartStation = capturedLocals4.outerClosure.segStartStation + capturedLocals4.segment.Length;
					}
					if (capturedLocals3.segmentProfile.Count > 0)
					{
						capturedLocals3.segmentProfile.SortByStation();
						Profile profile = capturedLocals3.segmentProfile.Filter((float)capturedLocals2.tolerance);
						if (profile.Count > 0)
						{
							if (capturedLocals2.profiles[i] == null)
							{
								capturedLocals2.profiles[i] = profile;
							}
							else
							{
								capturedLocals2.profiles[i].AddRange(profile);
								capturedLocals2.profiles[i].SortByStation();
							}
						}
					}
				}
			}
		}), 256);
	}

	public void EnsureTriangleSpatialIndex()
	{
		checked
		{
			if (_triangleSpatialIndex == null)
			{
				Extent extent = new Extent(MaxX, MinX, MaxY, MinY);
				_triangleSpatialIndex = new SpatialIndex<int>(extent);
				int count = Triangles.Count() - 1;
				for (int i = 0; i <= count; i++)
				{
					_triangleSpatialIndex.Add(TriangleExtent(i), i);
				}
			}
		}
	}

	public SpatialIndex<int> GetTriangleSpatialIndex()
	{
		EnsureTriangleSpatialIndex();
		return _triangleSpatialIndex;
	}

	public Tin SwapZM()
	{
		PointMs pointMs = new PointMs();
		CopyTo(pointMs);
		pointMs.SwapZM();
		return new Tin(pointMs, Triangles);
	}

	public Tin SolveLaplaceZ()
	{
		PointMs pointMs = new PointMs();
		CopyTo(pointMs);
		double[] z = pointMs.GetZ();
		if (SolveLaplaceValues(z))
		{
			pointMs.SetZ(z);
			return new Tin(pointMs, Triangles);
		}
		return null;
	}

	public Tin SolveLaplaceM()
	{
		PointMs pointMs = new PointMs();
		CopyTo(pointMs);
		double[] m = pointMs.GetM();
		if (SolveLaplaceValues(m))
		{
			pointMs.SetM(m);
			return new Tin(pointMs, Triangles);
		}
		return null;
	}

	public Tin SolveLaplaceZM()
	{
		PointMs pointMs = new PointMs();
		CopyTo(pointMs);
		double[] z = pointMs.GetZ();
		double[] m = pointMs.GetM();
		if (SolveLaplaceValues(z) && SolveLaplaceValues(m))
		{
			pointMs.SetZM(z, m);
			return new Tin(pointMs, Triangles);
		}
		return null;
	}

	public Tin SolveLaplace(double[] v)
	{
		if (SolveLaplaceValues(v))
		{
			PointMs pointMs = new PointMs();
			CopyTo(pointMs);
			pointMs.SetZ(v);
			return new Tin(pointMs, Triangles);
		}
		return null;
	}

	public bool SolveLaplaceValues(double[] v)
	{
		_ = Environment.TickCount;
		checked
		{
			List<int>[] intListArray = new List<int>[_pointM.Count - 1 + 1];
			List<double>[] doubleListArray = new List<double>[_pointM.Count - 1 + 1];
			int count2 = _pointM.Count - 1;
			for (int i = 0; i <= count2; i++)
			{
				intListArray[i] = new List<int>();
				doubleListArray[i] = new List<double>();
			}
			int count3 = Triangles.Count() - 1;
			for (int j = 0; j <= count3; j++)
			{
				double x = _pointM[Triangles[j].Point(0)].X;
				double y = _pointM[Triangles[j].Point(0)].Y;
				double x2 = _pointM[Triangles[j].Point(1)].X;
				double y2 = _pointM[Triangles[j].Point(1)].Y;
				double x3 = _pointM[Triangles[j].Point(2)].X;
				double y3 = _pointM[Triangles[j].Point(2)].Y;
				double[] doubleValues = new double[3];
				double[] doubleValuesArray = new double[3];
				doubleValues[0] = y2 - y3;
				doubleValues[1] = y3 - y;
				doubleValues[2] = y - y2;
				doubleValuesArray[0] = x2 - x3;
				doubleValuesArray[1] = x3 - x;
				doubleValuesArray[2] = x - x2;
				double triangleAreaTerm = TriangleArea(j) * 0.5;
				if (triangleAreaTerm == 0.0)
				{
					continue;
				}
				int ordinal = 0;
				do
				{
					int point = Triangles[j].Point(ordinal);
					int ordinal2 = 0;
					do
					{
						if (ordinal != ordinal2)
						{
							int point2 = Triangles[j].Point(ordinal2);
							int indexOf = intListArray[point].IndexOf(point2);
							if (indexOf == -1)
							{
								indexOf = intListArray[point].Count;
								intListArray[point].Add(point2);
								doubleListArray[point].Add(0.0);
							}
							doubleListArray[point][indexOf] += (doubleValues[ordinal] * doubleValues[ordinal2] + doubleValuesArray[ordinal] * doubleValuesArray[ordinal2]) / triangleAreaTerm;
						}
						ordinal2++;
					}
					while (ordinal2 <= 2);
					ordinal++;
				}
				while (ordinal <= 2);
			}
			int ordinal3 = 0;
			int count4 = _pointM.Count - 1;
			for (int k = 0; k <= count4; k++)
			{
				ordinal3 += intListArray[k].Count;
			}
			int count = _pointM.Count;
			double[] doubleValues2 = new double[count - 1 + 1];
			int[] intValues = new int[count + ordinal3 + 1];
			double[] doubleValues3 = new double[count + ordinal3 + 1];
			double[] doubleValues4 = new double[count - 1 + 1];
			intValues[0] = count + 1;
			int countOffset = count + 1;
			int count5 = _pointM.Count - 1;
			for (int l = 0; l <= count5; l++)
			{
				double computed = 0.0;
				int count6 = intListArray[l].Count - 1;
				for (int m = 0; m <= count6; m++)
				{
					doubleValues3[countOffset] = doubleListArray[l][m];
					computed += doubleValues3[countOffset];
					intValues[countOffset] = intListArray[l][m];
					countOffset++;
				}
				doubleValues3[l] = 0.0 - computed;
				intValues[l + 1] = countOffset;
			}
			double maxValue = double.MaxValue;
			double minValue = double.MinValue;
			int count7 = _pointM.Count - 1;
			for (int n = 0; n <= count7; n++)
			{
				if (v[n] < maxValue)
				{
					maxValue = v[n];
				}
				if (v[n] > minValue)
				{
					minValue = v[n];
				}
			}
			double maxValueMinValueTerm = (maxValue + minValue) * 0.5;
			int ordinal4 = 0;
			int count8 = _pointM.Count - 1;
			for (int intervalIndex = 0; intervalIndex <= count8; intervalIndex++)
			{
				if (intListArray[intervalIndex].Count == 0)
				{
					doubleValues2[intervalIndex] = 0.0;
					doubleValues4[intervalIndex] = 0.0;
					doubleValues3[intervalIndex] = 1.0;
					continue;
				}
				if (double.IsNaN(v[intervalIndex]))
				{
					doubleValues2[intervalIndex] = 0.0;
					doubleValues4[intervalIndex] = maxValueMinValueTerm;
					continue;
				}
				doubleValues2[intervalIndex] = v[intervalIndex];
				doubleValues4[intervalIndex] = v[intervalIndex];
				doubleValues3[intervalIndex] = 1.0;
				int ordinal5 = intValues[intervalIndex];
				int count9 = intValues[intervalIndex + 1] - 1;
				for (int ordinal6 = ordinal5; ordinal6 <= count9; ordinal6++)
				{
					doubleValues3[ordinal6] = 0.0;
				}
				ordinal4++;
			}
			if (ordinal4 == count)
			{
				_laplaceConvergence = SparseSolver.enumConvergenceResult.NoUnknowns;
			}
			else
			{
				double tolerance = (minValue - maxValue) * 1E-06;
				if (tolerance == 0.0)
				{
					tolerance = 1E-06;
				}
				SparseSolver sparseSolver = new SparseSolver();
				sparseSolver.SetSparseMatrix(count, intValues, doubleValues3);
				_laplaceConvergence = sparseSolver.SolveMatrix(doubleValues2, tolerance, doubleValues4);
				int count10 = _pointM.Count - 1;
				for (int intervalIndex2 = 0; intervalIndex2 <= count10; intervalIndex2++)
				{
					if (double.IsNaN(v[intervalIndex2]))
					{
						if (doubleValues4[intervalIndex2] < maxValue)
						{
							v[intervalIndex2] = maxValue;
						}
						else if (doubleValues4[intervalIndex2] > minValue)
						{
							v[intervalIndex2] = minValue;
						}
						else
						{
							v[intervalIndex2] = doubleValues4[intervalIndex2];
						}
					}
				}
			}
			if (_laplaceConvergence == SparseSolver.enumConvergenceResult.Crashed)
			{
				return false;
			}
			return true;
		}
	}

	public void SetZ(int idx, float assignedZ)
	{
		PointM pointM = _pointM[idx];
		pointM.Z = assignedZ;
		_pointM[idx] = pointM;
	}

	public void SetZ(IList<float> values)
	{
		if (values == null || values.Count != _pointM.Count)
		{
			throw new ArgumentException("Set Z Values incorrect length");
		}
		checked
		{
			int count = values.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM pointM = _pointM[i];
				pointM.Z = values[i];
				_pointM[i] = pointM;
			}
		}
	}

	public void DebugExportShapefile(string shpFilename)
	{
		TinFeatureLayer tinFeatureLayer = new TinFeatureLayer("newlayer");
		tinFeatureLayer.AddFeature(this);
		tinFeatureLayer.SourceFilename = shpFilename;
		tinFeatureLayer.Save();
	}
}
