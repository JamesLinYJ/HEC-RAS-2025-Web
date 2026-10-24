using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Xml;
using H5Assist.VB;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Utilities;
using Utility;
using Utility.Extensions;

namespace RasMapperLib;

public class PolygonElevationModificationLayer : PolygonFeatureLayer, IElevationModificationLayer, IContainsControlPointsLayer, IHasProfileProperties
{
	private class CachedPolygon
	{
		public float Elevation;

		public DevelopmentTin DevTin;

		public Tin Tin;

		public Profile PerimeterProfile;

		public bool UseBoundaryElevs;

		public bool UseShapeZ;

		public byte[] ControlPointHash;

		public CachedPolygon(DevelopmentTin devTin, float elevation, byte[] ctrlPtHash, Profile perimeterProfile, bool useBoundaryElevs, bool useShapeZ)
		{
			DevTin = devTin;
			Tin = devTin.MakeCleanTin();
			Elevation = elevation;
			ControlPointHash = ctrlPtHash;
			PerimeterProfile = perimeterProfile;
			UseBoundaryElevs = useBoundaryElevs;
			UseShapeZ = useShapeZ;
		}

		public bool Matches(float elev, byte[] ctrlPtHash, Profile perimProfile, bool useBoundaryElevs, bool useShapeZ)
		{
			bool sequenceEqual = false;
			if (PerimeterProfile == null && perimProfile == null)
			{
				sequenceEqual = true;
			}
			else if (PerimeterProfile != null && perimProfile != null)
			{
				sequenceEqual = PerimeterProfile.SequenceEqual(perimProfile);
			}
			if (Elevation == elev && UseBoundaryElevs == useBoundaryElevs && UseShapeZ == useShapeZ && Hash.HashEquals(ControlPointHash, ctrlPtHash) && sequenceEqual)
			{
				return true;
			}
			return false;
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__42_002D0
	{
		public RowColRangeM rcr;

		public ElevationModificationType modType;

		public _Closure_0024__42_002D1 outerClosure;

		public _Closure_0024__42_002D0(_Closure_0024__42_002D0 other)
		{
			if (other != null)
			{
				rcr = other.rcr;
				modType = other.modType;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int r)
		{
			int minCol = rcr.MinCol;
			int maxCol = rcr.MaxCol;
			for (int i = minCol; i <= maxCol; i = checked(i + 1))
			{
				int cellIndex = outerClosure.raster.CellIndex(r, i);
				outerClosure.floatValues[cellIndex] = ElevationModificationGroup.GetReplacementValue(outerClosure.floatValues[cellIndex], modType, outerClosure.tinVals[cellIndex]);
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__42_002D1
	{
		public RasterM raster;

		public float[] floatValues;

		public float[] tinVals;

		public _Closure_0024__42_002D1(_Closure_0024__42_002D1 other)
		{
			if (other != null)
			{
				raster = other.raster;
				floatValues = other.floatValues;
				tinVals = other.tinVals;
			}
		}
	}

	public const string PolygonType = "Polygon";

	public const string sPolygonCache = "Cached Polygon Info";

	public const string sElevPtTol = "Elev Pt Tolerance";

	public const string sGenerateBoundryElevs = "Generate Boundary Elevations";

	public const string sUseShapeZ = "Use ShapeFile Z Elevations";

	public const string sProfile = "Profile";

	public bool InsertAsRectangle;

	private TerrainLayer _associatedTerrainLayer;

	public ElevationControlPointLayer ControlPoints;

	private int _defaultElevPtTol;

	public override bool CanMoveToAnotherLevel { get; }

	public override bool CanMoveWithinLevel { get; }

	public override bool CanRename
	{
		get
		{
			if (!IsEditing)
			{
				return false;
			}
			return true;
		}
	}

	public override bool CanRemove
	{
		get
		{
			if (IsEditing)
			{
				return true;
			}
			return false;
		}
	}

	public override bool UsesParentSourceFilename { get; }

	public override bool ForwardEditRequestsToParent { get; }

	public virtual string HDFGroup => "/Modifications/" + LayerName;

	public int DefaultElevPtTol
	{
		get
		{
			return _defaultElevPtTol;
		}
		private set
		{
			_defaultElevPtTol = value;
		}
	}

	public PolygonElevationModificationLayer(string lyrName, TerrainLayer terrainLayer)
		: base(lyrName)
	{
		InsertAsRectangle = false;
		CanMoveToAnotherLevel = false;
		CanMoveWithinLevel = false;
		UsesParentSourceFilename = true;
		ForwardEditRequestsToParent = true;
		_associatedTerrainLayer = terrainLayer;
		DefaultElevPtTol = 20;
		ControlPoints = new ElevationControlPointLayer("Control Points", HDFGroup + "/Control Points", useParentSourceFileName: true, GetLayerValueAtPoint, GetElevationChangingPointOnAllPerimeters);
		ControlPoints.FeatureChanged += ControlPointsChanged;
		Nodes.Add(ControlPoints);
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		ElevationModificationGroup.InitializeElevationModTypeSerializer();
		AddManagedAttributeColumn("Name", "Name", typeof(string));
		AddManagedAttributeColumn("Elevation Value", "Elevation Value", typeof(float));
		AddManagedAttributeColumn("Elevation Type", "Elevation Type", typeof(ElevationModificationType), null, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Elev Pt Tolerance", "Elev Pt Tolerance", typeof(float));
		AddManagedAttributeColumn("Generate Boundary Elevations", "Generate Boundary Elevations", typeof(bool), null, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Use ShapeFile Z Elevations", "Use ShapeFile Z Elevations", typeof(bool), null, DataColumnViewState.GrayedOut);
		AddAttributeColumn("Profile", typeof(IProfileInfo), null, DataColumnViewState.GrayedOut);
		AddAttributeColumn("Cached Polygon Info", typeof(CachedPolygon), null, DataColumnViewState.Invisible);
	}

	protected override void LoadFeatureTable()
	{
		if (string.IsNullOrEmpty(SourceFilename) || !SourceFileExists)
		{
			return;
		}
		checked
		{
			try
			{
				using H5Reader h5Reader = new H5Reader(SourceFilename);
				if (!h5Reader.ExistsAsGroup(HDFGroup))
				{
					return;
				}
				HDF5Storage.ReadPolyFeatures(this, h5Reader, HDFGroup + "/Polygon");
				string datasetPrefix = HDFGroup + "/Profile";
				List<SavedProfileInfo> profileMetadata = HDF5Storage.ReadProfileMetadata(h5Reader, datasetPrefix);
				if (profileMetadata.Count != FeatureCount())
				{
					int count = FeatureCount() - 1;
					for (int i = 0; i <= count; i++)
					{
						SetProfile(i, new Profile());
					}
				}
				int count2 = System.Math.Min(profileMetadata.Count, FeatureCount()) - 1;
				for (int j = 0; j <= count2; j++)
				{
					SetProfile(j, profileMetadata[j].GetProfile());
				}
				string hdfGroupTerm = HDFGroup + "/Attributes";
				if (h5Reader.ExistsAsDataset(hdfGroupTerm))
				{
					ReadManagedColumns(h5Reader, hdfGroupTerm);
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

	protected override bool SaveFeatureTableInternal()
	{
		if (!IsFeatureTableLoaded())
		{
			return false;
		}
		if (string.IsNullOrEmpty(SourceFilename))
		{
			return false;
		}
		if (!SourceFilename.RoughlyEndsWith(".hdf"))
		{
			SourceFilename += ".hdf";
		}
		if (FeatureCount() == 0 && !SourceFileExists)
		{
			return true;
		}
		using (H5Writer h5Writer = new H5Writer(SourceFilename))
		{
			if (FeatureCount() == 0)
			{
				h5Writer.Delete(HDFGroup);
				return true;
			}
			string datasetName = HDFGroup + "/Attributes";
			WriteManagedColumns(h5Writer, datasetName);
			HDF5Storage.WritePolygonFeatures(this, h5Writer, HDFGroup + "/Polygon");
			h5Writer.WriteAttribute(HDFGroup, "Type", "Polygon");
		}
		return true;
	}

	protected override void XMLLoadChildren(XmlElement xmlEl)
	{
		if (!SourceFileExists || xmlEl == null)
		{
			return;
		}
		XmlNodeList xmlNodeList = xmlEl.SelectNodes("Layer");
		foreach (XmlElement xmlNodeList2 in xmlNodeList)
		{
			if (string.Equals(Layer.GetLayerNameFromXML(xmlNodeList2), ControlPoints.Name))
			{
				ControlPoints.XMLLoad(xmlNodeList2);
				break;
			}
		}
	}

	protected override TerrainLayer GetAssociatedTerrainLayer()
	{
		return _associatedTerrainLayer;
	}

	internal Profile ComputePerimeterProfile(int fid, ref PointMs internalCtrlPts)
	{
		Polygon polygon = Polygon(fid);
		if (!polygon.IsValid())
		{
			return null;
		}
		Profile profile = new Profile();
		HashSet<double> doubleSet = new HashSet<double>();
		double[] cumulativeSegmentLengths = null;
		bool condition = false;
		if (internalCtrlPts == null)
		{
			internalCtrlPts = new PointMs();
		}
		float elevPtTolerance = GetElevPtTolerance(fid);
		Extent extent = Polygon(fid).ComputeExtent();
		extent = extent.Extend(elevPtTolerance * 2f);
		ControlPoints.ComputeExtent();
		ControlPoints.EnsureQuadtree(ControlPoints.Extent);
		List<int> spatialQueryFIDs = ControlPoints.SpatialQueryFIDs(extent);
		checked
		{
			int count = spatialQueryFIDs.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM pointM = ControlPoints[spatialQueryFIDs[i]].PointM();
				float elevation = ControlPoints.GetElevation(spatialQueryFIDs[i]);
				if (double.IsNaN(elevation))
				{
					continue;
				}
				double actualDistance = 0.0;
				PointOnPolyline ptOnPoly = polygon.NearestPointOnPolyline(pointM, ref actualDistance);
				if (polygon.Contains(pointM))
				{
					pointM.Z = ControlPoints.GetElevation(spatialQueryFIDs[i]);
					internalCtrlPts.Add(pointM);
				}
				else if (GetGenerateBoundaryElevs(fid) && !GetUseShapeFileZ(fid) && actualDistance <= (double)elevPtTolerance)
				{
					double distanceAlongFast = polygon.DistanceAlongFast(ptOnPoly, ref cumulativeSegmentLengths);
					if (!doubleSet.Contains(distanceAlongFast))
					{
						profile.Add(distanceAlongFast, elevation);
						doubleSet.Add(distanceAlongFast);
						condition = true;
					}
				}
			}
			if ((!condition && GetGenerateBoundaryElevs(fid)) || GetUseShapeFileZ(fid))
			{
				List<PointOnPolyline> copyPointsAsPointOnPolyline = polygon.CopyPointsAsPointOnPolyline();
				List<int> intList = new List<int>();
				List<int> intListList = new List<int>();
				int count2 = polygon.Count - 1;
				float polygonTerm = default;
				for (int j = 0; j <= count2; j++)
				{
					double distanceAlongFast2 = polygon.DistanceAlongFast(copyPointsAsPointOnPolyline[j], ref cumulativeSegmentLengths);
					if (!doubleSet.Contains(distanceAlongFast2))
					{
						if (GetGenerateBoundaryElevs(fid))
						{
							intList.Add(j);
							intListList.Add(profile.Count);
						}
						else
						{
							polygonTerm = (float)polygon[j].Z;
						}
						profile.Add(distanceAlongFast2, polygonTerm);
						doubleSet.Add(distanceAlongFast2);
					}
				}
				PointM[] pointMs = new PointM[intList.Count - 1 + 1];
				int count3 = intList.Count - 1;
				for (int k = 0; k <= count3; k++)
				{
					pointMs[k] = polygon[intList[k]];
				}
				float[] layerValuesAtPoints = GetLayerValuesAtPoints(pointMs, useObstructions: false);
				int count4 = layerValuesAtPoints.Length - 1;
				for (int l = 0; l <= count4; l++)
				{
					profile[intListList[l]] = new Point2D(profile[intListList[l]].X, layerValuesAtPoints[l]);
				}
				if (!condition && GetGenerateBoundaryElevs(fid) && !GetUseShapeFileZ(fid))
				{
					List<Polyline> polylines = new List<Polyline>();
					polylines.Add(polygon);
					Profile[] profiles = _associatedTerrainLayer.ComputeProfiles(polylines, 0.01f, null, doObstructions: false);
					int count5 = profiles[0].Count - 1;
					for (int m = 0; m <= count5; m++)
					{
						float x = profiles[0][m].X;
						if (!doubleSet.Contains(x))
						{
							float y = profiles[0][m].Y;
							profile.Add(x, y);
							doubleSet.Add(x);
						}
					}
				}
			}
			if (profile.Count == 0)
			{
				float elevationModificationValue = GetElevationModificationValue(fid);
				profile.Add(0f, elevationModificationValue);
				profile.Add(polygon.Length, elevationModificationValue);
			}
			profile.SortByStation();
			return profile;
		}
	}

	private CachedPolygon EnsureGetPolygonCache(int polygonID, byte[] controlPointHash = null)
	{
		CachedPolygon polygonCache = GetPolygonCache(polygonID);
		if (controlPointHash == null)
		{
			controlPointHash = ControlPoints.GetDataHash();
		}
		if (polygonCache == null)
		{
			CachedPolygon cachedPolygon = ComputePolygonCache(polygonID, controlPointHash);
			SetPolygonCache(polygonID, cachedPolygon);
			return cachedPolygon;
		}
		if (polygonCache.Matches(GetElevationModificationValue(polygonID), controlPointHash, GetProfile(polygonID), GetGenerateBoundaryElevs(polygonID), GetUseShapeFileZ(polygonID)))
		{
			return polygonCache;
		}
		CachedPolygon cachedPolygon2 = ComputePolygonCache(polygonID, controlPointHash);
		SetPolygonCache(polygonID, cachedPolygon2);
		return cachedPolygon2;
	}

	private CachedPolygon ComputePolygonCache(int polygonID, byte[] controlPointHash = null)
	{
		if (!RasMapperLib.Polygon.IsValidPolygon(Polygon(polygonID)))
		{
			return null;
		}
		Profile perimeterProfile = null;
		DevelopmentTin developmentTin = ComputeDevTin(polygonID, ref perimeterProfile);
		if (developmentTin == null)
		{
			return null;
		}
		if (controlPointHash == null)
		{
			controlPointHash = ControlPoints.GetDataHash();
		}
		SetProfile(polygonID, perimeterProfile);
		return new CachedPolygon(developmentTin, GetElevationModificationValue(polygonID), controlPointHash, perimeterProfile, GetGenerateBoundaryElevs(polygonID), GetUseShapeFileZ(polygonID));
	}

	private DevelopmentTin ComputeDevTin(int fid, [Optional][DefaultParameterValue(null)] ref Profile perimeterProfile)
	{
		if (!Polygon(fid).IsValid())
		{
			return null;
		}
		GetElevationModificationValue(fid);
		PointMs internalCtrlPts = null;
		perimeterProfile = ComputePerimeterProfile(fid, ref internalCtrlPts);
		PointMs pointMs = ComputePerimeterPointsFromPerimeterProfile(this, fid, perimeterProfile);
		Polygon perimeter = new Polygon(pointMs);
		return new DelaunayTINFactory(0.01).ComputeTIN(MBR: pointMs.ComputeExtent().Ratio(3.0), perimeter: perimeter, breaklines: null, massPoints: internalCtrlPts, stitches: null);
	}

	internal static PointMs ComputePerimeterPointsFromPerimeterProfile(FeatureLayer layer, int fid, Profile perimeterProfile)
	{
		Polyline polyline = (Polyline)layer.Feature(fid);
		if (!polyline.IsValid())
		{
			return null;
		}
		polyline.CopyPoints();
		double[] cumulativeSegmentLengths = null;
		Polyline polyline2 = polyline;
		List<PointM> injectedPts = null;
		polyline = polyline2.MakePolylineZFromProfile(perimeterProfile, ref injectedPts);
		Dictionary<PointM, PointOnPolyline> pointOnPolylineByPointM = new Dictionary<PointM, PointOnPolyline>();
		List<PointOnPolyline> copyPointsAsPointOnPolyline = polyline.CopyPointsAsPointOnPolyline();
		checked
		{
			int count = copyPointsAsPointOnPolyline.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				pointOnPolylineByPointM[copyPointsAsPointOnPolyline[i].PointM] = copyPointsAsPointOnPolyline[i];
			}
			PointMs pointMs = polyline.CopyPoints();
			Dictionary<int, float> floatByInt = new Dictionary<int, float>();
			int count2 = pointMs.Count - 1;
			double actualDistance;
			for (int j = 0; j <= count2; j++)
			{
				PointM pointM = pointMs.PointM(j);
				PointOnPolyline pointOnPolyline;
				if (pointOnPolylineByPointM.ContainsKey(pointM))
				{
					pointOnPolyline = pointOnPolylineByPointM[pointM];
				}
				else
				{
					Polyline polyline3 = polyline;
					PointM ptM = pointM;
					actualDistance = 0.0;
					pointOnPolyline = polyline3.NearestPointOnPolyline(ptM, ref actualDistance);
					pointOnPolylineByPointM.Add(pointM, pointOnPolyline);
				}
				float distanceAlongFast = (float)polyline.DistanceAlongFast(pointOnPolyline, ref cumulativeSegmentLengths);
				if (distanceAlongFast <= perimeterProfile[0].X)
				{
					floatByInt.Add(j, (float)(polyline.Length + (double)distanceAlongFast));
					continue;
				}
				if (distanceAlongFast > perimeterProfile[perimeterProfile.Count - 1].X)
				{
					floatByInt.Add(j, distanceAlongFast);
					continue;
				}
				pointM.Z = perimeterProfile.Query(distanceAlongFast);
				pointMs[j] = pointM;
			}
			Profile profile = new Profile();
			profile.Add(perimeterProfile[perimeterProfile.Count - 1]);
			Polyline polyline4 = polyline;
			Polyline polyline5 = polyline;
			PointM ptM2 = pointMs[0];
			actualDistance = 0.0;
			_ = (float)polyline4.DistanceAlongFast(polyline5.NearestPointOnPolyline(ptM2, ref actualDistance), ref cumulativeSegmentLengths);
			profile.Add(new Point2D((float)polyline.Length + perimeterProfile[0].X, perimeterProfile[0].Y));
			foreach (KeyValuePair<int, float> localValue in floatByInt)
			{
				PointM pointM2 = pointMs[localValue.Key];
				pointM2.Z = profile.Query(localValue.Value);
				pointMs[localValue.Key] = pointM2;
			}
			return pointMs;
		}
	}

	public void ObstructRaster(RasterM raster, float[] values)
	{
		_Closure_0024__42_002D1 capturedLocals = new _Closure_0024__42_002D1(null);
		capturedLocals.raster = raster;
		capturedLocals.floatValues = values;
		checked
		{
			capturedLocals.tinVals = new float[capturedLocals.raster.CellCount - 1 + 1];
			int count = FeatureCount() - 1;
			_Closure_0024__42_002D0 capturedLocals2 = default;
			for (int i = 0; i <= count; i++)
			{
				capturedLocals2 = new _Closure_0024__42_002D0(capturedLocals2);
				capturedLocals2.outerClosure = capturedLocals;
				Polygon polygon = Polygon(i);
				if (!RasMapperLib.Polygon.IsValidPolygon(polygon) || float.IsNaN(GetElevationModificationValue(i)))
				{
					continue;
				}
				capturedLocals2.modType = GetElevationModificationType(i);
				Extent extent = polygon.ComputeExtent();
				if (capturedLocals2.outerClosure.raster.Overlaps(extent))
				{
					CachedPolygon cachedPolygon = EnsureGetPolygonCache(i);
					if (cachedPolygon != null)
					{
						Buffers.ArraySetAllVB(capturedLocals2.outerClosure.tinVals, -9999f);
						cachedPolygon.Tin.Rasterize(capturedLocals2.outerClosure.raster, capturedLocals2.outerClosure.tinVals);
						capturedLocals2.rcr = capturedLocals2.outerClosure.raster.RowColRange(extent, limitToRaster: true);
						System.Threading.Tasks.Parallel.For(capturedLocals2.rcr.MinRow, capturedLocals2.rcr.MaxRow + 1, capturedLocals2._Lambda_0024__0);
					}
				}
			}
		}
	}

	void IElevationModificationLayer.ObstructRaster(RasterM raster, float[] values)
	{
		//ILSpy generated this explicit interface implementation from .override directive in ObstructRaster
		this.ObstructRaster(raster, values);
	}

	public void ObstructProfiles(IList<Polyline> polylines, IList<Profile> profiles)
	{
		BitArray bitArray = new BitArray(profiles.Count);
		SpatialIndex<int> spatialIndex = EnsureSpatialIndex();
		Dictionary<int, Polygon> polygonByInt = new Dictionary<int, Polygon>();
		Dictionary<int, SpatialIndex<int>> spatialIndexByInt = new Dictionary<int, SpatialIndex<int>>();
		checked
		{
			int count = polylines.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				List<int> elements = spatialIndex.GetElements(polylines[i]);
				int count2 = elements.Count - 1;
				for (int j = 0; j <= count2; j++)
				{
					int element = elements[j];
					Polygon polygon = Polygon(element);
					if (!RasMapperLib.Polygon.IsValidPolygon(polygon))
					{
						continue;
					}
					ElevationModificationType elevationModificationType = GetElevationModificationType(element);
					if (!polygonByInt.ContainsKey(element))
					{
						polygonByInt[element] = GeoProcessing.BufferPolygon(polygon, 1E-05, 100000.0);
					}
					polygon = polygonByInt[element];
					Extent extent = polygon.ComputeExtent();
					CachedPolygon cachedPolygon = EnsureGetPolygonCache(element);
					if (cachedPolygon == null)
					{
						continue;
					}
					Profile[] profilesArray = new Profile[1];
					cachedPolygon.Tin.ComputeProfiles(new Polyline[1] { polylines[i] }, profilesArray);
					Polyline polyline = polylines[i];
					if (!extent.Overlaps(polyline))
					{
						continue;
					}
					Profile pf = profiles[i];
					float polylineLength = (float)polyline.Length;
					SpatialIndex<int> segmentSpatialIdx = null;
					if (spatialIndexByInt.ContainsKey(element))
					{
						segmentSpatialIdx = spatialIndexByInt[element];
					}
					if (!bitArray[i])
					{
						pf.SortByStation();
						bitArray[i] = true;
					}
					if (polygon.Contains(polyline))
					{
						ElevationModificationGroup.ReplaceProfileSubsection(ref pf, polylineLength, 0f, polylineLength, elevationModificationType, profilesArray[0]);
						profiles[i] = pf;
						continue;
					}
					List<Tuple<double, double>> containedSubsegments = polygon.GetContainedSubsegments(polyline, ref segmentSpatialIdx);
					if (!spatialIndexByInt.ContainsKey(element))
					{
						spatialIndexByInt[element] = segmentSpatialIdx;
					}
					if (!containedSubsegments.IsNullOrEmpty())
					{
						int count3 = containedSubsegments.Count - 1;
						for (int k = 0; k <= count3; k++)
						{
							Tuple<double, double> tuple = containedSubsegments[k];
							ElevationModificationGroup.ReplaceProfileSubsection(ref pf, polylineLength, (float)tuple.Item1, (float)tuple.Item2, elevationModificationType, profilesArray[0]);
							profiles[i] = pf;
						}
					}
				}
			}
		}
	}

	void IElevationModificationLayer.ObstructProfiles(IList<Polyline> polylines, IList<Profile> profiles)
	{
		//ILSpy generated this explicit interface implementation from .override directive in ObstructProfiles
		this.ObstructProfiles(polylines, profiles);
	}

	public void ObstructPoints(IPoints points, float[] valuesPerPoint, SpatialIndex<int> pointSpIdx)
	{
		if (FeatureCount() == 0)
		{
			return;
		}
		List<int> elementsAtPoints = EnsureSpatialIndex().GetElementsAtPoints(points);
		checked
		{
			int count = elementsAtPoints.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				int elementsAtPoint = elementsAtPoints[i];
				if (!RasMapperLib.Polygon.IsValidPolygon(Polygon(elementsAtPoint)))
				{
					continue;
				}
				ElevationModificationType elevationModificationType = GetElevationModificationType(elementsAtPoint);
				CachedPolygon cachedPolygon = EnsureGetPolygonCache(elementsAtPoint);
				if (cachedPolygon == null)
				{
					continue;
				}
				float[] a = null;
				Buffers.ArraySetAllVB(ref a, -9999f, points.PointMCount());
				cachedPolygon.Tin.ComputePointElevations(points, a);
				int count2 = points.PointMCount() - 1;
				for (int j = 0; j <= count2; j++)
				{
					if (a[j] != -9999f)
					{
						valuesPerPoint[j] = ElevationModificationGroup.GetReplacementValue(valuesPerPoint[j], elevationModificationType, a[j]);
					}
				}
			}
		}
	}

	void IElevationModificationLayer.ObstructPoints(IPoints points, float[] valuesPerPoint, SpatialIndex<int> pointSpIdx)
	{
		//ILSpy generated this explicit interface implementation from .override directive in ObstructPoints
		this.ObstructPoints(points, valuesPerPoint, pointSpIdx);
	}

	protected override void OnFeatureChanged(FeatureChangedEventArgs args)
	{
		base.OnFeatureChanged(args);
		if (args.ChangeType == FeatureChangedType.FeatureAdded || args.ChangeType == FeatureChangedType.FeatureModified)
		{
			foreach (int featureIndicesChanged in args.FeatureIndicesChanged)
			{
				SetPolygonCache(featureIndicesChanged, null);
			}
		}
		if (args.ChangeType != FeatureChangedType.FeatureAdded)
		{
			return;
		}
		foreach (int featureIndicesChanged2 in args.FeatureIndicesChanged)
		{
			SetElevPtTolerance(featureIndicesChanged2, DefaultElevPtTol);
		}
	}

	public Tin ComputeTin(int fid)
	{
		Profile perimeterProfile = null;
		return ComputeDevTin(fid, ref perimeterProfile).MakeCleanTin();
	}

	public bool GetUseShapeFileZ(int fid)
	{
		return DataTableExtensions.GetBoolean(FeatureTable(), fid, "Use ShapeFile Z Elevations");
	}

	public void SetUseShapeFileZ(int fid, bool useShapeFileZ)
	{
		FeatureRow(fid)["Use ShapeFile Z Elevations"] = useShapeFileZ;
	}

	public bool GetGenerateBoundaryElevs(int fid)
	{
		return DataTableExtensions.GetBoolean(FeatureTable(), fid, "Generate Boundary Elevations");
	}

	public void SetGenerateBoundaryElevs(int fid, bool generateBoundaryElevs)
	{
		FeatureRow(fid)["Generate Boundary Elevations"] = generateBoundaryElevs;
	}

	public Profile GetProfile(int fid)
	{
		return GetProfileProperties(fid)?.GetProfile();
	}

	Profile IHasProfileProperties.GetProfile(int fid)
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetProfile
		return this.GetProfile(fid);
	}

	public void SetProfile(int fid, Profile pf)
	{
		InMemoryProfileInfo pfInfo = new InMemoryProfileInfo(pf);
		SetProfileProperties(fid, pfInfo);
	}

	void IHasProfileProperties.SetProfile(int fid, Profile pf)
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetProfile
		this.SetProfile(fid, pf);
	}

	private IProfileInfo GetProfileProperties(int fid)
	{
		return FeatureRow(fid)["Profile"] as IProfileInfo;
	}

	IProfileInfo IHasProfileProperties.GetProfileProperties(int fid)
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetProfileProperties
		return this.GetProfileProperties(fid);
	}

	private void SetProfileProperties(int fid, IProfileInfo pfInfo)
	{
		FeatureRow(fid)["Profile"] = pfInfo;
	}

	void IHasProfileProperties.SetProfileProperties(int fid, IProfileInfo pfInfo)
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetProfileProperties
		this.SetProfileProperties(fid, pfInfo);
	}

	public float GetElevPtTolerance(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Elev Pt Tolerance");
	}

	float IContainsControlPointsLayer.GetElevPtTolerance(int fid)
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetElevPtTolerance
		return this.GetElevPtTolerance(fid);
	}

	public void SetElevPtTolerance(int fid, float elevPtTolerance)
	{
		FeatureRow(fid)["Elev Pt Tolerance"] = elevPtTolerance;
	}

	public float GetElevationModificationValue(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Elevation Value");
	}

	public void SetElevationModificationValue(int fid, float elevationModificationValue)
	{
		FeatureRow(fid)["Elevation Value"] = elevationModificationValue;
	}

	public ElevationModificationType GetElevationModificationType(int fid)
	{
		object objectValue = RuntimeHelpers.GetObjectValue(FeatureRow(fid)["Elevation Type"]);
		if (objectValue == null || Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)))
		{
			return ElevationModificationType.SetValue;
		}
		return (ElevationModificationType)Conversions.ToInteger(objectValue);
	}

	public void SetElevationModificationType(int fid, ElevationModificationType modType)
	{
		FeatureRow(fid)["Elevation Type"] = modType;
	}

	private int IContainsControlPointsLayer_FeatureCount()
	{
		return FeatureCount();
	}

	int IContainsControlPointsLayer.FeatureCount()
	{
		//ILSpy generated this explicit interface implementation from .override directive in IContainsControlPointsLayer_FeatureCount
		return this.IContainsControlPointsLayer_FeatureCount();
	}

	public PointOnPolyline NearestPointOnPolyline(int idx, PointM pt)
	{
		Polygon polygon = Polygon(idx);
		double actualDistance = 0.0;
		return polygon.NearestPointOnPolyline(pt, ref actualDistance);
	}

	PointOnPolyline IContainsControlPointsLayer.NearestPointOnPolyline(int idx, PointM pt)
	{
		//ILSpy generated this explicit interface implementation from .override directive in NearestPointOnPolyline
		return this.NearestPointOnPolyline(idx, pt);
	}

	public Dictionary<int, PointM> GetElevationChangingPointOnAllPerimeters(int crtlPtFID)
	{
		Dictionary<int, PointM> elevationChangingPointOnAllPerimeters = new Dictionary<int, PointM>();
		checked
		{
			if (crtlPtFID <= ControlPoints.FeatureCount() - 1)
			{
				List<int> elements = EnsureSpatialIndex().GetElements(ControlPoints[crtlPtFID].PointM());
				int count = elements.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					int element = elements[i];
					PointOnPolyline pointOnPolyline = NearestPointOnPolyline(element, ControlPoints[crtlPtFID].PointM());
					double distanceTo = pointOnPolyline.PointM.DistanceTo(ControlPoints[crtlPtFID].PointM());
					float elevPtTolerance = GetElevPtTolerance(i);
					if (distanceTo < (double)elevPtTolerance)
					{
						elevationChangingPointOnAllPerimeters.Add(element, pointOnPolyline.PointM);
					}
				}
			}
			return elevationChangingPointOnAllPerimeters;
		}
	}

	Dictionary<int, PointM> IContainsControlPointsLayer.GetElevationChangingPointOnAllPerimeters(int crtlPtFID)
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetElevationChangingPointOnAllPerimeters
		return this.GetElevationChangingPointOnAllPerimeters(crtlPtFID);
	}

	public float GetLayerValueAtPoint(PointM pt, bool useObstructions)
	{
		PointM[] points = new PointM[1] { pt };
		return _associatedTerrainLayer.ComputePointElevations(points, useObstructions)[0];
	}

	float IContainsControlPointsLayer.GetLayerValueAtPoint(PointM pt, bool useObstructions)
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetLayerValueAtPoint
		return this.GetLayerValueAtPoint(pt, useObstructions);
	}

	public float[] GetLayerValuesAtPoints(PointM[] ptArr, bool useObstructions)
	{
		return _associatedTerrainLayer.ComputePointElevations(ptArr, useObstructions);
	}

	public void ControlPointsChanged(object sender, FeatureChangedEventArgs args)
	{
		using (new PlotLayerConsolidator(SharedData.RasMapper))
		{
			foreach (int featureIndicesChanged in args.FeatureIndicesChanged)
			{
				Dictionary<int, PointM> elevationChangingPointOnAllPerimeters = GetElevationChangingPointOnAllPerimeters(featureIndicesChanged);
				HashSet<int> idxs = new HashSet<int>(elevationChangingPointOnAllPerimeters.Keys);
				ForceFeatureChange(new FeatureChangedEventArgs(idxs, FeatureChangedType.FeatureModified), plotLayers: true);
			}
		}
	}

	void IContainsControlPointsLayer.ControlPointsChanged(object sender, FeatureChangedEventArgs args)
	{
		//ILSpy generated this explicit interface implementation from .override directive in ControlPointsChanged
		this.ControlPointsChanged(sender, args);
	}

	private CachedPolygon GetPolygonCache(int fid)
	{
		return FeatureRow(fid)["Cached Polygon Info"] as CachedPolygon;
	}

	private void SetPolygonCache(int fid, CachedPolygon cachedPolygon)
	{
		FeatureRow(fid)["Cached Polygon Info"] = cachedPolygon;
	}
}
