using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using Geospatial.DataStructures;
using Geospatial.GDALAssist;
using Geospatial.Rasters;
using Geospatial.Rasters.Classifications;
using Geospatial.Vectors;
using H5Assist.VB;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Mapping;
using RasMapperLib.Structures;
using RasMapperLib.Terrain;
using RasMapperLib.Utilities;
using TiffAssist;
using Utility;
using Utility.Extensions;
using Utility.Progress;
using Utility.Systems;

namespace RasMapperLib;

public class RASGeometry : Layer, IStreamableObject
{
	public struct ManningsNInfo
	{
		public double StartSta;

		public double EndSta;

		public int LandCover;

		public int Override;

		public float nValue;
	}

	[Serializable]
	[CompilerGenerated]
private sealed class _Closure_0024__
	{
		public static readonly _Closure_0024__ _0024I;

		public static Func<PolylineIntersection, PointM> _0024I427_002D12;

		public static Func<PointM, Geospatial.Vectors.Point> _0024I486_002D6;

		public static Func<Geospatial.Vectors.Point, PointM> _0024I486_002D7;

		static _Closure_0024__()
		{
			_0024I = new _Closure_0024__();
		}

		[SpecialName]
		internal bool _Lambda_0024__298_002D0(Layer lyr)
		{
			return lyr is FeatureLayer;
		}

		[SpecialName]
		internal Layer _Lambda_0024__300_002D0(GeometryAssociation ga)
		{
			return Layer.TryFindOrCreateLayer(ga.AbsoluteFilename, SharedData.RasMapper?.MapLayersGroup, [SpecialName] () => new LandCoverLayer(ga.AbsoluteFilename));
		}

		[SpecialName]
		internal bool _Lambda_0024__342_002D0(Layer l)
		{
			return l is IGeometryLayer;
		}

		[SpecialName]
		internal bool _Lambda_0024__343_002D0(Layer lyr)
		{
			return lyr is FeatureLayer;
		}

		[SpecialName]
		internal FeatureLayer _Lambda_0024__343_002D1(Layer l)
		{
			return (FeatureLayer)l;
		}

		[SpecialName]
		internal bool _Lambda_0024__367_002D0(KeyValuePair<string, float> kvp)
		{
			if (!float.IsNaN(kvp.Value) && kvp.Value != float.MaxValue)
			{
				return kvp.Value == -9999f;
			}
			return true;
		}

		[SpecialName]
		internal int _Lambda_0024__427_002D9(PolylineIntersection pi)
		{
			return pi.BSegmentIndex;
		}

		[SpecialName]
		internal double _Lambda_0024__427_002D10(PolylineIntersection pi)
		{
			return pi.BFraction;
		}

		[SpecialName]
		internal int _Lambda_0024__427_002D11(PolylineIntersection pl)
		{
			return pl.APolylineIndex;
		}

		[SpecialName]
		internal PointM _Lambda_0024__427_002D12(PolylineIntersection pi)
		{
			return pi.Point;
		}

		[SpecialName]
		internal PointM _Lambda_0024__429_002D0(PolylineIntersection pli)
		{
			return pli.Point;
		}

		[SpecialName]
		internal PointM _Lambda_0024__430_002D0(List<PolylineIntersection> piLst)
		{
			if (piLst.IsNullOrEmpty())
			{
				return PointM.Undefined();
			}
			return piLst.First().Point;
		}

		[SpecialName]
		internal PointOnPolyline _Lambda_0024__432_002D0(PolylineIntersection pli)
		{
			return pli.APointOnPolyline;
		}

		[SpecialName]
		internal bool _Lambda_0024__477_002D0(string s1, string s2)
		{
			return string.Equals(s1, s2, StringComparison.CurrentCultureIgnoreCase);
		}

		[SpecialName]
		internal int _Lambda_0024__484_002D1(float id)
		{
			return checked((int)System.Math.Round(id));
		}

		[SpecialName]
		internal double _Lambda_0024__484_002D3(ManningsNInfo manningsninfo)
		{
			return manningsninfo.StartSta;
		}

		[SpecialName]
		internal double _Lambda_0024__484_002D4(ManningsNInfo manningsninfo)
		{
			return manningsninfo.StartSta;
		}

		[SpecialName]
		internal double _Lambda_0024__484_002D5(ManningsNInfo manningsninfo)
		{
			return manningsninfo.StartSta;
		}

		[SpecialName]
		internal double _Lambda_0024__484_002D6(ManningsNInfo manningsninfo)
		{
			return manningsninfo.StartSta;
		}

		[SpecialName]
		internal bool _Lambda_0024__486_002D0(PointM pt)
		{
			return pt.IsUndefined();
		}

		[SpecialName]
		internal Geospatial.Vectors.Point _Lambda_0024__486_002D1(PointM ptm)
		{
			return new Geospatial.Vectors.Point(ptm.X, ptm.Y);
		}

		[SpecialName]
		internal PointM _Lambda_0024__486_002D2(Geospatial.Vectors.Point gdrpt)
		{
			return new PointM(gdrpt.X, gdrpt.Y);
		}

		[SpecialName]
		internal bool _Lambda_0024__486_002D3(List<PolylineIntersection> lst)
		{
			return lst.IsNullOrEmpty();
		}

		[SpecialName]
		internal Geospatial.Vectors.Point _Lambda_0024__486_002D4(PointM ptm)
		{
			return new Geospatial.Vectors.Point(ptm.X, ptm.Y);
		}

		[SpecialName]
		internal PointM _Lambda_0024__486_002D5(Geospatial.Vectors.Point gdrpt)
		{
			return new PointM(gdrpt.X, gdrpt.Y);
		}

		[SpecialName]
		internal Geospatial.Vectors.Point _Lambda_0024__486_002D6(PointM ptm)
		{
			return new Geospatial.Vectors.Point(ptm.X, ptm.Y);
		}

		[SpecialName]
		internal PointM _Lambda_0024__486_002D7(Geospatial.Vectors.Point gdrpt)
		{
			return new PointM(gdrpt.X, gdrpt.Y);
		}

		[SpecialName]
		internal Geospatial.Vectors.Point _Lambda_0024__486_002D8(PointM ptm)
		{
			return new Geospatial.Vectors.Point(ptm.X, ptm.Y);
		}

		[SpecialName]
		internal PointM _Lambda_0024__486_002D9(Geospatial.Vectors.Point gdrpt)
		{
			return new PointM(gdrpt.X, gdrpt.Y);
		}

		[SpecialName]
		internal Geospatial.Vectors.Point _Lambda_0024__486_002D10(PointM ptm)
		{
			return new Geospatial.Vectors.Point(ptm.X, ptm.Y);
		}

		[SpecialName]
		internal PointM _Lambda_0024__486_002D11(Geospatial.Vectors.Point gdrpt)
		{
			return new PointM(gdrpt.X, gdrpt.Y);
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__286_002D0
	{
		public string lyrLookupName;

		public _Closure_0024__286_002D0(_Closure_0024__286_002D0 other)
		{
			if (other != null)
			{
				lyrLookupName = other.lyrLookupName;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__0(XmlElement lyrxml)
		{
			return Layer.GetTypeNameFromXML(lyrxml).Equals(lyrLookupName, StringComparison.OrdinalIgnoreCase);
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__286_002D1
	{
		public string lyrName;

		public _Closure_0024__286_002D1(_Closure_0024__286_002D1 other)
		{
			if (other != null)
			{
				lyrName = other.lyrName;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__1(XmlElement node)
		{
			return Layer.GetLayerNameFromXML(node).Equals(lyrName, StringComparison.OrdinalIgnoreCase);
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__413_002D0
	{
		public GDALProgressTracker gd;

		public ProgressReporter childReporter;

		public _Closure_0024__413_002D0(_Closure_0024__413_002D0 other)
		{
			if (other != null)
			{
				gd = other.gd;
				childReporter = other.childReporter;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(char c)
		{
			int? parseGDALStdOut = gd.ParseGDALStdOut(c);
			if (parseGDALStdOut.HasValue)
			{
				childReporter.ReportProgress(parseGDALStdOut.Value);
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__414_002D0
	{
		public bool includeOverbanks;

		public RASXS xs;

		public float[] floatValues;

		public _Closure_0024__414_002D0(_Closure_0024__414_002D0 other)
		{
			if (other != null)
			{
				includeOverbanks = other.includeOverbanks;
				xs = other.xs;
				floatValues = other.floatValues;
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__414_002D1
	{
		public int usid;

		public int dsid;

		public Profile usXSProfile;

		public Profile dsXSProfile;

		public _Closure_0024__414_002D0 outerClosure;

		public _Closure_0024__414_002D1(_Closure_0024__414_002D1 other)
		{
			if (other != null)
			{
				usid = other.usid;
				dsid = other.dsid;
				usXSProfile = other.usXSProfile;
				dsXSProfile = other.dsXSProfile;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int i, float z, float m)
		{
			if (outerClosure.includeOverbanks || System.Math.Abs(m) < 0.999999f)
			{
				float stationWithCenterlineZeroValue = outerClosure.xs.ComputeStationWithCenterlineZeroValue(usid, m);
				float stationWithCenterlineZeroValue2 = outerClosure.xs.ComputeStationWithCenterlineZeroValue(dsid, m);
				float interpolateY = (float)usXSProfile.InterpolateY(stationWithCenterlineZeroValue);
				float interpolateY2 = (float)dsXSProfile.InterpolateY(stationWithCenterlineZeroValue2);
				float computed = interpolateY * z + interpolateY2 * (1f - z);
				if (outerClosure.floatValues[i] == -9999f)
				{
					outerClosure.floatValues[i] = computed;
				}
				else
				{
					outerClosure.floatValues[i] = System.Math.Min(outerClosure.floatValues[i], computed);
				}
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__427_002D0
	{
		public RiverMap.Junction newJxn;

		public _Closure_0024__427_002D0(_Closure_0024__427_002D0 other)
		{
			if (other != null)
			{
				newJxn = other.newJxn;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__0(RiverMap.Junction jj)
		{
			return Operators.CompareString(jj.Name, newJxn.Name, TextCompare: false) == 0;
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__427_002D1
	{
		public string rivname;

		public string reachname;

		public string upstreamConnectionName;

		public string downstreamConnectionName;

		public _Closure_0024__427_002D1(_Closure_0024__427_002D1 other)
		{
			if (other != null)
			{
				rivname = other.rivname;
				reachname = other.reachname;
				upstreamConnectionName = other.upstreamConnectionName;
				downstreamConnectionName = other.downstreamConnectionName;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__1(RiverMap.River a)
		{
			return Operators.CompareString(a.Name, rivname, TextCompare: false) == 0;
		}

		[SpecialName]
		internal bool _Lambda_0024__2(RiverMap.Reach a)
		{
			return Operators.CompareString(a.Name, reachname, TextCompare: false) == 0;
		}

		[SpecialName]
		internal bool _Lambda_0024__3(RiverMap.Junction jj)
		{
			return Operators.CompareString(jj.Name, upstreamConnectionName, TextCompare: false) == 0;
		}

		[SpecialName]
		internal bool _Lambda_0024__4(RiverMap.D2Connection jj)
		{
			return Operators.CompareString(jj.Name, upstreamConnectionName, TextCompare: false) == 0;
		}

		[SpecialName]
		internal bool _Lambda_0024__5(RiverMap.Junction jj)
		{
			return Operators.CompareString(jj.Name, downstreamConnectionName, TextCompare: false) == 0;
		}

		[SpecialName]
		internal bool _Lambda_0024__6(RiverMap.D2Connection jj)
		{
			return Operators.CompareString(jj.Name, downstreamConnectionName, TextCompare: false) == 0;
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__427_002D2
	{
		public RiverMap.Reach rch;

		public _Closure_0024__427_002D2(_Closure_0024__427_002D2 other)
		{
			if (other != null)
			{
				rch = other.rch;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__7(RiverMap.Reach rr)
		{
			return Operators.CompareString(rr.River.Name, rch.River.Name, TextCompare: false) == 0;
		}

		[SpecialName]
		internal bool _Lambda_0024__8(RiverMap.Reach rr)
		{
			return Operators.CompareString(rr.River.Name, rch.River.Name, TextCompare: false) == 0;
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__480_002D0
	{
		public RASXSInterpolationSurface.StreamMtoStripM usStrm2Strip;

		public RASXSInterpolationSurface.StreamMtoStripM dsStrm2Strip;

		public _Closure_0024__480_002D0(_Closure_0024__480_002D0 other)
		{
			if (other != null)
			{
				usStrm2Strip = other.usStrm2Strip;
				dsStrm2Strip = other.dsStrm2Strip;
			}
		}

		[SpecialName]
		internal double _Lambda_0024__0(PointM ptm)
		{
			return usStrm2Strip.MStrip(ptm);
		}

		[SpecialName]
		internal double _Lambda_0024__1(PointM ptm)
		{
			return dsStrm2Strip.MStrip(ptm);
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__484_002D0
	{
		public RasterM tilesRaster;

		public Dictionary<int, List<PolylineSegmentStartStation>> tilesToProcess;

		public int[] landCoverIDs;

		public FinalNValueLayer landCoverMann;

		public Dictionary<int, List<ManningsNInfo>> rasterLCValues;

		public _Closure_0024__484_002D0(_Closure_0024__484_002D0 other)
		{
			if (other != null)
			{
				tilesRaster = other.tilesRaster;
				tilesToProcess = other.tilesToProcess;
				landCoverIDs = other.landCoverIDs;
				landCoverMann = other.landCoverMann;
				rasterLCValues = other.rasterLCValues;
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__484_002D1
	{
		public int xsID;

		public _Closure_0024__484_002D0 outerClosure;

		public _Closure_0024__484_002D1(_Closure_0024__484_002D1 other)
		{
			if (other != null)
			{
				xsID = other.xsID;
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__484_002D2
	{
		public int segIndex;

		public _Closure_0024__484_002D3 outerClosure;

		public _Closure_0024__484_002D2(_Closure_0024__484_002D2 other)
		{
			if (other != null)
			{
				segIndex = other.segIndex;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int n)
		{
			if (n >= 0 && n <= outerClosure.outerClosure.outerClosure.tilesRaster.CellMaxIndex())
			{
				if (!outerClosure.outerClosure.outerClosure.tilesToProcess.ContainsKey(n))
				{
					outerClosure.outerClosure.outerClosure.tilesToProcess[n] = new List<PolylineSegmentStartStation>();
				}
				outerClosure.outerClosure.outerClosure.tilesToProcess[n].Add(new PolylineSegmentStartStation(outerClosure.outerClosure.xsID, segIndex, outerClosure.segStartStation));
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__484_002D3
	{
		public double segStartStation;

		public _Closure_0024__484_002D1 outerClosure;

		public _Closure_0024__484_002D3(_Closure_0024__484_002D3 other)
		{
			if (other != null)
			{
				segStartStation = other.segStartStation;
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__484_002D4
	{
		public RasterM tileRaster;

		public _Closure_0024__484_002D0 outerClosure;

		public _Closure_0024__484_002D4(_Closure_0024__484_002D4 other)
		{
			if (other != null)
			{
				tileRaster = other.tileRaster;
			}
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__484_002D5
	{
		public SegmentM segment;

		public double startingStation;

		public int xsID;

		public _Closure_0024__484_002D4 outerClosure;

		public _Closure_0024__484_002D5(_Closure_0024__484_002D5 other)
		{
			if (other != null)
			{
				segment = other.segment;
				startingStation = other.startingStation;
				xsID = other.xsID;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__2(int row, int col)
		{
			if (!outerClosure.tileRaster.get_InRange(row, col))
			{
				return;
			}
			int cellIndex = outerClosure.tileRaster.CellIndex(row, col);
			int landCoverIDIndex = outerClosure.outerClosure.landCoverIDs[cellIndex];
			_ = outerClosure.tileRaster.CellSize;
			PointM pointM = new PointM(outerClosure.tileRaster.CellMinX(col), outerClosure.tileRaster.CellMinY(row));
			PointM pointM2 = new PointM(outerClosure.tileRaster.CellMinX(col), outerClosure.tileRaster.CellMaxY(row));
			PointM pointM3 = new PointM(outerClosure.tileRaster.CellMaxX(col), outerClosure.tileRaster.CellMaxY(row));
			PointM pointM4 = new PointM(outerClosure.tileRaster.CellMaxX(col), outerClosure.tileRaster.CellMinY(row));
			List<SegmentM> segmentMs = new List<SegmentM>
			{
				new SegmentM(pointM, pointM2),
				new SegmentM(pointM2, pointM3),
				new SegmentM(pointM3, pointM4),
				new SegmentM(pointM4, pointM)
			};
			Polygon polygon = new Polygon(new PointMs { pointM, pointM2, pointM3, pointM4, pointM });
			Polyline pline = new Polyline(new PointMs { segment.A, segment.B });
			List<double> doubleList = new List<double>();
			double x = default;
			double y = default;
			double t = default;
			double t2 = default;
			foreach (SegmentM segmentM in segmentMs)
			{
				if (segment.Intersects(segmentM, ref x, ref y, ref t, ref t2))
				{
					double computed = startingStation + segment.Length * t;
					doubleList.Add(computed);
					_ = doubleList.Count;
					_ = 1;
				}
			}
			if (doubleList.Count > 0)
			{
				double computed2 = 0.0;
				double doubleList2 = doubleList[0];
				if (doubleList.Count > 1)
				{
					computed2 = doubleList[1];
				}
				else
				{
					computed2 = ((!polygon.Contains(segment.A)) ? (startingStation + segment.Length) : startingStation);
				}
				double startSta = System.Math.Min(doubleList2, computed2);
				double endSta = System.Math.Max(doubleList2, computed2);
				if (startSta == 0.0)
				{
					startSta = endSta;
				}
				ManningsNInfo manningsNInfo = default;
				manningsNInfo.StartSta = startSta;
				manningsNInfo.EndSta = endSta;
				manningsNInfo.LandCover = landCoverIDIndex;
				manningsNInfo.nValue = outerClosure.outerClosure.landCoverMann.GetManningsFromTable(landCoverIDIndex, -1);
				manningsNInfo.Override = -1;
				if (!outerClosure.outerClosure.rasterLCValues.ContainsKey(xsID))
				{
					outerClosure.outerClosure.rasterLCValues[xsID] = new List<ManningsNInfo>();
				}
				outerClosure.outerClosure.rasterLCValues[xsID].Add(manningsNInfo);
			}
			else if (polygon.Contains(pline))
			{
				ManningsNInfo manningsNInfo2 = default;
				manningsNInfo2.StartSta = startingStation;
				manningsNInfo2.EndSta = startingStation + segment.Length;
				manningsNInfo2.LandCover = landCoverIDIndex;
				manningsNInfo2.nValue = outerClosure.outerClosure.landCoverMann.GetManningsFromTable(landCoverIDIndex, -1);
				manningsNInfo2.Override = -1;
				if (!outerClosure.outerClosure.rasterLCValues.ContainsKey(xsID))
				{
					outerClosure.outerClosure.rasterLCValues[xsID] = new List<ManningsNInfo>();
				}
				outerClosure.outerClosure.rasterLCValues[xsID].Add(manningsNInfo2);
			}
		}
	}

	public const string sRasVersionString = "HEC-RAS 5.1.0 Alpha 4 May 2020";

	public const string sGeometryVersionString = "1.0.17 (13Jan2021)";

	public const string sGeometry = "Geometry";

	public const string sErrors = "Errors";

	public const string sRiverGroup = "River Lines";

	public const string sRiversLayerName = "Rivers";

	public const string sBankLines = "Bank Lines";

	public const string sJunction = "Junction";

	public const string sJunctions = "Junctions";

	public const string sFlowPaths = "Flow Paths";

	public const string sRiverStationing = "River Station Markers";

	public const string sXSGroup = "Cross Sections";

	public const string sXSLayerName = "Cross Sections";

	public const string sEdgeLine = "Edge Lines";

	public const string sXSInterpolationSurface = "Interpolation Surface";

	public const string sIneffectiveFlowArea = "Ineffective Flow Areas";

	public const string sBlockedObstruction = "Blocked Obstructions";

	public const string sManningsN = "Mannings n Values";

	public const string sStorageAreas = "Storage Areas";

	public const string s2DFlowArea = "2D Flow Areas";

	public const string sSA2DFlowArea = "SA/2D Area";

	public const string sMeshPerimeters = "Perimeters";

	public const string sMeshPoints = "Computation Points";

	public const string sBreakLines = "Breaklines";

	public const string sMeshRegions = "Refinement Regions";

	public const string sBCGroup = "Boundary Conditions";

	public const string sBoundaryConditionLines = "Boundary Condition Lines";

	public const string sInitialConditionPoints = "Initial Condition Points";

	public const string sRLLayerName = "Reference Lines";

	public const string sRPLayerName = "Reference Points";

	public const string sPipeNodesLayerName = "Pipe Nodes";

	public const string sPipeLinksLayerName = "Pipe Links";

	public const string sLandCoverRegionHash = "Property Tables LC Hash";

	public const string sPropertyTablesTime = "Property Tables Last Computed";

	public const string sInfiltrationOverrideHash = "Infiltration Override Table Hash";

	public const string sExtents = "Extents";

	public const string sUpdateGroup = "Geometry/AutoUpdateParameters";

	public const string sUpdateXSElevationsOverbanks = "XS Elevations - Overbanks";

	public const string sUpdateXSElevationsChannel = "XS Elevations - Channel";

	public const string sUpdateXSRiverStations = "XS River Stations";

	public const string sUpdateXSBankStations = "XS Bank Stations";

	public const string sUpdateXSReachLengths = "XS Reach Lengths";

	public const string sUpdateXSIneffectiveAreas = "XS Ineffective Areas";

	public const string sUpdateXSBlockedObstructions = "XS Blocked Obstructions";

	public const string sUpdateXSManningsNValues = "XS Mannings n Values";

	public const string sUpdateStructureRiverStations = "Structure River Stations";

	public const string sUpdateStructureElevations = "Structure Elevations";

	public const string sUpdateSAEV = "SA Elev-Vol Info";

	public const string sRiver = "River";

	public const string sReach = "Reach";

	public const string sRiverStation = "River Station";

	public const string sNodeName = "Node Name";

	public const string sNodeDescription = "Node Descriptions";

	public const string sUSXSID = "USXSID";

	public const string sDSXSID = "DSXSID";

	public const string sTrackTerrain = "Track Terrain";

	public const string sAutoUpdate = "Auto Update";

	private const string sSIUnits = "SI Units";

	public const string sGeometryCompleteAttribute = "Complete Geometry";

	public const string sPlotModelRegion = "Plot Model Region";

	public const int MaxRiverNameLength = 16;

	public const int MaxReachNameLength = 16;

	public const int MaxJunctionNameLength = 16;

	private DateTime _hdfFileDateLastRead;

	private string _terrainFilename;

	private string _terrainLayername;

	private TerrainLayer _terrain;

	public List<Layer> Layers;

	public HashSet<Layer> OnlyDataLayers;

	public List<GroupLayer> Groups;

	private static IList<Type> RenderRank;

	[CompilerGenerated]
	private RASRiver _River;

	[CompilerGenerated]
	private RASXS _XS;

	[CompilerGenerated]
	private RASBankLines _BankLines;

	[CompilerGenerated]
	private RASEdgeLines _EdgeLines;

	[CompilerGenerated]
	private FlowPaths _FlowPathLines;

	[CompilerGenerated]
	private RASXSInterpolationSurface _XSInterpolationSurface;

	[CompilerGenerated]
	private IneffectiveFlowAreaLayer _IneffectiveFlowAreas;

	[CompilerGenerated]
	private BlockedObstructionLayer _BlockedObstructions;

	[CompilerGenerated]
	private RiverStations _RiverStationing;

	[CompilerGenerated]
	private RASJunction _Junctions;

	[CompilerGenerated]
	private StructureLayer _Structures;

	[CompilerGenerated]
	private BridgesCulvertsLayer _BridgesCulverts;

	[CompilerGenerated]
	private VirtualGeometryFeatureLayer _BridgesCulvertsCulvertGroups;

	[CompilerGenerated]
	private VirtualGeometryFeatureLayer _BridgesCulvertsCulvertBarrels;

	[CompilerGenerated]
	private InlineStructureLayer _InlineStructures;

	[CompilerGenerated]
	private VirtualGeometryFeatureLayer _InlineGateGroups;

	[CompilerGenerated]
	private VirtualGeometryFeatureLayer _InlineGateOpenings;

	[CompilerGenerated]
	private VirtualGeometryFeatureLayer _InlineCulvertGroups;

	[CompilerGenerated]
	private VirtualGeometryFeatureLayer _InlineCulvertBarrels;

	[CompilerGenerated]
	private VirtualGeometryFeatureLayer _InlineRatingCurves;

	[CompilerGenerated]
	private VirtualGeometryFeatureLayer _InlineFlowTimeSeries;

	[CompilerGenerated]
	private LateralStructureLayer _LateralStructures;

	[CompilerGenerated]
	private VirtualGeometryFeatureLayer _LateralGateGroups;

	[CompilerGenerated]
	private VirtualGeometryFeatureLayer _LateralGateOpenings;

	[CompilerGenerated]
	private VirtualGeometryFeatureLayer _LateralCulvertGroups;

	[CompilerGenerated]
	private VirtualGeometryFeatureLayer _LateralCulvertBarrels;

	[CompilerGenerated]
	private VirtualGeometryFeatureLayer _LateralRatingCurves;

	[CompilerGenerated]
	private VirtualGeometryFeatureLayer _LateralFlowTimeSeries;

	[CompilerGenerated]
	private SA2DStructureLayer _SA2DStructures;

	[CompilerGenerated]
	private VirtualGeometryFeatureLayer _SA2DGateGroups;

	[CompilerGenerated]
	private VirtualGeometryFeatureLayer _SA2DGateOpenings;

	[CompilerGenerated]
	private VirtualGeometryFeatureLayer _SA2DCulvertGroups;

	[CompilerGenerated]
	private VirtualGeometryFeatureLayer _SA2DCulvertBarrels;

	[CompilerGenerated]
	private VirtualGeometryFeatureLayer _SA2DRatingCurves;

	[CompilerGenerated]
	private VirtualGeometryFeatureLayer _SA2DFlowTimeSeries;

	[CompilerGenerated]
	private TimeSeriesOutlet _StructureTimeSeries;

	[CompilerGenerated]
	private RatingCurveOutlet _StructureRatingCurves;

	[CompilerGenerated]
	private CulvertGroupLayer _CulvertGroups;

	[CompilerGenerated]
	private GateGroupLayer _GateGroups;

	[CompilerGenerated]
	private CulvertBarrelLayer _CulvertBarrels;

	[CompilerGenerated]
	private GateOpeningLayer _GateOpenings;

	[CompilerGenerated]
	private PumpStationLayer _PumpStations;

	[CompilerGenerated]
	private PumpGroupLayer _PumpGroups;

	[CompilerGenerated]
	private PumpLayer _Pumps;

	[CompilerGenerated]
	private RASStorageArea _StorageArea;

	[CompilerGenerated]
	private RASD2FlowArea _D2FlowArea;

	[CompilerGenerated]
	private MeshPerimeterLayer _MeshPerimeters;

	[CompilerGenerated]
	private MeshPointLayer _MeshPoints;

	[CompilerGenerated]
	private RASBreakLines _BreakLines;

	[CompilerGenerated]
	private MeshRegionLayer _MeshRegions;

	[CompilerGenerated]
	private RasLandCoverRegions _LandCoverRegions;

	[CompilerGenerated]
	private FinalNValueLayer _LandCoverManningsN;

	[CompilerGenerated]
	private RASBoundaryConditionLines _BoundaryConditionLines;

	[CompilerGenerated]
	private RASInitialConditionsPoints _InitialConditionPoints;

	[CompilerGenerated]
	private RASReferencePoints _ReferencePoints;

	[CompilerGenerated]
	private RasReferenceLines _ReferenceLines;

	[CompilerGenerated]
	private RASPipeLinks _PipeLinks;

	[CompilerGenerated]
	private RASPipeNodes _PipeNodes;

	[CompilerGenerated]
	private RASErrors _Errors;

	private RASGeometryMapPoints _mapPixels;

	private double _nominalLength;

	private bool _isResult;

	private bool _SIUnits;

	private double _filterToleranceXY;

	private static Dictionary<Type, string> GeometryLayerNameMap;

	private bool _layerLoaded;

	private GeometryAssociation _nValueAssociation;

	private ParameterResampler _landCoverResampler;

	[CompilerGenerated]
	private InterpretationRasterizerLayer _NValueRasterizationLayer;

	private GeometryAssociation _infiltrationAssociation;

	[CompilerGenerated]
	private InterpretationOverrideLayer _InfiltrationOverrideRegions;

	[CompilerGenerated]
	private InterpretationRasterizerLayer _InfiltrationRasterizer;

	private ParameterResampler _infiltrationParameterResampler;

	private GeometryAssociation _impervAssociation;

	private ParameterResampler _impervParameterResampler;

	[CompilerGenerated]
	private InterpretationOverrideLayer _PercentImperviousOverrideRegions;

	[CompilerGenerated]
	private InterpretationRasterizerLayer _PercentImperviousRasterizer;

	private GeometryAssociation _sedimentSoilsAssociation;

	private XSLSIntersectionData _XSsIntersectsRivers;

	private XSLSIntersectionData _StructuresIntersectsRivers;

	private XSLSIntersectionData _XSsIntersectsEdges;

	private XSLSIntersectionData _XSsIntersectsBanks;

	private XSLSIntersectionData _XSsIntersectsFlowPaths;

	public DateTime HDFFileDateLastRead => _hdfFileDateLastRead;

	public override bool CanEdit => !IsResultFile;

	public override bool CanDelete => false;

	public RASRiver River
	{
		[CompilerGenerated]
		get
		{
			return _River;
		}
	}

	public RASXS XS
	{
		[CompilerGenerated]
		get
		{
			return _XS;
		}
	}

	public RASBankLines BankLines
	{
		[CompilerGenerated]
		get
		{
			return _BankLines;
		}
	}

	public RASEdgeLines EdgeLines
	{
		[CompilerGenerated]
		get
		{
			return _EdgeLines;
		}
	}

	public FlowPaths FlowPathLines
	{
		[CompilerGenerated]
		get
		{
			return _FlowPathLines;
		}
	}

	public RASXSInterpolationSurface XSInterpolationSurface
	{
		[CompilerGenerated]
		get
		{
			return _XSInterpolationSurface;
		}
	}

	public IneffectiveFlowAreaLayer IneffectiveFlowAreas
	{
		[CompilerGenerated]
		get
		{
			return _IneffectiveFlowAreas;
		}
	}

	public BlockedObstructionLayer BlockedObstructions
	{
		[CompilerGenerated]
		get
		{
			return _BlockedObstructions;
		}
	}

	public InterpolationSurfaceMLayer InterpolationSurfaceM { get; }

	public RiverStations RiverStationing
	{
		[CompilerGenerated]
		get
		{
			return _RiverStationing;
		}
	}

	public RASJunction Junctions
	{
		[CompilerGenerated]
		get
		{
			return _Junctions;
		}
	}

	public StructureLayer Structures
	{
		[CompilerGenerated]
		get
		{
			return _Structures;
		}
	}

	public BridgesCulvertsLayer BridgesCulverts
	{
		[CompilerGenerated]
		get
		{
			return _BridgesCulverts;
		}
	}

	public VirtualGeometryFeatureLayer BridgesCulvertsCulvertGroups
	{
		[CompilerGenerated]
		get
		{
			return _BridgesCulvertsCulvertGroups;
		}
	}

	public VirtualGeometryFeatureLayer BridgesCulvertsCulvertBarrels
	{
		[CompilerGenerated]
		get
		{
			return _BridgesCulvertsCulvertBarrels;
		}
	}

	public InlineStructureLayer InlineStructures
	{
		[CompilerGenerated]
		get
		{
			return _InlineStructures;
		}
	}

	public VirtualGeometryFeatureLayer InlineGateGroups
	{
		[CompilerGenerated]
		get
		{
			return _InlineGateGroups;
		}
	}

	public VirtualGeometryFeatureLayer InlineGateOpenings
	{
		[CompilerGenerated]
		get
		{
			return _InlineGateOpenings;
		}
	}

	public VirtualGeometryFeatureLayer InlineCulvertGroups
	{
		[CompilerGenerated]
		get
		{
			return _InlineCulvertGroups;
		}
	}

	public VirtualGeometryFeatureLayer InlineCulvertBarrels
	{
		[CompilerGenerated]
		get
		{
			return _InlineCulvertBarrels;
		}
	}

	public VirtualGeometryFeatureLayer InlineRatingCurves
	{
		[CompilerGenerated]
		get
		{
			return _InlineRatingCurves;
		}
	}

	public VirtualGeometryFeatureLayer InlineFlowTimeSeries
	{
		[CompilerGenerated]
		get
		{
			return _InlineFlowTimeSeries;
		}
	}

	public LateralStructureLayer LateralStructures
	{
		[CompilerGenerated]
		get
		{
			return _LateralStructures;
		}
	}

	public VirtualGeometryFeatureLayer LateralGateGroups
	{
		[CompilerGenerated]
		get
		{
			return _LateralGateGroups;
		}
	}

	public VirtualGeometryFeatureLayer LateralGateOpenings
	{
		[CompilerGenerated]
		get
		{
			return _LateralGateOpenings;
		}
	}

	public VirtualGeometryFeatureLayer LateralCulvertGroups
	{
		[CompilerGenerated]
		get
		{
			return _LateralCulvertGroups;
		}
	}

	public VirtualGeometryFeatureLayer LateralCulvertBarrels
	{
		[CompilerGenerated]
		get
		{
			return _LateralCulvertBarrels;
		}
	}

	public VirtualGeometryFeatureLayer LateralRatingCurves
	{
		[CompilerGenerated]
		get
		{
			return _LateralRatingCurves;
		}
	}

	public VirtualGeometryFeatureLayer LateralFlowTimeSeries
	{
		[CompilerGenerated]
		get
		{
			return _LateralFlowTimeSeries;
		}
	}

	public SA2DStructureLayer SA2DStructures
	{
		[CompilerGenerated]
		get
		{
			return _SA2DStructures;
		}
	}

	public VirtualGeometryFeatureLayer SA2DGateGroups
	{
		[CompilerGenerated]
		get
		{
			return _SA2DGateGroups;
		}
	}

	public VirtualGeometryFeatureLayer SA2DGateOpenings
	{
		[CompilerGenerated]
		get
		{
			return _SA2DGateOpenings;
		}
	}

	public VirtualGeometryFeatureLayer SA2DCulvertGroups
	{
		[CompilerGenerated]
		get
		{
			return _SA2DCulvertGroups;
		}
	}

	public VirtualGeometryFeatureLayer SA2DCulvertBarrels
	{
		[CompilerGenerated]
		get
		{
			return _SA2DCulvertBarrels;
		}
	}

	public VirtualGeometryFeatureLayer SA2DRatingCurves
	{
		[CompilerGenerated]
		get
		{
			return _SA2DRatingCurves;
		}
	}

	public VirtualGeometryFeatureLayer SA2DFlowTimeSeries
	{
		[CompilerGenerated]
		get
		{
			return _SA2DFlowTimeSeries;
		}
	}

	public TimeSeriesOutlet StructureTimeSeries
	{
		[CompilerGenerated]
		get
		{
			return _StructureTimeSeries;
		}
	}

	public RatingCurveOutlet StructureRatingCurves
	{
		[CompilerGenerated]
		get
		{
			return _StructureRatingCurves;
		}
	}

	public CulvertGroupLayer CulvertGroups
	{
		[CompilerGenerated]
		get
		{
			return _CulvertGroups;
		}
	}

	public GateGroupLayer GateGroups
	{
		[CompilerGenerated]
		get
		{
			return _GateGroups;
		}
	}

	public CulvertBarrelLayer CulvertBarrels
	{
		[CompilerGenerated]
		get
		{
			return _CulvertBarrels;
		}
	}

	public GateOpeningLayer GateOpenings
	{
		[CompilerGenerated]
		get
		{
			return _GateOpenings;
		}
	}

	public PumpStationLayer PumpStations
	{
		[CompilerGenerated]
		get
		{
			return _PumpStations;
		}
	}

	public PumpGroupLayer PumpGroups
	{
		[CompilerGenerated]
		get
		{
			return _PumpGroups;
		}
	}

	public PumpLayer Pumps
	{
		[CompilerGenerated]
		get
		{
			return _Pumps;
		}
	}

	public RASStorageArea StorageArea
	{
		[CompilerGenerated]
		get
		{
			return _StorageArea;
		}
	}

	public RASD2FlowArea D2FlowArea
	{
		[CompilerGenerated]
		get
		{
			return _D2FlowArea;
		}
	}

	public MeshPerimeterLayer MeshPerimeters
	{
		[CompilerGenerated]
		get
		{
			return _MeshPerimeters;
		}
	}

	public MeshPointLayer MeshPoints
	{
		[CompilerGenerated]
		get
		{
			return _MeshPoints;
		}
	}

	public RASBreakLines BreakLines
	{
		[CompilerGenerated]
		get
		{
			return _BreakLines;
		}
	}

	public MeshRegionLayer MeshRegions
	{
		[CompilerGenerated]
		get
		{
			return _MeshRegions;
		}
	}

	public RasLandCoverRegions LandCoverRegions
	{
		[CompilerGenerated]
		get
		{
			return _LandCoverRegions;
		}
	}

	public FinalNValueLayer LandCoverManningsN
	{
		[CompilerGenerated]
		get
		{
			return _LandCoverManningsN;
		}
	}

	public RASBoundaryConditionLines BoundaryConditionLines
	{
		[CompilerGenerated]
		get
		{
			return _BoundaryConditionLines;
		}
	}

	public RASInitialConditionsPoints InitialConditionPoints
	{
		[CompilerGenerated]
		get
		{
			return _InitialConditionPoints;
		}
	}

	public RASReferencePoints ReferencePoints
	{
		[CompilerGenerated]
		get
		{
			return _ReferencePoints;
		}
	}

	public RasReferenceLines ReferenceLines
	{
		[CompilerGenerated]
		get
		{
			return _ReferenceLines;
		}
	}

	public RASPipeLinks PipeLinks
	{
		[CompilerGenerated]
		get
		{
			return _PipeLinks;
		}
	}

	public RASPipeNodes PipeNodes
	{
		[CompilerGenerated]
		get
		{
			return _PipeNodes;
		}
	}

	public RASErrors Errors
	{
		[CompilerGenerated]
		get
		{
			return _Errors;
		}
	}

	public bool IsResultFile => _isResult;

	public RASResults Results { get; set; }

	public override string DefaultLayerName { get; }

	public override Extent Extent
	{
		get
		{
			if (_extent == null)
			{
				Extent extent = River.Extent;
				Extent extent2 = XS.Extent;
				Extent extent3 = XSInterpolationSurface.Extent;
				Extent extent4 = StorageArea.Extent;
				Extent extent5 = D2FlowArea.Extent;
				_extent = new Extent(extent, extent2, extent3, extent4, extent5);
			}
			return _extent;
		}
	}

	public bool HDFFileIsFree
	{
		get
		{
			bool result;
			try
			{
				using (new H5Writer(SourceFilename))
				{
				}
				result = true;
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				result = false;
				ProjectData.ClearProjectError();
			}
			return result;
		}
	}

	public bool SIUnits => _SIUnits;

	public double FilterToleranceXY
	{
		get
		{
			if (double.IsNaN(_filterToleranceXY))
			{
				double filterToleranceXY = ((!_SIUnits) ? 0.1 : 0.03);
				double nominalLength = NominalLength();
				if (double.IsNaN(nominalLength) || nominalLength == 0.0)
				{
					_filterToleranceXY = filterToleranceXY;
				}
				else
				{
					_filterToleranceXY = System.Math.Min(filterToleranceXY, nominalLength * 0.0001);
				}
			}
			return _filterToleranceXY;
		}
	}

	public double FilterToleranceZ
	{
		get
		{
			if (_SIUnits)
			{
				return 0.003;
			}
			return 0.01;
		}
	}

	public double FilterToleranceTIN => FilterToleranceXY;

	public override Type BaseGroupType => typeof(GeometriesGroup);

	public override bool CanMoveToAnotherLevel { get; }

	public override bool CanRename { get; }

	public override bool CanRemove => !IsResultFile;

	public override string LayerPropertiesText { get; }

	public string TerrainFilename => _terrainFilename;

	public string TerrainLayername => _terrainLayername;

	public string TerrainFileDate
	{
		get
		{
			if (File.Exists(_terrainFilename))
			{
				return DSS.ConvertToDSSDateTime(FileSystem.FileDateTime(_terrainFilename));
			}
			return "";
		}
	}

	public TerrainLayer Terrain
	{
		get
		{
			if (_terrain != null)
			{
				return _terrain;
			}
			if (!SourceFileExists)
			{
				return null;
			}
			string inSingleQuotes = Name.InSingleQuotes();
			if (IsResultFile && Results != null)
			{
				inSingleQuotes = Results.Name.InSingleQuotes();
			}
			if (SharedData.RasMapper != null)
			{
				TerrainLayer terrainLayer = SharedData.RasMapper.TerrainsGroup.FindTerrain(_terrainFilename);
				if (terrainLayer != null)
				{
					_terrain = terrainLayer;
					return _terrain;
				}
				if (terrainLayer == null)
				{
					terrainLayer = SharedData.RasMapper.TerrainsGroup.FindTerrainByName(_terrainLayername);
				}
				if (terrainLayer == null)
				{
					terrainLayer = SharedData.RasMapper.TerrainsGroup.FindAllTerrains().FirstOrDefault();
					if (terrainLayer != null)
					{
						SetTerrain(terrainLayer);
						SharedData.ReportMessage("Geometry " + inSingleQuotes + " association was set to the first available terrain " + terrainLayer.Name.InSingleQuotes());
					}
				}
				else
				{
					SetTerrain(terrainLayer);
				}
				_terrain = terrainLayer;
			}
			else if (File.Exists(_terrainFilename))
			{
				_terrain = new TerrainLayer("Terrain", _terrainFilename);
			}
			else if (SharedData.RasMapDoc != null)
			{
				TerrainLayer terrainFromXML = RasMapperCom.GetTerrainFromXML(SharedData.RasMapDoc, _terrainLayername);
				if (terrainFromXML != null)
				{
					SetTerrain(terrainFromXML);
				}
				else
				{
					string[] terrainFilesAvailable = RasMapperCom.TerrainFilesAvailable();
					foreach (string terrainFilename in terrainFilesAvailable)
					{
						TerrainLayer terrainLayer2 = new TerrainLayer("Terrain", terrainFilename);
						if (terrainLayer2.AllSourceFilesExist())
						{
							SetTerrain(terrainLayer2);
							SharedData.ReportMessage("Geometry " + inSingleQuotes + " association was set to the first valid terrain layer (" + _terrain.Name + ")");
							break;
						}
					}
				}
			}
			return _terrain;
		}
		set
		{
			SetTerrain(value);
		}
	}

	public GeometryAssociation NValueAssociation => _nValueAssociation;

	public LandCoverLayer NValueLayer
	{
		get
		{
			return _nValueAssociation?.Layer as LandCoverLayer;
		}
		set
		{
			_nValueAssociation?.ChangeAssociation(value);
			WriteParameters();
			NValueDataChanged();
		}
	}

	public ParameterResampler NValueResampler
	{
		get
		{
			checked
			{
				if (_landCoverResampler == null)
				{
					if (NValueLayer == null)
					{
						return null;
					}
					IClassificationResampler resampler = NValueLayer.Resampler;
					if (resampler == null)
					{
						return null;
					}
					List<ParameterizedPolygon> parameterizedPolygons = new List<ParameterizedPolygon>();
					int count = LandCoverRegions.FeatureCount() - 1;
					for (int i = 0; i <= count; i++)
					{
						Polygon polygon = LandCoverRegions.Polygon(i);
						if (!Polygon.IsValidPolygon(polygon))
						{
							continue;
						}
						Dictionary<string, float> landCoverMapping_StripNoData = LandCoverRegions.GetLandCoverMapping_StripNoData(i);
						Parameter parameter = new Parameter("ManningsN");
						foreach (KeyValuePair<string, float> landCoverMapping_StripNoData2 in landCoverMapping_StripNoData)
						{
							parameter.AddOverride(landCoverMapping_StripNoData2.Key, landCoverMapping_StripNoData2.Value);
						}
						ParameterSet pset = new ParameterSet(new Parameter[1] { parameter });
						parameterizedPolygons.Add(new ParameterizedPolygon(Converter.Convert(polygon), pset));
					}
					Parameter baseNValueClassificationVariable = LandCoverRegions.GetBaseNValueClassificationVariable();
					StripNoData(baseNValueClassificationVariable);
					ParameterSet higherPriority = new ParameterSet(new Parameter[1] { baseNValueClassificationVariable });
					ParameterSet baseVariables = NValueLayer.Parameters.OverrideWith(higherPriority);
					_landCoverResampler = new ParameterResampler(resampler, baseVariables, parameterizedPolygons);
				}
				return _landCoverResampler;
			}
		}
	}

	public InterpretationRasterizerLayer NValueRasterizationLayer
	{
		[CompilerGenerated]
		get
		{
			return _NValueRasterizationLayer;
		}
	}

	public GeometryAssociation InfiltrationAssociation => _infiltrationAssociation;

	public LandCoverLayer InfiltrationLayer
	{
		get
		{
			return _infiltrationAssociation?.Layer as LandCoverLayer;
		}
		set
		{
			_infiltrationAssociation?.ChangeAssociation(value);
			WriteParameters();
			InfiltrationDataChanged();
		}
	}

	public InterpretationOverrideLayer InfiltrationOverrideRegions
	{
		[CompilerGenerated]
		get
		{
			return _InfiltrationOverrideRegions;
		}
	}

	public InterpretationRasterizerLayer InfiltrationRasterizer
	{
		[CompilerGenerated]
		get
		{
			return _InfiltrationRasterizer;
		}
	}

	public ParameterResampler InfiltrationResampler
	{
		get
		{
			if (_infiltrationParameterResampler == null)
			{
				_infiltrationParameterResampler = MakeDefaultParameterResampler(InfiltrationLayer, InfiltrationOverrideRegions);
			}
			return _infiltrationParameterResampler;
		}
	}

	public GeometryAssociation PercentImperviousAssociation => _impervAssociation;

	public LandCoverLayer PercentImperviousLayer
	{
		get
		{
			return _impervAssociation?.Layer as LandCoverLayer;
		}
		set
		{
			_impervAssociation?.ChangeAssociation(value);
			WriteParameters();
		}
	}

	public ParameterResampler PercentImperviousResampler
	{
		get
		{
			if (_impervParameterResampler == null)
			{
				_impervParameterResampler = MakeDefaultParameterResampler(PercentImperviousLayer, PercentImperviousOverrideRegions);
			}
			return _impervParameterResampler;
		}
	}

	public InterpretationOverrideLayer PercentImperviousOverrideRegions
	{
		[CompilerGenerated]
		get
		{
			return _PercentImperviousOverrideRegions;
		}
	}

	public InterpretationRasterizerLayer PercentImperviousRasterizer
	{
		[CompilerGenerated]
		get
		{
			return _PercentImperviousRasterizer;
		}
	}

	public GeometryAssociation SedimentSoilsAssociation => _sedimentSoilsAssociation;

	public LandCoverLayer SedimentSoilsLayer
	{
		get
		{
			return _sedimentSoilsAssociation?.Layer as LandCoverLayer;
		}
		set
		{
			_sedimentSoilsAssociation.ChangeAssociation(value);
			WriteParameters();
		}
	}

	public ParameterResampler SedimentSoilsResampler
	{
		get
		{
			IClassificationResampler classificationResampler = SedimentSoilsLayer?.Resampler;
			if (classificationResampler == null)
			{
				return null;
			}
			return new ParameterResampler(classificationResampler, new ParameterSet());
		}
	}

	public void StreamOut(BinaryWriter bw)
	{
		Pipe.WriteString(bw, SourceFilename);
		bw.Write(_isResult);
		River.Stream(bw);
		XS.Stream(bw);
		IneffectiveFlowAreas.Stream(bw);
		BlockedObstructions.Stream(bw);
		BankLines.Stream(bw);
		FlowPathLines.Stream(bw);
		EdgeLines.Stream(bw);
		XSInterpolationSurface.Stream(bw);
		D2FlowArea.Stream(bw);
		StorageArea.Stream(bw);
		Junctions.Stream(bw);
		RiverStationing.Stream(bw);
		Structures.Stream(bw);
		BoundaryConditionLines.Stream(bw);
	}

	void IStreamableObject.StreamOut(BinaryWriter bw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in StreamOut
		this.StreamOut(bw);
	}

	public void StreamIn(BinaryReader br)
	{
		SourceFilename = Pipe.ReadString(br);
		_isResult = br.ReadBoolean();
		_River = RASRiver.Stream(br, this);
		_XS = RASXS.Stream(br, this);
		_IneffectiveFlowAreas = IneffectiveFlowAreaLayer.Stream(br, this);
		_BlockedObstructions = BlockedObstructionLayer.Stream(br, this);
		_BankLines = RASBankLines.Stream(br, this);
		_FlowPathLines = FlowPaths.Stream(br, this);
		_EdgeLines = RASEdgeLines.Stream(br, this);
		_XSInterpolationSurface = RASXSInterpolationSurface.Stream(br, this);
		_D2FlowArea = RASD2FlowArea.Stream(br, this);
		_StorageArea = RASStorageArea.Stream(br, this);
		_Junctions = RASJunction.Stream(br, this);
		_RiverStationing = RiverStations.Stream(br, this);
		_Structures = StructureLayer.Stream(br, this);
		_BoundaryConditionLines = RASBoundaryConditionLines.Stream(br, this);
		_MeshPerimeters = new MeshPerimeterLayer(D2FlowArea);
	}

	void IStreamableObject.StreamIn(BinaryReader br)
	{
		//ILSpy generated this explicit interface implementation from .override directive in StreamIn
		this.StreamIn(br);
	}

	public void LoadAll()
	{
		if (SourceFileExists)
		{
			using (H5Reader h5r = new H5Reader(SourceFilename))
			{
				LoadOne(River, h5r);
				LoadOne(XS, h5r);
				LoadOne(IneffectiveFlowAreas, h5r);
				LoadOne(BlockedObstructions, h5r);
				LoadOne(BankLines, h5r);
				LoadOne(FlowPathLines, h5r);
				LoadOne(EdgeLines, h5r);
				LoadOne(XSInterpolationSurface, h5r);
				LoadOne(D2FlowArea, h5r);
				LoadOne(StorageArea, h5r);
				LoadOne(Junctions, h5r);
				LoadOne(RiverStationing, h5r);
				LoadOne(Structures, h5r);
				LoadOne(BoundaryConditionLines, h5r);
			}
		}
	}

	private void LoadOne(FeatureLayer ftLayer, H5Reader h5r)
	{
		ftLayer.InitializeEmptyFeatureTable();
		ftLayer.SuppressTableReload(boolValue: true);
		ftLayer.SuppressFeatureChanged(boolValue: true);
		((IGeometryLayer)ftLayer).HDFLoadFeatureTable(h5r);
	}

	public string GetBasicInfo()
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine(SourceFilename);
		stringBuilder.AppendLine("Is Result: " + IsResultFile);
		stringBuilder.AppendLine("Geometry Title = " + GeometryShortID());
		if (IsResultFile)
		{
			stringBuilder.AppendLine("Plan Title = " + Results.PlanAttributes.PlanTitle);
		}
		if (IsResultFile)
		{
			stringBuilder.AppendLine("Plan Short ID = " + Results.PlanAttributes.PlanShortID);
		}
		stringBuilder.AppendLine();
		Action<string, FeatureLayer> action = [SpecialName] (string textValue, FeatureLayer fl) =>
		{
			try
			{
				stringBuilder.AppendLine(textValue + ": " + fl.FeatureCount());
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				stringBuilder.AppendLine(textValue + ": (Unknown, Error Loading)");
				ProjectData.ClearProjectError();
			}
		};
		action("Reaches", River);
		action("Cross Sections", XS);
		action("Ineffective Flow Areas", IneffectiveFlowAreas);
		action("Blocked Obstructions", BlockedObstructions);
		action("Bank Lines", BankLines);
		action("Edge Lines", EdgeLines);
		action("Flow Path Lines", FlowPathLines);
		action("Storage Areas", StorageArea);
		action("2D Flow Areas", D2FlowArea);
		action("Breaklines", BreakLines);
		action("Structures", Structures);
		action("Culvert Groups", CulvertGroups);
		action("Culvert Barrels", CulvertBarrels);
		action("Gate Groups", GateGroups);
		action("Gate Openings", GateOpenings);
		return stringBuilder.ToString();
	}

	public string GetDetailedInfo()
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine(SourceFilename);
		stringBuilder.AppendLine("Is Result: " + IsResultFile);
		stringBuilder.AppendLine("Geometry Title = " + GeometryShortID());
		if (IsResultFile)
		{
			stringBuilder.AppendLine("Plan Title = " + Results.PlanAttributes.PlanTitle);
		}
		if (IsResultFile)
		{
			stringBuilder.AppendLine("Plan Short ID = " + Results.PlanAttributes.PlanShortID);
		}
		stringBuilder.AppendLine();
		RiverMap riverMap = CreateRiverMap();
		if (riverMap.Rivers.Count != 0)
		{
			foreach (RiverMap.River river in riverMap.Rivers)
			{
				stringBuilder.AppendLine("River " + river.Name.InSingleQuotes());
				stringBuilder.AppendLine("Length = " + river.Length + " " + SharedData.LengthUnits);
				List<RiverMap.Reach> reachesUpstreamToDownstream = river.GetReachesUpstreamToDownstream();
				if (!reachesUpstreamToDownstream.IsNullOrEmpty())
				{
					stringBuilder.AppendLine("\tUpstream Name: " + River.GetUpstreamName(reachesUpstreamToDownstream.First().ID));
					stringBuilder.AppendLine("\tUpstream Type: " + River.GetUpstreamType(reachesUpstreamToDownstream.First().ID));
				}
				foreach (RiverMap.Reach reachesUpstreamToDownstream2 in reachesUpstreamToDownstream)
				{
					stringBuilder.AppendLine("\tReach " + reachesUpstreamToDownstream2.Name.InSingleQuotes());
					stringBuilder.AppendLine("\t\t" + reachesUpstreamToDownstream2.CrossSections.Count + " Cross-Sections");
					foreach (RiverMap.CrossSection crossSection in reachesUpstreamToDownstream2.CrossSections)
					{
						_ = crossSection;
					}
					stringBuilder.AppendLine("\t " + River.GetDownstreamName(reachesUpstreamToDownstream2.ID));
				}
				if (!reachesUpstreamToDownstream.IsNullOrEmpty())
				{
					stringBuilder.AppendLine("\tDownstream Name: " + River.GetDownstreamName(reachesUpstreamToDownstream.Last().ID));
					stringBuilder.AppendLine("\tDownstream Type: " + River.GetDownstreamType(reachesUpstreamToDownstream.Last().ID));
				}
			}
		}
		else
		{
			stringBuilder.AppendLine("(No Rivers)");
		}
		stringBuilder.AppendLine();
		if (riverMap.StorageAreas.Count != 0)
		{
			foreach (RiverMap.StorageArea storageArea in riverMap.StorageAreas)
			{
				stringBuilder.AppendLine("Storage Area: " + storageArea.Name.InSingleQuotes());
				stringBuilder.AppendLine("\t" + System.Math.Round(StorageArea.Polygon(storageArea.ID).Area) + " sq-" + SharedData.ShortLengthUnits);
				stringBuilder.AppendLine("\t" + System.Math.Round(StorageArea.MinElevation(storageArea.ID)) + " " + SharedData.ShortLengthUnits + " min-elev");
				stringBuilder.AppendLine("\t" + StorageArea.GetVolumeElevationCurve(storageArea.ID)?.Count.ToString() + " pts in Vol-Elev curve.");
			}
		}
		else
		{
			stringBuilder.AppendLine("(No Storage Areas)");
		}
		stringBuilder.AppendLine();
		if (riverMap.Meshes.Count == 0)
		{
			stringBuilder.AppendLine("(No Meshes)");
		}
		else
		{
			stringBuilder.AppendLine("Meshes");
			foreach (RiverMap.Mesh mesh in riverMap.Meshes)
			{
				stringBuilder.AppendLine("\tMesh: " + mesh.Name.InSingleQuotes());
				stringBuilder.AppendLine("\t\t" + mesh.NonVirtualCellCount + " cells (non-virtual)");
				stringBuilder.AppendLine("\t\t" + mesh.FaceCount + " faces");
				stringBuilder.AppendLine("\t\t" + mesh.FacePointCount + " face-points (nodes)");
				stringBuilder.AppendLine("\t\t" + System.Math.Round(D2FlowArea.Mesh(mesh.ID).Perimeter.Area) + " sq-" + SharedData.ShortLengthUnits);
				stringBuilder.AppendLine("\t\tProperty Tables: " + D2FlowArea.ContainsCurrentTables(mesh.ID)?.Message.InSingleQuotes());
			}
		}
		stringBuilder.AppendLine();
		checked
		{
			if (BoundaryConditionLines.FeatureCount() == 0)
			{
				stringBuilder.AppendLine("(No BC Lines)");
			}
			else
			{
				stringBuilder.AppendLine("BC Lines");
				int count = BoundaryConditionLines.FeatureCount() - 1;
				for (int i = 0; i <= count; i++)
				{
					stringBuilder.AppendLine("\t" + BoundaryConditionLines.GetFeatureName(i));
				}
			}
			stringBuilder.AppendLine();
			if (Structures.FeatureCount() == 0)
			{
				stringBuilder.AppendLine("(No Structures)");
			}
			else
			{
				stringBuilder.AppendLine("Structures");
				int count2 = Structures.FeatureCount() - 1;
				for (int j = 0; j <= count2; j++)
				{
					string featureName = Structures.GetFeatureName(j);
					stringBuilder.AppendLine("\t" + featureName);
					List<int> elementsWithinStructure = StructureElementLayerExtensions.FindElementsWithinStructure(CulvertGroups, j);
					if (elementsWithinStructure.IsNullOrEmpty())
					{
						stringBuilder.AppendLine("\t\t(No Culvert Groups)");
					}
					else
					{
						stringBuilder.AppendLine("\t\tCulvert Groups " + elementsWithinStructure.Count.ToString().InParentheses());
						foreach (int elementsWithinStructure2 in elementsWithinStructure)
						{
							stringBuilder.AppendLine("\t\tGroup: " + CulvertGroups.GetFeatureName(elementsWithinStructure2).InSingleQuotes());
							List<int> elementsWithinGroup = IOpeningLayerExtensions.FindElementsWithinGroup(CulvertBarrels, elementsWithinStructure2);
							if (elementsWithinGroup.IsNullOrEmpty())
							{
								stringBuilder.AppendLine("\t\t(No Culverts)");
								continue;
							}
							stringBuilder.AppendLine("\t\t\tCulvert Barrels " + elementsWithinGroup.Count.ToString().InParentheses());
							foreach (int elementsWithinGroup2 in elementsWithinGroup)
							{
								stringBuilder.AppendLine("\t\t\tBarrel: " + CulvertBarrels.GetFeatureName(elementsWithinGroup2).InSingleQuotes());
							}
						}
					}
					List<int> elementsWithinStructureList = StructureElementLayerExtensions.FindElementsWithinStructure(GateGroups, j);
					if (elementsWithinStructureList.IsNullOrEmpty())
					{
						stringBuilder.AppendLine("\t\t(No Gate Groups)");
						continue;
					}
					stringBuilder.AppendLine("\t\tGate Groups " + elementsWithinStructureList.Count.ToString().InParentheses());
					foreach (int elementsWithinStructure3 in elementsWithinStructureList)
					{
						stringBuilder.AppendLine("\t\tGroup: " + GateGroups.GetFeatureName(elementsWithinStructure3).InSingleQuotes());
						List<int> elementsWithinGroupList = IOpeningLayerExtensions.FindElementsWithinGroup(GateOpenings, elementsWithinStructure3);
						if (elementsWithinGroupList.IsNullOrEmpty())
						{
							stringBuilder.AppendLine("\t\t(No Gates)");
							continue;
						}
						stringBuilder.AppendLine("\t\t\tGate Openings " + elementsWithinGroupList.Count.ToString().InParentheses());
						foreach (int elementsWithinGroup3 in elementsWithinGroupList)
						{
							stringBuilder.AppendLine("\t\t\tOpening: " + GateOpenings.GetFeatureName(elementsWithinGroup3).InSingleQuotes());
						}
					}
				}
			}
			return stringBuilder.ToString();
		}
	}

	static RASGeometry()
	{
		RenderRank = new Type[26]
		{
			typeof(RASErrors),
			typeof(RASBoundaryConditionLines),
			typeof(BankStationLayer),
			typeof(RASJunction),
			typeof(RiverStations),
			typeof(RASEdgeLines),
			typeof(CulvertBarrelLayer),
			typeof(GateOpeningLayer),
			typeof(CulvertGroupLayer),
			typeof(GateGroupLayer),
			typeof(StructureLayer),
			typeof(RASXS),
			typeof(RASRiver),
			typeof(RASBankLines),
			typeof(FlowPaths),
			typeof(RasLandCoverRegions),
			typeof(MeshPointLayer),
			typeof(RASBreakLines),
			typeof(MeshRegionLayer),
			typeof(RASD2FlowArea),
			typeof(MeshPerimeterLayer),
			typeof(RASStorageArea),
			typeof(IneffectiveFlowAreaLayer),
			typeof(BlockedObstructionLayer),
			typeof(FinalNValueLayer),
			typeof(RASXSInterpolationSurface)
		};
		GeometryLayerNameMap = new Dictionary<Type, string>();
		IEnumerable<Type> types = Assembly.GetExecutingAssembly().GetTypes();
		foreach (Type localValue in types)
		{
			if (Enumerable.Contains(localValue.GetInterfaces(), typeof(IGeometryLayer)))
			{
				GeometryLayerNameMap[localValue] = localValue.Name;
			}
		}
	}

	public RASGeometry()
	{
		_terrainFilename = "";
		_terrainLayername = "";
		_terrain = null;
		Layers = new List<Layer>();
		OnlyDataLayers = new HashSet<Layer>();
		Groups = new List<GroupLayer>();
		_mapPixels = null;
		_nominalLength = double.NaN;
		_isResult = false;
		_SIUnits = false;
		_filterToleranceXY = double.NaN;
		_layerLoaded = false;
		DefaultLayerName = "Geometry";
		CanMoveToAnotherLevel = false;
		CanRename = false;
		LayerPropertiesText = "RAS Geometry Properties";
	}

	public RASGeometry(string filename)
		: base(GeometryShortID(filename), filename)
	{
		_terrainFilename = "";
		_terrainLayername = "";
		_terrain = null;
		Layers = new List<Layer>();
		OnlyDataLayers = new HashSet<Layer>();
		Groups = new List<GroupLayer>();
		_mapPixels = null;
		_nominalLength = double.NaN;
		_isResult = false;
		_SIUnits = false;
		_filterToleranceXY = double.NaN;
		_layerLoaded = false;
		DefaultLayerName = "Geometry";
		CanMoveToAnotherLevel = false;
		CanRename = false;
		LayerPropertiesText = "RAS Geometry Properties";
		CompleteLoadingLayer();
	}

	public RASGeometry(RASResults res)
		: base("Geometry", res.SourceFilename)
	{
		_terrainFilename = "";
		_terrainLayername = "";
		_terrain = null;
		Layers = new List<Layer>();
		OnlyDataLayers = new HashSet<Layer>();
		Groups = new List<GroupLayer>();
		_mapPixels = null;
		_nominalLength = double.NaN;
		_isResult = false;
		_SIUnits = false;
		_filterToleranceXY = double.NaN;
		_layerLoaded = false;
		DefaultLayerName = "Geometry";
		CanMoveToAnotherLevel = false;
		CanRename = false;
		LayerPropertiesText = "RAS Geometry Properties";
		Results = res;
		CompleteLoadingLayer();
	}

	public static RASGeometry CreateEmpty(string filename, string title, SharedData.EnumUnits units)
	{
		if (File.Exists(filename))
		{
			throw new Exception(filename + " already exists, please delete it before creating a new Geometry file with this name.");
		}
		using (H5Writer h5Writer = new H5Writer(filename))
		{
			h5Writer.WriteAttribute("/", "File Type", "HEC-RAS Geometry");
			h5Writer.WriteAttribute("/", "File Version", "HEC-RAS 5.1.0 Alpha 4 May 2020");
			h5Writer.WriteAttribute("/", "Units System", SharedData.GetUnitSystemName(units));
			SharedData.UnitsSystem();
			h5Writer.CreateGroup("Geometry");
			h5Writer.WriteAttribute("Geometry", "Version", "1.0.17 (13Jan2021)");
			h5Writer.WriteAttribute("Geometry", "Title", title);
		}
		return new RASGeometry(filename);
	}

	protected override void XMLSaveChildren(XmlDocument doc, XmlElement xmlEl)
	{
		foreach (Layer layer in Layers)
		{
			XmlElement xmlElement = layer.XMLSave(doc, recurse: false);
			if (xmlElement != null)
			{
				xmlEl.AppendChild(xmlElement);
			}
		}
		foreach (GroupLayer group in Groups)
		{
			XmlElement xmlElement2 = group?.XMLSave(doc, recurse: false);
			if (xmlElement2 != null)
			{
				xmlEl.AppendChild(xmlElement2);
			}
		}
	}

	protected override void XMLLoadChildren(XmlElement xmlEl)
	{
		XmlNodeList layerXMLNodes = xmlEl.SelectNodes("Layer");
		if (!_layerLoaded)
		{
			CompleteLoadingLayer();
		}
		PushGeometryLayerXML(layerXMLNodes);
	}

	private void PushGeometryLayerXML(XmlNodeList layerXMLNodes)
	{
		if (layerXMLNodes == null)
		{
			return;
		}
		_Closure_0024__286_002D0 capturedLocals = default;
		_Closure_0024__286_002D1 capturedLocals2 = default;
		foreach (Layer layer in Layers)
		{
			capturedLocals = new _Closure_0024__286_002D0(capturedLocals);
			capturedLocals.lyrLookupName = "";
			if (!GeometryLayerNameMap.TryGetValue(layer.GetType(), out capturedLocals.lyrLookupName))
			{
				capturedLocals.lyrLookupName = layer.GetType().Name.ToString();
			}
			List<XmlElement> xmlElements = layerXMLNodes.Cast<XmlElement>().Where(capturedLocals._Lambda_0024__0).ToList();
			if (xmlElements.Count == 0)
			{
				continue;
			}
			if (xmlElements.Count == 1)
			{
				XmlElement xmlEl = xmlElements[0];
				layer.XMLLoad(xmlEl);
				continue;
			}
			capturedLocals2 = new _Closure_0024__286_002D1(capturedLocals2);
			capturedLocals2.lyrName = layer.Name;
			List<XmlElement> xmlElementsList = xmlElements.Where(capturedLocals2._Lambda_0024__1).ToList();
			if (xmlElementsList.Count == 0)
			{
				continue;
			}
			if (xmlElementsList.Count == 1)
			{
				layer.XMLLoad(xmlElementsList[0]);
			}
			else
			{
				if (!(layer is VirtualFeatureLayer))
				{
					continue;
				}
				foreach (XmlElement xmlElement in xmlElementsList)
				{
					if (VirtualFeatureLayer.GetParentIdentifierFromXML(xmlElement).RoughlyEquals(((VirtualFeatureLayer)layer).GetParentIdentifiers()))
					{
						layer.XMLLoad(xmlElement);
						break;
					}
				}
			}
		}
		foreach (GroupLayer group in Groups)
		{
			foreach (XmlElement layerXMLNode in layerXMLNodes)
			{
				if (Operators.CompareString(Layer.GetLayerNameFromXML(layerXMLNode).ToLowerInvariant(), group?.Name.ToLowerInvariant(), TextCompare: false) == 0)
				{
					group?.XMLLoad(layerXMLNode);
				}
			}
		}
	}

	private void CompleteLoadingLayer()
	{
		Nodes.Clear();
		ReadParameters();
		CompleteLoading_1D();
		CompleteLoading_2D();
		CompleteLoading_Structures();
		CompleteLoading_BoundaryConditions();
		CompleteLoading_ReferenceGeometry();
		if (SharedData.DebugMode)
		{
			CompleteLoading_PipeGeometry();
		}
		CompleteLoading_NValues();
		CompleteLoading_Infiltration();
		CompleteLoading_Sediment();
		_Errors = new RASErrors(this);
		Layers.Add(Errors);
		Nodes.Add(Errors);
		_XSsIntersectsRivers = new XSLSIntersectionData(XS, River);
		_StructuresIntersectsRivers = new XSLSIntersectionData(Structures, River);
		_XSsIntersectsBanks = new XSLSIntersectionData(XS, BankLines);
		_XSsIntersectsEdges = new XSLSIntersectionData(XS, EdgeLines);
		_XSsIntersectsFlowPaths = new XSLSIntersectionData(XS, FlowPathLines);
		_layerLoaded = true;
	}

	private void CompleteLoading_1D()
	{
		_River = new RASRiver(this);
		Layers.Add(River);
		_Junctions = new RASJunction(this);
		Layers.Add(Junctions);
		_BankLines = new RASBankLines(this);
		Layers.Add(BankLines);
		_FlowPathLines = new FlowPaths(this);
		Layers.Add(FlowPathLines);
		Nodes.Add(River);
		River.Nodes.Add(Junctions);
		River.Nodes.Add(BankLines);
		River.Nodes.Add(FlowPathLines);
		_RiverStationing = new RiverStations(this);
		River.Nodes.Add(RiverStationing);
		Layers.Add(RiverStationing);
		_XS = new RASXS(this);
		Layers.Add(XS);
		_EdgeLines = new RASEdgeLines(this);
		Layers.Add(EdgeLines);
		_XSInterpolationSurface = new RASXSInterpolationSurface(this);
		Layers.Add(XSInterpolationSurface);
		Nodes.Add(XS);
		_IneffectiveFlowAreas = new IneffectiveFlowAreaLayer(this);
		XS.Nodes.Add(IneffectiveFlowAreas);
		Layers.Add(IneffectiveFlowAreas);
		_BlockedObstructions = new BlockedObstructionLayer(this);
		XS.Nodes.Add(BlockedObstructions);
		Layers.Add(BlockedObstructions);
		XS.Nodes.Add(EdgeLines);
		XS.Nodes.Add(XSInterpolationSurface);
		_StorageArea = new RASStorageArea(this);
		Nodes.Add(StorageArea);
		Layers.Add(StorageArea);
	}

	private void CompleteLoading_2D()
	{
		_D2FlowArea = new RASD2FlowArea(this);
		Nodes.Add(D2FlowArea);
		Layers.Add(D2FlowArea);
		_MeshPerimeters = new MeshPerimeterLayer(D2FlowArea);
		D2FlowArea.Nodes.Add(MeshPerimeters);
		Layers.Add(MeshPerimeters);
		_MeshPoints = new MeshPointLayer(this);
		D2FlowArea.Nodes.Add(MeshPoints);
		Layers.Add(MeshPoints);
		_BreakLines = new RASBreakLines(this);
		D2FlowArea.Nodes.Add(BreakLines);
		Layers.Add(BreakLines);
		_MeshRegions = new MeshRegionLayer(this);
		D2FlowArea.Nodes.Add(MeshRegions);
		Layers.Add(MeshRegions);
	}

	private void CompleteLoading_NValues()
	{
		_LandCoverRegions = new RasLandCoverRegions(this);
		InterpretationOverrideGroupLayer interpretationOverrideGroupLayer = new InterpretationOverrideGroupLayer(this, "Manning's n", _LandCoverRegions);
		Nodes.Add(interpretationOverrideGroupLayer);
		Groups.Add(interpretationOverrideGroupLayer);
		interpretationOverrideGroupLayer.Nodes.Add(LandCoverRegions);
		Layers.Add(LandCoverRegions);
		_LandCoverManningsN = new FinalNValueLayer(this);
		interpretationOverrideGroupLayer.Nodes.Add(LandCoverManningsN);
		Layers.Add(LandCoverManningsN);
		_NValueRasterizationLayer = new InterpretationRasterizerLayer("TEMP N-Value Rasterizer", [SpecialName] () => NValueResampler, null);
		interpretationOverrideGroupLayer.Nodes.Add(NValueRasterizationLayer);
		Layers.Add(NValueRasterizationLayer);
	}

	private void CompleteLoading_Infiltration()
	{
		_InfiltrationOverrideRegions = new InterpretationOverrideLayer(this, "Calibration Regions", "Infiltration", [SpecialName] () => InfiltrationLayer);
		_PercentImperviousOverrideRegions = new InterpretationOverrideLayer(this, "Calibration Regions", "Percent Impervious", [SpecialName] () => PercentImperviousLayer, new string[1] { "ManningsN" }.ToList());
		InterpretationOverrideGroupLayer interpretationOverrideGroupLayer = new InterpretationOverrideGroupLayer(this, "Infiltration", _InfiltrationOverrideRegions);
		Nodes.Add(interpretationOverrideGroupLayer);
		Groups.Add(interpretationOverrideGroupLayer);
		InterpretationOverrideGroupLayer interpretationOverrideGroupLayer2 = new InterpretationOverrideGroupLayer(this, "Percent Impervious", _PercentImperviousOverrideRegions);
		Nodes.Add(interpretationOverrideGroupLayer2);
		Groups.Add(interpretationOverrideGroupLayer2);
		interpretationOverrideGroupLayer.Nodes.Add(InfiltrationOverrideRegions);
		Layers.Add(InfiltrationOverrideRegions);
		InfiltrationOverrideRegions.OnDataChanged += InfiltrationDataChanged;
		_InfiltrationRasterizer = new InterpretationRasterizerLayer("Final Values", [SpecialName] () => InfiltrationResampler, _InfiltrationOverrideRegions);
		interpretationOverrideGroupLayer.Nodes.Add(InfiltrationRasterizer);
		Layers.Add(InfiltrationRasterizer);
		interpretationOverrideGroupLayer2.Nodes.Add(PercentImperviousOverrideRegions);
		Layers.Add(PercentImperviousOverrideRegions);
		PercentImperviousOverrideRegions.OnDataChanged += PercentImperviousDataChanged;
		_PercentImperviousRasterizer = new InterpretationRasterizerLayer("Final Values", [SpecialName] () => PercentImperviousResampler, _PercentImperviousOverrideRegions, "Percent Impervious");
		interpretationOverrideGroupLayer2.Nodes.Add(PercentImperviousRasterizer);
		Layers.Add(PercentImperviousRasterizer);
	}

	private void CompleteLoading_Sediment()
	{
	}

	private void CompleteLoading_BoundaryConditions()
	{
		_BoundaryConditionLines = new RASBoundaryConditionLines(this);
		Nodes.Add(BoundaryConditionLines);
		Layers.Add(BoundaryConditionLines);
		_InitialConditionPoints = new RASInitialConditionsPoints(this);
		Nodes.Add(InitialConditionPoints);
		Layers.Add(InitialConditionPoints);
	}

	private void CompleteLoading_Structures()
	{
		_Structures = new StructureLayer(this);
		_StructureTimeSeries = new TimeSeriesOutlet(this);
		Layers.Add(StructureTimeSeries);
		OnlyDataLayers.Add(StructureTimeSeries);
		_StructureRatingCurves = new RatingCurveOutlet(this);
		Layers.Add(StructureRatingCurves);
		OnlyDataLayers.Add(StructureRatingCurves);
		_CulvertGroups = new CulvertGroupLayer(this);
		Layers.Add(CulvertGroups);
		OnlyDataLayers.Add(CulvertGroups);
		_CulvertBarrels = new CulvertBarrelLayer(this);
		Layers.Add(CulvertBarrels);
		OnlyDataLayers.Add(CulvertBarrels);
		_GateGroups = new GateGroupLayer(this);
		Layers.Add(GateGroups);
		OnlyDataLayers.Add(GateGroups);
		_GateOpenings = new GateOpeningLayer(this);
		Layers.Add(GateOpenings);
		OnlyDataLayers.Add(GateOpenings);
		_BridgesCulverts = new BridgesCulvertsLayer(Structures);
		Nodes.Add(BridgesCulverts);
		Layers.Add(BridgesCulverts);
		Func<int, StructureLayer.StructureType, bool> func = [SpecialName] (int structureIdx, StructureLayer.StructureType structType) =>
		{
			if (structureIdx == -1 || structureIdx >= Structures.FeatureCount())
			{
				return true;
			}
			DataRow featureRow = Structures.FeatureRow(structureIdx);
			return structType switch
			{
				StructureLayer.StructureType.Connection => SA2DStructures.FeatureRowFilter(featureRow), 
				StructureLayer.StructureType.Inline => InlineStructures.FeatureRowFilter(featureRow), 
				StructureLayer.StructureType.Lateral => LateralStructures.FeatureRowFilter(featureRow), 
				StructureLayer.StructureType.Bridge => BridgesCulverts.FeatureRowFilter(featureRow), 
				_ => false, 
			};
		};
		_BridgesCulvertsCulvertGroups = new VirtualGeometryFeatureLayer(featureRowFilter: [SpecialName] (DataRow row) =>
		{
			int toInteger = Conversions.ToInteger(row["Structure ID"]);
			return func(toInteger, StructureLayer.StructureType.Bridge);
		}, sourceLayer: CulvertGroups, countAttributeName: BridgesCulverts.CountAttributeName);
		Layers.Add(_BridgesCulvertsCulvertGroups);
		_BridgesCulvertsCulvertBarrels = new VirtualGeometryFeatureLayer(featureRowFilter: [SpecialName] (DataRow row) =>
		{
			if (Information.IsDBNull(RuntimeHelpers.GetObjectValue(RuntimeHelpers.GetObjectValue(row["Group ID"]))))
			{
				return true;
			}
			int tryGetStructureIndexCulvertGroupsTerm = -1;
			int toInteger2 = Conversions.ToInteger(row["Group ID"]);
			if (toInteger2 < CulvertGroups.FeatureCount() && toInteger2 >= 0)
			{
				tryGetStructureIndexCulvertGroupsTerm = CulvertGroups.TryGetStructureIndex(toInteger2);
			}
			return func(tryGetStructureIndexCulvertGroupsTerm, StructureLayer.StructureType.Bridge);
		}, sourceLayer: CulvertBarrels, countAttributeName: BridgesCulverts.CountAttributeName);
		BridgesCulverts.Nodes.Add(BridgesCulvertsCulvertBarrels);
		Layers.Add(_BridgesCulvertsCulvertBarrels);
		_InlineStructures = new InlineStructureLayer(Structures);
		Nodes.Add(InlineStructures);
		Layers.Add(InlineStructures);
		_InlineGateGroups = new VirtualGeometryFeatureLayer(featureRowFilter: [SpecialName] (DataRow row) =>
		{
			int toInteger3 = Conversions.ToInteger(row["Structure ID"]);
			return func(toInteger3, StructureLayer.StructureType.Inline);
		}, sourceLayer: GateGroups, countAttributeName: InlineStructures.CountAttributeName);
		Layers.Add(_InlineGateGroups);
		_InlineGateOpenings = new VirtualGeometryFeatureLayer(featureRowFilter: [SpecialName] (DataRow row) =>
		{
			if (Information.IsDBNull(RuntimeHelpers.GetObjectValue(RuntimeHelpers.GetObjectValue(row["Group ID"]))))
			{
				return true;
			}
			int tryGetStructureIndexGateGroupsTerm = -1;
			int toInteger4 = Conversions.ToInteger(row["Group ID"]);
			if (toInteger4 < GateGroups.FeatureCount() && toInteger4 >= 0)
			{
				tryGetStructureIndexGateGroupsTerm = GateGroups.TryGetStructureIndex(toInteger4);
			}
			return func(tryGetStructureIndexGateGroupsTerm, StructureLayer.StructureType.Inline);
		}, sourceLayer: GateOpenings, countAttributeName: InlineStructures.CountAttributeName);
		InlineStructures.Nodes.Add(_InlineGateOpenings);
		Layers.Add(_InlineGateOpenings);
		_InlineCulvertGroups = new VirtualGeometryFeatureLayer(featureRowFilter: [SpecialName] (DataRow row) =>
		{
			int toInteger5 = Conversions.ToInteger(row["Structure ID"]);
			return func(toInteger5, StructureLayer.StructureType.Inline);
		}, sourceLayer: CulvertGroups, countAttributeName: InlineStructures.CountAttributeName);
		Layers.Add(_InlineCulvertGroups);
		_InlineCulvertBarrels = new VirtualGeometryFeatureLayer(featureRowFilter: [SpecialName] (DataRow row) =>
		{
			if (Information.IsDBNull(RuntimeHelpers.GetObjectValue(RuntimeHelpers.GetObjectValue(row["Group ID"]))))
			{
				return true;
			}
			int tryGetStructureIndexCulvertGroupsTerm2 = -1;
			int toInteger6 = Conversions.ToInteger(row["Group ID"]);
			if (toInteger6 < CulvertGroups.FeatureCount() && toInteger6 >= 0)
			{
				tryGetStructureIndexCulvertGroupsTerm2 = CulvertGroups.TryGetStructureIndex(toInteger6);
			}
			return func(tryGetStructureIndexCulvertGroupsTerm2, StructureLayer.StructureType.Inline);
		}, sourceLayer: CulvertBarrels, countAttributeName: InlineStructures.CountAttributeName);
		InlineStructures.Nodes.Add(_InlineCulvertBarrels);
		Layers.Add(_InlineCulvertBarrels);
		Predicate<DataRow> featureRowFilter = [SpecialName] (DataRow row) =>
		{
			if (Information.IsDBNull(RuntimeHelpers.GetObjectValue(RuntimeHelpers.GetObjectValue(row["Structure ID"]))))
			{
				return true;
			}
			int toInteger7 = Conversions.ToInteger(row["Structure ID"]);
			return func(toInteger7, StructureLayer.StructureType.Inline);
		};
		_InlineRatingCurves = new VirtualGeometryFeatureLayer(StructureRatingCurves, InlineStructures.CountAttributeName, featureRowFilter);
		InlineStructures.Nodes.Add(_InlineRatingCurves);
		Layers.Add(_InlineRatingCurves);
		_InlineFlowTimeSeries = new VirtualGeometryFeatureLayer(StructureTimeSeries, InlineStructures.CountAttributeName, featureRowFilter);
		InlineStructures.Nodes.Add(_InlineFlowTimeSeries);
		Layers.Add(_InlineFlowTimeSeries);
		_LateralStructures = new LateralStructureLayer(Structures);
		Nodes.Add(LateralStructures);
		Layers.Add(LateralStructures);
		_LateralGateGroups = new VirtualGeometryFeatureLayer(featureRowFilter: [SpecialName] (DataRow row) =>
		{
			int toInteger8 = Conversions.ToInteger(row["Structure ID"]);
			return func(toInteger8, StructureLayer.StructureType.Lateral);
		}, sourceLayer: GateGroups, countAttributeName: LateralStructures.CountAttributeName);
		Layers.Add(_LateralGateGroups);
		_LateralGateOpenings = new VirtualGeometryFeatureLayer(featureRowFilter: [SpecialName] (DataRow row) =>
		{
			if (Information.IsDBNull(RuntimeHelpers.GetObjectValue(RuntimeHelpers.GetObjectValue(row["Group ID"]))))
			{
				return true;
			}
			int tryGetStructureIndexGateGroupsTerm2 = -1;
			int toInteger9 = Conversions.ToInteger(row["Group ID"]);
			if (toInteger9 < GateGroups.FeatureCount() && toInteger9 >= 0)
			{
				tryGetStructureIndexGateGroupsTerm2 = GateGroups.TryGetStructureIndex(toInteger9);
			}
			return func(tryGetStructureIndexGateGroupsTerm2, StructureLayer.StructureType.Lateral);
		}, sourceLayer: GateOpenings, countAttributeName: LateralStructures.CountAttributeName);
		LateralStructures.Nodes.Add(_LateralGateOpenings);
		Layers.Add(_LateralGateOpenings);
		_LateralCulvertGroups = new VirtualGeometryFeatureLayer(featureRowFilter: [SpecialName] (DataRow row) =>
		{
			int toInteger10 = Conversions.ToInteger(row["Structure ID"]);
			return func(toInteger10, StructureLayer.StructureType.Lateral);
		}, sourceLayer: CulvertGroups, countAttributeName: LateralStructures.CountAttributeName);
		Layers.Add(_LateralCulvertGroups);
		_LateralCulvertBarrels = new VirtualGeometryFeatureLayer(featureRowFilter: [SpecialName] (DataRow row) =>
		{
			if (Information.IsDBNull(RuntimeHelpers.GetObjectValue(RuntimeHelpers.GetObjectValue(row["Group ID"]))))
			{
				return true;
			}
			int tryGetStructureIndexCulvertGroupsTerm3 = -1;
			int toInteger11 = Conversions.ToInteger(row["Group ID"]);
			if (toInteger11 < CulvertGroups.FeatureCount() && toInteger11 >= 0)
			{
				tryGetStructureIndexCulvertGroupsTerm3 = CulvertGroups.TryGetStructureIndex(toInteger11);
			}
			return func(tryGetStructureIndexCulvertGroupsTerm3, StructureLayer.StructureType.Lateral);
		}, sourceLayer: CulvertBarrels, countAttributeName: LateralStructures.CountAttributeName);
		LateralStructures.Nodes.Add(_LateralCulvertBarrels);
		Layers.Add(_LateralCulvertBarrels);
		featureRowFilter = [SpecialName] (DataRow row) =>
		{
			if (Information.IsDBNull(RuntimeHelpers.GetObjectValue(RuntimeHelpers.GetObjectValue(row["Structure ID"]))))
			{
				return true;
			}
			int toInteger12 = Conversions.ToInteger(row["Structure ID"]);
			return func(toInteger12, StructureLayer.StructureType.Lateral);
		};
		_LateralRatingCurves = new VirtualGeometryFeatureLayer(StructureRatingCurves, LateralStructures.CountAttributeName, featureRowFilter);
		LateralStructures.Nodes.Add(_LateralRatingCurves);
		Layers.Add(_LateralRatingCurves);
		_LateralFlowTimeSeries = new VirtualGeometryFeatureLayer(StructureTimeSeries, LateralStructures.CountAttributeName, featureRowFilter);
		LateralStructures.Nodes.Add(_LateralFlowTimeSeries);
		Layers.Add(_LateralFlowTimeSeries);
		_SA2DStructures = new SA2DStructureLayer(Structures);
		Nodes.Add(SA2DStructures);
		Layers.Add(SA2DStructures);
		_SA2DGateGroups = new VirtualGeometryFeatureLayer(featureRowFilter: [SpecialName] (DataRow row) =>
		{
			int toInteger13 = Conversions.ToInteger(row["Structure ID"]);
			return func(toInteger13, StructureLayer.StructureType.Connection);
		}, sourceLayer: GateGroups, countAttributeName: SA2DStructures.CountAttributeName);
		Layers.Add(_SA2DGateGroups);
		_SA2DGateOpenings = new VirtualGeometryFeatureLayer(featureRowFilter: [SpecialName] (DataRow row) =>
		{
			if (Information.IsDBNull(RuntimeHelpers.GetObjectValue(RuntimeHelpers.GetObjectValue(row["Group ID"]))))
			{
				return true;
			}
			int tryGetStructureIndexGateGroupsTerm3 = -1;
			int toInteger14 = Conversions.ToInteger(row["Group ID"]);
			if (toInteger14 < GateGroups.FeatureCount() && toInteger14 >= 0)
			{
				tryGetStructureIndexGateGroupsTerm3 = GateGroups.TryGetStructureIndex(toInteger14);
			}
			return func(tryGetStructureIndexGateGroupsTerm3, StructureLayer.StructureType.Connection);
		}, sourceLayer: GateOpenings, countAttributeName: SA2DStructures.CountAttributeName);
		SA2DStructures.Nodes.Add(_SA2DGateOpenings);
		Layers.Add(_SA2DGateOpenings);
		_SA2DCulvertGroups = new VirtualGeometryFeatureLayer(featureRowFilter: [SpecialName] (DataRow row) =>
		{
			int toInteger15 = Conversions.ToInteger(row["Structure ID"]);
			return func(toInteger15, StructureLayer.StructureType.Connection);
		}, sourceLayer: CulvertGroups, countAttributeName: SA2DStructures.CountAttributeName);
		Layers.Add(_SA2DCulvertGroups);
		_SA2DCulvertBarrels = new VirtualGeometryFeatureLayer(featureRowFilter: [SpecialName] (DataRow row) =>
		{
			if (Information.IsDBNull(RuntimeHelpers.GetObjectValue(RuntimeHelpers.GetObjectValue(row["Group ID"]))))
			{
				return true;
			}
			int tryGetStructureIndexCulvertGroupsTerm4 = -1;
			int toInteger16 = Conversions.ToInteger(row["Group ID"]);
			if (toInteger16 < CulvertGroups.FeatureCount() && toInteger16 >= 0)
			{
				tryGetStructureIndexCulvertGroupsTerm4 = CulvertGroups.TryGetStructureIndex(toInteger16);
			}
			return func(tryGetStructureIndexCulvertGroupsTerm4, StructureLayer.StructureType.Connection);
		}, sourceLayer: CulvertBarrels, countAttributeName: SA2DStructures.CountAttributeName);
		SA2DStructures.Nodes.Add(_SA2DCulvertBarrels);
		Layers.Add(_SA2DCulvertBarrels);
		featureRowFilter = [SpecialName] (DataRow row) =>
		{
			if (Information.IsDBNull(RuntimeHelpers.GetObjectValue(RuntimeHelpers.GetObjectValue(row["Structure ID"]))))
			{
				return true;
			}
			int toInteger17 = Conversions.ToInteger(row["Structure ID"]);
			return func(toInteger17, StructureLayer.StructureType.Connection);
		};
		_SA2DRatingCurves = new VirtualGeometryFeatureLayer(StructureRatingCurves, SA2DStructures.CountAttributeName, featureRowFilter);
		SA2DStructures.Nodes.Add(_SA2DRatingCurves);
		Layers.Add(_SA2DRatingCurves);
		_SA2DFlowTimeSeries = new VirtualGeometryFeatureLayer(StructureTimeSeries, SA2DStructures.CountAttributeName, featureRowFilter);
		SA2DStructures.Nodes.Add(_SA2DFlowTimeSeries);
		Layers.Add(_SA2DFlowTimeSeries);
		_PumpStations = new PumpStationLayer(this);
		Nodes.Add(PumpStations);
		Layers.Add(PumpStations);
		_PumpGroups = new PumpGroupLayer(this);
		Layers.Add(PumpGroups);
		_Pumps = new PumpLayer(this);
		PumpStations.Nodes.Add(Pumps);
		Layers.Add(Pumps);
		Layers.Add(Structures);
		OnlyDataLayers.Add(Structures);
	}

	private void CompleteLoading_ReferenceGeometry()
	{
		_ReferencePoints = new RASReferencePoints(this);
		Layers.Add(ReferencePoints);
		Nodes.Add(ReferencePoints);
		_ReferenceLines = new RasReferenceLines(this);
		Layers.Add(ReferenceLines);
		Nodes.Add(ReferenceLines);
	}

	private void CompleteLoading_PipeGeometry()
	{
		_PipeLinks = new RASPipeLinks(this);
		Layers.Add(PipeLinks);
		Nodes.Add(PipeLinks);
		_PipeNodes = new RASPipeNodes(this);
		Layers.Add(PipeNodes);
		Nodes.Add(PipeNodes);
	}

	public void EnsureDataMatchesCurrentFileData()
	{
		if (DateTime.Compare(_hdfFileDateLastRead, SourceFileDate) != 0)
		{
			Refresh();
		}
	}

	public void Refresh()
	{
		ErrorMessage = "";
		ReadParameters();
		_mapPixels = null;
		foreach (FeatureLayer featureLayer in Layers.Where([SpecialName] (Layer lyr) => lyr is FeatureLayer))
		{
			featureLayer.ClearFeatureTable();
		}
		SharedData.ClearAllCaches();
	}

	public void ReadParameters()
	{
		ErrorMessage = "";
		try
		{
			InitializeAssociations();
			if (!File.Exists(SourceFilename))
			{
				if (SharedData.DebugMode)
				{
					ErrorMessage = "Geometry file '" + SourceFilename + "' not found.";
				}
				else
				{
					ErrorMessage = "Geometry file '" + System.IO.Path.GetFileName(SourceFilename) + "' not found.";
				}
				return;
			}
			using H5Reader h5Reader = new H5Reader(SourceFilename);
			_hdfFileDateLastRead = SourceFileDate;
			_isResult = h5Reader.ExistsAsGroup("Results");
			if (!h5Reader.ExistsAsGroup("Geometry"))
			{
				throw new BadFileException("Geometry/Result file is corrupted. Please recompute.");
			}
			string sourceFilename = SourceFilename;
			List<string> attributeNames = h5Reader.GetAttributeNames("Geometry");
			if (attributeNames.Count == 0)
			{
				throw new BadFileException("Geometry/Result file is corrupted. Please recompute.");
			}
			foreach (string attributeName in attributeNames)
			{
				switch (attributeName)
				{
				case "SI Units":
				{
					string textValue = "";
					h5Reader.ReadAttributeScalar("Geometry", "SI Units", ref textValue);
					bool.TryParse(textValue, out _SIUnits);
					break;
				}
				case "Terrain Filename":
				{
					string textValue2 = "";
					h5Reader.ReadAttributeScalar("Geometry", "Terrain Filename", ref textValue2);
					_terrainFilename = RasMapperLib.Utilities.Path.MakeAbsolute(textValue2, sourceFilename);
					break;
				}
				case "Terrain Layername":
					h5Reader.ReadAttributeScalar("Geometry", "Terrain Layername", ref _terrainLayername);
					break;
				}
			}
			_nValueAssociation.LoadAssociation(h5Reader, "Geometry");
			_infiltrationAssociation.LoadAssociation(h5Reader, "Geometry");
			_impervAssociation.LoadAssociation(h5Reader, "Geometry");
			_sedimentSoilsAssociation.LoadAssociation(h5Reader, "Geometry");
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			ErrorMessage = "Error reading geometry parameters from file '" + System.IO.Path.GetFileName(SourceFilename) + "':  " + ex2.Message;
			if (IsResultFile && Results != null)
			{
				Results.ErrorMessage = ErrorMessage;
				ErrorMessage = ErrorMessage;
			}
			ProjectData.ClearProjectError();
		}
	}

	private void InitializeAssociations()
	{
		Func<GeometryAssociation, Layer> resolveLayer = [SpecialName] (GeometryAssociation ga) => Layer.TryFindOrCreateLayer(ga.AbsoluteFilename, SharedData.RasMapper?.MapLayersGroup, [SpecialName] () => new LandCoverLayer(ga.AbsoluteFilename));
		_nValueAssociation = new GeometryAssociation("Land Cover", SourceFilename, resolveLayer);
		_nValueAssociation.AssociationChanged += [SpecialName] (Layer prev, Layer after) =>
		{
			LandCoverLayer landCoverLayer = prev as LandCoverLayer;
			LandCoverLayer landCoverLayer2 = after as LandCoverLayer;
			if (landCoverLayer != null)
			{
				landCoverLayer.DataChanged -= NValueDataChanged;
			}
			if (landCoverLayer2 != null)
			{
				landCoverLayer2.DataChanged += NValueDataChanged;
			}
		};
		_infiltrationAssociation = new GeometryAssociation("Infiltration", SourceFilename, resolveLayer);
		_infiltrationAssociation.AssociationChanged += [SpecialName] (Layer prev, Layer after) =>
		{
			LandCoverLayer landCoverLayer = prev as LandCoverLayer;
			LandCoverLayer landCoverLayer2 = after as LandCoverLayer;
			if (landCoverLayer != null)
			{
				landCoverLayer.DataChanged -= InfiltrationDataChanged;
			}
			if (landCoverLayer2 != null)
			{
				landCoverLayer2.DataChanged += InfiltrationDataChanged;
			}
		};
		_impervAssociation = new GeometryAssociation("Percent Impervious", SourceFilename, resolveLayer);
		_impervAssociation.AssociationChanged += [SpecialName] (Layer prev, Layer after) =>
		{
			LandCoverLayer landCoverLayer = prev as LandCoverLayer;
			LandCoverLayer landCoverLayer2 = after as LandCoverLayer;
			if (landCoverLayer != null)
			{
				landCoverLayer.DataChanged -= PercentImperviousDataChanged;
			}
			if (landCoverLayer2 != null)
			{
				landCoverLayer2.DataChanged += PercentImperviousDataChanged;
			}
		};
		_sedimentSoilsAssociation = new GeometryAssociation("Sediment Bed Material", SourceFilename, resolveLayer);
	}

	public void WriteParameters()
	{
		if (IsEditingData)
		{
			return;
		}
		using H5Writer h5Writer = new H5Writer(SourceFilename);
		try
		{
			string textValue = "Geometry";
			if (!h5Writer.ExistsAsGroup(textValue))
			{
				h5Writer.CreateGroup(textValue);
			}
			h5Writer.WriteAttribute(textValue, "SI Units", _SIUnits.ToString());
			string relative = RasMapperLib.Utilities.Path.MakeRelative(_terrainFilename, SourceFilename);
			h5Writer.WriteAttribute(textValue, "Terrain Filename", relative);
			h5Writer.WriteAttribute(textValue, "Terrain File Date", TerrainFileDate);
			h5Writer.WriteAttribute(textValue, "Terrain Layername", _terrainLayername);
			NValueAssociation.SaveAssociation(h5Writer, textValue);
			InfiltrationAssociation.SaveAssociation(h5Writer, textValue);
			SedimentSoilsAssociation.SaveAssociation(h5Writer, textValue);
			PercentImperviousAssociation.SaveAssociation(h5Writer, textValue);
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			if (SharedData.RasMapper != null)
			{
				SharedData.RasMapper.StatusMessageAdd("Error writing geometry parameters to file: " + SourceFilename);
			}
			ProjectData.ClearProjectError();
		}
	}

	public SharedData.EnumUnits ReadUnitSystem()
	{
		if (!SourceFileExists)
		{
			return SharedData.EnumUnits.USCustomary;
		}
		using H5Reader h5Reader = new H5Reader(SourceFilename);
		Dictionary<string, object> allAttributes = h5Reader.ReadAllAttributes("/");
		object localValue = null;
		if (allAttributes.TryGetValue("Units System", out localValue))
		{
			string trimTerm = localValue as string;
			if (string.IsNullOrEmpty(trimTerm))
			{
				return SharedData.EnumUnits.USCustomary;
			}
			trimTerm = trimTerm.Trim();
			if (string.Equals(trimTerm, "US Customary", StringComparison.CurrentCultureIgnoreCase))
			{
				return SharedData.EnumUnits.USCustomary;
			}
			if (string.Equals(trimTerm, "SI Units", StringComparison.CurrentCultureIgnoreCase))
			{
				return SharedData.EnumUnits.SIUnits;
			}
			return SharedData.EnumUnits.USCustomary;
		}
		return SharedData.EnumUnits.USCustomary;
	}

	private void PropagateFileNotFoundErr()
	{
		ErrorMessage = "File '" + System.IO.Path.GetFileName(SourceFilename) + "' not found.";
		RASResults results = Results;
		if (results == null)
		{
			string errorMessage = "Geometry file '" + System.IO.Path.GetFileName(SourceFilename) + "' not found. Please recompute your geometry.";
			ErrorMessage = errorMessage;
		}
		else
		{
			string errorMessage2 = (ErrorMessage = "Results file '" + System.IO.Path.GetFileName(SourceFilename) + "' not found. Please recompute your results.");
			results.ErrorMessage = errorMessage2;
		}
	}

	public bool Overlaps(RasterM raster)
	{
		if (raster.Overlaps(_StorageArea.Extent) || raster.Overlaps(_D2FlowArea.Extent) || raster.Overlaps(_XSInterpolationSurface.Extent))
		{
			return true;
		}
		return false;
	}

	public void ClearMapPixels()
	{
		_mapPixels = null;
	}

	public RASGeometryMapPoints MapPixels(RasterM raster)
	{
		if (_mapPixels == null)
		{
			_mapPixels = new RASGeometryMapPoints(this);
		}
		if (_mapPixels.Raster == null || !_mapPixels.Raster.EqualRasterM(raster))
		{
			_mapPixels.ComputeForRaster(raster);
		}
		return _mapPixels;
	}

	public RASGeometryMapPoints MapPixels(IPoints pms)
	{
		RASGeometryMapPoints rASGeometryMapPoints = new RASGeometryMapPoints(this);
		rASGeometryMapPoints.ComputeForPoints(pms, reportTime: true);
		return rASGeometryMapPoints;
	}

	public RASGeometryMapPoints MapPixels(Polyline cutline, ref List<PointOnPolyline> intersections, double maxSegmentLen = double.NaN)
	{
		RASGeometryMapPoints rASGeometryMapPoints = new RASGeometryMapPoints(this);
		rASGeometryMapPoints.ComputeForLine(cutline, ref intersections, maxSegmentLen);
		return rASGeometryMapPoints;
	}

	public RASGeometryMapPoints MapPixels(Polyline cutline, TerrainLayer cutTerrain, ref List<PointOnPolyline> intersections, double maxSegmentLen = double.NaN)
	{
		RASGeometryMapPoints rASGeometryMapPoints = new RASGeometryMapPoints(this);
		rASGeometryMapPoints.ComputeForLine(cutline, cutTerrain, ref intersections, maxSegmentLen);
		return rASGeometryMapPoints;
	}

	public RASGeometryMapPoints MapPixels(Polyline cutline, List<PointOnPolyline> extraPoints, ref List<PointOnPolyline> intersections, double maxSegmentLen = double.NaN)
	{
		RASGeometryMapPoints rASGeometryMapPoints = new RASGeometryMapPoints(this);
		rASGeometryMapPoints.ComputeForLine(cutline, extraPoints, ref intersections, maxSegmentLen);
		return rASGeometryMapPoints;
	}

	private double NominalLength()
	{
		XS.FeatureTable();
		River.FeatureTable();
		StorageArea.FeatureTable();
		D2FlowArea.FeatureTable();
		if (double.IsNaN(_nominalLength))
		{
			Extent extent = NominalExtent();
			if (extent != null)
			{
				_nominalLength = extent.DeltaXY;
			}
		}
		return _nominalLength;
	}

	public Extent NominalExtent()
	{
		return new Extent(XS.Extent, River.Extent, StorageArea.Extent, D2FlowArea.Extent, MeshPerimeters.Extent, MeshPoints.Extent);
	}

	public static double DefaultFilterToleranceTIN(bool SIUnits)
	{
		if (SIUnits)
		{
			return 0.03;
		}
		return 0.1;
	}

	public List<RASGeometry> FindAllGeometries()
	{
		List<RASGeometry> rasGeometries = new List<RASGeometry>();
		FindGeometries(Nodes, rasGeometries);
		return rasGeometries;
	}

	private void FindGeometries(TreeNodeCollection treeNodes, List<RASGeometry> geometries)
	{
		foreach (TreeNode treeNode in treeNodes)
		{
			if (treeNode is RASGeometry)
			{
				geometries.Add((RASGeometry)treeNode);
			}
			if (treeNode.Nodes.Count > 0)
			{
				FindGeometries(treeNode.Nodes, geometries);
			}
		}
	}

	protected override string GetEditContextMenuTitle()
	{
		return "Edit Geometry";
	}

	public void Save()
	{
		using H5Writer hw = new H5Writer(SourceFilename);
		foreach (IGeometryLayer iGeometryLayer in Layers.Where([SpecialName] (Layer l) => l is IGeometryLayer))
		{
			iGeometryLayer?.HDFSaveFeatureTable(hw);
		}
	}

	public void LoadSaveAllLayers()
	{
		List<FeatureLayer> featureLayers = (from l in Layers
			where l is FeatureLayer
			select (FeatureLayer)l).ToList();
		foreach (FeatureLayer featureLayer in featureLayers)
		{
			featureLayer.FeatureTable();
		}
		using (new MultiLayerReloadSuppressor(featureLayers))
		{
			foreach (FeatureLayer featureLayer2 in featureLayers)
			{
				featureLayer2.Save();
			}
		}
	}

	public static string GetAssociatedTerrainFilename(string geoHDFFilename)
	{
		if (File.Exists(geoHDFFilename))
		{
			using H5Reader h5Reader = new H5Reader(geoHDFFilename);
			string textValue = "Geometry";
			if (h5Reader.ExistsAsGroup(textValue) && h5Reader.GetAttributeNames(textValue).Contains("Terrain Filename"))
			{
				string textValue2 = "";
				h5Reader.ReadAttributeScalar(textValue, "Terrain Filename", ref textValue2);
				return RasMapperLib.Utilities.Path.MakeAbsolute(textValue2, geoHDFFilename);
			}
		}
		return "";
	}

	public void SetTerrain(TerrainLayer terrain)
	{
		SetTerrainTemporary(terrain);
		WriteParameters();
	}

	public static void UpdateAssociatedFilenames(string prevFilename, string newFilename)
	{
		if (!File.Exists(newFilename))
		{
			return;
		}
		string textValue = "Geometry";
		using H5Writer h5Writer = new H5Writer(newFilename);
		if (!h5Writer.ExistsAsGroup(textValue))
		{
			return;
		}
		if (h5Writer.GetAttributeNames(textValue).Contains("Terrain Filename"))
		{
			UpdateAssociatedFilenames(h5Writer, prevFilename, newFilename, textValue, "Terrain Filename");
		}
		GeometryAssociation.UpdateAssociations(h5Writer, prevFilename, "Geometry", GeometryAssociation.AllKnownAssociationNames);
		string path = "Geometry/2D Flow Areas";
		if (!h5Writer.ExistsAsGroup(path))
		{
			return;
		}
		List<string> groupNames = h5Writer.GetGroupNames(path);
		foreach (string groupName in groupNames)
		{
			string pathGroupNameTerm = path + "/" + groupName;
			if (h5Writer.GetAttributeNames(pathGroupNameTerm).Contains("Terrain Filename"))
			{
				UpdateAssociatedFilenames(h5Writer, prevFilename, newFilename, pathGroupNameTerm, "Terrain Filename");
			}
			GeometryAssociation.UpdateAssociations(h5Writer, prevFilename, pathGroupNameTerm, GeometryAssociation.AllKnownAssociationNames);
		}
	}

	private static void UpdateAssociatedFilenames(H5Writer hw, string prevFilename, string newFilename, string groupname, string attrname)
	{
		string makeRelativePathTerm = "";
		hw.ReadAttributeScalar(groupname, attrname, ref makeRelativePathTerm);
		makeRelativePathTerm = RasMapperLib.Utilities.Path.MakeRelative(RasMapperLib.Utilities.Path.MakeAbsolute(makeRelativePathTerm, prevFilename), newFilename);
		hw.WriteAttribute(groupname, attrname, makeRelativePathTerm);
	}

	public void SetTerrainTemporary(TerrainLayer terrain)
	{
		_terrain = terrain;
		if (_terrain != null)
		{
			_terrainFilename = _terrain.SourceFilename;
			_terrainLayername = _terrain.Name;
		}
	}

	private static void StripNoData(Parameter cv)
	{
		cv.Values.RemoveWhere([SpecialName] (KeyValuePair<string, float> kvp) => float.IsNaN(kvp.Value) || kvp.Value == float.MaxValue || kvp.Value == -9999f);
	}

	private void NValueDataChanged()
	{
		NValueRasterizationLayer?.NotifyDataChanged();
		_landCoverResampler = null;
	}

	private void InfiltrationDataChanged()
	{
		InfiltrationRasterizer?.NotifyDataChanged();
		_infiltrationParameterResampler = null;
	}

	private static ParameterResampler MakeDefaultParameterResampler(LandCoverLayer layer, InterpretationOverrideLayer overrideLyr)
	{
		if (layer == null)
		{
			return null;
		}
		IClassificationResampler resampler = layer.Resampler;
		if (resampler == null)
		{
			return null;
		}
		List<ParameterizedPolygon> overridePolygonMap = overrideLyr.GetOverridePolygonMap();
		return new ParameterResampler(resampler, overrideLyr.BaseInterpretationMergedWithLC, overridePolygonMap);
	}

	private void PercentImperviousDataChanged()
	{
		PercentImperviousRasterizer?.NotifyDataChanged();
		_impervParameterResampler = null;
	}

	public void CreateXSGeoTiff(bool includeOverbanks, double cellSize, string newFilename, ProgressReporter reporter = null)
	{
		_Closure_0024__413_002D0 capturedLocals = default;
		_Closure_0024__413_002D0 capturedLocals2 = new _Closure_0024__413_002D0(capturedLocals);
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		capturedLocals2.childReporter = reporter.WithProgressModifier(0, 0.5f);
		XS.FeatureTable();
		RASXSInterpolationSurface xSInterpolationSurface = XSInterpolationSurface;
		xSInterpolationSurface.EnsureInterpolationSurface();
		xSInterpolationSurface.FeatureTable();
		Extent extent = xSInterpolationSurface.Extent;
		if (cellSize <= 0.0)
		{
			MessageBox.Show("Cell size must be greater than zero.");
			return;
		}
		int ordinal = 256;
		checked
		{
			int systemDeltaYTerm = (int)System.Math.Ceiling(extent.DeltaY / cellSize);
			int systemDeltaXTerm = (int)System.Math.Ceiling(extent.DeltaX / cellSize);
			if (systemDeltaYTerm < 2)
			{
				MessageBox.Show("Cross Section vertical range is " + Strings.Format(extent.DeltaY) + " and with specified cell size " + Strings.Format(cellSize) + " results in less than one row. Reduce cell size and try again.");
				return;
			}
			if (systemDeltaXTerm < 2)
			{
				MessageBox.Show("Cross Section horizontal range is " + Strings.Format(extent.DeltaY) + " and with specified cell size " + Strings.Format(cellSize) + " results in less than one row. Reduce cell size and try again.");
				return;
			}
			double minX = extent.MinX;
			double maxY = extent.MaxY;
			_ = (double)systemDeltaXTerm;
			_ = (double)systemDeltaYTerm;
			TiffMetadata<float> tiffMetadata = new TiffMetadata<float>(-9999f, RoundTo.None);
			tiffMetadata.DataType = "Elevation";
			tiffMetadata.DataUnits = SharedData.LengthUnits;
			tiffMetadata.TileHeight = ordinal;
			tiffMetadata.TileWidth = ordinal;
			using (FloatTiffWriter floatTiffWriter = new FloatTiffWriter(newFilename, systemDeltaXTerm, systemDeltaYTerm, ComputeStats: true, tiffMetadata))
			{
				int numTilesWide = floatTiffWriter.GetNumTilesWide();
				int numTilesTall = floatTiffWriter.GetNumTilesTall();
				int numTilesWideTimesNumTilesTall = numTilesWide * numTilesTall;
				double maxX = minX + (double)numTilesWide * cellSize * (double)ordinal;
				double minY = maxY - (double)numTilesTall * cellSize * (double)ordinal;
				RasterM rasterM = new RasterM(numTilesTall, numTilesWide, maxX, minX, maxY, minY);
				RasterM raster = new RasterM();
				float[] floatValues = new float[ordinal * ordinal - 1 + 1];
				int count = numTilesTall - 1;
				for (int i = 0; i <= count; i++)
				{
					int count2 = numTilesWide - 1;
					for (int j = 0; j <= count2; j++)
					{
						int cellIndex = rasterM.CellIndex(i, j);
						rasterM.SetCellTileRasterM(cellIndex, ordinal, raster);
						Buffers.ArraySetAllVB(floatValues, -9999f);
						GenerateXSTerrain(raster, floatValues, includeOverbanks, xsisAlreadyValid: true);
						floatTiffWriter.WriteTile(cellIndex, floatValues);
						if (unchecked(cellIndex % 50) == 0)
						{
							capturedLocals2.childReporter.ReportProgress((int)System.Math.Round((double)(100 * cellIndex) / (double)numTilesWideTimesNumTilesTall));
						}
					}
				}
			}
			capturedLocals2.childReporter = reporter.WithProgressModifier(50, 0.25f);
			InterpolatedLayer.AddHistogram(newFilename, null, capturedLocals2.childReporter);
			capturedLocals2.childReporter = reporter.WithProgressModifier(75, 0.25f);
			capturedLocals2.gd = new GDALProgressTracker();
			Action<char> reportStdOut = [SpecialName] (char c) =>
			{
				int? parseGDALStdOut = capturedLocals2.gd.ParseGDALStdOut(c);
				if (parseGDALStdOut.HasValue)
				{
					capturedLocals2.childReporter.ReportProgress(parseGDALStdOut.Value);
				}
			};
			InterpolatedLayer.AddSRSGeorefAddo(newFilename, systemDeltaXTerm, systemDeltaYTerm, minX, maxY, cellSize, UseNearest: false, reportStdOut, null, null);
		}
	}

	public void GenerateXSTerrain(RasterM raster, float[] values, bool includeOverbanks, bool xsisAlreadyValid = false)
	{
		_Closure_0024__414_002D0 capturedLocals = new _Closure_0024__414_002D0(null);
		capturedLocals.floatValues = values;
		capturedLocals.includeOverbanks = includeOverbanks;
		RasterizeTriangle rasterizeTriangle = new RasterizeTriangle(raster);
		capturedLocals.xs = XS;
		capturedLocals.xs.EnsureBanklineCenterStationValues();
		capturedLocals.xs.FeatureTable();
		RASXSInterpolationSurface xSInterpolationSurface = XSInterpolationSurface;
		if (!xsisAlreadyValid)
		{
			xSInterpolationSurface.EnsureInterpolationSurface();
		}
		checked
		{
			int count = xSInterpolationSurface.FeatureCount() - 1;
			_Closure_0024__414_002D1 capturedLocals2 = default;
			PointM p = default;
			PointM p2 = default;
			PointM p3 = default;
			for (int i = 0; i <= count; i++)
			{
				capturedLocals2 = new _Closure_0024__414_002D1(capturedLocals2);
				capturedLocals2.outerClosure = capturedLocals;
				Tin tin = xSInterpolationSurface.Tin(i);
				if (raster.Overlaps(tin))
				{
					capturedLocals2.usid = xSInterpolationSurface.GetUpstreamXSID(i);
					capturedLocals2.dsid = xSInterpolationSurface.GetDownstreamXSID(i);
					capturedLocals2.usXSProfile = capturedLocals2.outerClosure.xs.GetProfile(capturedLocals2.usid);
					capturedLocals2.dsXSProfile = capturedLocals2.outerClosure.xs.GetProfile(capturedLocals2.dsid);
					int count2 = tin.TriangleCount() - 1;
					for (int j = 0; j <= count2; j++)
					{
						tin.TrianglePointMs(j, ref p, ref p2, ref p3);
						rasterizeTriangle.ComputeCellsZM(p, p2, p3, capturedLocals2._Lambda_0024__0);
					}
				}
			}
		}
	}

	public List<Polygon> GetGeometryRegionPolygons()
	{
		double filterToleranceXY = FilterToleranceXY;
		XSInterpolationSurface.EnsureInterpolationSurface();
		List<Polygon> polygons = new List<Polygon>(checked(D2FlowArea.FeatureCount() + StorageArea.FeatureCount() + XSInterpolationSurface.FeatureCount()));
		foreach (MeshFV2D meshe in D2FlowArea.Meshes())
		{
			polygons.Add(meshe.Perimeter);
		}
		foreach (Polygon polygon2 in StorageArea.Polygons())
		{
			polygons.Add(polygon2);
		}
		foreach (Tin tin in XSInterpolationSurface.Tins())
		{
			Polygon polygon = GeoProcessing.MergeTin(tin, filterToleranceXY);
			if (polygon != null)
			{
				polygons.Add(polygon);
			}
		}
		return polygons;
	}

	public void ReduceTerrain(object sender, EventArgs e)
	{
		if ((DialogResult)6 == MessageBox.Show("Do you want to reduce the terrain data to only cover the current geometry?", "HEC-RAS", MessageBoxButtons.YesNo))
		{
			_ = Terrain;
		}
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
		if (XS.FeatureCount() == 0)
		{
			return null;
		}
		if (River.FeatureCount() == 0)
		{
			return null;
		}
		if (IsResultFile)
		{
			return null;
		}
		_ = Junctions;
		_ = River;
		_ = XS;
		Hash hash = new Hash();
		River.MD5HashAppend(hash);
		BankLines.MD5HashAppend(hash);
		EdgeLines.MD5HashAppend(hash);
		XS.MD5HashAppend(hash);
		IneffectiveFlowAreas.MD5HashAppend(hash);
		BlockedObstructions.MD5HashAppend(hash);
		Junctions.MD5HashAppend(hash);
		return hash.HashBytes();
	}

	private byte[] ReadStoredHash()
	{
		if (!File.Exists(SourceFilename))
		{
			return null;
		}
		using H5Reader h5Reader = new H5Reader(SourceFilename);
		byte[] values = null;
		_ = XSInterpolationSurface;
		if (!h5Reader.TryReadAttribute("Geometry/Cross Section Interpolation Surfaces", "Source Data Hash", ref values))
		{
			return null;
		}
		return values;
	}

	public bool CompleteForComputations(bool forceRecompute = false, ProgressReporter prog = null)
	{
		bool result;
		try
		{
			if (prog == null)
			{
				prog = ProgressReporter.None();
			}
			if (!BankLines.EnsureBankLines())
			{
				GeometryCompletionProblem(prog);
				result = false;
			}
			else if (!IneffectiveFlowAreas.EnsureIneffectiveFlowAreas())
			{
				GeometryCompletionProblem(prog);
				result = false;
			}
			else if (!BlockedObstructions.EnsureBlockedObstructions())
			{
				GeometryCompletionProblem(prog);
				result = false;
			}
			else if (!EdgeLines.EnsureEdgeLines())
			{
				GeometryCompletionProblem(prog);
				result = false;
			}
			else if (!XS.CompleteForComputations(prog))
			{
				GeometryCompletionProblem(prog);
				result = false;
			}
			else if (!XSInterpolationSurface.CompleteForComputations(prog))
			{
				GeometryCompletionProblem(prog);
				result = false;
			}
			else if (!StorageArea.CompleteForComputations(prog))
			{
				GeometryCompletionProblem(prog);
				result = false;
			}
			else if (!D2FlowArea.EnsurePropertyTables(forceRecompute))
			{
				GeometryCompletionProblem(prog);
				result = false;
			}
			else if (!BoundaryConditionLines.EnsureConnectivity(prog))
			{
				GeometryCompletionProblem(prog);
				result = false;
			}
			else if (!ReferenceLines.ComputeConnectivity(prog))
			{
				GeometryCompletionProblem(prog);
				result = false;
			}
			else if (!InitialConditionPoints.EnsureInitialConditions())
			{
				GeometryCompletionProblem(prog);
				result = false;
			}
			else if (!CulvertBarrels.EnsureConnectivity(prog))
			{
				GeometryCompletionProblem(prog);
				result = false;
			}
			else if (!GateOpenings.EnsureConnectivity(prog))
			{
				GeometryCompletionProblem(prog);
				result = false;
			}
			else if (!Pumps.EnsureConnectivity(prog))
			{
				GeometryCompletionProblem(prog);
				result = false;
			}
			else if (!StructureTimeSeries.EnsureConnectivity())
			{
				GeometryCompletionProblem(prog);
				result = false;
			}
			else if (!StructureRatingCurves.EnsureConnectivity())
			{
				GeometryCompletionProblem(prog);
				result = false;
			}
			else
			{
				WriteMetaAttributes(complete: true);
				prog.ReportMessage("LAYER=COMPLETE");
				prog.ReportProgress(100);
				result = true;
			}
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			WriteMetaAttributes(complete: false);
			prog.ReportError("Error completing geometry for computations:");
			prog.ReportError(ex2.Message);
			result = false;
			ProjectData.ClearProjectError();
		}
		return result;
	}

	private void GeometryCompletionProblem(ProgressReporter prog)
	{
		WriteMetaAttributes(complete: false);
		prog.ReportMessage("LAYER=Error");
		prog.ReportProgress(0);
		prog.ReportProgress(-100);
	}

	private void WriteMetaAttributes(bool complete)
	{
		using H5Writer h5Writer = new H5Writer(SourceFilename);
		h5Writer.WriteAttribute("Geometry", "Complete Geometry", General.BooleanToString(complete));
		SharedData.HDFWriteSRS(h5Writer);
		try
		{
			if (!complete)
			{
				string location = Assembly.GetExecutingAssembly().Location;
				string directoryName = System.IO.Path.GetDirectoryName(location);
				if (Environment.Is64BitProcess)
				{
					directoryName = Directory.GetParent(directoryName).FullName;
				}
				if (File.Exists(location))
				{
					FileInfo fileInfo = new FileInfo(location);
					h5Writer.WriteAttribute("Geometry", System.IO.Path.GetFileName(location) + " Date", fileInfo.LastWriteTime.ToString());
				}
				string combine = System.IO.Path.Combine(directoryName, "Ras.exe");
				if (File.Exists(combine))
				{
					FileInfo fileInfo2 = new FileInfo(combine);
					h5Writer.WriteAttribute("Geometry", System.IO.Path.GetFileName(combine) + " Date", fileInfo2.LastWriteTime.ToString());
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

	public static bool GeometryFileComplete(string hdfFilename)
	{
		bool result;
		if (!File.Exists(hdfFilename))
		{
			result = false;
		}
		else
		{
			try
			{
				string textValue = "";
				using (H5Reader h5Reader = new H5Reader(hdfFilename))
				{
					if (h5Reader.ExistsAsAttribute("Geometry", "Complete Geometry"))
					{
						h5Reader.ReadAttributeScalar("Geometry", "Complete Geometry", ref textValue);
					}
				}
				result = General.StringToBoolean(textValue);
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				result = false;
				ProjectData.ClearProjectError();
			}
		}
		return result;
	}

	public static bool ProcessPolygonAssociations(H5Writer hw, string hdfBaseGroupName, List<Polygon> pgons, GeometryAssociation association, ParameterResampler resampler, Func<int, string> errMessageGenerator, ProgressReporter reporter)
	{
		if (association == null)
		{
			return true;
		}
		Layer layer = association.Layer;
		if (layer == null)
		{
			return true;
		}
		if (!layer.AllSourceFilesExist())
		{
			reporter.ReportError("Associated layer missing necessary files: " + association.AbsoluteFilename);
			return false;
		}
		if (resampler == null)
		{
			reporter.ReportError("Could not load data for associated layer " + layer.SourceFilename.InDoubleQuotes() + ". " + association.AssociationName + " parameters not written.");
			return false;
		}
		string hdfBaseGroupNameAssociationNameTerm = hdfBaseGroupName + "/" + association.AssociationName;
		hw.CreateGroup(hdfBaseGroupNameAssociationNameTerm);
		checked
		{
			float[][] floatValues = new float[resampler.ProfileCount - 1 + 1][];
			int count = resampler.ProfileCount - 1;
			for (int i = 0; i <= count; i++)
			{
				float[] floatValuesArray = new float[pgons.Count - 1 + 1];
				floatValues[i] = floatValuesArray;
			}
			Dictionary<int, float>[] floatByIntArray = new Dictionary<int, float>[pgons.Count - 1 + 1];
			int count2 = pgons.Count - 1;
			for (int j = 0; j <= count2; j++)
			{
				Polygon poly = pgons[j];
				Dictionary<int, float> classifierFractions = null;
				float[] perProfileWeightedAverage = null;
				if (!resampler.SamplePolygon(Converter.Convert(poly), 10000, out classifierFractions, out perProfileWeightedAverage))
				{
					reporter.ReportError(errMessageGenerator(j));
					return false;
				}
				int count3 = resampler.ProfileCount - 1;
				for (int k = 0; k <= count3; k++)
				{
					floatValues[k][j] = perProfileWeightedAverage[k];
				}
				floatByIntArray[j] = classifierFractions;
			}
			int count4 = resampler.ProfileCount - 1;
			for (int l = 0; l <= count4; l++)
			{
				string profileName = resampler.ProfileNames[l];
				hw.WriteArray(hdfBaseGroupNameAssociationNameTerm + "/" + profileName, floatValues[l]);
			}
			int[,] intValues = new int[pgons.Count - 1 + 1, 2];
			List<int> intList = new List<int>();
			List<float> floatList = new List<float>();
			int ordinal = 0;
			int count5 = pgons.Count - 1;
			for (int m = 0; m <= count5; m++)
			{
				Dictionary<int, float> floatByInt = floatByIntArray[m];
				intValues[m, 0] = ordinal;
				intValues[m, 1] = floatByInt.Count;
				ordinal += floatByInt.Count;
				foreach (KeyValuePair<int, float> localValue in floatByInt)
				{
					intList.Add(localValue.Key);
					floatList.Add(localValue.Value);
				}
			}
			hw.WriteArray(hdfBaseGroupNameAssociationNameTerm + "/Classification Info", intValues);
			hw.WriteArray(hdfBaseGroupNameAssociationNameTerm + "/Classification Value IDs", intList.ToArray());
			hw.WriteArray(hdfBaseGroupNameAssociationNameTerm + "/Classification Value Fractions", floatList.ToArray());
			association.SaveAssociation(hw, hdfBaseGroupNameAssociationNameTerm);
			return true;
		}
	}

	public void ValidateGeometry(bool forceShowErrors)
	{
		Errors.ClearErrors();
		RiverMap riverMap = CreateRiverMap();
		Stopwatch stopwatch = Stopwatch.StartNew();
		foreach (RiverMap.River river in riverMap.Rivers)
		{
			foreach (RiverMap.Reach reach in river.Reaches)
			{
				if (reach.Errors.Count > 0)
				{
					Errors.AddError(new GeometryErrorCollection(River, reach.ID, reach.Errors));
				}
			}
		}
		foreach (RiverMap.Junction junction in riverMap.Junctions)
		{
			if (junction.Errors.Count > 0)
			{
				Errors.AddError(new GeometryErrorCollection(Junctions, junction.ID, junction.Errors));
			}
		}
		foreach (RiverMap.CrossSection crossSection in riverMap.CrossSections)
		{
			if (crossSection.Errors.Count > 0)
			{
				Errors.AddError(new GeometryErrorCollection(XS, crossSection.ID, crossSection.Errors));
			}
		}
		foreach (RiverMap.Bankline bankline in riverMap.Banklines)
		{
			if (bankline.Errors.Count > 0)
			{
				Errors.AddError(new GeometryErrorCollection(BankLines, bankline.ID, bankline.Errors));
			}
		}
		foreach (RiverMap.FlowPathLine flowPathLine in riverMap.FlowPathLines)
		{
			if (flowPathLine.Errors.Count > 0)
			{
				Errors.AddError(new GeometryErrorCollection(FlowPathLines, flowPathLine.ID, flowPathLine.Errors));
			}
		}
		foreach (RiverMap.StorageArea storageArea in riverMap.StorageAreas)
		{
			if (storageArea.Errors.Count > 0)
			{
				Errors.AddError(new GeometryErrorCollection(StorageArea, storageArea.ID, storageArea.Errors));
			}
		}
		foreach (RiverMap.RiverStation riverStation in riverMap.RiverStations)
		{
			if (riverStation.Errors.Count > 0)
			{
				Errors.AddError(new GeometryErrorCollection(RiverStationing, riverStation.ID, riverStation.Errors));
			}
		}
		foreach (GeometryIntersectionErrorCollection intersectionError in riverMap.IntersectionErrors)
		{
			Errors.AddError(intersectionError);
		}
		if (forceShowErrors && !Errors.Checked)
		{
			Errors.Checked = true;
		}
		Errors.DrawNode();
		stopwatch.Stop();
		SharedData.RASMapperReplot();
	}

	public RiverMap CreateRiverMapWithWaitCursor(bool loadSpatialIntersectionData = true)
	{
		using (new WaitCursor(SharedData.RasMapper))
		{
			return CreateRiverMap(loadSpatialIntersectionData);
		}
	}

	public RiverMap CreateRiverMap(bool loadSpatialIntersectionData = true)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		RiverMap riverMap = new RiverMap();
		riverMap.HasSpatialData = loadSpatialIntersectionData;
		riverMap.Valid = true;
		checked
		{
			try
			{
				int count = _StorageArea.FeatureCount() - 1;
				int dupIdx;
				for (int i = 0; i <= count; i++)
				{
					RiverMap.StorageArea storageArea = new RiverMap.StorageArea();
					storageArea.ID = i;
					storageArea.Name = _StorageArea.GetFeatureName(i);
					riverMap.StorageAreas.Add(storageArea);
					RiverMap.D2Connection d2Connection = new RiverMap.D2Connection();
					d2Connection.Name = storageArea.Name;
					d2Connection.ID = storageArea.ID;
					riverMap.D2Connections.Add(d2Connection);
					Polygon polygon = StorageArea.Polygon(i);
					if (!polygon.IsValid())
					{
						storageArea.AddError("Invalid polygon.");
					}
					if (loadSpatialIntersectionData)
					{
						dupIdx = -1;
						if (polygon.HasConsecutiveIdenticalPoints(ref dupIdx))
						{
							storageArea.AddError("Polygon has duplicate points - consider filtering.");
						}
					}
				}
				dupIdx = _D2FlowArea.FeatureCount() - 1;
				int dupIdx2;
				for (int j = 0; j <= dupIdx; j++)
				{
					RiverMap.Mesh mesh = new RiverMap.Mesh();
					mesh.Name = _D2FlowArea.GetFeatureName(j);
					mesh.ID = j;
					MeshFV2D meshFV2D = _D2FlowArea.Meshes().ElementAtOrDefault(j);
					if (meshFV2D == null)
					{
						meshFV2D = MeshFV2D.NullMesh();
					}
					mesh.CellCount = meshFV2D.CellCount;
					mesh.NonVirtualCellCount = meshFV2D.NonVirtualCellCount;
					mesh.FaceCount = meshFV2D.FaceCount;
					mesh.FacePointCount = meshFV2D.FacePointCount;
					mesh.CellAverageSize = meshFV2D.CellAverageSize;
					if (!meshFV2D.IsComplete())
					{
						mesh.AddError(meshFV2D.StatusMessage);
					}
					riverMap.Meshes.Add(mesh);
					RiverMap.D2Connection d2Connection2 = new RiverMap.D2Connection();
					d2Connection2.Name = mesh.Name;
					d2Connection2.ID = mesh.ID;
					riverMap.D2Connections.Add(d2Connection2);
					Polygon polygon2 = MeshPerimeters.Polygon(j);
					if (!polygon2.IsValid())
					{
						mesh.AddError("Invalid perimeter polygon.");
					}
					if (loadSpatialIntersectionData)
					{
						dupIdx2 = -1;
						if (polygon2.HasConsecutiveIdenticalPoints(ref dupIdx2))
						{
							mesh.AddError("Polygon has duplicate points - consider filtering.");
						}
					}
				}
				dupIdx2 = _Junctions.FeatureCount() - 1;
				_Closure_0024__427_002D0 capturedLocals = default;
				for (int k = 0; k <= dupIdx2; k++)
				{
					capturedLocals = new _Closure_0024__427_002D0(capturedLocals);
					capturedLocals.newJxn = new RiverMap.Junction();
					capturedLocals.newJxn.Name = _Junctions.GetFeatureName(k);
					capturedLocals.newJxn.ID = k;
					if (riverMap.Junctions.Where(capturedLocals._Lambda_0024__0).Count() > 0)
					{
						capturedLocals.newJxn.AddError("Identical Junction name.");
					}
					riverMap.Junctions.Add(capturedLocals.newJxn);
				}
				int count2 = _River.FeatureCount() - 1;
				_Closure_0024__427_002D1 capturedLocals2 = default;
				int dupIdx3;
				for (int l = 0; l <= count2; l++)
				{
					capturedLocals2 = new _Closure_0024__427_002D1(capturedLocals2);
					capturedLocals2.rivname = _River.GetRiverName(l);
					capturedLocals2.reachname = _River.GetReachName(l);
					if (string.IsNullOrEmpty(capturedLocals2.rivname) || string.IsNullOrEmpty(capturedLocals2.reachname))
					{
						continue;
					}
					RiverMap.River river = riverMap.Rivers.FirstOrDefault(capturedLocals2._Lambda_0024__1);
					if (river == null)
					{
						river = new RiverMap.River();
						river.Name = capturedLocals2.rivname;
						riverMap.Rivers.Add(river);
					}
					Polyline polyline = River.Polyline(l);
					RiverMap.Reach reach = new RiverMap.Reach();
					reach.River = river;
					reach.ID = l;
					reach.Length = polyline.Length;
					reach.SegmentCount = polyline.SegmentCount();
					reach.Name = capturedLocals2.reachname;
					if (river.Reaches.FirstOrDefault(capturedLocals2._Lambda_0024__2) != null)
					{
						reach.AddError("Multiple identical Reaches on this River.");
					}
					if (!polyline.IsValid())
					{
						reach.AddError("Invalid polyline");
					}
					if (loadSpatialIntersectionData)
					{
						dupIdx3 = -1;
						if (polyline.HasConsecutiveIdenticalPoints(ref dupIdx3))
						{
							reach.AddError("Polyline has duplicate points - consider filtering.");
						}
					}
					river.Reaches.Add(reach);
					capturedLocals2.upstreamConnectionName = River.GetUpstreamName(l);
					if (_River.UpstreamIsJunction(l))
					{
						RiverMap.Junction junction = riverMap.Junctions.FirstOrDefault(capturedLocals2._Lambda_0024__3);
						if (junction == null)
						{
							reach.AddError("Missing upstream Junction " + capturedLocals2.upstreamConnectionName.InSingleQuotes());
							reach.UpstreamConnection = new RiverMap.MissingConnection();
						}
						else
						{
							reach.UpstreamConnection = junction;
							junction.DSReaches.Add(reach);
						}
					}
					else if (River.UpstreamIs2DArea(l) || River.UpstreamIsStorageArea(l))
					{
						RiverMap.D2Connection d2Connection3 = riverMap.D2Connections.FirstOrDefault(capturedLocals2._Lambda_0024__4);
						if (d2Connection3 == null)
						{
							reach.AddError("Missing upstream Mesh/SA " + capturedLocals2.upstreamConnectionName.InSingleQuotes());
							reach.UpstreamConnection = new RiverMap.MissingConnection();
						}
						else
						{
							reach.UpstreamConnection = d2Connection3;
							d2Connection3.DSReaches.Add(reach);
						}
					}
					else if (River.UpstreamIsExternal(l))
					{
						reach.UpstreamConnection = new RiverMap.ExternalConnection();
					}
					else
					{
						reach.UpstreamConnection = new RiverMap.MissingConnection();
					}
					capturedLocals2.downstreamConnectionName = River.GetDownstreamName(l);
					if (River.DownstreamIsJunction(l))
					{
						RiverMap.Junction junction2 = riverMap.Junctions.FirstOrDefault(capturedLocals2._Lambda_0024__5);
						if (junction2 == null)
						{
							reach.AddError("Missing downstream Junction " + capturedLocals2.downstreamConnectionName.InSingleQuotes());
							reach.DownstreamConnection = new RiverMap.MissingConnection();
						}
						else
						{
							reach.DownstreamConnection = junction2;
							junction2.USReaches.Add(reach);
						}
					}
					else if (River.DownstreamIs2DArea(l) || River.DownstreamIsStorageArea(l))
					{
						RiverMap.D2Connection d2Connection4 = riverMap.D2Connections.FirstOrDefault(capturedLocals2._Lambda_0024__6);
						if (d2Connection4 == null)
						{
							reach.AddError("Missing downstream Mesh/SA " + capturedLocals2.downstreamConnectionName.InSingleQuotes());
							reach.DownstreamConnection = new RiverMap.MissingConnection();
						}
						else
						{
							reach.DownstreamConnection = d2Connection4;
							d2Connection4.USReaches.Add(reach);
						}
					}
					else if (River.DownstreamIsExternal(l))
					{
						reach.DownstreamConnection = new RiverMap.ExternalConnection();
					}
					else
					{
						reach.DownstreamConnection = new RiverMap.MissingConnection();
					}
				}
				new List<double[]>();
				foreach (RiverMap.River river3 in riverMap.Rivers)
				{
					List<RiverMap.Reach> reachesDownstreamToUpstream = river3.GetReachesDownstreamToUpstream();
					if (reachesDownstreamToUpstream.Count != river3.Reaches.Count)
					{
						foreach (RiverMap.Reach reach3 in river3.Reaches)
						{
							if (!reachesDownstreamToUpstream.Contains(reach3))
							{
								reach3.AddError("Disconnected from river-line " + river3.Name.InSingleQuotes());
							}
						}
					}
					if (!loadSpatialIntersectionData)
					{
						continue;
					}
					river3.CombinedPolyline = River.GetCombinedRiverPolyline(river3.Name, riverMap);
					river3.UserStationing = UserStationingComputer.LoadRiverStationing(this, river3.Name, null, riverMap);
					river3.CumulativeSegLengths = river3.CombinedPolyline.CumulativeSegmentLengths();
					foreach (RiverMap.Reach reach4 in river3.Reaches)
					{
						reach4.CumulativeSegLengths = River.Polyline(reach4.ID).CumulativeSegmentLengths();
					}
				}
				_Closure_0024__427_002D2 capturedLocals3 = default;
				foreach (RiverMap.Junction junction3 in riverMap.Junctions)
				{
					if (junction3.USReaches.Count == 0 && junction3.DSReaches.Count == 0)
					{
						junction3.AddError("No Reaches connected.");
					}
					else if (junction3.USReaches.Count == 0)
					{
						junction3.AddError("No upstream Reaches connected.");
					}
					else if (junction3.DSReaches.Count == 0)
					{
						junction3.AddError("No downstream Reaches connected.");
					}
					else if (junction3.USReaches.Count > 1 && junction3.DSReaches.Count > 1)
					{
						junction3.AddError("Junction has multiple upstream and downstream Reaches.");
					}
					else if (junction3.USReaches.Count != 1 || junction3.DSReaches.Count != 1)
					{
						using List<RiverMap.Reach>.Enumerator usReaches = junction3.USReaches.GetEnumerator();
						while (usReaches.MoveNext())
						{
							capturedLocals3 = new _Closure_0024__427_002D2(capturedLocals3);
							capturedLocals3.rch = usReaches.Current;
							if (junction3.USReaches.Where(capturedLocals3._Lambda_0024__7).Count() > 1)
							{
								junction3.AddError("Multiple upstream connections to Rivers with the same name - " + capturedLocals3.rch.River.Name.InSingleQuotes());
							}
							if (junction3.DSReaches.Where(capturedLocals3._Lambda_0024__8).Count() > 1)
							{
								junction3.AddError("Multiple downstream connections to Rivers with the same name - " + capturedLocals3.rch.River.Name.InSingleQuotes());
							}
						}
					}
					else
					{
						junction3.AddError("Junction needs at least 3 Reaches.");
					}
				}
				dupIdx3 = _BankLines.FeatureCount() - 1;
				int dupIdx4;
				for (int m = 0; m <= dupIdx3; m++)
				{
					Polyline polyline2 = _BankLines.Polyline(m);
					RiverMap.Bankline bankline = new RiverMap.Bankline();
					bankline.ID = m;
					bankline.NPoints = polyline2.Count;
					bankline.Length = polyline2.Length;
					riverMap.Banklines.Add(bankline);
					if (!polyline2.IsValid())
					{
						bankline.AddError("Invalid polyline");
					}
					if (loadSpatialIntersectionData)
					{
						dupIdx4 = -1;
						if (polyline2.HasConsecutiveIdenticalPoints(ref dupIdx4))
						{
							bankline.AddError("Polyline has duplicate points - consider filtering.");
						}
					}
				}
				dupIdx4 = XS.FeatureCount() - 1;
				int dupIdx5;
				for (int n = 0; n <= dupIdx4; n++)
				{
					string riverName = IHasRRRSLayerExtensions.GetRiverName(XS, n);
					string reachName = IHasRRRSLayerExtensions.GetReachName(XS, n);
					string riverStationName = IHasRRRSLayerExtensions.GetRiverStationName(XS, n);
					XS.GetNodeName(n);
					string nodeDescription = XS.GetNodeDescription(n);
					RiverMap.CrossSection crossSection = new RiverMap.CrossSection();
					crossSection.ID = n;
					crossSection.Name = riverStationName;
					crossSection.Description = nodeDescription;
					float leftBankStation = XS.GetLeftBankStation(n);
					float rightBankStation = XS.GetRightBankStation(n);
					float leftOverbankLength = XS.GetLeftOverbankLength(n);
					float rightOverbankLength = XS.GetRightOverbankLength(n);
					float channelLength = XS.GetChannelLength(n);
					crossSection.LeftBankStation = leftBankStation;
					crossSection.RightBankStation = rightBankStation;
					crossSection.LeftOBLength = leftOverbankLength;
					crossSection.RightOBLength = rightOverbankLength;
					crossSection.ChannelLength = channelLength;
					riverMap.CrossSections.Add(crossSection);
					if (float.IsNaN(leftBankStation))
					{
						crossSection.AddError("Left Bank Station not defined.");
					}
					if (float.IsNaN(rightBankStation))
					{
						crossSection.AddError("Right Bank Station not defined.");
					}
					if (string.IsNullOrEmpty(riverStationName))
					{
						riverStationName = "";
					}
					if (riverStationName.EndsWith("*"))
					{
						riverStationName = riverStationName.Remove(riverStationName.Length - 1);
					}
					if (!double.TryParse(riverStationName, out crossSection.RiverStationUser))
					{
						crossSection.RiverStationUser = double.NaN;
						crossSection.AddError("Incorrectly named XS");
					}
					Polyline polyline3 = XS.Polyline(n);
					if (!polyline3.IsValid())
					{
						crossSection.AddError("Invalid polyline");
					}
					if (loadSpatialIntersectionData)
					{
						IProfileInfo profileProperties = XS.GetProfileProperties(n);
						double length = polyline3.Length;
						double lastStation = XS.GetProfileProperties(n).LastStation;
						double absoluteLengthAboveLastStationOverLength = System.Math.Abs((length - lastStation) / length);
						dupIdx5 = -1;
						if (polyline3.HasConsecutiveIdenticalPoints(ref dupIdx5))
						{
							crossSection.AddError("Polyline has duplicate points - consider filtering.");
						}
						if (System.Math.Abs(absoluteLengthAboveLastStationOverLength) > 0.01)
						{
							crossSection.AddError("XS Profile length is different than Polyline length (>1%)");
						}
						if (!float.IsNaN(leftBankStation) && !float.IsNaN(rightBankStation))
						{
							List<PolylineIntersection> polylineIntersections = (from pi in XSIntersectBanks(n)
								orderby pi.BSegmentIndex, pi.BFraction
								select pi).ToList();
							if (polylineIntersections.Count < 2)
							{
								crossSection.AddError("XS intersects < 2 banklines");
							}
							else if (polylineIntersections.Count > 2)
							{
								crossSection.AddError("XS intersects > 2 banklines");
							}
							else
							{
								PolylineIntersection polylineIntersection = polylineIntersections[0];
								PolylineIntersection polylineIntersection2 = polylineIntersections[1];
								double distanceAlong = polyline3.DistanceAlong(polylineIntersection.BPointOnPolyline);
								double distanceAlong2 = polyline3.DistanceAlong(polylineIntersection2.BPointOnPolyline);
								double leftBankStationAboveFirstStation = leftBankStation - profileProperties.FirstStation;
								double rightBankStationAboveFirstStation = rightBankStation - profileProperties.FirstStation;
								double systemDistanceAlongTerm = System.Math.Abs((leftBankStationAboveFirstStation - distanceAlong) / distanceAlong);
								double systemDistanceAlong2Term = System.Math.Abs((rightBankStationAboveFirstStation - distanceAlong2) / distanceAlong2);
								if (System.Math.Abs(systemDistanceAlongTerm) > 0.01)
								{
									crossSection.AddError("Left Bank Station point is different than the bankline intersection (>1%)");
								}
								if (System.Math.Abs(systemDistanceAlong2Term) > 0.01)
								{
									crossSection.AddError("Right Bank Station point is different than the bankline intersection (>1%)");
								}
							}
						}
					}
					if (string.IsNullOrEmpty(riverName) || string.IsNullOrEmpty(reachName))
					{
						crossSection.Reach = null;
						crossSection.RiverStationUser = double.MinValue;
						crossSection.ChannelStation = double.MinValue;
						crossSection.AddError("No associated River Reach");
						continue;
					}
					RiverMap.Reach reach2 = riverMap.GetReach(riverName, reachName);
					if (reach2 == null)
					{
						crossSection.AddError("Associated Reach not found.");
						continue;
					}
					crossSection.Reach = reach2;
					reach2.CrossSections.Add(crossSection);
					if (!loadSpatialIntersectionData)
					{
						continue;
					}
					List<PolylineIntersection> xsIntersectRiver = XSIntersectRiver(n);
					if (xsIntersectRiver.Count != 1)
					{
						crossSection.AddError("XS must intersect exactly one Reach.");
						continue;
					}
					PolylineIntersection polylineIntersection3 = xsIntersectRiver.First();
					int aPolylineIndex = polylineIntersection3.APolylineIndex;
					if (reach2 != null && reach2.ID != aPolylineIndex)
					{
						crossSection.AddError("XS doesn't intersect the associated Reach.");
						continue;
					}
					Polyline polyline4 = River.Polyline(aPolylineIndex);
					double channelStation = XS.Polyline(polylineIntersection3.BPolylineIndex).DistanceAlong(polylineIntersection3.BPointOnPolyline);
					crossSection.ChannelStation = channelStation;
					if (reach2 != null)
					{
						double distanceFromTopOfReach = polyline4.DistanceAlongFast(polylineIntersection3.APointOnPolyline, ref reach2.CumulativeSegLengths);
						crossSection.DistanceFromTopOfReach = distanceFromTopOfReach;
						Polyline combinedPolyline = reach2.River.CombinedPolyline;
						if (combinedPolyline == null || !combinedPolyline.IsValid())
						{
							crossSection.RiverStationComputed_WithRMM = 0.0;
						}
						else
						{
							int reachStartingSegmentIndex = reach2.River.ReachStartingSegmentIndex(reach2);
							if (reachStartingSegmentIndex != -1)
							{
								PointOnPolyline pointOnPolyline = new PointOnPolyline(polylineIntersection3.APointOnPolyline.PointM, reachStartingSegmentIndex + polylineIntersection3.ASegmentIndex, polylineIntersection3.AFraction);
								crossSection.RiverStationComputed_WithRMM = RASXS.ComputeRSUnrounded(combinedPolyline, pointOnPolyline, reach2.River.UserStationing, reach2.River.CumulativeSegLengths);
								crossSection.DistanceFromEndOfRiver = combinedPolyline.Length - combinedPolyline.DistanceAlongFast(pointOnPolyline, ref reach2.River.CumulativeSegLengths);
							}
							else
							{
								crossSection.RiverStationComputed_WithRMM = RASXS.ComputeRSUnrounded(polyline4, polylineIntersection3.APointOnPolyline, null, reach2.CumulativeSegLengths);
								crossSection.DistanceFromEndOfRiver = reach2.Length - crossSection.DistanceFromTopOfReach;
							}
						}
					}
					if (RASXS.IsBackwards(XS, n))
					{
						crossSection.AddError("XS is reversed (must draw left->right downstream)");
					}
				}
				dupIdx5 = FlowPathLines.FeatureCount() - 1;
				for (int id = 0; id <= dupIdx5; id++)
				{
					Polyline polyline5 = FlowPathLines.Polyline(id);
					RiverMap.FlowPathLine flowPathLine = new RiverMap.FlowPathLine();
					flowPathLine.ID = id;
					flowPathLine.NPoints = polyline5.Count;
					flowPathLine.Length = polyline5.Length;
					if (!polyline5.IsValid())
					{
						flowPathLine.AddError("Invalid polyline");
					}
					if (loadSpatialIntersectionData)
					{
						int dupIdx6 = -1;
						if (polyline5.HasConsecutiveIdenticalPoints(ref dupIdx6))
						{
							flowPathLine.AddError("Polyline has duplicate points - consider filtering.");
						}
					}
					riverMap.FlowPathLines.Add(flowPathLine);
				}
				foreach (RiverMap.River river4 in riverMap.Rivers)
				{
					foreach (RiverMap.Reach reach5 in river4.Reaches)
					{
						reach5.SortCrossSections();
						if (reach5.CrossSections.Count < 2)
						{
							reach5.AddError("Each Reach must have at least 2 XSs.");
						}
						int dupIdx6 = reach5.CrossSections.Count - 1;
						for (int intervalIndex = 0; intervalIndex <= dupIdx6; intervalIndex++)
						{
							RiverMap.CrossSection crossSection2 = reach5.CrossSections[intervalIndex];
							float leftOBLength = crossSection2.LeftOBLength;
							float rightOBLength = crossSection2.RightOBLength;
							float channelLength2 = crossSection2.ChannelLength;
							if (intervalIndex != reach5.CrossSections.Count - 1)
							{
								if (float.IsNaN(channelLength2) || channelLength2 <= 0f)
								{
									crossSection2.AddError("Channel Reach Length not defined.");
								}
								if (float.IsNaN(leftOBLength) || leftOBLength <= 0f)
								{
									crossSection2.AddError("Left Reach Length not defined.");
								}
								if (float.IsNaN(rightOBLength) || rightOBLength <= 0f)
								{
									crossSection2.AddError("Right Reach Length not defined.");
								}
							}
						}
					}
				}
				foreach (RiverMap.River river5 in riverMap.Rivers)
				{
					double maxValue = double.MaxValue;
					foreach (RiverMap.Reach reachesUpstreamToDownstream in river5.GetReachesUpstreamToDownstream())
					{
						foreach (RiverMap.CrossSection crossSection3 in reachesUpstreamToDownstream.CrossSections)
						{
							double riverStationUser = crossSection3.RiverStationUser;
							if (!double.IsNaN(riverStationUser))
							{
								if (riverStationUser >= maxValue)
								{
									crossSection3.AddError("XS naming error - River Station out of order.");
								}
								maxValue = riverStationUser;
							}
						}
					}
				}
				foreach (RiverMap.River river6 in riverMap.Rivers)
				{
					List<float> floatList = new List<float>();
					string name = river6.Name;
					int count3 = RiverStationing.FeatureCount() - 1;
					for (int id2 = 0; id2 <= count3; id2++)
					{
						string river2 = RiverStationing.GetRiver(id2);
						RiverStationing.GetReach(id2);
						float userStation = RiverStationing.GetUserStation(id2);
						if (Operators.CompareString(river2, name, TextCompare: false) != 0)
						{
							continue;
						}
						RiverMap.RiverStation riverStation = new RiverMap.RiverStation(name, userStation);
						riverStation.ID = id2;
						riverStation.Name = "";
						riverMap.RiverStations.Add(riverStation);
						foreach (float computed in floatList)
						{
							float computed2 = computed;
							if ((double)System.Math.Abs(userStation - computed2) < 0.001)
							{
								riverStation.AddError("Duplicate User Station Values (within .001)");
							}
						}
						floatList.Add(userStation);
					}
				}
				int count4 = Structures.FeatureCount() - 1;
				for (int id3 = 0; id3 <= count4; id3++)
				{
					RiverMap.StructureElement structureElement = new RiverMap.StructureElement();
					structureElement.ID = id3;
					structureElement.Name = Structures.GetFeatureName(id3);
					riverMap.Structures.Add(structureElement);
				}
				if (loadSpatialIntersectionData)
				{
					foreach (RiverMap.FlowPathLine flowPathLine2 in riverMap.FlowPathLines)
					{
						Polyline polyline6 = FlowPathLines.Polyline(flowPathLine2.ID);
						if (polyline6 == null || polyline6.Count <= 1)
						{
							continue;
						}
						List<PolylineIntersection> spatialIntersect = BankLines.SpatialIntersect(polyline6);
						if (spatialIntersect.Count <= 0)
						{
							continue;
						}
						IEnumerable<IGrouping<int, PolylineIntersection>> enumerable = from pl in spatialIntersect
							group pl by pl.APolylineIndex;
						foreach (IGrouping<int, PolylineIntersection> enumerable2 in enumerable)
						{
							riverMap.AddIntersectionError(BankLines, FlowPathLines, enumerable2.Key, flowPathLine2.ID, new PointMs(enumerable2.Select((_Closure_0024__._0024I427_002D12 == null) ? (_Closure_0024__._0024I427_002D12 = [SpecialName] (PolylineIntersection pi) => pi.Point) : _Closure_0024__._0024I427_002D12)), new GeometryError("Bankline intersected FlowPathLine"));
						}
					}
					foreach (RiverMap.CrossSection crossSection4 in riverMap.CrossSections)
					{
						List<PolylineIntersection> xsIntersectFlowPaths = XSIntersectFlowPaths(crossSection4.ID);
						if (xsIntersectFlowPaths.Count > 2)
						{
							crossSection4.AddError("More than 2 Flow Paths intersect XS.");
						}
						else if (xsIntersectFlowPaths.Count == 1)
						{
							crossSection4.AddError("Only 1 Flow Path intersects XS.");
						}
					}
				}
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				riverMap.Valid = false;
				riverMap.InvalidMessage = ex2.Message;
				ProjectData.ClearProjectError();
			}
			stopwatch.Stop();
			_ = SharedData.IsTestingUtility;
			return riverMap;
		}
	}

	public void GetInvertLine(int rivID, ref PointMs xsIntersectionPoints, ref Profile invertLine)
	{
		Polyline polyline = River.Polyline(rivID);
		List<PolylineIntersection> riverXSIntersections = RiverXSIntersections(rivID);
		RASXS xS = XS;
		invertLine = new Profile("Station", "Elevation");
		xsIntersectionPoints = new PointMs();
		foreach (PolylineIntersection riverXSIntersection in riverXSIntersections)
		{
			xsIntersectionPoints.Add(riverXSIntersection.Point);
			int bPolylineIndex = riverXSIntersection.BPolylineIndex;
			float minChannelElevation = xS.GetMinChannelElevation(bPolylineIndex);
			double x = polyline.DistanceAlong(riverXSIntersection.APointOnPolyline);
			invertLine.Add(x, minChannelElevation);
		}
	}

	public PointMs GetRiverXSIntersectionPoints(int rivIndex)
	{
		PointMs pointMs = new PointMs();
		pointMs.AddRange((from pli in RiverXSIntersections(rivIndex)
			select pli.Point).ToList());
		return pointMs;
	}

	public List<PointM> GetAllXSRiverIntersectionPoints()
	{
		return (from piLst in _XSsIntersectsRivers.GetAllXSIntersections()
			select piLst.IsNullOrEmpty() ? PointM.Undefined() : piLst.First().Point).ToList();
	}

	public List<PolylineIntersection>[] GetAllXSRiverIntersections()
	{
		return _XSsIntersectsRivers.GetAllXSIntersections();
	}

	public int FindNearestCrossSection(int rivIndex, PointOnPolyline rivPoP)
	{
		List<PolylineIntersection> riverXSIntersections = RiverXSIntersections(rivIndex);
		if (riverXSIntersections.IsNullOrEmpty())
		{
			return -1;
		}
		Polyline polyline = River.Polyline(rivIndex);
		if (!Polyline.IsValidPolyline(polyline))
		{
			return -1;
		}
		float distanceAlong = (float)polyline.DistanceAlong(rivPoP);
		List<PolylineIntersection> riverXSIntersectionsList = riverXSIntersections;
		List<PointOnPolyline> pts = riverXSIntersectionsList.Select([SpecialName] (PolylineIntersection pli) => pli.APointOnPolyline).ToList();
		float[] distancesAlongSorted = polyline.DistancesAlongSorted(pts);
		double maxValue = double.MaxValue;
		int result = -1;
		checked
		{
			int count = distancesAlongSorted.Count() - 1;
			for (int intervalIndex = 0; intervalIndex <= count; intervalIndex++)
			{
				float maxValue2 = System.Math.Abs(distancesAlongSorted[intervalIndex] - distanceAlong);
				if ((double)maxValue2 < maxValue)
				{
					maxValue = maxValue2;
					result = riverXSIntersectionsList[intervalIndex].BPolylineIndex;
				}
			}
			return result;
		}
	}

	public List<PolylineIntersection> RiverXSIntersections(int reachID)
	{
		return _XSsIntersectsRivers.LSIntersections(reachID);
	}

	public List<PolylineIntersection> XSIntersectRiver(int xsid)
	{
		return _XSsIntersectsRivers.XSIntersections(xsid);
	}

	public List<PolylineIntersection> StructureIntersectRiver(int fid)
	{
		return _StructuresIntersectsRivers.XSIntersections(fid);
	}

	public List<PolylineIntersection> UnknownIntersectRiver<T>(T layer, int fid) where T : PolylineFeatureLayer
,IHasRRRSLayer	{
		if (layer is RASXS)
		{
			return XSIntersectRiver(fid);
		}
		if (layer is StructureLayer)
		{
			return StructureIntersectRiver(fid);
		}
		throw new NotImplementedException();
	}

	public List<PolylineIntersection> XSIntersectBanks(int xsid)
	{
		return _XSsIntersectsBanks.XSIntersections(xsid);
	}

	public List<PolylineIntersection> BanklinesIntersectXS(int bl)
	{
		return _XSsIntersectsBanks.LSIntersections(bl);
	}

	public List<PolylineIntersection> XSIntersectEdges(int xsid)
	{
		return _XSsIntersectsEdges.XSIntersections(xsid);
	}

	public List<PolylineIntersection> XSIntersectFlowPaths(int xsid)
	{
		return _XSsIntersectsFlowPaths.XSIntersections(xsid);
	}

	public List<PolylineIntersection> FlowPathsIntersectXS(int fpl)
	{
		return _XSsIntersectsFlowPaths.LSIntersections(fpl);
	}

	public void EnforceBanklinesOnCrossSections()
	{
		checked
		{
			using (new WaitCursor(SharedData.RasMapper))
			{
				SharedData.ReportMessage("Computing Bank Stations...");
				int count = XS.FeatureCount() - 1;
				for (int i = 0; i <= count; i++)
				{
					EnforceBanklinesOnCrossSection(i);
				}
				SharedData.ReportMessage("Computing Bank Stations... done", replacePreviousIfPossible: true);
			}
		}
	}

	public void EnforceBanklinesOnCrossSections(IList<int> xsids)
	{
		foreach (int xsid in xsids)
		{
			EnforceBanklinesOnCrossSection(xsid);
		}
	}

	public void EnforceBanklinesOnCrossSection(int xsid)
	{
		List<PolylineIntersection> xsIntersectBanks = XSIntersectBanks(xsid);
		if (xsIntersectBanks.Count == 2)
		{
			Polyline polyline = XS.Polyline(xsid);
			PolylineIntersection polylineIntersection = xsIntersectBanks[0];
			PolylineIntersection polylineIntersection2 = xsIntersectBanks[1];
			PointOnPolyline bPointOnPolyline = polylineIntersection.BPointOnPolyline;
			PointOnPolyline bPointOnPolyline2 = polylineIntersection2.BPointOnPolyline;
			double systemDistanceAlongTerm = System.Math.Round(polyline.DistanceAlong(bPointOnPolyline), SharedData.HorizontalDecimalPlaces);
			double systemDistanceAlongTerm2 = System.Math.Round(polyline.DistanceAlong(bPointOnPolyline2), SharedData.HorizontalDecimalPlaces);
			if (systemDistanceAlongTerm < systemDistanceAlongTerm2)
			{
				XS.SetBankStations(xsid, (float)systemDistanceAlongTerm, (float)systemDistanceAlongTerm2);
			}
			else
			{
				XS.SetBankStations(xsid, (float)systemDistanceAlongTerm2, (float)systemDistanceAlongTerm);
			}
		}
	}

	public void ComputeAllIneffectiveAreasForXSs(bool forceElevUpdate)
	{
		using (new WaitCursor(SharedData.RasMapper))
		{
			SharedData.ReportMessage("Computing Ineffective Flow Areas...");
			List<int> xsids = Enumerable.Range(0, XS.FeatureCount()).ToList();
			ComputeIneffectiveAreasForXSs(xsids, forceElevUpdate);
			SharedData.ReportMessage("Computing Ineffective Flow Areas... done", replacePreviousIfPossible: true);
		}
	}

	public void ComputeIneffectiveAreasForXSs(IList<int> xsids, bool forceElevUpdate)
	{
		foreach (int xsid in xsids)
		{
			ComputeIneffectiveAreasForXS(xsid, forceElevUpdate);
		}
	}

	public void ComputeIneffectiveAreasForXS(int xsID, bool forceElevUpdate)
	{
		List<RASXS.IneffectiveBlock> ineffectiveBlocks = new List<RASXS.IneffectiveBlock>();
		checked
		{
			int count = _IneffectiveFlowAreas.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				ineffectiveBlocks.AddRange(ComputeIneffectiveBlocks(xsID, i, forceElevUpdate));
			}
			_XS.SetIneffectives(xsID, ineffectiveBlocks);
			SharedData.RASMapperReplot();
		}
	}

	private List<RASXS.IneffectiveBlock> ComputeIneffectiveBlocks(int xsID, int fid, bool forceElevUpdate)
	{
		List<RASXS.IneffectiveBlock> ineffectiveBlocks = new List<RASXS.IneffectiveBlock>();
		List<Tuple<PointOnPolyline, PointOnPolyline>> entryExitPoints = _IneffectiveFlowAreas.Polygon(fid).EntryExitPoints(_XS.Polyline(xsID));
		checked
		{
			float station = default;
			float station2 = default;
			foreach (Tuple<PointOnPolyline, PointOnPolyline> entryExitPoint in entryExitPoints)
			{
				GetStation1AndStation2UsingXS(entryExitPoint, xsID, ref station, ref station2);
				float naN = float.NaN;
				if (forceElevUpdate)
				{
					naN = _IneffectiveFlowAreas.GetElevation(fid);
				}
				else
				{
					List<RASXS.IneffectiveBlock> ineffectives = XS.GetIneffectives(xsID);
					if (ineffectives != null)
					{
						int count = ineffectives.Count - 1;
						for (int i = 0; i <= count; i++)
						{
							RASXS.IneffectiveBlock ineffectiveBlock = ineffectives[i];
							float leftStation = ineffectiveBlock.LeftStation;
							float rightStation = ineffectiveBlock.RightStation;
							if ((leftStation >= station && leftStation < station2) || (rightStation >= station && rightStation < station2))
							{
								naN = ineffectiveBlock.Elevation;
								break;
							}
						}
					}
					if (float.IsNaN(naN))
					{
						naN = _IneffectiveFlowAreas.GetElevation(fid);
					}
				}
				ineffectiveBlocks.Add(new RASXS.IneffectiveBlock(station, station2, naN, perm: true));
			}
			return ineffectiveBlocks;
		}
	}

	internal void GetStation1AndStation2UsingXS(Tuple<PointOnPolyline, PointOnPolyline> containedSubsegment, int xsid, ref float station1, ref float station2)
	{
		double station12 = _XS.Polyline(xsid).DistanceAlong(containedSubsegment.Item1);
		double station12OverLength = station12 / _XS.Polyline(xsid).Length;
		station1 = _XS.ComputeXSStationFromFraction(xsid, (float)station12OverLength);
		double station22 = _XS.Polyline(xsid).DistanceAlong(containedSubsegment.Item2);
		double station22OverLength = station22 / _XS.Polyline(xsid).Length;
		station2 = _XS.ComputeXSStationFromFraction(xsid, (float)station22OverLength);
		if (!(System.Math.Abs(station12 - (double)station1) > 0.01))
		{
			station1 = (float)station12;
		}
		if (!(System.Math.Abs(station22 - (double)station2) > 0.01))
		{
			station2 = (float)station22;
		}
		if (station1 > station2)
		{
			float station13 = station1;
			station1 = station2;
			station2 = station13;
		}
	}

	public void ComputeAllIneffectiveAreasIntersectingXS(IList<int> fids, bool forceElevUpdate)
	{
		HashSet<int> xSsThatIntersectObstructedAreas = GetXSsThatIntersectObstructedAreas(IneffectiveFlowAreas, fids);
		ComputeIneffectiveAreasForXSs(xSsThatIntersectObstructedAreas.ToList(), forceElevUpdate);
	}

	public void ComputeAllBlockedObstructionsIntersectingXS(IList<int> fids, bool forceElevUpdate)
	{
		HashSet<int> xSsThatIntersectObstructedAreas = GetXSsThatIntersectObstructedAreas(BlockedObstructions, fids);
		ComputeBlockedObstructionsForXSs(xSsThatIntersectObstructedAreas.ToList(), forceElevUpdate);
	}

	private HashSet<int> GetXSsThatIntersectObstructedAreas(ObstructionAreaLayer layer, IList<int> fids)
	{
		SpatialIndex<Tuple<int, int>> segmentSpatialIndex = XS.GetSegmentSpatialIndex();
		HashSet<int> xSsThatIntersectObstructedAreas = new HashSet<int>();
		HashSet<int> intSet = new HashSet<int>();
		if (segmentSpatialIndex != null)
		{
			foreach (int fid in fids)
			{
				List<Tuple<int, int>> elements = segmentSpatialIndex.GetElements(layer.Polygon(fid).GetExtent());
				foreach (Tuple<int, int> element in elements)
				{
					if (!intSet.Contains(element.Item1) && layer.Polygon(fid).Intersects(XS.Polyline(element.Item1)))
					{
						xSsThatIntersectObstructedAreas.Add(element.Item1);
					}
				}
			}
		}
		return xSsThatIntersectObstructedAreas;
	}

	public void ComputeAllIneffectiveAreas(bool forceElevUpdate)
	{
		using (new WaitCursor(SharedData.RasMapper))
		{
			SharedData.ReportMessage("Computing Ineffective Flow Areas...");
			List<int> fids = Enumerable.Range(0, IneffectiveFlowAreas.FeatureCount()).ToList();
			ComputeIneffectiveAreasUsingIAFIDs(fids, forceElevUpdate);
			SharedData.ReportMessage("Computing Ineffective Flow Areas... done", replacePreviousIfPossible: true);
		}
	}

	public void ComputeIneffectiveAreasUsingIAFIDs(IList<int> fids, bool forceElevUpdate)
	{
		Dictionary<int, List<RASXS.IneffectiveBlock>> xsidToIneffectiveBlock = new Dictionary<int, List<RASXS.IneffectiveBlock>>();
		foreach (int fid in fids)
		{
			ComputeIneffectiveAreaUsingIAFIDInternal(fid, forceElevUpdate, ref xsidToIneffectiveBlock);
		}
		foreach (KeyValuePair<int, List<RASXS.IneffectiveBlock>> xsidToIneffectiveBlock2 in xsidToIneffectiveBlock)
		{
			XS.SetIneffectives(xsidToIneffectiveBlock2.Key, xsidToIneffectiveBlock2.Value);
		}
		SharedData.RASMapperReplot();
	}

	public void ComputeIneffectiveAreasUsingIAFID(int fid, bool forceElevUpdate)
	{
		List<int> intList = new List<int>();
		intList.Add(fid);
		ComputeIneffectiveAreasUsingIAFIDs(intList, forceElevUpdate);
	}

	private void ComputeIneffectiveAreaUsingIAFIDInternal(int fid, bool forceElevUpdate, ref Dictionary<int, List<RASXS.IneffectiveBlock>> xsidToIneffectiveBlock)
	{
		List<Tuple<int, int>> elements = XS.GetSegmentSpatialIndex().GetElements(IneffectiveFlowAreas.Polygon(fid).GetExtent());
		HashSet<int> intSet = new HashSet<int>();
		if (xsidToIneffectiveBlock == null)
		{
			xsidToIneffectiveBlock = new Dictionary<int, List<RASXS.IneffectiveBlock>>();
		}
		checked
		{
			foreach (Tuple<int, int> element in elements)
			{
				if (intSet.Contains(element.Item1))
				{
					continue;
				}
				List<RASXS.IneffectiveBlock> ineffectiveBlocks = null;
				List<RASXS.IneffectiveBlock> ineffectiveBlocksList = new List<RASXS.IneffectiveBlock>();
				int count = IneffectiveFlowAreas.FeatureCount() - 1;
				for (int i = 0; i <= count; i++)
				{
					intSet.Add(element.Item1);
					List<RASXS.IneffectiveBlock> ineffectiveBlocks2 = ComputeIneffectiveBlocks(element.Item1, i, forceElevUpdate);
					if (i == fid)
					{
						ineffectiveBlocks = ineffectiveBlocks2;
					}
					else
					{
						ineffectiveBlocksList.AddRange(ineffectiveBlocks2);
					}
				}
				List<RASXS.IneffectiveBlock> ineffectives = XS.GetIneffectives(element.Item1);
				List<RASXS.IneffectiveBlock> ineffectiveBlocks3 = ineffectiveBlocksList;
				if (ineffectives != null)
				{
					ineffectiveBlocks3 = new List<RASXS.IneffectiveBlock>();
					HashSet<RASXS.IneffectiveBlock> ineffectiveBlocksSet = new HashSet<RASXS.IneffectiveBlock>();
					int count2 = ineffectives.Count - 1;
					for (int j = 0; j <= count2; j++)
					{
						int count3 = ineffectiveBlocksList.Count - 1;
						for (int k = 0; k <= count3; k++)
						{
							if (!ineffectiveBlocksSet.Contains(ineffectiveBlocksList[k]) && (double)System.Math.Abs(ineffectives[j].LeftStation - ineffectiveBlocksList[k].LeftStation) < 0.01 && (double)System.Math.Abs(ineffectives[j].RightStation - ineffectiveBlocksList[k].RightStation) < 0.01)
							{
								ineffectiveBlocksSet.Add(ineffectiveBlocksList[k]);
								ineffectiveBlocks3.Add(ineffectives[j]);
							}
						}
					}
				}
				if (ineffectiveBlocks != null)
				{
					ineffectiveBlocks3.AddRange(ineffectiveBlocks);
				}
				if (!xsidToIneffectiveBlock.ContainsKey(element.Item1))
				{
					xsidToIneffectiveBlock[element.Item1] = new List<RASXS.IneffectiveBlock>();
				}
				xsidToIneffectiveBlock[element.Item1].AddRange(ineffectiveBlocks3);
			}
		}
	}

	public void ComputeAllBlockedObstructionsForXSs(bool forceElevUpdate)
	{
		using (new WaitCursor(SharedData.RasMapper))
		{
			SharedData.ReportMessage("Computing Blocked Obstructions...");
			List<int> xsids = Enumerable.Range(0, XS.FeatureCount()).ToList();
			ComputeBlockedObstructionsForXSs(xsids, forceElevUpdate);
			SharedData.ReportMessage("Computing Blocked Obstructions... done", replacePreviousIfPossible: true);
		}
	}

	public void ComputeBlockedObstructionsForXSs(IList<int> xsids, bool forceElevUpdate)
	{
		foreach (int xsid in xsids)
		{
			ComputeBlockedObstructionsForXS(xsid, forceElevUpdate);
		}
	}

	private List<RASXS.Obstruction> ComputeObstructions(int xsID, int fid, bool forceElevUpdate)
	{
		List<RASXS.Obstruction> obstructionsList = new List<RASXS.Obstruction>();
		List<Tuple<PointOnPolyline, PointOnPolyline>> entryExitPoints = _BlockedObstructions.Polygon(fid).EntryExitPoints(_XS.Polyline(xsID));
		checked
		{
			float station = default;
			float station2 = default;
			foreach (Tuple<PointOnPolyline, PointOnPolyline> entryExitPoint in entryExitPoints)
			{
				GetStation1AndStation2UsingXS(entryExitPoint, xsID, ref station, ref station2);
				float naN = float.NaN;
				if (forceElevUpdate)
				{
					naN = _BlockedObstructions.GetElevation(fid);
				}
				else
				{
					List<RASXS.Obstruction> obstructions = XS.GetObstructions(xsID);
					if (obstructions != null)
					{
						int count = obstructions.Count - 1;
						for (int i = 0; i <= count; i++)
						{
							RASXS.Obstruction obstruction = obstructions[i];
							float leftStation = obstruction.LeftStation;
							float rightStation = obstruction.RightStation;
							if ((leftStation >= station && leftStation < station2) || (rightStation >= station && rightStation < station2))
							{
								naN = obstruction.Elevation;
								break;
							}
						}
					}
					if (float.IsNaN(naN))
					{
						naN = _BlockedObstructions.GetElevation(fid);
					}
				}
				obstructionsList.Add(new RASXS.Obstruction(station, station2, naN));
			}
			return obstructionsList;
		}
	}

	public void ComputeBlockedObstructionsForXS(int xsID, bool forceElevUpdate)
	{
		List<RASXS.Obstruction> obstructions = new List<RASXS.Obstruction>();
		checked
		{
			int count = _BlockedObstructions.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				obstructions.AddRange(ComputeObstructions(xsID, i, forceElevUpdate));
			}
			_XS.SetObstructions(xsID, obstructions);
			SharedData.RASMapperReplot();
		}
	}

	public void ComputeAllBlockedObstructions(bool forceElevUpdate)
	{
		using (new WaitCursor(SharedData.RasMapper))
		{
			SharedData.ReportMessage("Computing Blocked Obstructions...");
			List<int> fids = Enumerable.Range(0, BlockedObstructions.FeatureCount()).ToList();
			ComputeBlockedObstructionsUsingBOFIDs(fids, forceElevUpdate);
			SharedData.ReportMessage("Computing Blocked Obstructions... done", replacePreviousIfPossible: true);
		}
	}

	public void ComputeBlockedObstructionsUsingBOFIDs(IList<int> fids, bool forceElevUpdate)
	{
		Dictionary<int, List<RASXS.Obstruction>> xsidToObstruction = new Dictionary<int, List<RASXS.Obstruction>>();
		foreach (int fid in fids)
		{
			ComputeBlockedObstructionsUsingBOFIDInternal(fid, forceElevUpdate, ref xsidToObstruction);
		}
		foreach (KeyValuePair<int, List<RASXS.Obstruction>> xsidToObstruction2 in xsidToObstruction)
		{
			XS.SetObstructions(xsidToObstruction2.Key, xsidToObstruction2.Value);
		}
		SharedData.RASMapperReplot();
	}

	public void ComputeBlockedObstructionsUsingBOFID(int fid, bool forceElevUpdate)
	{
		List<int> intList = new List<int>();
		intList.Add(fid);
		ComputeBlockedObstructionsUsingBOFIDs(intList, forceElevUpdate);
	}

	private void ComputeBlockedObstructionsUsingBOFIDInternal(int fid, bool forceElevUpdate, ref Dictionary<int, List<RASXS.Obstruction>> xsidToObstruction)
	{
		List<Tuple<int, int>> elements = XS.GetSegmentSpatialIndex().GetElements(BlockedObstructions.Polygon(fid).GetExtent());
		HashSet<int> intSet = new HashSet<int>();
		if (xsidToObstruction == null)
		{
			xsidToObstruction = new Dictionary<int, List<RASXS.Obstruction>>();
		}
		checked
		{
			foreach (Tuple<int, int> element in elements)
			{
				if (intSet.Contains(element.Item1))
				{
					continue;
				}
				List<RASXS.Obstruction> obstructionsList = null;
				List<RASXS.Obstruction> obstructions2 = new List<RASXS.Obstruction>();
				int count = BlockedObstructions.FeatureCount() - 1;
				for (int i = 0; i <= count; i++)
				{
					intSet.Add(element.Item1);
					List<RASXS.Obstruction> obstructions3 = ComputeObstructions(element.Item1, i, forceElevUpdate);
					if (i == fid)
					{
						obstructionsList = obstructions3;
					}
					else
					{
						obstructions2.AddRange(obstructions3);
					}
				}
				List<RASXS.Obstruction> obstructions = XS.GetObstructions(element.Item1);
				List<RASXS.Obstruction> obstructions4 = obstructions2;
				if (obstructions != null)
				{
					obstructions4 = new List<RASXS.Obstruction>();
					HashSet<RASXS.Obstruction> obstructionsSet = new HashSet<RASXS.Obstruction>();
					int count2 = obstructions.Count - 1;
					for (int j = 0; j <= count2; j++)
					{
						int count3 = obstructions2.Count - 1;
						for (int k = 0; k <= count3; k++)
						{
							if (!obstructionsSet.Contains(obstructions2[k]) && (double)System.Math.Abs(obstructions[j].LeftStation - obstructions2[k].LeftStation) < 0.01 && (double)System.Math.Abs(obstructions[j].RightStation - obstructions2[k].RightStation) < 0.01)
							{
								obstructionsSet.Add(obstructions2[k]);
								obstructions4.Add(obstructions[j]);
							}
						}
					}
				}
				if (obstructionsList != null)
				{
					obstructions4.AddRange(obstructionsList);
				}
				if (!xsidToObstruction.ContainsKey(element.Item1))
				{
					xsidToObstruction[element.Item1] = new List<RASXS.Obstruction>();
				}
				xsidToObstruction[element.Item1].AddRange(obstructions4);
			}
		}
	}

	public void CreateFlowPathLinesForXSs(IList<int> xsids, [Optional][DefaultParameterValue(null)] ref RiverMap rivMap)
	{
		if (xsids.IsNullOrEmpty())
		{
			return;
		}
		List<BankProperties> bankProperties = new List<BankProperties>();
		FlowPathLines.DeleteAllFeatures();
		foreach (int xsid in xsids)
		{
			bankProperties.Add(CreateFlowPathLinesForXSInternal(xsid));
		}
		RASBankLines rASBankLines = new RASBankLines(this);
		rASBankLines.ComputeBankLines(bankProperties);
		checked
		{
			int count = rASBankLines.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				FlowPathLines.AddFeature(rASBankLines.Polyline(i));
			}
		}
	}

	private BankProperties CreateFlowPathLinesForXSInternal(int xsid)
	{
		PointM leftBankPoint = XS.GetLeftBankPoint(xsid);
		Polyline polyline = XS.Polyline(xsid);
		double actualDistance = 0.0;
		PointOnPolyline ptOnPoly = polyline.NearestPointOnPolyline(leftBankPoint, ref actualDistance);
		double distanceAlongPolylineTerm = XS.Polyline(xsid).DistanceAlong(ptOnPoly) * (2.0 / 3.0);
		PointOnPolyline pointOnPolyline = XS.Polyline(xsid).PointAtDistance(distanceAlongPolylineTerm);
		PointM rightBankPoint = XS.GetRightBankPoint(xsid);
		Polyline polyline2 = XS.Polyline(xsid);
		actualDistance = 0.0;
		PointOnPolyline ptOnPoly2 = polyline2.NearestPointOnPolyline(rightBankPoint, ref actualDistance);
		double distanceAlong = XS.Polyline(xsid).DistanceAlong(ptOnPoly2);
		distanceAlong = (XS.Polyline(xsid).Length - distanceAlong) * (1.0 / 3.0) + distanceAlong;
		PointOnPolyline pointOnPolyline2 = XS.Polyline(xsid).PointAtDistance(distanceAlong);
		return new BankProperties((float)distanceAlongPolylineTerm, (float)pointOnPolyline.Fraction, pointOnPolyline, (float)distanceAlong, (float)pointOnPolyline2.Fraction, pointOnPolyline2);
	}

	public void ComputeReachLengthsForXSs(IList<int> xsids, [Optional][DefaultParameterValue(null)] ref RiverMap rivMap)
	{
		if (xsids.IsNullOrEmpty())
		{
			return;
		}
		if (rivMap == null)
		{
			rivMap = CreateRiverMap(loadSpatialIntersectionData: false);
		}
		RiverMap.CrossSection crossSection = null;
		foreach (int xsid in xsids)
		{
			crossSection = rivMap.GetCrossSection(xsid);
			ComputeReachLengthOnXS(crossSection);
		}
	}

	public void ComputeReachLengthOnXS(RiverMap.CrossSection cs)
	{
		if (cs != null)
		{
			RiverMap.CrossSection crossSection = cs.DownstreamCrossSection();
			if (crossSection == null)
			{
				_XS.SetChannelLength(cs.ID, float.NaN);
				_XS.SetLeftOverbankLength(cs.ID, float.NaN);
				_XS.SetRightOverbankLength(cs.ID, float.NaN);
			}
			else
			{
				ComputeReachLengthOnXS(cs.ID, crossSection.ID);
			}
		}
	}

	public void ComputeReachLengthOnXS(int xsid, int dsXSID)
	{
		checked
		{
			if (xsid < 0 || xsid > XS.FeatureCount() - 1)
			{
				return;
			}
			if (dsXSID < 0 || dsXSID > XS.FeatureCount() - 1)
			{
				_XS.SetChannelLength(xsid, float.NaN);
				_XS.SetLeftOverbankLength(xsid, float.NaN);
				_XS.SetRightOverbankLength(xsid, float.NaN);
				return;
			}
			List<PolylineIntersection> xsIntersectFlowPaths = XSIntersectFlowPaths(xsid);
			List<PolylineIntersection> xsIntersectRiver = XSIntersectRiver(xsid);
			List<PolylineIntersection> xsIntersectFlowPathsList = XSIntersectFlowPaths(dsXSID);
			List<PolylineIntersection> xsIntersectRiverList = XSIntersectRiver(dsXSID);
			if (xsIntersectRiver.Count != 1 || xsIntersectRiverList.Count != 1)
			{
				return;
			}
			PolylineIntersection polylineIntersection = xsIntersectRiver[0];
			PolylineIntersection polylineIntersection2 = xsIntersectRiverList[0];
			if (polylineIntersection.APolylineIndex != polylineIntersection2.APolylineIndex)
			{
				return;
			}
			Polyline polyline = _River.Polyline(polylineIntersection.APolylineIndex);
			double distanceAlong = polyline.DistanceAlong(polylineIntersection.APointOnPolyline);
			double systemHorizontalDecimalPlacesTerm = (float)System.Math.Abs(polyline.DistanceAlong(polylineIntersection2.APointOnPolyline) - distanceAlong);
			systemHorizontalDecimalPlacesTerm = System.Math.Round(systemHorizontalDecimalPlacesTerm, SharedData.HorizontalDecimalPlaces);
			_XS.SetChannelLength(xsid, (float)systemHorizontalDecimalPlacesTerm);
			if (xsIntersectFlowPaths.Count > 2 || xsIntersectFlowPathsList.Count > 2 || xsIntersectFlowPaths.Count == 1 || xsIntersectFlowPathsList.Count == 1)
			{
				return;
			}
			if (xsIntersectFlowPaths.Count == 0 || xsIntersectFlowPathsList.Count == 0)
			{
				_XS.SetLeftOverbankLength(xsid, (float)systemHorizontalDecimalPlacesTerm);
				_XS.SetRightOverbankLength(xsid, (float)systemHorizontalDecimalPlacesTerm);
				return;
			}
			PolylineIntersection polylineIntersection3 = xsIntersectFlowPaths[0];
			PolylineIntersection polylineIntersection4 = xsIntersectFlowPaths[1];
			PolylineIntersection polylineIntersection5 = xsIntersectFlowPathsList[0];
			PolylineIntersection polylineIntersection6 = xsIntersectFlowPathsList[1];
			if (polylineIntersection3.APolylineIndex == polylineIntersection5.APolylineIndex)
			{
				Polyline polyline2 = FlowPathLines.Polyline(polylineIntersection3.APolylineIndex);
				double distanceAlong2 = polyline2.DistanceAlong(polylineIntersection3.APointOnPolyline);
				double systemHorizontalDecimalPlacesTerm2 = System.Math.Abs(polyline2.DistanceAlong(polylineIntersection5.APointOnPolyline) - distanceAlong2);
				systemHorizontalDecimalPlacesTerm2 = System.Math.Round(systemHorizontalDecimalPlacesTerm2, SharedData.HorizontalDecimalPlaces);
				_XS.SetLeftOverbankLength(xsid, (float)systemHorizontalDecimalPlacesTerm2);
			}
			else if (systemHorizontalDecimalPlacesTerm > 0.0)
			{
				_XS.SetLeftOverbankLength(xsid, (float)systemHorizontalDecimalPlacesTerm);
			}
			if (polylineIntersection4.APolylineIndex == polylineIntersection6.APolylineIndex)
			{
				Polyline polyline3 = FlowPathLines.Polyline(polylineIntersection4.APolylineIndex);
				double distanceAlong3 = polyline3.DistanceAlong(polylineIntersection4.APointOnPolyline);
				double systemHorizontalDecimalPlacesTerm3 = System.Math.Abs(polyline3.DistanceAlong(polylineIntersection6.APointOnPolyline) - distanceAlong3);
				systemHorizontalDecimalPlacesTerm3 = System.Math.Round(systemHorizontalDecimalPlacesTerm3, SharedData.HorizontalDecimalPlaces);
				_XS.SetRightOverbankLength(xsid, (float)systemHorizontalDecimalPlacesTerm3);
			}
			else if (systemHorizontalDecimalPlacesTerm > 0.0)
			{
				_XS.SetRightOverbankLength(xsid, (float)systemHorizontalDecimalPlacesTerm);
			}
		}
	}

	public void ComputeJunctionDistanceOnReach(int usXSID, int dsXSID)
	{
		if (checked(usXSID < 0 || dsXSID < 0 || usXSID > XS.FeatureCount() - 1 || dsXSID > XS.FeatureCount() - 1))
		{
			return;
		}
		List<PolylineIntersection> xsIntersectRiver = XSIntersectRiver(usXSID);
		List<PolylineIntersection> xsIntersectRiverList = XSIntersectRiver(dsXSID);
		if (xsIntersectRiver.Count == 1 && xsIntersectRiverList.Count == 1)
		{
			PolylineIntersection polylineIntersection = xsIntersectRiver[0];
			PolylineIntersection polylineIntersection2 = xsIntersectRiverList[0];
			int aPolylineIndex = polylineIntersection.APolylineIndex;
			if (aPolylineIndex == polylineIntersection2.APolylineIndex)
			{
				Polyline polyline = _River.Polyline(aPolylineIndex);
				double distanceAlong = polyline.DistanceAlong(polylineIntersection.APointOnPolyline);
				double polylineDistanceAlongTerm = polyline.Length - polyline.DistanceAlong(polylineIntersection2.APointOnPolyline);
				River.SetUpstreamXSDistance(aPolylineIndex, (float)distanceAlong);
				River.SetDownstreamXSDistance(aPolylineIndex, (float)polylineDistanceAlongTerm);
			}
		}
	}

	public void ComputeAllJunctionLengths(RiverMap rivMap = null)
	{
		checked
		{
			using (new WaitCursor(SharedData.RasMapper))
			{
				SharedData.ReportMessage("Computing Reach Lengths...");
				if (rivMap == null)
				{
					rivMap = CreateRiverMap(loadSpatialIntersectionData: false);
				}
				foreach (RiverMap.River river in rivMap.Rivers)
				{
					foreach (RiverMap.Reach reach in river.Reaches)
					{
						int count = reach.CrossSections.Count - 2;
						for (int i = 0; i <= count; i++)
						{
							int iD = reach.CrossSections[i].ID;
							int iD2 = reach.CrossSections[i + 1].ID;
							ComputeReachLengthOnXS(iD, iD2);
						}
						if (reach.CrossSections.Count > 0)
						{
							ComputeJunctionDistanceOnReach(reach.CrossSections.First().ID, reach.CrossSections.Last().ID);
						}
					}
				}
				SharedData.ReportMessage("Computing Reach Lengths... done", replacePreviousIfPossible: true);
			}
		}
	}

	public void RenameRiverReach(int rivFID, string newRivName, string newReachName, bool pushRiverNameChangeToOtherReaches)
	{
		string riverName = River.GetRiverName(rivFID);
		string reachName = River.GetReachName(rivFID);
		Func<string, string, bool> func = [SpecialName] (string s1, string s2) => string.Equals(s1, s2, StringComparison.CurrentCultureIgnoreCase);
		checked
		{
			if (pushRiverNameChangeToOtherReaches)
			{
				int count = River.FeatureCount() - 1;
				for (int ordinal = 0; ordinal <= count; ordinal++)
				{
					string riverName2 = River.GetRiverName(ordinal);
					string reachName2 = River.GetReachName(ordinal);
					if (func(riverName2, riverName))
					{
						River.SetRiverName(ordinal, newRivName);
						if (func(reachName2, reachName))
						{
							River.SetReachName(ordinal, newReachName);
						}
					}
				}
				int count2 = XS.FeatureCount() - 1;
				for (int ordinal2 = 0; ordinal2 <= count2; ordinal2++)
				{
					string riverName3 = IHasRRRSLayerExtensions.GetRiverName(XS, ordinal2);
					string reachName3 = IHasRRRSLayerExtensions.GetReachName(XS, ordinal2);
					if (func(riverName3, riverName))
					{
						IHasRRRSLayerExtensions.SetRiverName(XS, ordinal2, newRivName);
						if (func(reachName3, reachName))
						{
							IHasRRRSLayerExtensions.SetReachName(XS, ordinal2, newReachName);
						}
					}
				}
				return;
			}
			River.SetRiverName(rivFID, newRivName);
			River.SetReachName(rivFID, newReachName);
			int count3 = XS.FeatureCount() - 1;
			for (int ordinal3 = 0; ordinal3 <= count3; ordinal3++)
			{
				string riverName4 = IHasRRRSLayerExtensions.GetRiverName(XS, ordinal3);
				string reachName4 = IHasRRRSLayerExtensions.GetReachName(XS, ordinal3);
				if (func(riverName4, riverName) && func(reachName4, reachName))
				{
					IHasRRRSLayerExtensions.SetRiverName(XS, ordinal3, newRivName);
					IHasRRRSLayerExtensions.SetReachName(XS, ordinal3, newReachName);
				}
			}
		}
	}

	public void UpdateRiverStationingAssociations()
	{
		checked
		{
			int count = RiverStationing.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				UpdateRiverStationingAssociations(i);
			}
		}
	}

	public void UpdateRiverStationingAssociations(int rsFid)
	{
		float unitsSharedDataTerm = 0f;
		unitsSharedDataTerm = ((SharedData.Units != SharedData.EnumUnits.SIUnits) ? 100f : 30f);
		PointM ptM = RiverStationing.Point(rsFid).PointM();
		int ordinal = -1;
		double maxValue = double.MaxValue;
		PointOnPolyline ptOnPoly = null;
		checked
		{
			int count = River.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Polyline polyline = River.Polyline(i);
				if (polyline.IsValid())
				{
					double actualDistance = 0.0;
					PointOnPolyline pointOnPolyline = polyline.NearestPointOnPolyline(ptM, ref actualDistance);
					if (actualDistance < maxValue)
					{
						maxValue = actualDistance;
						ordinal = i;
						ptOnPoly = pointOnPolyline;
					}
				}
			}
			if (ordinal == -1 || maxValue > (double)unitsSharedDataTerm)
			{
				RiverStationing.SetRiver(rsFid, "", "", 0f);
				return;
			}
			Polyline polyline2 = River.Polyline(ordinal);
			string riverName = River.GetRiverName(ordinal);
			string reachName = River.GetReachName(ordinal);
			double distanceAlong = polyline2.DistanceAlong(ptOnPoly);
			RiverStationing.SetRiver(rsFid, riverName, reachName, (float)distanceAlong);
		}
	}

	public PolygonFeatureLayer CreateXSVegetationRegions()
	{
		string columnName = "XSID";
		string columnName2 = "Left Sta";
		string columnName3 = "Right Sta";
		string columnName4 = "Strip";
		PolygonFeatureLayer polygonFeatureLayer = new PolygonFeatureLayer("XS Vegetation Regions");
		polygonFeatureLayer.AddAttributeColumn("River", typeof(string));
		polygonFeatureLayer.AddAttributeColumn("Reach", typeof(string));
		polygonFeatureLayer.AddAttributeColumn("River Station", typeof(string));
		polygonFeatureLayer.AddAttributeColumn(columnName, typeof(int));
		polygonFeatureLayer.AddAttributeColumn(columnName4, typeof(string));
		polygonFeatureLayer.AddAttributeColumn(columnName2, typeof(float));
		polygonFeatureLayer.AddAttributeColumn(columnName3, typeof(float));
		RASXS xS = XS;
		int[] usISID = null;
		int[] dsISID = null;
		RASXSInterpolationSurface xSInterpolationSurface = XSInterpolationSurface;
		xSInterpolationSurface.EnsureInterpolationSurface();
		xSInterpolationSurface.XSIDsToISIDMap(ref usISID, ref dsISID);
		checked
		{
			RASXSInterpolationSurface.StreamMtoStripM[] streamMtoStripMs = new RASXSInterpolationSurface.StreamMtoStripM[xSInterpolationSurface.FeatureCount() - 1 + 1];
			Polygon[] polygons = new Polygon[xSInterpolationSurface.FeatureCount() - 1 + 1];
			Polygon[] polygonsArray = new Polygon[xSInterpolationSurface.FeatureCount() - 1 + 1];
			int count = xSInterpolationSurface.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Tin tin = (Tin)xSInterpolationSurface.Feature(i);
				streamMtoStripMs[i] = new RASXSInterpolationSurface.StreamMtoStripM(tin);
				polygons[i] = GeoProcessing.ContourTINZ(tin, 0f, 0.5f);
				polygonsArray[i] = GeoProcessing.ContourTINZ(tin, 0.5f, 1f);
			}
			double filterToleranceXY = FilterToleranceXY;
			int count2 = xS.FeatureCount() - 1;
			_Closure_0024__480_002D0 capturedLocals = default;
			int lobSlicesHydraulicTableParametersTerm = default;
			float computed = default;
			float unity = default;
			for (int j = 0; j <= count2; j++)
			{
				capturedLocals = new _Closure_0024__480_002D0(capturedLocals);
				DataRow dataRow = xS.FeatureTable().Rows[j];
				xS.Polyline(j);
				Tin tin2 = null;
				capturedLocals.usStrm2Strip = null;
				Polygon clippedPgon = null;
				if (usISID[j] != -1)
				{
					tin2 = (Tin)xSInterpolationSurface.Feature(usISID[j]);
					capturedLocals.usStrm2Strip = streamMtoStripMs[usISID[j]];
					clippedPgon = polygons[usISID[j]];
				}
				Tin tin3 = null;
				capturedLocals.dsStrm2Strip = null;
				Polygon clippedPgon2 = null;
				if (dsISID[j] != -1)
				{
					tin3 = xSInterpolationSurface.Tin(dsISID[j]);
					capturedLocals.dsStrm2Strip = streamMtoStripMs[dsISID[j]];
					clippedPgon2 = polygonsArray[dsISID[j]];
				}
				int ordinal = 0;
				HydraulicTableParameters hydraulicTableParameters = xS.GetHydraulicTableParameters(j);
				int ordinal2 = 0;
				do
				{
					string textValue = "";
					switch (ordinal2)
					{
					case 0:
						lobSlicesHydraulicTableParametersTerm = hydraulicTableParameters.LOBSlices;
						computed = -2f;
						unity = -1f;
						textValue = "LOB";
						break;
					case 1:
						lobSlicesHydraulicTableParametersTerm = hydraulicTableParameters.ChannelSlices;
						computed = -1f;
						unity = 1f;
						textValue = "Channel";
						break;
					case 2:
						lobSlicesHydraulicTableParametersTerm = hydraulicTableParameters.ROBSlices;
						computed = 1f;
						unity = 2f;
						textValue = "ROB";
						break;
					}
					if (lobSlicesHydraulicTableParametersTerm > 0)
					{
						float computed2 = (unity - computed) / (float)lobSlicesHydraulicTableParametersTerm;
						float computed3 = computed;
						int count3 = lobSlicesHydraulicTableParametersTerm;
						for (int k = 1; k <= count3; k++)
						{
							float computed4 = computed3 + computed2;
							Polygon polygon = null;
							Polygon polygon2 = null;
							if (tin2 != null)
							{
								polygon = GeoProcessing.ContourTIN(tin2, computed3, computed4, capturedLocals._Lambda_0024__0, filterToleranceXY);
							}
							if (polygon != null)
							{
								polygon = GeoProcessing.IntersectPolygon(polygon, clippedPgon);
							}
							if (tin3 != null)
							{
								polygon2 = GeoProcessing.ContourTIN(tin3, computed3, computed4, capturedLocals._Lambda_0024__1, filterToleranceXY);
							}
							if (polygon2 != null)
							{
								polygon2 = GeoProcessing.IntersectPolygon(polygon2, clippedPgon2);
							}
							float dataRow22 = xS.ComputeStation(j, computed3);
							float dataRow23 = xS.ComputeStation(j, computed4);
							Polygon polygon3 = null;
							if (polygon != null || polygon2 != null)
							{
								if (polygon2 == null)
								{
									polygon3 = polygon;
								}
								else if (polygon == null)
								{
									polygon3 = polygon2;
								}
								else
								{
									Point point = xS.PointOnCutline(j, dataRow22);
									Point point2 = xS.PointOnCutline(j, dataRow23);
									Polygon polygon4 = polygon;
									PointM ptM = point.PointM();
									double actualDistance = 0.0;
									PointOnPolyline endPoint = polygon4.NearestPointOnPolyline(ptM, ref actualDistance);
									Polygon polygon5 = polygon;
									PointM ptM2 = point2.PointM();
									actualDistance = 0.0;
									PointOnPolyline startPoint = polygon5.NearestPointOnPolyline(ptM2, ref actualDistance);
									Polygon polygon6 = polygon2;
									PointM ptM3 = point.PointM();
									actualDistance = 0.0;
									PointOnPolyline startPoint2 = polygon6.NearestPointOnPolyline(ptM3, ref actualDistance);
									Polygon polygon7 = polygon2;
									PointM ptM4 = point2.PointM();
									actualDistance = 0.0;
									PointOnPolyline endPoint2 = polygon7.NearestPointOnPolyline(ptM4, ref actualDistance);
									PointMs pointMs = new PointMs();
									polygon.PointsBetweenInclusive(startPoint, endPoint, pointMs);
									polygon2.PointsBetweenInclusive(startPoint2, endPoint2, pointMs);
									pointMs.FilterXY(filterToleranceXY);
									polygon3 = new Polygon(pointMs);
								}
							}
							if (polygon3 != null)
							{
								DataRow dataRow2 = polygonFeatureLayer.AddFeature(polygon3);
								dataRow2["River"] = RuntimeHelpers.GetObjectValue(dataRow["River"]);
								dataRow2["Reach"] = RuntimeHelpers.GetObjectValue(dataRow["Reach"]);
								dataRow2["River Station"] = RuntimeHelpers.GetObjectValue(dataRow["River Station"]);
								dataRow2[columnName] = j;
								dataRow2[columnName4] = textValue + " " + Strings.Format(k);
								dataRow2[columnName2] = dataRow22;
								dataRow2[columnName3] = dataRow23;
							}
							computed3 += computed2;
							ordinal++;
						}
					}
					ordinal2++;
				}
				while (ordinal2 <= 2);
			}
			return polygonFeatureLayer;
		}
	}

	public void EvaluateReachConnections([Optional][DefaultParameterValue(null)] ref List<int> jxnList)
	{
		double radius = 30.0;
		if (jxnList == null)
		{
			jxnList = new List<int>();
		}
		RASRiver river = River;
		RASJunction junctions = Junctions;
		if (river.FeatureCount() == 0)
		{
			return;
		}
		checked
		{
			int count = river.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				river.ClearUpstreamConnection(i);
				river.ClearDownstreamConnection(i);
			}
			if (river.FeatureCount() > 1)
			{
				NeighborhoodSpatialIndex<Tuple<int, bool>> neighborhoodSpatialIndex = new NeighborhoodSpatialIndex<Tuple<int, bool>>(radius);
				int count2 = river.FeatureCount() - 1;
				for (int j = 0; j <= count2; j++)
				{
					Polyline polyline = river.Polyline(j);
					if (polyline != null && polyline.Count >= 2)
					{
						PointM pointM = polyline.FirstPointM();
						PointM pointM2 = polyline.LastPointM();
						neighborhoodSpatialIndex.Add(new Geospatial.Vectors.Point(pointM.X, pointM.Y), new Tuple<int, bool>(j, item2: true));
						neighborhoodSpatialIndex.Add(new Geospatial.Vectors.Point(pointM2.X, pointM2.Y), new Tuple<int, bool>(j, item2: false));
					}
				}
				RASJunction rASJunction = new RASJunction();
				junctions.CopyTo(rASJunction);
				junctions.DeleteAllFeatures();
				foreach (Neighborhood<Tuple<int, bool>> neighborhood in neighborhoodSpatialIndex.Neighborhoods)
				{
					if (neighborhood.Items.Count() < 3)
					{
						continue;
					}
					List<Tuple<int, bool>> valueTupleList = new List<Tuple<int, bool>>();
					double computed = 0.0;
					double computed2 = 0.0;
					foreach (Tuple<Geospatial.Vectors.Point, Tuple<int, bool>> valueTuple in neighborhood.Items)
					{
						computed += valueTuple.Item1.X;
						computed2 += valueTuple.Item1.Y;
						valueTupleList.Add(valueTuple.Item2);
					}
					int neighborhoodTerm = neighborhood.Items.Count();
					PointM pointM3 = new PointM(computed / (double)neighborhoodTerm, computed2 / (double)neighborhoodTerm);
					junctions.AddFeature(new Point(pointM3));
					float computed3 = 60f;
					List<int> spatialQueryFIDs = rASJunction.SpatialQueryFIDs(pointM3, computed3);
					int spatialQueryFIDIndexTerm = -1;
					double maxValue = double.MaxValue;
					foreach (int spatialQueryFIDIndex in spatialQueryFIDs)
					{
						double distanceTo = rASJunction.Point(spatialQueryFIDIndex).PointM().DistanceTo(pointM3);
						if (distanceTo < maxValue)
						{
							maxValue = distanceTo;
							spatialQueryFIDIndexTerm = spatialQueryFIDIndex;
						}
					}
					if (spatialQueryFIDIndexTerm >= 0)
					{
						int otherFID = junctions.FeatureCount() - 1;
						rASJunction.TransferJunctionTo(junctions, spatialQueryFIDIndexTerm, otherFID);
					}
					else
					{
						jxnList.Add(junctions.FeatureCount() - 1);
					}
					string featureName = junctions.GetFeatureName(junctions.FeatureCount() - 1);
					foreach (Tuple<int, bool> tuple in valueTupleList)
					{
						int ordinal = tuple.Item1;
						if (tuple.Item2)
						{
							river.SetUpstreamJunction(ordinal, featureName);
						}
						else
						{
							river.SetDownstreamJunction(ordinal, featureName);
						}
					}
				}
			}
			int count3 = river.FeatureCount() - 1;
			for (int k = 0; k <= count3; k++)
			{
				Polyline polyline2 = river.Polyline(k);
				if (!polyline2.IsValid())
				{
					river.ClearUpstreamConnection(k);
					river.ClearDownstreamConnection(k);
					continue;
				}
				int count4 = D2FlowArea.FeatureCount() - 1;
				for (int l = 0; l <= count4; l++)
				{
					Polygon polygon = MeshPerimeters.Polygon(l);
					if (polygon.IsValid())
					{
						string featureName2 = D2FlowArea.GetFeatureName(l);
						if (polygon.Contains(polyline2.FirstPointM()))
						{
							river.SetUpstreamD2FlowArea(k, featureName2);
						}
						if (polygon.Contains(polyline2.LastPointM()))
						{
							river.SetDownstreamD2FlowArea(k, featureName2);
						}
					}
				}
				int count5 = StorageArea.FeatureCount() - 1;
				for (int m = 0; m <= count5; m++)
				{
					Polygon polygon2 = StorageArea.Polygon(m);
					if (polygon2.IsValid())
					{
						string featureName3 = StorageArea.GetFeatureName(m);
						if (polygon2.Contains(polyline2.FirstPointM()))
						{
							river.SetUpstreamStorageArea(k, featureName3);
						}
						if (polygon2.Contains(polyline2.LastPointM()))
						{
							river.SetDownstreamStorageArea(k, featureName3);
						}
					}
				}
				if (string.IsNullOrEmpty(river.GetUpstreamName(k)))
				{
					river.SetUpstreamExternal(k);
				}
				if (string.IsNullOrEmpty(river.GetDownstreamName(k)))
				{
					river.SetDownstreamExternal(k);
				}
			}
		}
	}

	public static string GeometryShortID(string hdfFilename)
	{
		string textValue = "Geometry";
		try
		{
			if (File.Exists(hdfFilename) && H5Reader.IsValid(hdfFilename))
			{
				using (H5Reader h5Reader = new H5Reader(hdfFilename))
				{
					if (h5Reader.ExistsAsGroup("Geometry") && !h5Reader.TryReadAttributeScalar("Geometry", "Title", ref textValue))
					{
						textValue = "Invalid Geometry";
					}
				}
				textValue = RasMapperLib.Utilities.Path.EnsureFileCharacters(textValue);
			}
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			textValue = "Invalid Geometry";
			ProjectData.ClearProjectError();
		}
		return textValue;
	}

	public string GeometryShortID()
	{
		return GeometryShortID(SourceFilename);
	}

	public void ExtractManningsN(IList<int> xsIDs = null, bool reportMessages = true)
	{
		RASXS xS = XS;
		if (xsIDs == null)
		{
			xsIDs = Enumerable.Range(0, xS.FeatureCount()).ToList();
		}
		checked
		{
			if (reportMessages)
			{
				int count = xsIDs.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					int xsid = xsIDs[i];
					string frictionMode = XS.GetFrictionMode(xsid);
					if (Operators.CompareString(frictionMode, "Basic Mann n", TextCompare: false) != 0 && Operators.CompareString(frictionMode, "Horiz Mann n", TextCompare: false) != 0)
					{
						MessageBox.Show("Vertical Variation in Manning's n values has been specified for some of the cross sections. Horizontal Manning's n values will be ignored for those cross sections.", "Some Horitzonal Values Will Be Ignored", MessageBoxButtons.Ok, MessageBoxIcon.Warning);
					}
				}
			}
			using (new WaitCursor(SharedData.RasMapper))
			{
				if (reportMessages)
				{
					SharedData.ReportMessage("Computing Manning's n Values...");
				}
				Dictionary<int, List<ManningsNInfo>> manningsNInfoListByInt = new Dictionary<int, List<ManningsNInfo>>();
				Dictionary<int, List<ManningsNInfo>> manningsNInfoListByInt2 = new Dictionary<int, List<ManningsNInfo>>();
				if (NValueLayer != null && NValueLayer.SourceFileExists)
				{
					_Closure_0024__484_002D0 capturedLocals = new _Closure_0024__484_002D0(null);
					capturedLocals.landCoverMann = LandCoverManningsN;
					capturedLocals.landCoverMann.EnsureCurrentData();
					capturedLocals.rasterLCValues = new Dictionary<int, List<ManningsNInfo>>();
					if (File.Exists(NValueLayer.TiffFilename))
					{
						using ConvertToIntegerTiffReader convertToIntegerTiffReader = new ConvertToIntegerTiffReader(NValueLayer.TiffFilename);
						capturedLocals.tilesRaster = ImageLayer.TiffTileRaster(convertToIntegerTiffReader.GetReader());
						int cols = (int)System.Math.Round(NValueLayer.Extent.Width / capturedLocals.tilesRaster.CellSize);
						int rows = (int)System.Math.Round(NValueLayer.Extent.Height / capturedLocals.tilesRaster.CellSize);
						capturedLocals.tilesRaster = new RasterM(rows, cols, NValueLayer.Extent);
					}
					else
					{
						int ordinal = 10000;
						double systemTerm = System.Math.Round(System.Math.Sqrt(NValueLayer.Extent.Width * NValueLayer.Extent.Height / (double)ordinal), 0);
						double y = System.Math.Floor(System.Math.Log10(systemTerm) + 1.0);
						systemTerm /= System.Math.Pow(10.0, y);
						systemTerm = System.Math.Round(systemTerm, 1) * System.Math.Pow(10.0, y);
						int cols2 = System.Math.Max(1, (int)System.Math.Round(NValueLayer.Extent.Width / systemTerm));
						int rows2 = System.Math.Max(1, (int)System.Math.Round(NValueLayer.Extent.Height / systemTerm));
						capturedLocals.tilesRaster = new RasterM(rows2, cols2, NValueLayer.Extent);
					}
					SpatialIndex<int> spatialIndex = new SpatialIndex<int>(capturedLocals.tilesRaster);
					capturedLocals.tilesToProcess = new Dictionary<int, List<PolylineSegmentStartStation>>();
					RasterizeSegment rasterizeSegment = new RasterizeSegment(capturedLocals.tilesRaster);
					using (IEnumerator<int> xsIDs2 = xsIDs.GetEnumerator())
					{
						_Closure_0024__484_002D1 capturedLocals2 = default;
						_Closure_0024__484_002D3 capturedLocals3 = default;
						_Closure_0024__484_002D2 capturedLocals4 = default;
						while (xsIDs2.MoveNext())
						{
							capturedLocals2 = new _Closure_0024__484_002D1(capturedLocals2);
							capturedLocals2.outerClosure = capturedLocals;
							capturedLocals2.xsID = xsIDs2.Current;
							capturedLocals3 = new _Closure_0024__484_002D3(capturedLocals3);
							capturedLocals3.outerClosure = capturedLocals2;
							Polyline polyline = (Polyline)xS.Feature(capturedLocals3.outerClosure.xsID);
							spatialIndex.Add(polyline, capturedLocals3.outerClosure.xsID);
							capturedLocals3.segStartStation = 0.0;
							int count2 = polyline.SegmentCount() - 1;
							for (int j = 0; j <= count2; j++)
							{
								capturedLocals4 = new _Closure_0024__484_002D2(capturedLocals4);
								capturedLocals4.outerClosure = capturedLocals3;
								capturedLocals4.segIndex = j;
								SegmentM segment = polyline.Segment(capturedLocals4.segIndex);
								if (segment.ClipToExtent(capturedLocals4.outerClosure.outerClosure.outerClosure.tilesRaster))
								{
									rasterizeSegment.ComputeCells(segment, capturedLocals4._Lambda_0024__0);
								}
								capturedLocals4.outerClosure.segStartStation = capturedLocals4.outerClosure.segStartStation + polyline.Segment(j).Length;
							}
						}
					}
					capturedLocals.landCoverIDs = null;
					_Closure_0024__484_002D4 capturedLocals5 = default;
					_Closure_0024__484_002D5 capturedLocals6 = default;
					foreach (int key in capturedLocals.tilesToProcess.Keys)
					{
						capturedLocals5 = new _Closure_0024__484_002D4(capturedLocals5);
						capturedLocals5.outerClosure = capturedLocals;
						capturedLocals5.tileRaster = new RasterM();
						capturedLocals5.outerClosure.tilesRaster.SetCellTileRasterM(key, 256, capturedLocals5.tileRaster);
						float[] floatValues = new float[65536];
						Buffers.ArraySetAllVB(floatValues, -9999f);
						NValueLayer.GetTileValues(floatValues, capturedLocals5.tileRaster, "ID");
						capturedLocals5.outerClosure.landCoverIDs = floatValues.Select([SpecialName] (float id) => (int)System.Math.Round(id)).ToArray();
						List<PolylineSegmentStartStation> polylineSegmentStartStations = capturedLocals5.outerClosure.tilesToProcess[key];
						int count3 = polylineSegmentStartStations.Count - 1;
						for (int intervalIndex = 0; intervalIndex <= count3; intervalIndex++)
						{
							capturedLocals6 = new _Closure_0024__484_002D5(capturedLocals6);
							capturedLocals6.outerClosure = capturedLocals5;
							PolylineSegmentStartStation polylineSegmentStartStation = polylineSegmentStartStations[intervalIndex];
							capturedLocals6.xsID = polylineSegmentStartStation.PolylineIndex;
							int segmentIndex = polylineSegmentStartStation.SegmentIndex;
							capturedLocals6.segment = ((Polyline)xS.Feature(capturedLocals6.xsID)).Segment(segmentIndex);
							RasterizeSegment rasterizeSegment2 = new RasterizeSegment(capturedLocals6.outerClosure.tileRaster);
							capturedLocals6.startingStation = polylineSegmentStartStation.SegmentStartingStation;
							rasterizeSegment2.ComputeCells(capturedLocals6.segment, capturedLocals6._Lambda_0024__2);
						}
					}
					foreach (int xsID in xsIDs)
					{
						if (!capturedLocals.rasterLCValues.ContainsKey(xsID))
						{
							continue;
						}
						List<ManningsNInfo> orderBy = capturedLocals.rasterLCValues[xsID].OrderBy([SpecialName] (ManningsNInfo manningsninfo) => manningsninfo.StartSta).ToList();
						List<ManningsNInfo> manningsNInfos = new List<ManningsNInfo>();
						ManningsNInfo manningsNInfo4 = orderBy.First();
						manningsNInfo4.StartSta = 0.0;
						manningsNInfos.Add(manningsNInfo4);
						int ordinal2 = 0;
						foreach (ManningsNInfo orderBy2 in orderBy)
						{
							int index = manningsNInfos.Count - 1;
							ManningsNInfo manningsNInfo5 = manningsNInfos.Last();
							if (orderBy2.LandCover != manningsNInfo5.LandCover)
							{
								manningsNInfos.Add(orderBy2);
								manningsNInfo5.EndSta = orderBy2.StartSta;
								manningsNInfos[index] = manningsNInfo5;
							}
							if (ordinal2 == orderBy.Count - 1)
							{
								manningsNInfo5.EndSta = orderBy2.EndSta;
								manningsNInfos[index] = manningsNInfo5;
							}
							manningsNInfo4 = orderBy2;
							ordinal2++;
						}
						manningsNInfoListByInt.Add(xsID, manningsNInfos);
					}
				}
				RasLandCoverRegions landCoverRegions = LandCoverRegions;
				if (landCoverRegions.FeatureCount() > 0)
				{
					Dictionary<int, List<ManningsNInfo>> manningsNInfoListByInt3 = new Dictionary<int, List<ManningsNInfo>>();
					int count4 = xsIDs.Count - 1;
					ManningsNInfo manningsNInfo6 = default;
					for (int ordinal3 = 0; ordinal3 <= count4; ordinal3++)
					{
						List<ManningsNInfo> manningsNInfosList = new List<ManningsNInfo>();
						Polyline polyline2 = (Polyline)xS.Feature(ordinal3);
						int count5 = landCoverRegions.FeatureCount() - 1;
						for (int landCover = 0; landCover <= count5; landCover++)
						{
							List<Polyline> clipPolyline = landCoverRegions[landCover].ClipPolyline(polyline2);
							if (clipPolyline == null)
							{
								continue;
							}
							foreach (Polyline clipPolyline2 in clipPolyline)
							{
								PointM ptM = clipPolyline2.FirstPointM();
								double actualDistance = 0.0;
								PointOnPolyline ptOnPoly = polyline2.NearestPointOnPolyline(ptM, ref actualDistance);
								PointM ptM2 = clipPolyline2.LastPointM();
								actualDistance = 0.0;
								PointOnPolyline ptOnPoly2 = polyline2.NearestPointOnPolyline(ptM2, ref actualDistance);
								manningsNInfo6.StartSta = polyline2.DistanceAlong(ptOnPoly);
								manningsNInfo6.EndSta = polyline2.DistanceAlong(ptOnPoly2);
								manningsNInfo6.Override = landCover;
								manningsNInfo6.LandCover = landCover;
								manningsNInfo6.nValue = landCoverRegions.GetManningNValue(landCover);
								manningsNInfosList.Add(manningsNInfo6);
							}
						}
						manningsNInfoListByInt3.Add(ordinal3, manningsNInfosList);
					}
					foreach (int xsID2 in xsIDs)
					{
						if (manningsNInfoListByInt3.ContainsKey(xsID2))
						{
							List<ManningsNInfo> orderByList = manningsNInfoListByInt3[xsID2].OrderBy([SpecialName] (ManningsNInfo manningsninfo) => manningsninfo.StartSta).ToList();
							if (orderByList.Count != 0)
							{
								manningsNInfoListByInt2.Add(xsID2, orderByList);
							}
						}
					}
				}
				Dictionary<int, List<ManningsNInfo>> manningsNInfoListByInt4 = new Dictionary<int, List<ManningsNInfo>>();
				foreach (int xsID3 in xsIDs)
				{
					List<ManningsNInfo> manningsNInfos2 = new List<ManningsNInfo>();
					if (manningsNInfoListByInt2.Count > 0 && manningsNInfoListByInt2.ContainsKey(xsID3))
					{
						int count6 = manningsNInfoListByInt2[xsID3].Count - 1;
						for (int intervalIndex2 = 0; intervalIndex2 <= count6; intervalIndex2++)
						{
							ManningsNInfo manningsNInfo = manningsNInfoListByInt2[xsID3][intervalIndex2];
							double endSta = manningsNInfo.EndSta;
							List<int> spatialQueryFIDs = LandCoverRegions.SpatialQueryFIDs(XS.Polyline(xsID3).PointAtDistance(endSta).PointM, 0.0);
							int count7 = manningsNInfoListByInt2[xsID3].Count - 1;
							for (int intervalIndex3 = 0; intervalIndex3 <= count7; intervalIndex3++)
							{
								ManningsNInfo manningsNInfo2 = manningsNInfoListByInt2[xsID3][intervalIndex3];
								if (spatialQueryFIDs.Contains(manningsNInfo2.Override) && manningsNInfo2.StartSta > manningsNInfo.StartSta && manningsNInfo2.StartSta <= manningsNInfo.EndSta && manningsNInfo2.Override < manningsNInfo.Override)
								{
									ManningsNInfo manningsNInfo7 = new ManningsNInfo
									{
										StartSta = manningsNInfo.EndSta,
										EndSta = manningsNInfo2.EndSta,
										Override = manningsNInfo2.Override,
										nValue = manningsNInfo2.nValue
									};
									manningsNInfoListByInt2[xsID3][intervalIndex3] = manningsNInfo7;
								}
							}
						}
						foreach (ManningsNInfo manningsNInfo8 in manningsNInfoListByInt2[xsID3])
						{
							manningsNInfos2.Add(manningsNInfo8);
						}
					}
					if (manningsNInfoListByInt.Count > 0 && manningsNInfoListByInt.ContainsKey(xsID3))
					{
						foreach (ManningsNInfo manningsNInfo9 in manningsNInfoListByInt[xsID3])
						{
							ManningsNInfo current8 = manningsNInfo9;
							if (manningsNInfoListByInt2.ContainsKey(xsID3))
							{
								double startSta = current8.StartSta;
								List<int> spatialQueryFIDsList = LandCoverRegions.SpatialQueryFIDs(XS.Polyline(xsID3).PointAtDistance(startSta).PointM, 0.0);
								List<ManningsNInfo> manningsNInfos3 = manningsNInfoListByInt2[xsID3];
								double minValue = double.MinValue;
								while (spatialQueryFIDsList.Count != 0 && startSta < current8.EndSta)
								{
									double minValue2 = double.MinValue;
									int manningsNInfo10Term = -1;
									foreach (int spatialQueryFIDIndex in spatialQueryFIDsList)
									{
										foreach (ManningsNInfo manningsNInfo10 in manningsNInfos3)
										{
											if (spatialQueryFIDIndex == manningsNInfo10.Override)
											{
												if (manningsNInfo10.EndSta > minValue2)
												{
													minValue2 = manningsNInfo10.EndSta;
													manningsNInfo10Term = manningsNInfo10.Override;
												}
												break;
											}
										}
									}
									if (minValue == minValue2)
									{
										break;
									}
									startSta = minValue2;
									spatialQueryFIDsList = LandCoverRegions.SpatialQueryFIDs(XS.Polyline(xsID3).PointAtDistance(startSta).PointM, 0.0);
									minValue = minValue2;
								}
								if (startSta < current8.EndSta)
								{
									current8.StartSta = startSta;
									manningsNInfos2.Add(current8);
								}
							}
							else
							{
								manningsNInfos2.Add(current8);
							}
						}
					}
					manningsNInfoListByInt4.Add(xsID3, manningsNInfos2);
				}
				foreach (int xsID4 in xsIDs)
				{
					double startSta2 = 0.0;
					List<ManningsNInfo> orderBy3 = manningsNInfoListByInt4[xsID4].OrderBy([SpecialName] (ManningsNInfo manningsninfo) => manningsninfo.StartSta).ToList();
					double systemHorizontalDecimalPlacesTerm = 1.0 / System.Math.Pow(10.0, SharedData.HorizontalDecimalPlaces);
					foreach (ManningsNInfo orderBy4 in orderBy3)
					{
						if (orderBy4.StartSta > startSta2 && orderBy4.StartSta - startSta2 > systemHorizontalDecimalPlacesTerm)
						{
							ManningsNInfo manningsNInfo11 = new ManningsNInfo
							{
								StartSta = startSta2,
								EndSta = orderBy4.StartSta,
								nValue = -9999f,
								LandCover = -1,
								Override = -1
							};
							manningsNInfoListByInt4[xsID4].Add(manningsNInfo11);
						}
						startSta2 = orderBy4.EndSta;
					}
					if (orderBy3.Count == 0 || startSta2 < XS.Polyline(xsID4).Length)
					{
						ManningsNInfo manningsNInfo12 = new ManningsNInfo
						{
							StartSta = startSta2,
							EndSta = XS.Polyline(xsID4).Length,
							nValue = -9999f,
							LandCover = -1,
							Override = -1
						};
						manningsNInfoListByInt4[xsID4].Add(manningsNInfo12);
					}
				}
				foreach (int xsID5 in xsIDs)
				{
					List<double> doubleList = new List<double>();
					List<float> floatList = new List<float>();
					List<ManningsNInfo> orderBy5 = manningsNInfoListByInt4[xsID5].OrderBy([SpecialName] (ManningsNInfo manningsninfo) => manningsninfo.StartSta).ToList();
					if (orderBy5.Count == 0)
					{
						continue;
					}
					ManningsNInfo manningsNInfo3 = orderBy5.First();
					doubleList.Add(manningsNInfo3.StartSta);
					floatList.Add(manningsNInfo3.nValue);
					float nValue = manningsNInfo3.nValue;
					double endSta2 = manningsNInfo3.EndSta;
					float nValue2 = manningsNInfo3.nValue;
					bool condition = false;
					double endSta3 = 0.0;
					if (manningsNInfo3.Override != -1)
					{
						condition = true;
						endSta3 = manningsNInfo3.EndSta;
					}
					foreach (ManningsNInfo orderBy6 in orderBy5)
					{
						if (!orderBy6.Equals(manningsNInfo3))
						{
							if (condition && orderBy6.StartSta > endSta3)
							{
								doubleList.Add(endSta3);
								floatList.Add(nValue2);
							}
							if (orderBy6.Override != -1)
							{
								doubleList.Add(orderBy6.StartSta);
								floatList.Add(orderBy6.nValue);
								endSta3 = orderBy6.EndSta;
								condition = true;
							}
							else if (orderBy6.nValue != nValue)
							{
								doubleList.Add(orderBy6.StartSta);
								floatList.Add(orderBy6.nValue);
								condition = false;
							}
							nValue = orderBy6.nValue;
							if (orderBy6.EndSta > endSta2)
							{
								endSta2 = orderBy6.EndSta;
								nValue2 = orderBy6.nValue;
							}
						}
					}
					Point2DCollection point2DCollection = new Point2DCollection();
					int count8 = floatList.Count - 1;
					for (int intervalIndex4 = 0; intervalIndex4 <= count8; intervalIndex4++)
					{
						point2DCollection.Add(new Point2D((float)doubleList[intervalIndex4], floatList[intervalIndex4]));
					}
					xS.SetManningsN(xsID5, point2DCollection);
					if (Operators.CompareString(xS.GetFrictionMode(xsID5), "Basic Mann n", TextCompare: false) == 0)
					{
						xS.SetFrictionMode(xsID5, "Horiz Mann n");
					}
				}
				if (reportMessages)
				{
					SharedData.ReportMessage("Computing Manning's n Values... done", replacePreviousIfPossible: true);
				}
			}
		}
	}

	public bool SampleElementsAndSave(H5Writer h5w, GDALRaster gdr, string folder, ProgressReporter prog = null)
	{
		return SampleElementsAndSave(h5w, new RasterM(gdr), gdr.GetProjection(), folder, prog);
	}

	public bool SampleElementsAndSave(H5Writer h5w, RasterM nativeRaster, Projection nativeProjection, string folder, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		Projection projection = null;
		Projection projection2 = null;
		if (SharedData.SRSProjection == null)
		{
			projection = null;
			projection2 = null;
			prog.ReportWarning("No RasMapper projection set. Cannot warp input data, treating it like it's in the same projection as your project/geometry.");
		}
		else
		{
			projection2 = nativeProjection;
			projection = SharedData.SRSProjection;
			if (projection2 == null || projection2.IsValid() == SRSValidation.Corrupt)
			{
				projection2 = null;
				projection = null;
				if (!Extent.IsNullOrInfinite(nativeRaster.ComputeExtent()))
				{
					prog.ReportWarning("No projection could be parsed. Assuming Native (project) projection...");
				}
			}
		}
		checked
		{
			if (XS.FeatureCount() > 0)
			{
				string folderTerm = folder + "/Cross Sections";
				if (!h5w.ExistsAsGroup(folderTerm))
				{
					h5w.CreateGroup(folderTerm);
				}
				string datasetName = folderTerm + "/Info";
				string datasetName2 = folderTerm + "/Indexes";
				string datasetName3 = folderTerm + "/Weights";
				int[,] intValues = new int[XS.FeatureCount() - 1 + 1, 2];
				List<int> intList = new List<int>();
				List<float> floatList = new List<float>();
				List<PointM> allXSRiverIntersectionPoints = GetAllXSRiverIntersectionPoints();
				if (allXSRiverIntersectionPoints.Any([SpecialName] (PointM pt) => pt.IsUndefined()))
				{
					prog.ReportError("Some cross-sections do not cross a river line - could not apply wind data.");
					return false;
				}
				if (projection2 != null && projection != null)
				{
					Geospatial.Vectors.Point[] points = allXSRiverIntersectionPoints.Select([SpecialName] (PointM ptm) => new Geospatial.Vectors.Point(ptm.X, ptm.Y)).ToArray();
					Projector.TransformPoints(projection, projection2, points);
					allXSRiverIntersectionPoints = points.Select([SpecialName] (Geospatial.Vectors.Point gdrpt) => new PointM(gdrpt.X, gdrpt.Y)).ToList();
				}
				int indexCountSum = 0;
				int ordinal = 0;
				int count = XS.FeatureCount() - 1;
				for (int intervalIndex = 0; intervalIndex <= count; intervalIndex++)
				{
					intValues[intervalIndex, 0] = indexCountSum;
					List<int> indexes = null;
					List<float> weights = null;
					nativeRaster.GetBilinearWeights(allXSRiverIntersectionPoints[intervalIndex], ref indexes, ref weights);
					if (indexes.Count == 0)
					{
						intList.Add(0);
						floatList.Add(0f);
						indexCountSum++;
						ordinal++;
						intValues[intervalIndex, 1] = 1;
						continue;
					}
					int count2 = indexes.Count - 1;
					for (int intervalIndex2 = 0; intervalIndex2 <= count2; intervalIndex2++)
					{
						intList.Add(indexes[intervalIndex2]);
						floatList.Add(weights[intervalIndex2]);
					}
					indexCountSum += indexes.Count;
					intValues[intervalIndex, 1] = indexes.Count;
				}
				if (ordinal > 0)
				{
					prog.ReportError("Warning: " + ordinal + " cross-sections (" + System.Math.Round((double)(100 * ordinal) / (double)XS.FeatureCount()) + "%) were out-of-bounds of the given layer.");
				}
				h5w.WriteArray(datasetName, intValues);
				h5w.WriteArray(datasetName2, intList.ToArray());
				h5w.WriteArray(datasetName3, floatList.ToArray());
			}
			if (XSInterpolationSurface.FeatureCount() != 0)
			{
				string folderTerm2 = folder + "/Cross Section Interpolation Surfaces";
				if (!h5w.ExistsAsGroup(folderTerm2))
				{
					h5w.CreateGroup(folderTerm2);
				}
				string datasetName4 = folderTerm2 + "/Info";
				string datasetName5 = folderTerm2 + "/Indexes";
				string datasetName6 = folderTerm2 + "/Weights";
				int[,] intValues2 = new int[XSInterpolationSurface.FeatureCount() - 1 + 1, 2];
				List<int> intListList = new List<int>();
				List<float> floatListList = new List<float>();
				List<PolylineIntersection>[] allXSRiverIntersections = GetAllXSRiverIntersections();
				if (allXSRiverIntersections.Any([SpecialName] (List<PolylineIntersection> lst) => lst.IsNullOrEmpty()))
				{
					prog.ReportError("Some cross-sections do not cross a river line - invalid geometry.");
					return false;
				}
				double[][] doubleValues = new double[River.FeatureCount() - 1 + 1][];
				PointM[] pointMs = new PointM[XSInterpolationSurface.FeatureCount() - 1 + 1];
				int count3 = XSInterpolationSurface.FeatureCount() - 1;
				for (int intervalIndex3 = 0; intervalIndex3 <= count3; intervalIndex3++)
				{
					int upstreamXSID = XSInterpolationSurface.GetUpstreamXSID(intervalIndex3);
					int downstreamXSID = XSInterpolationSurface.GetDownstreamXSID(intervalIndex3);
					PolylineIntersection polylineIntersection = allXSRiverIntersections[upstreamXSID].FirstOrDefault();
					PolylineIntersection polylineIntersection2 = allXSRiverIntersections[downstreamXSID].FirstOrDefault();
					if (polylineIntersection.APolylineIndex == polylineIntersection2.APolylineIndex)
					{
						int aPolylineIndex = polylineIntersection.APolylineIndex;
						Polyline polyline = River.Polyline(aPolylineIndex);
						if (!Polyline.IsValidPolyline(polyline))
						{
							PointM point = polylineIntersection.Point;
							PointM point2 = polylineIntersection2.Point;
							pointMs[intervalIndex3] = new PointM((point.X + point2.X) / 2.0, (point.Y + point2.Y) / 2.0);
							continue;
						}
						double[] cumulativeSegmentLengths = doubleValues[aPolylineIndex];
						double distanceAlongFast = polyline.DistanceAlongFast(polylineIntersection.APointOnPolyline, ref cumulativeSegmentLengths);
						double distanceAlongFast2 = polyline.DistanceAlongFast(polylineIntersection2.APointOnPolyline, ref cumulativeSegmentLengths);
						doubleValues[aPolylineIndex] = cumulativeSegmentLengths;
						double distance = distanceAlongFast + (distanceAlongFast2 - distanceAlongFast) / 2.0;
						PointM pointM = polyline.PointAtDistance(distance).PointM;
						pointMs[intervalIndex3] = pointM;
					}
					else
					{
						PointM point3 = polylineIntersection.Point;
						PointM point4 = polylineIntersection2.Point;
						pointMs[intervalIndex3] = new PointM((point3.X + point4.X) / 2.0, (point3.Y + point4.Y) / 2.0);
					}
				}
				if (projection2 != null && projection != null)
				{
					Geospatial.Vectors.Point[] pointsArray = pointMs.Select([SpecialName] (PointM ptm) => new Geospatial.Vectors.Point(ptm.X, ptm.Y)).ToArray();
					Projector.TransformPoints(projection, projection2, pointsArray);
					pointMs = pointsArray.Select([SpecialName] (Geospatial.Vectors.Point gdrpt) => new PointM(gdrpt.X, gdrpt.Y)).ToArray();
				}
				int indexes2CountSum = 0;
				int ordinal2 = 0;
				int count4 = XSInterpolationSurface.FeatureCount() - 1;
				for (int intervalIndex4 = 0; intervalIndex4 <= count4; intervalIndex4++)
				{
					intValues2[intervalIndex4, 0] = indexes2CountSum;
					List<int> indexes2 = null;
					List<float> weights2 = null;
					nativeRaster.GetBilinearWeights(pointMs[intervalIndex4], ref indexes2, ref weights2);
					if (indexes2.Count == 0)
					{
						intListList.Add(0);
						floatListList.Add(0f);
						indexes2CountSum++;
						ordinal2++;
						intValues2[intervalIndex4, 1] = 1;
						continue;
					}
					int count5 = indexes2.Count - 1;
					for (int intervalIndex5 = 0; intervalIndex5 <= count5; intervalIndex5++)
					{
						intListList.Add(indexes2[intervalIndex5]);
						floatListList.Add(weights2[intervalIndex5]);
					}
					indexes2CountSum += indexes2.Count;
					intValues2[intervalIndex4, 1] = indexes2.Count;
				}
				if (ordinal2 > 0)
				{
					prog.ReportError("Warning: " + ordinal2 + " cross-section interpolation surfaces (" + System.Math.Round((double)(100 * ordinal2) / (double)XSInterpolationSurface.FeatureCount()) + "%) were out-of-bounds in the input raster.");
				}
				h5w.WriteArray(datasetName4, intValues2);
				h5w.WriteArray(datasetName5, intListList.ToArray());
				h5w.WriteArray(datasetName6, floatListList.ToArray());
			}
			if (StorageArea.FeatureCount() > 0)
			{
				string folderTerm3 = folder + "/Storage Areas";
				if (!h5w.ExistsAsGroup(folderTerm3))
				{
					h5w.CreateGroup(folderTerm3);
				}
				string datasetName7 = folderTerm3 + "/Info";
				string datasetName8 = folderTerm3 + "/Indexes";
				string datasetName9 = folderTerm3 + "/Weights";
				int[,] intValues3 = new int[StorageArea.FeatureCount() - 1 + 1, 2];
				List<int> intList2 = new List<int>();
				List<float> floatList2 = new List<float>();
				List<List<PointM>> pointMListList = new List<List<PointM>>();
				int count6 = StorageArea.FeatureCount() - 1;
				for (int ordinal3 = 0; ordinal3 <= count6; ordinal3++)
				{
					pointMListList.Add(StorageArea.Polygon(ordinal3).CopyPoints().ToList());
				}
				if (projection2 != null && projection != null)
				{
					int count7 = StorageArea.FeatureCount() - 1;
					for (int intervalIndex6 = 0; intervalIndex6 <= count7; intervalIndex6++)
					{
						Geospatial.Vectors.Point[] points2 = pointMListList[intervalIndex6].Select((_Closure_0024__._0024I486_002D6 == null) ? (_Closure_0024__._0024I486_002D6 = [SpecialName] (PointM ptm) => new Geospatial.Vectors.Point(ptm.X, ptm.Y)) : _Closure_0024__._0024I486_002D6).ToArray();
						Projector.TransformPoints(projection, projection2, points2);
						pointMListList[intervalIndex6] = points2.Select((_Closure_0024__._0024I486_002D7 == null) ? (_Closure_0024__._0024I486_002D7 = [SpecialName] (Geospatial.Vectors.Point gdrpt) => new PointM(gdrpt.X, gdrpt.Y)) : _Closure_0024__._0024I486_002D7).ToList();
					}
				}
				int indexes3CountSum = 0;
				int ordinal4 = 0;
				int count8 = StorageArea.FeatureCount() - 1;
				for (int intervalIndex7 = 0; intervalIndex7 <= count8; intervalIndex7++)
				{
					intValues3[intervalIndex7, 0] = indexes3CountSum;
					Polygon pgon = new Polygon(new PointMs(pointMListList[intervalIndex7]));
					List<int> indexes3 = null;
					List<float> weights3 = null;
					nativeRaster.GetWeights(pgon, 10000, ref indexes3, ref weights3);
					if (indexes3.Count == 0)
					{
						intList2.Add(0);
						floatList2.Add(0f);
						indexes3CountSum++;
						ordinal4++;
						intValues3[intervalIndex7, 1] = 1;
						continue;
					}
					int count9 = indexes3.Count - 1;
					for (int intervalIndex8 = 0; intervalIndex8 <= count9; intervalIndex8++)
					{
						intList2.Add(indexes3[intervalIndex8]);
						floatList2.Add(weights3[intervalIndex8]);
					}
					indexes3CountSum += indexes3.Count;
					intValues3[intervalIndex7, 1] = indexes3.Count;
				}
				if (ordinal4 > 0)
				{
					prog.ReportError("Warning: " + ordinal4 + " Storage Areas (" + System.Math.Round((double)(100 * ordinal4) / (double)StorageArea.FeatureCount()) + "%) were out-of-bounds in the input raster.");
				}
				h5w.WriteArray(datasetName7, intValues3);
				h5w.WriteArray(datasetName8, intList2.ToArray());
				h5w.WriteArray(datasetName9, floatList2.ToArray());
			}
			if (D2FlowArea.FeatureCount() > 0)
			{
				string folderTerm4 = folder + "/2D Flow Areas";
				if (!h5w.ExistsAsGroup(folderTerm4))
				{
					h5w.CreateGroup(folderTerm4);
				}
				int count10 = D2FlowArea.FeatureCount() - 1;
				for (int ordinal5 = 0; ordinal5 <= count10; ordinal5++)
				{
					MeshFV2D meshFV2D = D2FlowArea.Mesh(ordinal5);
					string featureName = D2FlowArea.GetFeatureName(ordinal5);
					string featureNameTerm = folderTerm4 + "/" + featureName;
					if (!h5w.ExistsAsGroup(featureNameTerm))
					{
						h5w.CreateGroup(featureNameTerm);
					}
					string datasetName10 = featureNameTerm + "/Cell Info";
					string datasetName11 = featureNameTerm + "/Cell Indexes";
					string datasetName12 = featureNameTerm + "/Cell Weights";
					List<PointM> pointMsList = new List<PointM>();
					int count11 = meshFV2D.NonVirtualCellCount - 1;
					for (int ordinal6 = 0; ordinal6 <= count11; ordinal6++)
					{
						pointMsList.Add(meshFV2D.Cell(ordinal6).Point.ToPointM());
					}
					if (projection2 != null && projection != null)
					{
						Geospatial.Vectors.Point[] points3 = pointMsList.Select([SpecialName] (PointM ptm) => new Geospatial.Vectors.Point(ptm.X, ptm.Y)).ToArray();
						Projector.TransformPoints(projection, projection2, points3);
						pointMsList = points3.Select([SpecialName] (Geospatial.Vectors.Point gdrpt) => new PointM(gdrpt.X, gdrpt.Y)).ToList();
					}
					int indexes4CountSum = 0;
					int ordinal7 = 0;
					int[,] intValues4 = new int[meshFV2D.NonVirtualCellCount - 1 + 1, 2];
					List<int> intList3 = new List<int>();
					List<float> floatList3 = new List<float>();
					int count12 = meshFV2D.NonVirtualCellCount - 1;
					for (int intervalIndex9 = 0; intervalIndex9 <= count12; intervalIndex9++)
					{
						intValues4[intervalIndex9, 0] = indexes4CountSum;
						List<int> indexes4 = null;
						List<float> weights4 = null;
						nativeRaster.GetBilinearWeights(pointMsList[intervalIndex9], ref indexes4, ref weights4);
						if (indexes4.Count == 0)
						{
							intList3.Add(0);
							floatList3.Add(0f);
							indexes4CountSum++;
							ordinal7++;
							intValues4[intervalIndex9, 1] = 1;
						}
						else
						{
							intList3.AddRange(indexes4);
							floatList3.AddRange(weights4);
							indexes4CountSum += indexes4.Count;
							intValues4[intervalIndex9, 1] = indexes4.Count;
						}
					}
					if (ordinal7 > 0)
					{
						prog.ReportError("Warning: Some of the 2D Cells in Mesh " + featureName.InDoubleQuotes() + " were out-of-bounds in the input raster.");
					}
					h5w.WriteArray(datasetName10, intValues4);
					h5w.WriteArray(datasetName11, intList3.ToArray());
					h5w.WriteArray(datasetName12, floatList3.ToArray());
					string datasetName13 = featureNameTerm + "/Face Info";
					string datasetName14 = featureNameTerm + "/Face Indexes";
					string datasetName15 = featureNameTerm + "/Face Weights";
					List<PointM> pointMs2 = new List<PointM>();
					int count13 = meshFV2D.FaceCount - 1;
					for (int ordinal8 = 0; ordinal8 <= count13; ordinal8++)
					{
						pointMs2.Add(meshFV2D.FaceSegment(ordinal8).MidPointXY());
					}
					if (projection2 != null && projection != null)
					{
						Geospatial.Vectors.Point[] points4 = pointMs2.Select([SpecialName] (PointM ptm) => new Geospatial.Vectors.Point(ptm.X, ptm.Y)).ToArray();
						Projector.TransformPoints(projection, projection2, points4);
						pointMs2 = points4.Select([SpecialName] (Geospatial.Vectors.Point gdrpt) => new PointM(gdrpt.X, gdrpt.Y)).ToList();
					}
					int indexes5CountSum = 0;
					int ordinal9 = 0;
					int[,] intValues5 = new int[meshFV2D.FaceCount - 1 + 1, 2];
					List<int> intList4 = new List<int>();
					List<float> floatList4 = new List<float>();
					int count14 = meshFV2D.FaceCount - 1;
					for (int intervalIndex10 = 0; intervalIndex10 <= count14; intervalIndex10++)
					{
						intValues5[intervalIndex10, 0] = indexes5CountSum;
						List<int> indexes5 = null;
						List<float> weights5 = null;
						nativeRaster.GetBilinearWeights(pointMs2[intervalIndex10], ref indexes5, ref weights5);
						if (indexes5.Count == 0)
						{
							intList4.Add(0);
							floatList4.Add(0f);
							indexes5CountSum++;
							ordinal9++;
							intValues5[intervalIndex10, 1] = 1;
						}
						else
						{
							intList4.AddRange(indexes5);
							floatList4.AddRange(weights5);
							indexes5CountSum += indexes5.Count;
							intValues5[intervalIndex10, 1] = indexes5.Count;
						}
					}
					if (ordinal9 > 0)
					{
						prog.ReportError("Warning: Some of the 2D Faces in Mesh " + featureName.InDoubleQuotes() + " were out-of-bounds in the input raster.");
					}
					h5w.WriteArray(datasetName13, intValues5);
					h5w.WriteArray(datasetName14, intList4.ToArray());
					h5w.WriteArray(datasetName15, floatList4.ToArray());
				}
			}
			return true;
		}
	}
}
