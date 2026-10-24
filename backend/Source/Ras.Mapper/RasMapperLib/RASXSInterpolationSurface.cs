using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Geospatial.Rasters.Classifications;
using H5Assist.VB;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Mapping;
using RasMapperLib.TinHelpers;
using RasMapperLib.Utilities;
using TiffBinaryReader;
using Utility;
using Utility.Extensions;
using Utility.Progress;

namespace RasMapperLib;

public class RASXSInterpolationSurface : TinFeatureLayer, IGeometryLayer
{
	private class XSInterpolationData
	{
		public PointMs LOB;

		public PointMs LCh;

		public PointMs RCh;

		public PointMs ROB;

		public List<PolylineIntersection> BankIntersections;

		public List<PolylineIntersection> EdgeIntersections;

		private RASGeometry _geom;

		private double _filterTolerance;

		private RASXS _xs;

		private int _xsid;

		private Polyline _polyline;

		private BankProperties _banks;

		public int XSID => _xsid;

		public Polyline Polyline => _polyline;

		public BankProperties Banks => _banks;

		public XSInterpolationData(RASGeometry geom)
		{
			LOB = new PointMs();
			LCh = new PointMs();
			RCh = new PointMs();
			ROB = new PointMs();
			_geom = geom;
			_xs = geom.XS;
			_filterTolerance = geom.FilterToleranceTIN;
		}

		public void SetXS(int xsid)
		{
			_xsid = xsid;
			_polyline = _xs.Polyline(xsid);
			_banks = _geom.XS.GetBankProperties(xsid);
			BankIntersections = _geom.XSIntersectBanks(xsid);
			EdgeIntersections = _geom.XSIntersectEdges(xsid);
			BankIntersections.Sort(PolylineIntersection.CompareB);
			EdgeIntersections.Sort(PolylineIntersection.CompareB);
		}

		public void ComputeParts(double z, PointOnPolyline IntersectPoint)
		{
			_polyline.PointsBeforePoP(_banks.LeftBankPoint, LOB);
			_polyline.PointSubsection(_banks.LeftBankPoint, IntersectPoint, LCh);
			_polyline.PointSubsection(IntersectPoint, _banks.RightBankPoint, RCh);
			_polyline.PointsAfterPoP(_banks.RightBankPoint, ROB);
			LOB.FilterXY(_filterTolerance);
			LCh.FilterXY(_filterTolerance);
			RCh.FilterXY(_filterTolerance);
			ROB.FilterXY(_filterTolerance);
			LOB.SetLinearZM(z, z, -2.0, -1.0);
			LCh.SetLinearZM(z, z, -1.0, 0.0);
			RCh.SetLinearZM(z, z, 0.0, 1.0);
			ROB.SetLinearZM(z, z, 1.0, 2.0);
		}

		public string Description()
		{
			return "River: " + River() + "  Reach: " + Reach() + "  River Station: " + RS();
		}

		public string River()
		{
			return IHasRRRSLayerExtensions.GetRiverName(_xs, _xsid);
		}

		public string Reach()
		{
			return IHasRRRSLayerExtensions.GetReachName(_xs, _xsid);
		}

		public string RS()
		{
			return IHasRRRSLayerExtensions.GetRiverStationName(_xs, _xsid);
		}
	}

	private class SurfaceFactory
	{
		private OffsetStreamLinesFactory osf;

		private double filterToleranceTIN;

		private RASBankLines Banks;

		private RASEdgeLines Edges;

		private PointMs Rstream;

		private PointMs RBstream;

		private PointMs LBstream;

		private PointMs Lstream;

		private PointMs perimeterPointMs;

		private List<PointIntersect> intersections;

		private PointMs region;

		private PointMs merged;

		public SurfaceFactory(RASGeometry geom)
		{
			Rstream = new PointMs();
			RBstream = new PointMs();
			LBstream = new PointMs();
			Lstream = new PointMs();
			perimeterPointMs = new PointMs();
			intersections = new List<PointIntersect>();
			region = new PointMs();
			merged = new PointMs();
			filterToleranceTIN = geom.FilterToleranceTIN;
			Banks = geom.BankLines;
			Edges = geom.EdgeLines;
			osf = new OffsetStreamLinesFactory(geom.FilterToleranceXY * 2.0);
		}

		public Tin XStoXSSurface(int usxsid, int dsxsid, PointMs stream, PolylineIntersection usIntersect, PolylineIntersection dsIntersect, XSInterpolationData us, XSInterpolationData ds, ref int leftBankLineFID, ref int rightBankLineFID, ref int leftEdgeLineFID, ref int rightEdgeLineFID, PointMs leftConstraint = null, PointMs rightConstraint = null)
		{
			us.ComputeParts(1.0, usIntersect.BPointOnPolyline);
			ds.ComputeParts(0.0, dsIntersect.BPointOnPolyline);
			if (GetExistingStreamLinePoints(us.Banks.LeftBankPoint, ds.Banks.LeftBankPoint, us.BankIntersections, ds.BankIntersections, Banks, LBstream, ref leftBankLineFID))
			{
				LBstream.FilterXY(filterToleranceTIN);
			}
			else
			{
				osf.ComputeOffset(stream, usIntersect.BPointOnPolyline, dsIntersect.BPointOnPolyline, us.Polyline, us.Banks.LeftBankPoint, ds.Polyline, ds.Banks.LeftBankPoint, LBstream);
			}
			if (GetExistingStreamLinePoints(us.Banks.RightBankPoint, ds.Banks.RightBankPoint, us.BankIntersections, ds.BankIntersections, Banks, RBstream, ref rightBankLineFID))
			{
				RBstream.FilterXY(filterToleranceTIN);
			}
			else
			{
				osf.ComputeOffset(stream, usIntersect.BPointOnPolyline, dsIntersect.BPointOnPolyline, us.Polyline, us.Banks.RightBankPoint, ds.Polyline, ds.Banks.RightBankPoint, RBstream);
			}
			if (GetExistingStreamLinePoints(us.Polyline.FirstPointOnPolyline(), ds.Polyline.FirstPointOnPolyline(), us.EdgeIntersections, ds.EdgeIntersections, Edges, Lstream, ref leftEdgeLineFID))
			{
				Lstream.FilterXY(filterToleranceTIN);
			}
			else
			{
				osf.ComputeOffset(stream, usIntersect.BPointOnPolyline, dsIntersect.BPointOnPolyline, us.Polyline, us.Polyline.FirstPointOnPolyline(), ds.Polyline, ds.Polyline.FirstPointOnPolyline(), Lstream);
			}
			if (GetExistingStreamLinePoints(us.Polyline.LastPointOnPolyline(), ds.Polyline.LastPointOnPolyline(), us.EdgeIntersections, ds.EdgeIntersections, Edges, Rstream, ref rightEdgeLineFID))
			{
				Rstream.FilterXY(filterToleranceTIN);
			}
			else
			{
				osf.ComputeOffset(stream, usIntersect.BPointOnPolyline, dsIntersect.BPointOnPolyline, us.Polyline, us.Polyline.LastPointOnPolyline(), ds.Polyline, ds.Polyline.LastPointOnPolyline(), Rstream);
			}
			if (leftConstraint != null)
			{
				MergeAtIntersections(Lstream, leftConstraint, takeRight: true);
			}
			if (rightConstraint != null)
			{
				MergeAtIntersections(Rstream, rightConstraint, takeRight: false);
			}
			Lstream.SetZM(double.NaN, -2.0);
			LBstream.SetZM(double.NaN, -1.0);
			stream.SetLinearZM(1.0, 0.0, 0.0, 0.0);
			RBstream.SetZM(double.NaN, 1.0);
			Rstream.SetZM(double.NaN, 2.0);
			double edgeLen = RasMapperLib.Utilities.Math.Max3(stream.ComputeLength() / 2.0, us.Polyline.Length / 8.0, ds.Polyline.Length / 8.0);
			double tolerance = filterToleranceTIN * 2.0;
			DevelopmentTin developmentTin = null;
			try
			{
				if (!MostlyStraight(LBstream, tolerance) || !MostlyStraight(RBstream, tolerance) || !MostlyStraight(stream, tolerance) || !MostlyStraight(Lstream, tolerance) || !MostlyStraight(Rstream, tolerance))
				{
					Rstream.Reverse();
					us.ROB.Reverse();
					us.RCh.Reverse();
					us.LCh.Reverse();
					us.LOB.Reverse();
					perimeterPointMs.Clear();
					perimeterPointMs.AddRange(Lstream, excludeFirst: false, excludeLast: true);
					perimeterPointMs.AddRange(ds.LOB);
					perimeterPointMs.AddRange(ds.LCh, excludeFirst: true, excludeLast: false);
					perimeterPointMs.AddRange(ds.RCh, excludeFirst: true, excludeLast: false);
					perimeterPointMs.AddRange(ds.ROB, excludeFirst: true, excludeLast: false);
					perimeterPointMs.AddRange(Rstream, excludeFirst: true, excludeLast: true);
					perimeterPointMs.AddRange(us.ROB);
					perimeterPointMs.AddRange(us.RCh, excludeFirst: true, excludeLast: false);
					perimeterPointMs.AddRange(us.LCh, excludeFirst: true, excludeLast: false);
					perimeterPointMs.AddRange(us.LOB, excludeFirst: true, excludeLast: false);
					List<Polyline> polylines = new List<Polyline>();
					polylines.Add(new Polyline(LBstream));
					polylines.Add(new Polyline(stream));
					polylines.Add(new Polyline(RBstream));
					double distanceTo = LBstream.First().DistanceTo(RBstream.First());
					double distanceTo2 = LBstream.Last().DistanceTo(RBstream.Last());
					double largestFlatTriangleEdge = System.Math.Min(distanceTo, distanceTo2) * 0.5;
					developmentTin = new DelaunayTINFactory(filterToleranceTIN).ComputeTIN(new Polygon(perimeterPointMs), polylines, null, null, removeDeletedTriangles: true, edgeLen, null, null, null, largestFlatTriangleEdge);
				}
				else
				{
					PointMs pointMs = new PointMs();
					pointMs.AddRange(us.LOB);
					pointMs.AddRange(us.LCh, excludeFirst: true, excludeLast: false);
					pointMs.AddRange(us.RCh, excludeFirst: true, excludeLast: false);
					pointMs.AddRange(us.ROB, excludeFirst: true, excludeLast: false);
					PointMs pointMs2 = new PointMs();
					pointMs2.AddRange(ds.LOB);
					pointMs2.AddRange(ds.LCh, excludeFirst: true, excludeLast: false);
					pointMs2.AddRange(ds.RCh, excludeFirst: true, excludeLast: false);
					pointMs2.AddRange(ds.ROB, excludeFirst: true, excludeLast: false);
					developmentTin = RibbonTINFactory.CreateTIN(new List<IPoints> { pointMs, pointMs2 }, filterToleranceTIN);
				}
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				throw new Exception("Error creating TIN for region between XS's:\r" + us.Description() + "\r" + ds.Description() + "\rerror = " + ex2.Message);
			}
			if (developmentTin != null && developmentTin.Triangles.Count > 0)
			{
				return developmentTin.MakeCleanTin().SolveLaplaceZM();
			}
			return null;
		}

		private bool MostlyStraight(PointMs points, double tolerance)
		{
			if (points.Count < 3)
			{
				return true;
			}
			SegmentM segmentM = new SegmentM(points.First(), points.Last());
			checked
			{
				int count = points.Count - 2;
				for (int i = 1; i <= count; i++)
				{
					PointM pt = points[i];
					SegmentM.RelativePosition relativePosition = SegmentM.RelativePosition.Undefined;
					if (segmentM.DistanceToPoint(pt, ref relativePosition) > tolerance)
					{
						return false;
					}
				}
				return true;
			}
		}

		private bool GetExistingStreamLinePoints(PointOnPolyline usPoint, PointOnPolyline dsPoint, List<PolylineIntersection> usIntersections, List<PolylineIntersection> dsIntersections, PolylineFeatureLayer layer, PointMs streamline, ref int foundLineFID)
		{
			double distance = filterToleranceTIN * 100.0;
			if (usIntersections != null && dsIntersections != null)
			{
				double maxValue = double.MaxValue;
				PolylineIntersection polylineIntersection = default;
				foreach (PolylineIntersection usIntersection in usIntersections)
				{
					if (usIntersection.Point.CloseTo(usPoint.PointM, distance))
					{
						double distanceTo = usIntersection.Point.DistanceTo(usPoint.PointM);
						if (distanceTo < maxValue)
						{
							maxValue = distanceTo;
							polylineIntersection = usIntersection;
						}
					}
				}
				if (maxValue != double.MaxValue)
				{
					double maxValue2 = double.MaxValue;
					PolylineIntersection polylineIntersection2 = default;
					foreach (PolylineIntersection dsIntersection in dsIntersections)
					{
						if (polylineIntersection.APolylineIndex == dsIntersection.APolylineIndex && dsIntersection.Point.CloseTo(dsPoint.PointM, distance))
						{
							double distanceTo2 = dsIntersection.Point.DistanceTo(dsPoint.PointM);
							if (distanceTo2 < maxValue2)
							{
								maxValue2 = distanceTo2;
								polylineIntersection2 = dsIntersection;
							}
						}
					}
					if (maxValue2 != double.MaxValue)
					{
						foundLineFID = polylineIntersection.APolylineIndex;
						layer.Polyline(polylineIntersection.APolylineIndex).PointSubsection(polylineIntersection.APointOnPolyline, polylineIntersection2.APointOnPolyline, streamline);
						return true;
					}
				}
			}
			return false;
		}

		private void MergeAtIntersections(PointMs a, PointMs b, bool takeRight)
		{
			if (b.Count == 0 || !a.First().EqualXY(b.First()) || !a.Last().EqualXY(b.Last()))
			{
				return;
			}
			a.Intersections(b, intersections);
			if (intersections.Count <= 1)
			{
				return;
			}
			merged.Clear();
			checked
			{
				int count = intersections.Count - 1;
				for (int i = 1; i <= count; i++)
				{
					PointIntersect pointIntersect = intersections[i - 1];
					PointIntersect pointIntersect2 = intersections[i];
					region.Clear();
					region.AddRange(a, pointIntersect.APointOnPolyline, pointIntersect2.APointOnPolyline);
					region.AddRangeReverse(b, pointIntersect.BPointOnPolyline, pointIntersect2.BPointOnPolyline);
					double area = region.ComputeArea();
					if ((takeRight & (area > 0.0)) | (!takeRight & (area < 0.0)))
					{
						merged.AddRange(a, pointIntersect.APointOnPolyline, pointIntersect2.APointOnPolyline);
					}
					else
					{
						merged.AddRange(b, pointIntersect.BPointOnPolyline, pointIntersect2.BPointOnPolyline);
					}
				}
				a.Clear();
				a.AddRange(merged);
			}
		}
	}

	private struct USDSXSID
	{

	}

	public class StreamMtoStripM
	{
		private float _usM0;

		private float _dsM0;

		public StreamMtoStripM(Tin tin)
		{
			int ordinal = -1;
			int ordinal2 = -1;
			int ordinal3 = -1;
			int ordinal4 = -1;
			int ordinal5 = -1;
			int ordinal6 = -1;
			checked
			{
				int count = tin.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					PointM pointM = tin.PointM(i);
					if (pointM.Z == 1.0)
					{
						if (ordinal == -1 || System.Math.Abs(pointM.M) < System.Math.Abs(((MultiPoint)tin).get_M(ordinal)))
						{
							ordinal = i;
						}
						if (ordinal2 == -1 || System.Math.Abs(pointM.M + 1.0) < System.Math.Abs(((MultiPoint)tin).get_M(ordinal2) + 1.0))
						{
							ordinal2 = i;
						}
						if (ordinal3 == -1 || System.Math.Abs(pointM.M - 1.0) < System.Math.Abs(((MultiPoint)tin).get_M(ordinal3) - 1.0))
						{
							ordinal3 = i;
						}
					}
					else if (pointM.Z == 0.0)
					{
						if (ordinal4 == -1 || System.Math.Abs(pointM.M) < System.Math.Abs(((MultiPoint)tin).get_M(ordinal4)))
						{
							ordinal4 = i;
						}
						if (ordinal5 == -1 || System.Math.Abs(pointM.M + 1.0) < System.Math.Abs(((MultiPoint)tin).get_M(ordinal5) + 1.0))
						{
							ordinal5 = i;
						}
						if (ordinal6 == -1 || System.Math.Abs(pointM.M - 1.0) < System.Math.Abs(((MultiPoint)tin).get_M(ordinal6) - 1.0))
						{
							ordinal6 = i;
						}
					}
				}
				if (ordinal2 == -1 || ordinal == -1 || ordinal3 == -1 || ordinal5 == -1 || ordinal4 == -1 || ordinal6 == -1)
				{
					throw new Exception("Bad interpolation surface.");
				}
				float distanceTo = (float)tin.PointM(ordinal).DistanceTo(tin.PointM(ordinal2));
				float distanceTo2 = (float)tin.PointM(ordinal).DistanceTo(tin.PointM(ordinal3));
				float distanceTo3 = (float)tin.PointM(ordinal4).DistanceTo(tin.PointM(ordinal5));
				float distanceTo4 = (float)tin.PointM(ordinal4).DistanceTo(tin.PointM(ordinal6));
				_usM0 = (float)((double)(distanceTo / (distanceTo + distanceTo2)) * 2.0 - 1.0);
				_dsM0 = (float)((double)(distanceTo3 / (distanceTo3 + distanceTo4)) * 2.0 - 1.0);
			}
		}

		public double MStrip(PointM pt)
		{
			if (pt.M <= -1.0)
			{
				return pt.M;
			}
			if (pt.M >= 1.0)
			{
				return pt.M;
			}
			if (pt.M < 0.0)
			{
				double ptUsM0Term = pt.M * ((double)_usM0 + 1.0) + (double)_usM0;
				double ptDsM0Term = pt.M * ((double)_dsM0 + 1.0) + (double)_dsM0;
				double ptTerm = ptUsM0Term * pt.Z + ptDsM0Term * (1.0 - pt.Z);
				if (ptTerm >= -1.0)
				{
					_ = 1.0;
				}
				return ptTerm;
			}
			double ptUsM0Term2 = pt.M * (1.0 - (double)_usM0) + (double)_usM0;
			double ptDsM0Term2 = pt.M * (1.0 - (double)_dsM0) + (double)_dsM0;
			double ptTerm2 = ptUsM0Term2 * pt.Z + ptDsM0Term2 * (1.0 - pt.Z);
			if (ptTerm2 >= -1.0)
			{
				_ = 1.0;
			}
			return ptTerm2;
		}
	}

	private static readonly double[] InterpolationDefaultValues = new double[2] { 0.0, 1.0 };

	private static readonly double[] InterpolationMDefaultValues = new double[5] { -2.0, -1.0, 0.0, 1.0, 2.0 };

	public const string sUSXSID = "USXSID";

	public const string sDSXSID = "DSXSID";

	public const string sLeftBankLineFID = "Left Bankline FID";

	public const string sRightBankLineFID = "Right Bankline FID";

	public const string sLeftEdgeLineFID = "Left Edgeline FID";

	public const string sRightEdgeLineFID = "Right Edgeline FID";

	public const string HashAttributeName = "Source Data Hash";

	public const string HDFGroupName = "Geometry/Cross Section Interpolation Surfaces";

	private const string sNormalizedTIN = "Normalized M TIN";

	private const string sPlotNormalizedTIN = "Plot M Normalized TIN";

	public const string sHiddenLengthForCourants = "Channel Length";

	private bool _hasComputedLengthsForCourant;

	private List<string> _currentProblems;

	private DateTime _currentProblemJunctionTime;

	private DateTime _currentProblemRiverTime;

	private DateTime _currentProbemXSTime;

	private bool _hotSwapFeature;

	private List<int> _renderOrder;

	public RASGeometry Geometry { get; }

	public override bool ForwardEditRequestsToParent { get; }

	public override string SimpleFeatureTypeName { get; set; }

	public override bool CanExportAsShapefile { get; }

	public override bool CanEdit { get; }
string IGeometryLayer.HDFGroupName=> "Geometry/Cross Section Interpolation Surfaces";

	public RASXSInterpolationSurface(RASGeometry geometry)
		: base("Interpolation Surface", geometry.SourceFilename)
	{
		_hasComputedLengthsForCourant = false;
		_currentProblems = new List<string>();
		_currentProblemJunctionTime = DateTime.MinValue;
		_currentProblemRiverTime = DateTime.MinValue;
		_currentProbemXSTime = DateTime.MinValue;
		ForwardEditRequestsToParent = true;
		SimpleFeatureTypeName = "XSIS";
		CanExportAsShapefile = true;
		CanEdit = false;
		_hotSwapFeature = false;
		Geometry = geometry;
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddAttributeColumn("USXSID", typeof(int));
		AddAttributeColumn("DSXSID", typeof(int));
		AddAttributeColumn("Normalized M TIN", typeof(Tin), null, DataColumnViewState.Invisible);
		AddAttributeColumn("Channel Length", typeof(double), null, DataColumnViewState.GrayedOut);
		AddAttributeColumn("Left Bankline FID", typeof(int), null, DataColumnViewState.Invisible);
		AddAttributeColumn("Right Bankline FID", typeof(int), null, DataColumnViewState.Invisible);
		AddAttributeColumn("Left Edgeline FID", typeof(int), null, DataColumnViewState.Invisible);
		AddAttributeColumn("Right Edgeline FID", typeof(int), null, DataColumnViewState.Invisible);
	}

	protected void HDFLoadFeatureTable(H5Reader hr)
	{
		HDF5Storage.ReadTINFeatures(this, hr, "Geometry/Cross Section Interpolation Surfaces/TIN");
		HDF5Storage.ReadColumns(this, hr, "Geometry/Cross Section Interpolation Surfaces/XSIDs", "USXSID", "DSXSID");
		HDF5Storage.ReadColumns(this, hr, "Geometry/Cross Section Interpolation Surfaces/Bounding Lines", "Left Bankline FID", "Right Bankline FID", "Left Edgeline FID", "Right Edgeline FID");
		LoadNormalizedMTIN();
	}

	void IGeometryLayer.HDFLoadFeatureTable(H5Reader hr)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFLoadFeatureTable
		this.HDFLoadFeatureTable(hr);
	}

	private void LoadNormalizedMTIN()
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Tin tin = Tin(i);
				if (tin == null)
				{
					continue;
				}
				try
				{
					StreamMtoStripM streamMtoStripM = new StreamMtoStripM(tin);
					PointMs pointMs = new PointMs(tin.PointMCount());
					int count2 = tin.PointMCount() - 1;
					for (int j = 0; j <= count2; j++)
					{
						PointM pointM = tin.PointM(j);
						pointM.M = streamMtoStripM.MStrip(pointM);
						pointMs.Add(pointM);
					}
					SetNormalizedMTIN(i, new Tin(pointMs, (TriangleStruct[])tin.Triangles.Clone()));
				}
				catch (Exception ex)
				{
					ProjectData.SetProjectError(ex);
					Exception ex2 = ex;
					ProjectData.ClearProjectError();
				}
			}
		}
	}

	protected void HDFSaveFeatureTable(H5Writer hw)
	{
		if (FeatureCount() == 0)
		{
			hw.Delete("Geometry/Cross Section Interpolation Surfaces");
			return;
		}
		hw.CreateGroup("Geometry/Cross Section Interpolation Surfaces");
		HDF5Storage.WriteTINFeatures(this, hw, "Geometry/Cross Section Interpolation Surfaces/TIN");
		HDF5Storage.WriteColumns(this, hw, "Geometry/Cross Section Interpolation Surfaces/XSIDs", "USXSID", "DSXSID");
		HDF5Storage.WriteColumns(this, hw, "Geometry/Cross Section Interpolation Surfaces/Bounding Lines", "Left Bankline FID", "Right Bankline FID", "Left Edgeline FID", "Right Edgeline FID");
	}

	void IGeometryLayer.HDFSaveFeatureTable(H5Writer hw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFSaveFeatureTable
		this.HDFSaveFeatureTable(hw);
	}

	public void Stream(BinaryWriter bw)
	{
		bw.Write(FeatureCount());
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Tin(i).Stream(bw);
				bw.Write(GetUpstreamXSID(i));
				bw.Write(GetDownstreamXSID(i));
			}
		}
	}

	public static RASXSInterpolationSurface Stream(BinaryReader br, RASGeometry geom)
	{
		RASXSInterpolationSurface rASXSInterpolationSurface = new RASXSInterpolationSurface(geom);
		rASXSInterpolationSurface.InitializeEmptyFeatureTable();
		rASXSInterpolationSurface.SuppressTableReload(boolValue: true);
		checked
		{
			using (new FeatureTableReloadSuppressor(rASXSInterpolationSurface))
			{
				int count = br.ReadInt32() - 1;
				for (int i = 0; i <= count; i++)
				{
					Tin feature = RasMapperLib.Tin.Stream(br);
					rASXSInterpolationSurface.AddFeature(feature);
					rASXSInterpolationSurface.SetUpstreamXSID(i, br.ReadInt32());
					rASXSInterpolationSurface.SetDownstreamXSID(i, br.ReadInt32());
				}
				return rASXSInterpolationSurface;
			}
		}
	}

	protected override void OnFeatureChanged(FeatureChangedEventArgs args)
	{
		base.OnFeatureChanged(args);
	}

	public override List<PlotOption> GetStaticPlotOptions()
	{
		List<PlotOption> staticPlotOptions = base.GetStaticPlotOptions();
		staticPlotOptions.Add(new PlotOption("Plot M Normalized TIN"));
		return staticPlotOptions;
	}

	public override Feature Feature(int fId)
	{
		if (_hotSwapFeature)
		{
			return GetNormalizedMTIN(fId);
		}
		return base.Feature(fId);
	}

	public List<int> GetRenderOrder()
	{
		if (_renderOrder == null)
		{
			_renderOrder = ComputeRenderOrder();
		}
		return _renderOrder;
	}

	private List<int> ComputeRenderOrder()
	{
		checked
		{
			List<int>[] intListArray = new List<int>[Geometry.XS.FeatureCount() - 1 + 1];
			List<int>[] intListArrayList = new List<int>[Geometry.XS.FeatureCount() - 1 + 1];
			int count = Geometry.XS.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				intListArray[i] = new List<int>();
				intListArrayList[i] = new List<int>();
			}
			int count2 = FeatureCount() - 1;
			for (int j = 0; j <= count2; j++)
			{
				int upstreamXSID = GetUpstreamXSID(j);
				int downstreamXSID = GetDownstreamXSID(j);
				if (upstreamXSID >= 0 && upstreamXSID < intListArrayList.Count())
				{
					intListArrayList[upstreamXSID].Add(j);
				}
				if (downstreamXSID >= 0 && downstreamXSID < intListArray.Count())
				{
					intListArray[downstreamXSID].Add(j);
				}
			}
			List<int> intList = new List<int>();
			int count3 = Geometry.XS.FeatureCount() - 1;
			for (int k = 0; k <= count3; k++)
			{
				List<int> intListArray2 = intListArray[k];
				List<int> intListArray3 = intListArrayList[k];
				if (intListArray2.Count > 1)
				{
					if (intListArray3.Count > 1)
					{
						continue;
					}
					string riverName = IHasRRRSLayerExtensions.GetRiverName(Geometry.XS, k);
					int ordinal = -1;
					foreach (int ordinal2 in intListArray2)
					{
						int upstreamXSID2 = GetUpstreamXSID(ordinal2);
						if (Operators.CompareString(IHasRRRSLayerExtensions.GetRiverName(Geometry.XS, upstreamXSID2), riverName, TextCompare: false) == 0)
						{
							ordinal = ordinal2;
							break;
						}
					}
					if (ordinal != -1)
					{
						intList.Add(ordinal);
					}
				}
				else
				{
					if (intListArray3.Count <= 1)
					{
						continue;
					}
					string riverName2 = IHasRRRSLayerExtensions.GetRiverName(Geometry.XS, k);
					int ordinal3 = -1;
					foreach (int ordinal4 in intListArray3)
					{
						int downstreamXSID2 = GetDownstreamXSID(ordinal4);
						if (Operators.CompareString(IHasRRRSLayerExtensions.GetRiverName(Geometry.XS, downstreamXSID2), riverName2, TextCompare: false) == 0)
						{
							ordinal3 = ordinal4;
							break;
						}
					}
					if (ordinal3 != -1)
					{
						intList.Add(ordinal3);
					}
				}
			}
			intList.AddRange(Enumerable.Range(0, FeatureCount()));
			return intList.Distinct().ToList();
		}
	}

	public int GetLeftBanklineFID(int fid)
	{
		return DataTableExtensions.GetInteger(FeatureTable(), fid, "Left Bankline FID");
	}

	public void SetLeftBankLineFID(int fid, int lineFID)
	{
		FeatureRow(fid)["Left Bankline FID"] = lineFID;
	}

	public int GetRightBanklineFID(int fid)
	{
		return DataTableExtensions.GetInteger(FeatureTable(), fid, "Right Bankline FID");
	}

	public void SetRightBankLineFID(int fid, int line)
	{
		FeatureRow(fid)["Right Bankline FID"] = line;
	}

	public int GetLeftEdgelineFID(int fid)
	{
		return DataTableExtensions.GetInteger(FeatureTable(), fid, "Left Edgeline FID");
	}

	public void SetLeftEdgeLineFID(int fid, int line)
	{
		FeatureRow(fid)["Left Edgeline FID"] = line;
	}

	public int GetRightEdgelineFID(int fid)
	{
		return DataTableExtensions.GetInteger(FeatureTable(), fid, "Right Edgeline FID");
	}

	public void SetRightEdgeLineFID(int fid, int line)
	{
		FeatureRow(fid)["Right Edgeline FID"] = line;
	}

	public int GetUpstreamXSID(int fid)
	{
		return DataTableExtensions.GetInteger(FeatureTable(), fid, "USXSID");
	}

	public void SetUpstreamXSID(int fid, int xsid)
	{
		FeatureRow(fid)["USXSID"] = xsid;
	}

	public int GetDownstreamXSID(int fid)
	{
		return DataTableExtensions.GetInteger(FeatureTable(), fid, "DSXSID");
	}

	public void SetDownstreamXSID(int fid, int xsid)
	{
		FeatureRow(fid)["DSXSID"] = xsid;
	}

	public Tin GetNormalizedMTIN(int fid)
	{
		object objectValue = RuntimeHelpers.GetObjectValue(FeatureRow(fid)["Normalized M TIN"]);
		if (objectValue == null || Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)))
		{
			return null;
		}
		return objectValue as Tin;
	}

	private void SetNormalizedMTIN(int fid, Tin tin)
	{
		FeatureRow(fid)["Normalized M TIN"] = tin;
	}

	public void EnsureLengthsForCourant()
	{
		if (!Geometry.IsResultFile || FeatureCount() == 0 || _hasComputedLengthsForCourant)
		{
			return;
		}
		_hasComputedLengthsForCourant = true;
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				int upstreamXSID = GetUpstreamXSID(i);
				SetLengthsForCourant(i, Geometry.XS.GetChannelLength(upstreamXSID));
			}
			if (Geometry.Junctions.FeatureCount() == 0)
			{
				return;
			}
			try
			{
				RiverMap riverMap = Geometry.CreateRiverMap();
				foreach (RiverMap.River river in riverMap.Rivers)
				{
					foreach (RiverMap.Reach reach in river.Reaches)
					{
						if (reach.ID == -1)
						{
							continue;
						}
						RiverMap.CrossSection crossSection = reach.DownstreamCrossSection();
						if (crossSection.ID < 0)
						{
							continue;
						}
						int iD = crossSection.ID;
						if (!(reach.DownstreamConnection is RiverMap.Junction junction))
						{
							continue;
						}
						foreach (RiverMap.Reach dSReach in junction.DSReaches)
						{
							if (dSReach == null || dSReach.ID < 0)
							{
								continue;
							}
							RiverMap.CrossSection crossSection2 = dSReach.UpstreamCrossSection();
							if (crossSection2 == null || crossSection2.ID < 0)
							{
								continue;
							}
							int iD2 = crossSection2.ID;
							int count2 = FeatureCount() - 1;
							for (int j = 0; j <= count2; j++)
							{
								int upstreamXSID2 = GetUpstreamXSID(j);
								int downstreamXSID = GetDownstreamXSID(j);
								if (upstreamXSID2 == iD && downstreamXSID == iD2)
								{
									SetLengthsForCourant(j, Geometry.River.GetDownstreamXSDistance(reach.ID) + Geometry.River.GetUpstreamXSDistance(dSReach.ID));
									break;
								}
							}
						}
						double len = Geometry.River.GetDownstreamXSDistance(reach.ID);
						SetLengthsForCourant(crossSection.ID, len);
					}
				}
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				ProjectData.ClearProjectError();
			}
		}
	}

	public double GetLengthsForCourant(int fid)
	{
		EnsureLengthsForCourant();
		return DataTableExtensions.GetDouble(FeatureTable(), fid, "Channel Length");
	}

	public void SetLengthsForCourant(int fID, double len)
	{
		FeatureRow(fID)["Channel Length"] = len;
	}

	public bool CompleteForComputations(ProgressReporter prog)
	{
		if (!EnsureInterpolationSurface())
		{
			return false;
		}
		CleanAssociationPropertyTables();
		if (FeatureCount() == 0)
		{
			return true;
		}
		if (!ProcessAssociationForComputations(Geometry.InfiltrationAssociation, Geometry.InfiltrationResampler, prog))
		{
			return false;
		}
		if (!ProcessAssociationForComputations(Geometry.PercentImperviousAssociation, Geometry.PercentImperviousResampler, prog))
		{
			return false;
		}
		return true;
	}

	public bool EnsureInterpolationSurface()
	{
		if (HashNeedToRecompute())
		{
			return ComputeInterpolationSurface();
		}
		if (Geometry.XS.FeatureCount() == 0)
		{
			using H5Writer h5Writer = new H5Writer(SourceFilename);
			h5Writer.Delete("Geometry/Cross Section Interpolation Surfaces");
		}
		return true;
	}

	private bool HashNeedToRecompute()
	{
		byte[] sourceDataHash = ComputeSourceDataHash();
		if (sourceDataHash == null)
		{
			return false;
		}
		byte[] hash = ReadStoredHash();
		if (Hash.HashEquals(sourceDataHash, hash))
		{
			return false;
		}
		return true;
	}

	private byte[] ComputeSourceDataHash()
	{
		RASGeometry geometry = Geometry;
		if (geometry == null)
		{
			return null;
		}
		if (geometry.XS.FeatureCount() == 0)
		{
			return null;
		}
		if (geometry.River.FeatureCount() == 0)
		{
			return null;
		}
		if (geometry.IsResultFile)
		{
			return null;
		}
		_ = geometry.Junctions;
		_ = geometry.River;
		_ = geometry.XS;
		Hash hash = new Hash();
		geometry.River.MD5HashAppend(hash);
		geometry.BankLines.MD5HashAppend(hash);
		geometry.EdgeLines.MD5HashAppend(hash);
		geometry.XS.MD5HashAppend(hash);
		geometry.Junctions.MD5HashAppend(hash);
		hash.Add("1.2");
		return hash.HashBytes();
	}

	private byte[] ReadStoredHash()
	{
		if (!File.Exists(SourceFilename))
		{
			return null;
		}
		using H5Reader h5Reader = new H5Reader(SourceFilename);
		if (h5Reader.ExistsAsGroup("Geometry/Cross Section Interpolation Surfaces") && h5Reader.ExistsAsAttribute("Geometry/Cross Section Interpolation Surfaces", "Source Data Hash"))
		{
			byte[] values = null;
			if (h5Reader.TryReadAttribute("Geometry/Cross Section Interpolation Surfaces", "Source Data Hash", ref values))
			{
				return values;
			}
		}
		return null;
	}

	private void ComputeListOfProblems()
	{
		DataTable dataTable = FeatureTable();
		_currentProblems.Clear();
		RASGeometry geometry = Geometry;
		RASJunction junctions = geometry.Junctions;
		RASRiver river = geometry.River;
		RASXS xS = geometry.XS;
		DataTable dataTable2 = xS.FeatureTable();
		_currentProblemJunctionTime = junctions.FeaturesTime();
		_currentProblemRiverTime = river.FeaturesTime();
		_currentProbemXSTime = xS.FeaturesTime();
		if (xS.FeatureCount() == 0)
		{
			return;
		}
		if (dataTable.Rows.Count == 0)
		{
			_currentProblems.Add("There are no stored interpolation surface");
			return;
		}
		int indexOf = dataTable.Columns.IndexOf("USXSID");
		int indexOf2 = dataTable.Columns.IndexOf("DSXSID");
		if (indexOf == -1 || indexOf2 == -1)
		{
			_currentProblems.Add("Interpolation surface layer does not have proper XS reference attributes");
			return;
		}
		int featureCount = xS.FeatureCount();
		checked
		{
			bool[] boolValues = new bool[featureCount - 1 + 1];
			foreach (DataRow row3 in dataTable.Rows)
			{
				int dataRowItemToInteger = Form.DataRowItemToInteger(row3, indexOf);
				int dataRowItemToInteger2 = Form.DataRowItemToInteger(row3, indexOf2);
				if (dataRowItemToInteger < 0 || dataRowItemToInteger2 < 0)
				{
					if (!_currentProblems.Contains("Interpolation surface not in geometry"))
					{
						_currentProblems.Add("Interpolation surface not in geometry");
					}
				}
				else if (dataRowItemToInteger >= featureCount || dataRowItemToInteger2 >= featureCount)
				{
					if (!_currentProblems.Contains("Interpolation surface has XS(s) with incorrect fID references"))
					{
						_currentProblems.Add("Interpolation surface has XS(s) with incorrect fID references");
					}
				}
				else
				{
					boolValues[dataRowItemToInteger] = true;
					boolValues[dataRowItemToInteger2] = true;
				}
			}
			List<int> intList = new List<int>();
			int count = xS.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (!boolValues[i])
				{
					intList.Add(i);
				}
			}
			if (intList.Count <= 0)
			{
				return;
			}
			_currentProblems.Add("Stored Interpolation Surface does not contain XS'(s) at:");
			int c = dataTable2.Columns.IndexOf("River");
			int c2 = dataTable2.Columns.IndexOf("Reach");
			int c3 = dataTable2.Columns.IndexOf("River Station");
			int count2 = intList.Count - 1;
			for (int j = 0; j <= count2; j++)
			{
				int index = intList[j];
				DataRow row2 = dataTable2.Rows[index];
				string dataRowItemToString = Form.DataRowItemToString(row2, c);
				string dataRowItemToString2 = Form.DataRowItemToString(row2, c2);
				string dataRowItemToString3 = Form.DataRowItemToString(row2, c3);
				_currentProblems.Add("   " + dataRowItemToString.PadRight(18) + dataRowItemToString2.PadRight(18) + dataRowItemToString3);
				if (j > 10)
				{
					_currentProblems.Add("   ...");
					break;
				}
			}
		}
	}

	private void CleanAssociationPropertyTables()
	{
		using H5Writer hw = new H5Writer(SourceFilename);
		CleanAssociationPropertyTables(hw);
	}

	private void CleanAssociationPropertyTables(H5Writer hw)
	{
		if (!hw.ExistsAsGroup("Geometry/Cross Section Interpolation Surfaces"))
		{
			return;
		}
		foreach (string fullyQualifiedGroupName in hw.GetFullyQualifiedGroupNames("Geometry/Cross Section Interpolation Surfaces"))
		{
			_ = fullyQualifiedGroupName;
		}
	}

	private bool ProcessAssociationForComputations(GeometryAssociation association, ParameterResampler resampler, ProgressReporter reporter)
	{
		using H5Writer hw = new H5Writer(SourceFilename);
		return RASGeometry.ProcessPolygonAssociations(hw, "Geometry/Cross Section Interpolation Surfaces", (from t in Tins()
			select InterpolationSurfaceAsPolygon(t)).ToList(), association, resampler, [SpecialName] (int idx) => "Fatal resampling error for Interpolation Surface " + GetFeatureName(idx) + " " + association.AssociationName.InParentheses(), reporter);
	}

	private Polygon InterpolationSurfaceAsPolygon(int fid)
	{
		Tin tin = Tin(fid);
		return InterpolationSurfaceAsPolygon(tin);
	}

	private Polygon InterpolationSurfaceAsPolygon(Tin tin)
	{
		return Converter.Convert(Converter.Convert(tin).GetPolygonHull()[0].OuterRing.Polygon);
	}

	public static void ComputeGenericInterpolationSurface(Polyline centerline, float crossSectionRadius, double filterToleranceTIN = 0.1)
	{
		PointMs leftOffset = null;
		PointMs rightOffset = null;
		centerline.GetOffsetPolygon(crossSectionRadius * 2f, ref leftOffset, ref rightOffset);
		PointMs pointMs = new PointMs
		{
			leftOffset.First(),
			centerline.First(),
			rightOffset.Last()
		};
		new PointMs();
		pointMs.Add(leftOffset.Last());
		pointMs.Add(centerline.Last());
		pointMs.Add(rightOffset.First());
	}

	public bool ComputeInterpolationSurface(bool writeToHDF = true)
	{
		RASGeometry geometry = Geometry;
		RASJunction junctions = geometry.Junctions;
		RASRiver river = geometry.River;
		checked
		{
			bool result;
			if (geometry.XS.FeatureCount() == 0 || river.FeatureCount() == 0)
			{
				if (writeToHDF)
				{
					using H5Writer h5Writer = new H5Writer(SourceFilename);
					h5Writer.Delete("Geometry/Cross Section Interpolation Surfaces");
				}
				result = false;
			}
			else
			{
				try
				{
					geometry.BankLines.EnsureBankLines();
					geometry.EdgeLines.EnsureEdgeLines();
					using (new FeatureTableReloadSuppressor(this))
					{
						ResetFeatureTable();
						Console.WriteLine("LAYER=" + Name);
						SharedData.ReportMessage("Computing XS Interpolation Surface");
						Stopwatch stopwatch = new Stopwatch();
						stopwatch.Start();
						SurfaceFactory surfaceFactory = new SurfaceFactory(geometry);
						List<List<PolylineIntersection>> polylineIntersectionListList = new List<List<PolylineIntersection>>();
						int riverXSIntersectionCountSum = 0;
						int count = river.FeatureCount() - 1;
						for (int i = 0; i <= count; i++)
						{
							List<PolylineIntersection> riverXSIntersections = geometry.RiverXSIntersections(i);
							riverXSIntersectionCountSum += riverXSIntersections.Count;
							polylineIntersectionListList.Add(riverXSIntersections);
						}
						int ordinal = 0;
						XSInterpolationData xSInterpolationData = new XSInterpolationData(geometry);
						XSInterpolationData xSInterpolationData2 = new XSInterpolationData(geometry);
						PointMs pointMs = new PointMs();
						PointMs pointMs2 = new PointMs();
						PointMs pointMs3 = new PointMs();
						int count2 = river.FeatureCount() - 1;
						for (int j = 0; j <= count2; j++)
						{
							Polyline polyline = river.Polyline(j);
							List<PolylineIntersection> polylineIntersections = polylineIntersectionListList[j];
							int count3 = polylineIntersections.Count - 1;
							for (int k = 1; k <= count3; k++)
							{
								PolylineIntersection usIntersect = polylineIntersections[k - 1];
								PolylineIntersection dsIntersect = polylineIntersections[k];
								polyline.PointSubsection(usIntersect.APointOnPolyline, dsIntersect.APointOnPolyline, pointMs);
								pointMs.FilterXY(geometry.FilterToleranceTIN);
								int bPolylineIndex = usIntersect.BPolylineIndex;
								int bPolylineIndex2 = dsIntersect.BPolylineIndex;
								if (bPolylineIndex != bPolylineIndex2)
								{
									xSInterpolationData.SetXS(bPolylineIndex);
									xSInterpolationData2.SetXS(bPolylineIndex2);
									int leftBankLineFID = -1;
									int rightBankLineFID = -1;
									int leftEdgeLineFID = -1;
									int rightEdgeLineFID = -1;
									Tin tin = surfaceFactory.XStoXSSurface(bPolylineIndex, bPolylineIndex2, pointMs, usIntersect, dsIntersect, xSInterpolationData, xSInterpolationData2, ref leftBankLineFID, ref rightBankLineFID, ref leftEdgeLineFID, ref rightEdgeLineFID);
									if (tin != null)
									{
										AddInterpolationSurface(tin, bPolylineIndex, bPolylineIndex2, leftBankLineFID, rightBankLineFID, leftEdgeLineFID, rightEdgeLineFID);
									}
									ordinal++;
									Console.WriteLine("PROGRESS=" + Conversions.ToString((double)ordinal / (double)riverXSIntersectionCountSum));
								}
							}
						}
						if (junctions != null && junctions.FeatureCount() > 0)
						{
							int count4 = junctions.FeatureCount() - 1;
							for (int l = 0; l <= count4; l++)
							{
								List<XStoJunction> xSsAroundJunction = junctions.XSsAroundJunction(l);
								PointMs[] pointMsArray = new PointMs[xSsAroundJunction.Count - 1 + 1];
								int count5 = xSsAroundJunction.Count - 1;
								for (int m = 0; m <= count5; m++)
								{
									int countOffset = m - 1;
									if (countOffset < 0)
									{
										countOffset = xSsAroundJunction.Count - 1;
									}
									XStoJunction xStoJunction = xSsAroundJunction[m];
									XStoJunction xStoJunction2 = xSsAroundJunction[countOffset];
									pointMs.Clear();
									pointMs.AddRange(xStoJunction.CrossSectionToJunction);
									pointMs.AddRangeReverse(xStoJunction2.CrossSectionToJunction);
									pointMs.FilterXY(geometry.FilterToleranceTIN);
									PointMs pointMs4 = new PointMs();
									if (xStoJunction.XSupstreamOfJunction)
									{
										pointMs4.Add(xStoJunction.XSPolyline.FirstPointM());
									}
									else
									{
										pointMs4.Add(xStoJunction.XSPolyline.LastPointM());
									}
									if (xStoJunction2.XSupstreamOfJunction)
									{
										pointMs4.Add(xStoJunction2.XSPolyline.LastPointM());
									}
									else
									{
										pointMs4.Add(xStoJunction2.XSPolyline.FirstPointM());
									}
									pointMsArray[m] = pointMs4;
								}
								int count6 = xSsAroundJunction.Count - 1;
								for (int n = 0; n <= count6; n++)
								{
									int count7 = xSsAroundJunction.Count - 1;
									for (int intervalIndex = 0; intervalIndex <= count7; intervalIndex++)
									{
										if (n == intervalIndex)
										{
											continue;
										}
										XStoJunction xStoJunction3 = xSsAroundJunction[n];
										XStoJunction xStoJunction4 = xSsAroundJunction[intervalIndex];
										if (!xStoJunction3.XSupstreamOfJunction || xStoJunction4.XSupstreamOfJunction)
										{
											continue;
										}
										pointMs2.Clear();
										pointMs2.AddRange(pointMsArray[n]);
										int ordinal2 = n;
										do
										{
											ordinal2--;
											if (ordinal2 == -1)
											{
												ordinal2 = xSsAroundJunction.Count - 1;
											}
											if (ordinal2 != intervalIndex)
											{
												XStoJunction xStoJunction5 = xSsAroundJunction[ordinal2];
												if (xStoJunction5.XSupstreamOfJunction)
												{
													pointMs2.AddRangeReverse(xStoJunction5.XSPolyline);
												}
												else
												{
													pointMs2.AddRange(xStoJunction5.XSPolyline);
												}
												pointMs2.AddRange(pointMsArray[ordinal2]);
											}
										}
										while (ordinal2 != intervalIndex);
										pointMs2.FilterXY(geometry.FilterToleranceTIN);
										pointMs3.Clear();
										ordinal2 = n;
										do
										{
											ordinal2++;
											if (ordinal2 == xSsAroundJunction.Count)
											{
												ordinal2 = 0;
											}
											if (ordinal2 != intervalIndex)
											{
												pointMs3.AddRangeReverse(pointMsArray[ordinal2]);
												XStoJunction xStoJunction6 = xSsAroundJunction[ordinal2];
												if (xStoJunction6.XSupstreamOfJunction)
												{
													pointMs3.AddRange(xStoJunction6.XSPolyline);
												}
												else
												{
													pointMs3.AddRangeReverse(xStoJunction6.XSPolyline);
												}
											}
										}
										while (ordinal2 != intervalIndex);
										pointMs3.AddRangeReverse(pointMsArray[intervalIndex]);
										pointMs3.FilterXY(geometry.FilterToleranceTIN);
										PolylineIntersection intersection = xStoJunction3.Intersection;
										PolylineIntersection intersection2 = xStoJunction4.Intersection;
										int bPolylineIndex3 = intersection.BPolylineIndex;
										int bPolylineIndex4 = intersection2.BPolylineIndex;
										xSInterpolationData.SetXS(bPolylineIndex3);
										xSInterpolationData2.SetXS(bPolylineIndex4);
										pointMs.Clear();
										pointMs.AddRange(xStoJunction3.CrossSectionToJunction);
										pointMs.AddRangeReverse(xStoJunction4.CrossSectionToJunction);
										pointMs.FilterXY(geometry.FilterToleranceTIN);
										int leftBankLineFID2 = -1;
										int rightBankLineFID2 = -1;
										int leftEdgeLineFID2 = -1;
										int rightEdgeLineFID2 = -1;
										Tin tin2 = surfaceFactory.XStoXSSurface(bPolylineIndex3, bPolylineIndex4, pointMs, intersection, intersection2, xSInterpolationData, xSInterpolationData2, ref leftBankLineFID2, ref rightBankLineFID2, ref leftEdgeLineFID2, ref rightEdgeLineFID2, pointMs2, pointMs3);
										if (tin2 != null)
										{
											AddInterpolationSurface(tin2, bPolylineIndex3, bPolylineIndex4, leftBankLineFID2, rightBankLineFID2, leftEdgeLineFID2, rightEdgeLineFID2);
										}
										ordinal++;
										Console.WriteLine("PROGRESS=" + Conversions.ToString((double)ordinal / (double)riverXSIntersectionCountSum));
									}
								}
							}
						}
						stopwatch.Stop();
						SharedData.ReportMessage("XS Interpolation Surface generated in " + Conversions.ToString(stopwatch.ElapsedMilliseconds) + " ms", replacePreviousIfPossible: true);
						LoadNormalizedMTIN();
						SaveFeatureTable();
						float[,] floatValues = new float[FeatureCount() - 1 + 1, 2];
						int count8 = FeatureCount() - 1;
						for (int intervalIndex2 = 0; intervalIndex2 <= count8; intervalIndex2++)
						{
							int upstreamXSID = GetUpstreamXSID(intervalIndex2);
							int downstreamXSID = GetDownstreamXSID(intervalIndex2);
							if (upstreamXSID != -1 && downstreamXSID != -1)
							{
								PointM pointM = Geometry.XS.ComputeOrthogonalVector(upstreamXSID);
								PointM pointM2 = Geometry.XS.ComputeOrthogonalVector(downstreamXSID);
								if (!pointM.IsUndefined() && !pointM2.IsUndefined())
								{
									PointM pointM3 = new PointM((pointM.X + pointM2.X) / 2.0, (pointM.Y + pointM2.Y) / 2.0);
									floatValues[intervalIndex2, 0] = (float)pointM3.X;
									floatValues[intervalIndex2, 1] = (float)pointM3.Y;
								}
							}
						}
						float[] floatValuesArray = new float[FeatureCount() - 1 + 1];
						int count9 = FeatureCount() - 1;
						for (int intervalIndex3 = 0; intervalIndex3 <= count9; intervalIndex3++)
						{
							Tin tin3 = Tin(intervalIndex3);
							floatValuesArray[intervalIndex3] = (float)tin3.Area;
						}
						if (writeToHDF)
						{
							using H5Writer h5Writer2 = new H5Writer(SourceFilename);
							h5Writer2.WriteAttribute("Geometry/Cross Section Interpolation Surfaces", "Source Data Hash", ComputeSourceDataHash());
							h5Writer2.WriteArray("Geometry/Cross Section Interpolation Surfaces/Orthogonal Vectors", floatValues);
							h5Writer2.WriteArray("Geometry/Cross Section Interpolation Surfaces/Areas", floatValuesArray);
						}
						Console.WriteLine("PROGRESS=1");
						result = true;
					}
				}
				catch (Exception ex)
				{
					ProjectData.SetProjectError(ex);
					Exception ex2 = ex;
					SharedData.ReportMessage("Error generating interpolation surface");
					SharedData.ReportMessage("  " + ex2.Message);
					result = false;
					ProjectData.ClearProjectError();
				}
			}
			return result;
		}
	}

	private bool ComputeVolumeElevationTables(H5Writer hw, ProgressReporter reporter = null)
	{
		float computed = 0.01f;
		float computed2 = 0.01f;
		float[] values = new float[100000];
		bool[] boolValues = new bool[100000];
		List<SegmentM> stitches = null;
		Histogram histogram = new Histogram(2560);
		Point2DCollection point2DCollection = new Point2DCollection();
		Point2DCollection point2DCollection2 = new Point2DCollection();
		TerrainLayer terrain = Geometry.Terrain;
		RASXS xS = Geometry.XS;
		bool result;
		checked
		{
			using (H5Reader h5Reader = new H5Reader(terrain.SourceFilename))
			{
				Dictionary<string, TiffFile> tiffFileByString = new Dictionary<string, TiffFile>();
				int count = terrain.RasterFileCount - 1;
				for (int i = 0; i <= count; i++)
				{
					string get_RasterFilename = terrain.get_RasterFilename(i);
					if (!File.Exists(get_RasterFilename))
					{
						DictionaryExtensionMethods.DisposeAll(tiffFileByString);
						throw new Exception("Error resampling terrain. File '" + get_RasterFilename + "' does not exist.");
					}
					tiffFileByString.Add(get_RasterFilename, new TiffFile(get_RasterFilename));
				}
				try
				{
					using HDFBufferedWriter<int> hDFBufferedWriter = new HDFBufferedWriter<int>(hw, "Geometry/Cross Section Interpolation Surfaces/Volume Elevation Info", FeatureCount(), 2, "Interpoation Surface", "Starting Index", "Count");
					using HDFBufferedWriter<float> hDFBufferedWriter2 = new HDFBufferedWriter<float>(hw, "Geometry/Cross Section Interpolation Surfaces/Volume Elevation Values", 0, 2, "row", "Elevation", "Volume");
					new Stopwatch();
					RasMapperLib.Utilities.Math.ProgressMod(FeatureCount());
					int count2 = FeatureCount() - 1;
					double x = default;
					double y = default;
					for (int j = 0; j <= count2; j++)
					{
						Tin tin = Feature(j) as Tin;
						int upstreamXSID = GetUpstreamXSID(j);
						int downstreamXSID = GetDownstreamXSID(j);
						double area = tin.Area;
						point2DCollection2.Clear();
						Array.Clear(boolValues, 0, boolValues.Count());
						RasterM rasterM = RasterM.ComputeRasterM(100000, tin.MaxX, tin.MinX, tin.MaxY, tin.MinY);
						terrain.Resample(rasterM, ref values, ref stitches, h5Reader.Filename, tiffFileByString);
						tin.Rasterize(rasterM, boolValues);
						float minValue = float.MinValue;
						float maxValue = float.MaxValue;
						int count3 = rasterM.Cols * rasterM.Rows - 1;
						for (int k = 0; k <= count3; k++)
						{
							if (!boolValues[k])
							{
								continue;
							}
							float computed3 = values[k];
							if (computed3 != -9999f)
							{
								if (computed3 > minValue)
								{
									minValue = computed3;
								}
								if (computed3 < maxValue)
								{
									maxValue = computed3;
								}
							}
						}
						if (minValue > maxValue)
						{
							histogram.Reset(maxValue, minValue);
							double cellSizePolynomial = rasterM.CellSize * rasterM.CellSize;
							float minValue2 = float.MinValue;
							int unity = 0;
							int count4 = rasterM.Cols * rasterM.Rows - 1;
							for (int l = 0; l <= count4; l++)
							{
								float minValue22 = values[l];
								if (boolValues[l] && minValue22 != -9999f)
								{
									if (minValue22 == minValue2)
									{
										unity++;
										continue;
									}
									histogram.AddArea(minValue2, cellSizePolynomial * (double)unity);
									minValue2 = minValue22;
									unity = 1;
								}
							}
							histogram.AddArea(minValue2, cellSizePolynomial * (double)unity);
							histogram.ComputeCumulativeValues(point2DCollection);
							float ratio = (float)(area / (double)point2DCollection.Last().X);
							point2DCollection.RatioX(ratio);
							point2DCollection2.Clear();
							point2DCollection2.Add(0f, maxValue);
							float computed4 = 0f;
							int count5 = point2DCollection.Count - 1;
							for (int m = 1; m <= count5; m++)
							{
								float point2DCollectionTerm = (float)(0.5 * (double)(point2DCollection[m - 1].X + point2DCollection[m].X));
								float point2DCollectionTerm2 = point2DCollection[m].Y - point2DCollection[m - 1].Y;
								computed4 += point2DCollectionTerm2 * point2DCollectionTerm;
								point2DCollection2.Add(computed4, point2DCollection[m].Y);
							}
							if (computed > 0f)
							{
								point2DCollection2 = point2DCollection2.Filter(computed);
							}
							if (computed2 > 0f)
							{
								double areaTerm = area * (double)computed2;
								int count6 = point2DCollection2.Count - 1;
								for (int n = 1; n <= count6; n++)
								{
									if (!((double)(point2DCollection2[n].X / (point2DCollection2[n].Y - point2DCollection2[0].Y)) > areaTerm))
									{
										continue;
									}
									if (n == 1)
									{
										break;
									}
									SegmentM segmentM = new SegmentM(point2DCollection2[n - 1].X, point2DCollection2[n - 1].Y, point2DCollection2[n].X, point2DCollection2[n].Y);
									SegmentM seg = new SegmentM(0.0, point2DCollection2[0].Y, (double)(point2DCollection2[n].Y - point2DCollection2[0].Y) * areaTerm, point2DCollection2[n].Y);
									if (segmentM.Intersects(seg, ref x, ref y))
									{
										if (n > 2)
										{
											point2DCollection2.RemoveRange(1, n - 2);
										}
										if ((double)point2DCollection2[2].Y - y > (double)computed)
										{
											point2DCollection2[1] = new Point2D((float)x, (float)y);
										}
										else
										{
											point2DCollection2.RemoveAt(1);
										}
									}
									break;
								}
							}
						}
						else
						{
							point2DCollection2.Clear();
							point2DCollection2.Add(0f, maxValue);
						}
						if (point2DCollection2.Count > 0)
						{
							float maxValue2 = float.MaxValue;
							maxValue2 = System.Math.Min(xS.GetProfile(upstreamXSID).MinElevation(), maxValue2);
							maxValue2 = System.Math.Min(xS.GetProfile(downstreamXSID).MinElevation(), maxValue2);
							if (maxValue2 < point2DCollection2[0].Y)
							{
								point2DCollection2.AssignY(0, maxValue2);
							}
						}
						hDFBufferedWriter.AddRow(hDFBufferedWriter2.RowsWritten, point2DCollection2.Count);
						foreach (Point2D point2DCollection22 in point2DCollection2)
						{
							hDFBufferedWriter2.AddRow(point2DCollection22.Y, point2DCollection22.X);
						}
					}
				}
				catch (Exception ex)
				{
					ProjectData.SetProjectError(ex);
					Exception ex2 = ex;
					reporter.ReportError("Property Tables error: " + ex2.Message);
					result = false;
					ProjectData.ClearProjectError();
					goto IL_064f;
				}
				finally
				{
					DictionaryExtensionMethods.DisposeAll(tiffFileByString);
				}
			}
			result = true;
			goto IL_064f;
		}
		IL_064f:
		return result;
	}

	private void AddInterpolationSurface(Tin tin, int usxsid, int dsxsid, int leftBankLineFID, int rightBankLineFID, int leftEdgeLineFID, int rightEdgeLineFID)
	{
		DataRow dataRow = AddFeature(tin);
		dataRow["USXSID"] = usxsid;
		dataRow["DSXSID"] = dsxsid;
		dataRow["Left Bankline FID"] = leftBankLineFID;
		dataRow["Right Bankline FID"] = rightBankLineFID;
		dataRow["Left Edgeline FID"] = leftEdgeLineFID;
		dataRow["Right Edgeline FID"] = rightEdgeLineFID;
	}

	public void XSIDsToISIDMap(ref int[] usISID, ref int[] dsISID)
	{
		RASXS xS = Geometry.XS;
		checked
		{
			if (xS.FeatureCount() != 0)
			{
				usISID = new int[xS.FeatureCount() - 1 + 1];
				dsISID = new int[xS.FeatureCount() - 1 + 1];
				Buffers.ArraySetAllVB(usISID, -1);
				Buffers.ArraySetAllVB(dsISID, -1);
				int count = FeatureCount() - 1;
				for (int i = 0; i <= count; i++)
				{
					FeatureRow(i);
					int upstreamXSID = GetUpstreamXSID(i);
					int downstreamXSID = GetDownstreamXSID(i);
					dsISID[upstreamXSID] = i;
					usISID[downstreamXSID] = i;
				}
			}
		}
	}
}
