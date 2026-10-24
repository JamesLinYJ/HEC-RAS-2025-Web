using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
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

public class GroundLineModificationLayer : PolylineFeatureLayer, IElevationModificationLayer, IContainsControlPointsLayer, IHasProfileProperties
{
	private class CachedLevee
	{
		public float MaxReach;

		public float LeftSlope;

		public float RightSlope;

		public float TopWidth;

		public ElevationModificationType ElevationModType;

		public DevelopmentTin DevTin;

		public Tin Tin;

		public Profile CenterProfile;

		public Profile UserEnteredProfile;

		public byte[] ControlPointHash;

		public CachedLevee(DevelopmentTin devTin, float maxReach, float leftSlope, float rightSlope, float topWidth, ElevationModificationType elevModType, Profile centerPf, byte[] ctrlPtHash, Profile userEnteredProfile)
		{
			DevTin = devTin;
			Tin = devTin.MakeCleanTin();
			MaxReach = maxReach;
			LeftSlope = leftSlope;
			RightSlope = rightSlope;
			TopWidth = topWidth;
			CenterProfile = centerPf;
			ControlPointHash = ctrlPtHash;
			UserEnteredProfile = userEnteredProfile;
			ElevationModType = elevModType;
		}

		public bool Matches(float maxReach, float leftSlope, float rightSlope, float topWidth, ElevationModificationType elevModType, byte[] ctrlPtHash, Profile userEnteredProfile)
		{
			bool sequenceEqual = false;
			if (UserEnteredProfile == null && userEnteredProfile == null)
			{
				sequenceEqual = true;
			}
			else if (UserEnteredProfile != null && userEnteredProfile != null)
			{
				sequenceEqual = UserEnteredProfile.SequenceEqual(userEnteredProfile);
			}
			if (MaxReach == maxReach && LeftSlope == leftSlope && RightSlope == rightSlope && TopWidth == topWidth && ElevationModType == elevModType && sequenceEqual && Hash.HashEquals(ControlPointHash, ctrlPtHash))
			{
				return true;
			}
			return false;
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__55_002D0
	{
		public RowColRangeM rcr;

		public ElevationModificationType modType;

		public _Closure_0024__55_002D1 outerClosure;

		public _Closure_0024__55_002D0(_Closure_0024__55_002D0 other)
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
	internal sealed class _Closure_0024__55_002D1
	{
		public RasterM raster;

		public float[] floatValues;

		public float[] tinVals;

		public _Closure_0024__55_002D1(_Closure_0024__55_002D1 other)
		{
			if (other != null)
			{
				raster = other.raster;
				floatValues = other.floatValues;
				tinVals = other.tinVals;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__58_002D0
	{
		public IList<Polyline> polylines;

		public Tin tin;

		public RasterizeSegment siRasterizeSegment;

		public SpatialIndex<int> triSpatialIndex;

		public _Closure_0024__58_002D0(_Closure_0024__58_002D0 other)
		{
			if (other != null)
			{
				polylines = other.polylines;
				tin = other.tin;
				siRasterizeSegment = other.siRasterizeSegment;
				triSpatialIndex = other.triSpatialIndex;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int fin)
		{
			checked
			{
				_Closure_0024__58_002D2 localValue = default;
				_Closure_0024__58_002D1 localValue2 = default;
				for (int i = start; i <= fin; i++)
				{
					localValue = new _Closure_0024__58_002D2(localValue);
					localValue.outerClosure = this;
					Polyline polyline = polylines[i];
					if (!RasMapperLib.Polyline.IsValidPolyline(polyline) || !polyline.MBROverlaps(tin))
					{
						continue;
					}
					localValue.segmentProfile = new Profile();
					localValue.segStartStation = 0.0;
					int count = polyline.SegmentCount() - 1;
					for (int j = 0; j <= count; j++)
					{
						localValue2 = new _Closure_0024__58_002D1(localValue2);
						localValue2.outerClosure = localValue;
						localValue2.segment = polyline.Segment(j);
						if (localValue2.segment.MBROverlaps(tin))
						{
							siRasterizeSegment.ComputeCells(localValue2.segment, localValue2._Lambda_0024__1);
						}
						localValue2.outerClosure.segStartStation = localValue2.outerClosure.segStartStation + localValue2.segment.Length;
					}
					if (localValue.segmentProfile.Count > 0)
					{
						localValue.segmentProfile.SortByStation();
					}
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__58_002D1
	{
		public SegmentM segment;

		public _Closure_0024__58_002D2 outerClosure;

		public _Closure_0024__58_002D1(_Closure_0024__58_002D1 other)
		{
			if (other != null)
			{
				segment = other.segment;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int n)
		{
			List<int> bin = outerClosure.outerClosure.triSpatialIndex.Bins[n];
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
	internal sealed class _Closure_0024__58_002D2
	{
		public Profile segmentProfile;

		public double segStartStation;

		public _Closure_0024__58_002D0 outerClosure;

		public _Closure_0024__58_002D2(_Closure_0024__58_002D2 other)
		{
			if (other != null)
			{
				segmentProfile = other.segmentProfile;
				segStartStation = other.segStartStation;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__66_002D0
	{
		public float maxReach;

		public float topElevation;

		public double baseElevation;

		public float topWidth;

		public List<Polyline> breaklines;

		public _Closure_0024__66_002D0(_Closure_0024__66_002D0 other)
		{
			if (other != null)
			{
				maxReach = other.maxReach;
				topElevation = other.topElevation;
				baseElevation = other.baseElevation;
				topWidth = other.topWidth;
				breaklines = other.breaklines;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(PointM startPoint, PointM perpUnitVec)
		{
			SegmentM segmentM = new SegmentM(startPoint, startPoint.Add(perpUnitVec));
			SegmentM segmentM2 = new SegmentM(startPoint, startPoint.Add(perpUnitVec.NegateXY()));
			segmentM = segmentM.PerpendicularSegment();
			segmentM2 = segmentM2.PerpendicularSegment();
			segmentM.ExtendB(maxReach);
			segmentM2.ExtendB(maxReach);
			segmentM.A.Z = topElevation;
			segmentM.B.Z = baseElevation;
			segmentM2.A.Z = topElevation;
			segmentM2.B.Z = baseElevation;
			PointMs pointMs = new PointMs { segmentM.B };
			PointM pointAtDistance = segmentM.PointAtDistance(topWidth / 2f);
			pointAtDistance.Z = topElevation;
			pointMs.Add(pointAtDistance);
			pointMs.Add(segmentM.A);
			PointM pointAtDistance2 = segmentM2.PointAtDistance(topWidth / 2f);
			pointAtDistance2.Z = topElevation;
			pointMs.Add(pointAtDistance2);
			pointMs.Add(segmentM2.B);
			breaklines.Add(new Polyline(pointMs));
		}
	}

	public const string sTopElevation = "Top Elevation";

	public const string sTopWidth = "Top Width";

	public const string sLeftSlope = "Left Slope";

	public const string sRightSlope = "Right Slope";

	public const string sLeveeCache = "Cached Levee Info";

	public const string sMaxReach = "Max Reach";

	public const string sElevPtTol = "Elev Pt Tolerance";

	public const string sProfile = "Profile";

	public const string LeveeType = "Levee";

	internal TerrainLayer _associatedTerrainLayer;

	private ElevationModificationType _defaultModifcationType;

	private const int DEFAULTDEFAULTELEVPTTOL = 50;

	private int _defaultElevPtTol;

	public PointM FirstLeftBoundingPoint;

	public PointM FirstLeftRidgePoint;

	public PointM FirstRightRidgePoint;

	public PointM FirstRightBoundingPoint;

	public ElevationControlPointLayer ControlPoints;

	public ElevationModificationType DefaultModificationType
	{
		get
		{
			return _defaultModifcationType;
		}
		private set
		{
			_defaultModifcationType = value;
		}
	}

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

	public GroundLineModificationLayer(string lyrName, TerrainLayer terrainLayer, ElevationModificationType defaultModificationType = ElevationModificationType.TakeHigher)
		: base(lyrName)
	{
		CanMoveToAnotherLevel = false;
		CanMoveWithinLevel = false;
		UsesParentSourceFilename = true;
		ForwardEditRequestsToParent = true;
		_associatedTerrainLayer = terrainLayer;
		DefaultModificationType = defaultModificationType;
		DefaultElevPtTol = 50;
		ControlPoints = new ElevationControlPointLayer("Control Points", HDFGroup + "/Control Points", useParentSourceFileName: true, GetLayerValueAtPoint, GetElevationChangingPointOnAllPerimeters);
		ControlPoints.FeatureChanged += ControlPointsChanged;
		Nodes.Add(ControlPoints);
	}

	public GroundLineModificationLayer(DataTable overrideDT)
		: base("Breaklines")
	{
		CanMoveToAnotherLevel = false;
		CanMoveWithinLevel = false;
		UsesParentSourceFilename = true;
		ForwardEditRequestsToParent = true;
		OverrideFeatureTable(overrideDT.Copy());
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		ElevationModificationGroup.InitializeElevationModTypeSerializer();
		AddManagedAttributeColumn("Name", "Name", typeof(string));
		AddManagedAttributeColumn("Elevation Type", "Elevation Type", typeof(ElevationModificationType), null, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Top Elevation", "Top Elevation", typeof(float), null, DataColumnViewState.Invisible);
		AddManagedAttributeColumn("Top Width", "Top Width", typeof(float));
		AddManagedAttributeColumn("Left Slope", "Left Slope", typeof(float));
		AddManagedAttributeColumn("Right Slope", "Right Slope", typeof(float));
		AddManagedAttributeColumn("Max Reach", "Max Reach", typeof(float));
		AddManagedAttributeColumn("Elev Pt Tolerance", "Elev Pt Tolerance", typeof(float));
		AddAttributeColumn("Profile", typeof(IProfileInfo), null, DataColumnViewState.GrayedOut);
		AddAttributeColumn("Cached Levee Info", typeof(CachedLevee), null, DataColumnViewState.Invisible);
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
				HDF5Storage.ReadPolyFeatures(this, h5Reader, HDFGroup + "/Polyline");
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
		checked
		{
			using (H5Writer h5Writer = new H5Writer(SourceFilename))
			{
				if (FeatureCount() == 0)
				{
					h5Writer.Delete(HDFGroup);
					return true;
				}
				string datasetName = HDFGroup + "/Attributes";
				WriteManagedColumns(h5Writer, datasetName);
				HDF5Storage.WritePolylineFeatures(this, h5Writer, HDFGroup + "/Polyline");
				try
				{
					List<Point2DCollection> point2DCollections = new List<Point2DCollection>();
					int count = FeatureCount() - 1;
					for (int i = 0; i <= count; i++)
					{
						point2DCollections.Add(GetProfile(i));
					}
					HDF5Storage.WritePointCollection(h5Writer, HDFGroup + "/Profile", point2DCollections);
				}
				catch (Exception ex)
				{
					ProjectData.SetProjectError(ex);
					Exception ex2 = ex;
					ProjectData.ClearProjectError();
				}
				h5Writer.WriteAttribute(HDFGroup, "Type", "Levee");
			}
			return true;
		}
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

	protected override void XMLLoadMetadata(XmlElement xmlLayer)
	{
		base.XMLLoadMetadata(xmlLayer);
		XmlNode xmlNode = xmlLayer.SelectSingleNode("DefaultModificationType");
		if (xmlNode != null)
		{
			DefaultModificationType = (ElevationModificationType)XML.IntegerAttribute(xmlNode, "Value", (int)DefaultModificationType);
		}
		XmlNode xmlNode2 = xmlLayer.SelectSingleNode("DefaultElevPtTol");
		if (xmlNode2 != null)
		{
			DefaultElevPtTol = XML.IntegerAttribute(xmlNode2, "Value", 50);
		}
	}

	protected override void XMLSaveMetadata(XmlDocument doc, XmlElement xmlLayer)
	{
		base.XMLSaveMetadata(doc, xmlLayer);
		XmlElement xmlElement = doc.CreateElement("DefaultModificationType");
		xmlElement.SetAttribute("Value", ((int)DefaultModificationType).ToString());
		xmlLayer.AppendChild(xmlElement);
		XmlElement xmlElement2 = doc.CreateElement("DefaultElevPtTol");
		xmlElement2.SetAttribute("Value", DefaultElevPtTol.ToString());
		xmlLayer.AppendChild(xmlElement2);
	}

	protected override void OnFeatureChanged(FeatureChangedEventArgs args)
	{
		base.OnFeatureChanged(args);
		if (args.ChangeType == FeatureChangedType.FeatureAdded || args.ChangeType == FeatureChangedType.FeatureModified)
		{
			foreach (int featureIndicesChanged in args.FeatureIndicesChanged)
			{
				SetLeveeCache(featureIndicesChanged, null);
			}
		}
		if (args.ChangeType != FeatureChangedType.FeatureAdded)
		{
			return;
		}
		foreach (int featureIndicesChanged2 in args.FeatureIndicesChanged)
		{
			SetElevationModificationType(featureIndicesChanged2, DefaultModificationType);
			SetElevPtTolerance(featureIndicesChanged2, DefaultElevPtTol);
		}
	}

	protected override TerrainLayer GetAssociatedTerrainLayer()
	{
		return _associatedTerrainLayer;
	}

	public override void StopEditing(bool save)
	{
		base.StopEditing(save);
		ControlPoints.StopEditing(save);
		ControlPoints.AfterStoppedEditing();
	}

	private CachedLevee EnsureGetLeveeCache(int leveeID, byte[] controlPointHash = null)
	{
		GetMaxReach(leveeID);
		CachedLevee leveeCache = GetLeveeCache(leveeID);
		if (controlPointHash == null)
		{
			controlPointHash = ControlPoints.GetDataHash();
		}
		if (leveeCache == null)
		{
			CachedLevee cachedLevee = ComputeLeveeCache(leveeID, controlPointHash);
			SetLeveeCache(leveeID, cachedLevee);
			return cachedLevee;
		}
		if (leveeCache.Matches(GetMaxReach(leveeID), GetLeftSlope(leveeID), GetUsableRightSlope(leveeID), GetTopWidth(leveeID), GetElevationModificationType(leveeID), controlPointHash, GetProfile(leveeID)))
		{
			return leveeCache;
		}
		CachedLevee cachedLevee2 = ComputeLeveeCache(leveeID, controlPointHash);
		SetLeveeCache(leveeID, cachedLevee2);
		return cachedLevee2;
	}

	private CachedLevee ComputeLeveeCache(int leveeID, byte[] controlPointHash = null)
	{
		if (!RasMapperLib.Polyline.IsValidPolyline(Polyline(leveeID)))
		{
			return null;
		}
		Profile centerProfile = null;
		DevelopmentTin developmentTin = ComputeDevTin(leveeID, ref centerProfile);
		if (developmentTin == null)
		{
			return null;
		}
		if (controlPointHash == null)
		{
			controlPointHash = ControlPoints.GetDataHash();
		}
		return new CachedLevee(developmentTin, GetMaxReach(leveeID), GetLeftSlope(leveeID), GetUsableRightSlope(leveeID), GetTopWidth(leveeID), GetElevationModificationType(leveeID), centerProfile, controlPointHash, GetProfile(leveeID));
	}

	public void ObstructRaster(RasterM raster, float[] values)
	{
		_Closure_0024__55_002D1 capturedLocals = new _Closure_0024__55_002D1(null);
		capturedLocals.raster = raster;
		capturedLocals.floatValues = values;
		if (FeatureCount() == 0)
		{
			return;
		}
		checked
		{
			capturedLocals.tinVals = new float[capturedLocals.raster.CellCount - 1 + 1];
			int count = FeatureCount() - 1;
			_Closure_0024__55_002D0 capturedLocals2 = default;
			for (int i = 0; i <= count; i++)
			{
				capturedLocals2 = new _Closure_0024__55_002D0(capturedLocals2);
				capturedLocals2.outerClosure = capturedLocals;
				Polyline polyline = Polyline(i);
				if (!RasMapperLib.Polyline.IsValidPolyline(polyline))
				{
					continue;
				}
				capturedLocals2.modType = GetElevationModificationType(i);
				float maxReach = GetMaxReach(i);
				Extent extent = polyline.GetExtent();
				extent = extent.Extend(maxReach);
				if (capturedLocals2.outerClosure.raster.Overlaps(extent))
				{
					CachedLevee cachedLevee = EnsureGetLeveeCache(i);
					if (cachedLevee != null)
					{
						Buffers.ArraySetAllVB(capturedLocals2.outerClosure.tinVals, -9999f);
						cachedLevee.Tin.Rasterize(capturedLocals2.outerClosure.raster, capturedLocals2.outerClosure.tinVals);
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
		if (FeatureCount() == 0)
		{
			return;
		}
		BitArray bitArray = new BitArray(profiles.Count);
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Polyline polyline = Polyline(i);
				if (!RasMapperLib.Polyline.IsValidPolyline(polyline))
				{
					continue;
				}
				ElevationModificationType elevationModificationType = GetElevationModificationType(i);
				float maxReach = GetMaxReach(i);
				Extent extent = polyline.GetExtent();
				extent = extent.Extend(maxReach);
				CachedLevee cachedLevee = EnsureGetLeveeCache(i);
				if (cachedLevee == null)
				{
					continue;
				}
				Profile[] profilesArray = new Profile[polylines.Count - 1 + 1];
				cachedLevee.Tin.ComputeProfiles(polylines, profilesArray);
				PointMs leftOffset = null;
				PointMs rightOffset = null;
				Polygon offsetPolygon = polyline.GetOffsetPolygon(maxReach, ref leftOffset, ref rightOffset);
				SpatialIndex<int> segmentSpatialIdx = null;
				int count2 = polylines.Count - 1;
				for (int j = 0; j <= count2; j++)
				{
					Polyline polyline2 = polylines[j];
					if (!extent.Overlaps(polyline2))
					{
						continue;
					}
					Profile pf = profiles[j];
					Profile profile = profilesArray[j];
					float polyline2Length = (float)polyline2.Length;
					if (pf.IsNullOrEmpty())
					{
						profiles[j] = profile;
					}
					if (profile.IsNullOrEmpty())
					{
						profiles[j] = pf;
					}
					if (!bitArray[j])
					{
						pf.SortByStation();
						bitArray[j] = true;
					}
					if (offsetPolygon.Contains(polyline2))
					{
						ElevationModificationGroup.ReplaceProfileSubsection(ref pf, polyline2Length, 0f, polyline2Length, elevationModificationType, profile);
						profiles[j] = pf;
						continue;
					}
					List<Tuple<double, double>> containedSubsegments = offsetPolygon.GetContainedSubsegments(polyline2, ref segmentSpatialIdx);
					if (!containedSubsegments.IsNullOrEmpty())
					{
						int count3 = containedSubsegments.Count - 1;
						for (int k = 0; k <= count3; k++)
						{
							Tuple<double, double> tuple = containedSubsegments[k];
							ElevationModificationGroup.ReplaceProfileSubsection(ref pf, polyline2Length, (float)tuple.Item1, (float)tuple.Item2, elevationModificationType, profile);
							profiles[j] = pf;
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
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (!RasMapperLib.Polyline.IsValidPolyline(Polyline(i)))
				{
					continue;
				}
				ElevationModificationType elevationModificationType = GetElevationModificationType(i);
				CachedLevee cachedLevee = EnsureGetLeveeCache(i);
				if (cachedLevee == null)
				{
					continue;
				}
				float[] a = null;
				Buffers.ArraySetAllVB(ref a, -9999f, points.PointMCount());
				cachedLevee.Tin.ComputePointElevations(points, a);
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

	public void ComputeProfilesParallel(Tin tin, IList<Polyline> polylines, Profile[] profiles, double tolerance = 0.0)
	{
		_Closure_0024__58_002D0 capturedLocals = default;
		_Closure_0024__58_002D0 capturedLocals2 = new _Closure_0024__58_002D0(capturedLocals);
		capturedLocals2.tin = tin;
		capturedLocals2.polylines = polylines;
		if (capturedLocals2.polylines == null || capturedLocals2.tin.TriangleCount() == 0)
		{
			return;
		}
		capturedLocals2.tin.EnsureTriangleSpatialIndex();
		capturedLocals2.triSpatialIndex = capturedLocals2.tin.GetTriangleSpatialIndex();
		capturedLocals2.siRasterizeSegment = new RasterizeSegment(capturedLocals2.triSpatialIndex.Raster);
		Utility.Parallel.SmartFor(capturedLocals2.polylines.Count, checked([SpecialName] (int start, int fin) =>
		{
			_Closure_0024__58_002D2 capturedLocals3 = default;
			_Closure_0024__58_002D1 capturedLocals4 = default;
			for (int i = start; i <= fin; i++)
			{
				capturedLocals3 = new _Closure_0024__58_002D2(capturedLocals3);
				capturedLocals3.outerClosure = capturedLocals2;
				Polyline polyline = capturedLocals2.polylines[i];
				if (RasMapperLib.Polyline.IsValidPolyline(polyline) && polyline.MBROverlaps(capturedLocals2.tin))
				{
					capturedLocals3.segmentProfile = new Profile();
					capturedLocals3.segStartStation = 0.0;
					int count = polyline.SegmentCount() - 1;
					for (int j = 0; j <= count; j++)
					{
						capturedLocals4 = new _Closure_0024__58_002D1(capturedLocals4);
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
					}
				}
			}
		}));
	}

	private Profile ComputeLeveeProfile(int fid)
	{
		Polyline polyline = Polyline(fid);
		if (!polyline.IsValid())
		{
			return null;
		}
		bool condition = false;
		bool condition2 = false;
		Profile profile = GetProfile(fid);
		Profile profile2 = new Profile();
		HashSet<double> doubleSet = new HashSet<double>();
		checked
		{
			if (profile != null)
			{
				_ = Polyline(fid).Length;
				float profileTerm = 0f;
				if (profile.Count > 0 && profile[0].X != 0f)
				{
					profileTerm = 0f - profile[0].X;
				}
				int count = profile.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					Point2D point2D = profile[i];
					Point2D point2D2 = new Point2D
					{
						X = point2D.X + profileTerm,
						Y = point2D.Y
					};
					if ((double)point2D.X > Polyline(fid).Length)
					{
						point2D2.X = (float)Polyline(fid).Length;
					}
					if (!doubleSet.Contains(point2D2.X))
					{
						profile2.Add(point2D2);
						doubleSet.Add(point2D2.X);
					}
				}
			}
			profile2.SortByStation();
			double length = polyline.Length;
			if (ControlPoints.Points().Count() == 0 && profile2.Count == 0)
			{
				profile2 = GetDefaultProfile(fid);
			}
			else
			{
				if (profile2.Count > 0 && profile2[0].X == 0f)
				{
					condition = true;
				}
				if (profile2.Count > 0 && (double)profile2[profile2.Count - 1].X == length)
				{
					condition2 = true;
				}
				float y = 0f;
				double maxValue = double.MaxValue;
				float elevation2 = 0f;
				double minValue = double.MinValue;
				float elevPtTolerance = GetElevPtTolerance(fid);
				Extent extent = Polyline(fid).ComputeExtent();
				extent = extent.Extend(elevPtTolerance * 2f);
				ControlPoints.ComputeExtent();
				ControlPoints.EnsureQuadtree(ControlPoints.Extent);
				List<int> spatialQueryFIDs = ControlPoints.SpatialQueryFIDs(extent);
				int count2 = spatialQueryFIDs.Count - 1;
				for (int j = 0; j <= count2; j++)
				{
					PointM ptM = ControlPoints[spatialQueryFIDs[j]].PointM();
					float elevation = ControlPoints.GetElevation(spatialQueryFIDs[j]);
					if (double.IsNaN(elevation))
					{
						continue;
					}
					double actualDistance = 0.0;
					PointOnPolyline pointOnPolyline = polyline.NearestPointOnPolyline(ptM, ref actualDistance);
					if (!(actualDistance <= (double)elevPtTolerance))
					{
						continue;
					}
					double distanceAlong = polyline.DistanceAlong(pointOnPolyline);
					if (!doubleSet.Contains(distanceAlong))
					{
						profile2.Add(distanceAlong, elevation);
						doubleSet.Add(distanceAlong);
						if (distanceAlong < maxValue)
						{
							maxValue = distanceAlong;
							y = elevation;
						}
						if (distanceAlong > minValue)
						{
							minValue = distanceAlong;
							elevation2 = elevation;
						}
					}
					if (pointOnPolyline.PointM.X == polyline[0].X && pointOnPolyline.PointM.Y == polyline[0].Y)
					{
						condition = true;
					}
					if (pointOnPolyline.PointM.X == polyline[polyline.Count - 1].X && pointOnPolyline.PointM.Y == polyline[polyline.Count - 1].Y)
					{
						condition2 = true;
					}
				}
				if (!condition && ControlPoints.FeatureCount() > 0 && (profile == null || profile.Count == 0) && !doubleSet.Contains(0.0))
				{
					profile2.Add(0f, y);
					doubleSet.Add(0.0);
				}
				if (!condition2 && ControlPoints.FeatureCount() > 0 && (profile == null || profile.Count == 0))
				{
					PointM ptM2 = polyline[polyline.Count - 1];
					double actualDistance2 = 0.0;
					PointOnPolyline ptOnPoly = polyline.NearestPointOnPolyline(ptM2, ref actualDistance2);
					double distanceAlong2 = polyline.DistanceAlong(ptOnPoly);
					if (!doubleSet.Contains(distanceAlong2))
					{
						profile2.Add(distanceAlong2, elevation2);
						doubleSet.Add(distanceAlong2);
					}
				}
				profile2.SortByStation();
				Profile profile3 = new Profile();
				double computed = 100.0;
				if (SharedData.Units == SharedData.EnumUnits.SIUnits)
				{
					computed /= 3.281;
				}
				double segmentMLengthSum = 0.0;
				int count3 = polyline.SegmentCount() - 1;
				for (int k = 0; k <= count3; k++)
				{
					SegmentM segmentM = polyline.Segment(k);
					if (!(segmentM.Length > computed))
					{
						continue;
					}
					int count4 = (int)System.Math.Floor(segmentM.Length / computed) - 1;
					for (int l = 0; l <= count4; l++)
					{
						double computed2 = (double)(l + 1) * computed;
						double segmentMLengthSum2 = segmentMLengthSum;
						if (!doubleSet.Contains(segmentMLengthSum2 + computed2))
						{
							profile3.Add(segmentMLengthSum2 + computed2, profile2.Query((float)(segmentMLengthSum2 + computed2)));
						}
					}
					segmentMLengthSum += segmentM.Length;
				}
				profile2.AddRange(profile3);
			}
			profile2.SortByStation();
			if (System.Math.Abs((double)profile2.LastStation() - length) > 0.01)
			{
				profile2.Add(new Point2D((float)length, profile2.Last().Y));
			}
			return profile2;
		}
	}

	public Profile GetDefaultProfile(int fid)
	{
		return new Profile
		{
			new Point2D(0f, GetLayerValueAtPoint(Polyline(fid)[0], useObstructions: false)),
			new Point2D((float)Polyline(fid).Length, GetLayerValueAtPoint(Polyline(fid)[checked(Polyline(fid).Count - 1)], useObstructions: false))
		};
	}

	public int ApproximateNumInjectedPts(int fid)
	{
		Polyline polyline = Polyline(fid);
		if (!polyline.IsValid())
		{
			return 0;
		}
		int ordinal = 0;
		checked
		{
			int count = polyline.Count - 2;
			for (int i = 0; i <= count; i++)
			{
				SegmentM segmentM = new SegmentM(polyline[i], polyline[i + 1]);
				if (segmentM.Length > 100.0)
				{
					ordinal += (int)System.Math.Floor(segmentM.Length / 100.0) - 1;
				}
			}
			return ordinal;
		}
	}

	private DevelopmentTin ComputeDevTin(int fid, [Optional][DefaultParameterValue(null)] ref Profile centerProfile)
	{
		Polyline polyline = Polyline(fid);
		if (!polyline.IsValid())
		{
			return null;
		}
		float maxReach = GetMaxReach(fid);
		if (maxReach <= 0f)
		{
			return null;
		}
		float topWidth = GetTopWidth(fid);
		if (topWidth <= 0f)
		{
			return null;
		}
		centerProfile = ComputeLeveeProfile(fid);
		List<PointM> injectedPts = null;
		Polyline polyline2 = polyline.MakePolylineZFromProfile(centerProfile, ref injectedPts);
		PointMs copyPoints = polyline2.CopyPoints();
		PointMs leftOffset = null;
		PointMs rightOffset = null;
		polyline2.GetOffsetPolygonZ(maxReach, ref leftOffset, ref rightOffset);
		PointMs leftOffset2 = null;
		PointMs rightOffset2 = null;
		polyline2.GetOffsetPolygonZ(topWidth, ref leftOffset2, ref rightOffset2);
		RasMapperLib.Polyline.FromBuffers(leftOffset2);
		RasMapperLib.Polyline.FromBuffers(rightOffset2);
		RasMapperLib.Polyline.FromBuffers(leftOffset);
		RasMapperLib.Polyline.FromBuffers(rightOffset);
		double[] cumulativeSegmentLengths = null;
		PointOnPolyline pointOnPolyline = null;
		checked
		{
			int count = leftOffset2.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM pointM = leftOffset2.PointM(i);
				if (double.IsNaN(pointM.Z))
				{
					PointM ptm = pointM;
					PointOnPolyline lastPop = pointOnPolyline;
					double actualDistance = 0.0;
					pointOnPolyline = polyline.NearestPointOnPolyline(ptm, lastPop, ref actualDistance);
					float xVal = (float)polyline.DistanceAlongFast(pointOnPolyline, ref cumulativeSegmentLengths);
					pointM.Z = centerProfile.Query(xVal);
					leftOffset2[i] = pointM;
				}
			}
			pointOnPolyline = null;
			int count2 = rightOffset2.Count - 1;
			for (int j = 0; j <= count2; j++)
			{
				PointM pointM2 = rightOffset2.PointM(j);
				if (double.IsNaN(pointM2.Z))
				{
					PointM ptm2 = pointM2;
					PointOnPolyline lastPop2 = pointOnPolyline;
					double actualDistance = 0.0;
					pointOnPolyline = polyline.NearestPointOnPolyline(ptm2, lastPop2, ref actualDistance);
					float xVal2 = (float)polyline.DistanceAlongFast(pointOnPolyline, ref cumulativeSegmentLengths);
					pointM2.Z = centerProfile.Query(xVal2);
					rightOffset2[j] = pointM2;
				}
			}
			float leftSlope = GetLeftSlope(fid);
			float usableRightSlope = GetUsableRightSlope(fid);
			float maxReachTopWidthTerm = maxReach / 2f - topWidth / 2f;
			pointOnPolyline = null;
			int count3 = leftOffset.Count - 1;
			for (int k = 0; k <= count3; k++)
			{
				PointM pointM3 = leftOffset.PointM(k);
				float queryCenterProfileTerm;
				if (double.IsNaN(pointM3.Z))
				{
					PointM ptm3 = pointM3;
					PointOnPolyline lastPop3 = pointOnPolyline;
					double actualDistance = 0.0;
					pointOnPolyline = polyline.NearestPointOnPolyline(ptm3, lastPop3, ref actualDistance);
					float xVal3 = (float)polyline.DistanceAlongFast(pointOnPolyline, ref cumulativeSegmentLengths);
					queryCenterProfileTerm = centerProfile.Query(xVal3);
				}
				else
				{
					queryCenterProfileTerm = (float)pointM3.Z;
				}
				pointM3.Z = (double)queryCenterProfileTerm - (double)maxReachTopWidthTerm * (1.0 / (double)leftSlope);
				leftOffset[k] = pointM3;
			}
			pointOnPolyline = null;
			int count4 = rightOffset.Count - 1;
			for (int l = 0; l <= count4; l++)
			{
				PointM pointM4 = rightOffset.PointM(l);
				float queryCenterProfileTerm2;
				if (double.IsNaN(pointM4.Z))
				{
					PointM ptm4 = pointM4;
					PointOnPolyline lastPop4 = pointOnPolyline;
					double actualDistance = 0.0;
					pointOnPolyline = polyline.NearestPointOnPolyline(ptm4, lastPop4, ref actualDistance);
					float xVal4 = (float)polyline.DistanceAlongFast(pointOnPolyline, ref cumulativeSegmentLengths);
					queryCenterProfileTerm2 = centerProfile.Query(xVal4);
				}
				else
				{
					queryCenterProfileTerm2 = (float)pointM4.Z;
				}
				pointM4.Z = (double)queryCenterProfileTerm2 + (double)maxReachTopWidthTerm * (1.0 / (double)usableRightSlope);
				rightOffset[l] = pointM4;
			}
			if (leftOffset2.Count > 0)
			{
				FirstLeftRidgePoint = leftOffset2[0];
			}
			if (rightOffset2.Count > 0)
			{
				FirstRightRidgePoint = rightOffset2[0];
			}
			if (leftOffset.Count > 0)
			{
				FirstLeftBoundingPoint = leftOffset[0];
			}
			if (rightOffset.Count > 0)
			{
				FirstRightBoundingPoint = rightOffset[0];
			}
			return RibbonTINFactory.CreateTIN(new List<IPoints> { leftOffset, leftOffset2, copyPoints, rightOffset2, rightOffset }, 0.01);
		}
	}

	private static PointMs ReformPointsFromSpatialIndex(Polyline poly, SpatialIndex<int> polySI)
	{
		List<int> elements = polySI.GetElements(poly);
		SortedDictionary<double, PointM> sortedDictionary = new SortedDictionary<double, PointM>();
		double[] cumulativeSegmentLengths = null;
		PointOnPolyline pointOnPolyline = null;
		foreach (PointM poly2 in poly)
		{
			PointOnPolyline lastPop = pointOnPolyline;
			double actualDistance = 0.0;
			pointOnPolyline = poly.NearestPointOnPolyline(poly2, lastPop, ref actualDistance);
			sortedDictionary[poly.DistanceAlongFast(pointOnPolyline, ref cumulativeSegmentLengths)] = poly2;
		}
		foreach (int element in elements)
		{
			SegmentM segmentM = poly.Segment(element);
			pointOnPolyline = new PointOnPolyline(segmentM.A, element, 0.0);
			PointM a = segmentM.A;
			PointOnPolyline lastPop2 = pointOnPolyline;
			double actualDistance = 0.0;
			sortedDictionary[poly.DistanceAlongFast(poly.NearestPointOnPolyline(a, lastPop2, ref actualDistance), ref cumulativeSegmentLengths)] = segmentM.A;
		}
		return new PointMs(sortedDictionary.Values);
	}

	private static void InjectIntersectionPoint(Polyline polylineUsedInSI, SpatialIndex<int> polyLineSpatialIndex, PointM intersectionPt, SegmentM ray, Dictionary<int, Tuple<SegmentM, SegmentM>> segIdxToAAndBSegs)
	{
		List<int> elements = polyLineSpatialIndex.GetElements(ray);
		SortedDictionary<double, int> sortedDictionary = new SortedDictionary<double, int>();
		foreach (int element in elements)
		{
			SegmentM segmentM = polylineUsedInSI.Segment(element);
			PointM pt = intersectionPt;
			SegmentM.RelativePosition relativePosition = SegmentM.RelativePosition.Undefined;
			sortedDictionary[segmentM.DistanceToPoint(pt, ref relativePosition)] = element;
		}
		double x = default;
		double y = default;
		double t = default;
		double t2 = default;
		foreach (int ordinal in sortedDictionary.Values)
		{
			SegmentM segmentM2 = polylineUsedInSI.Segment(ordinal);
			if (segmentM2.Intersects(ray))
			{
				segmentM2.IntersectPoint(ray, ref x, ref y, ref t, ref t2);
				SegmentM segmentM3 = new SegmentM(segmentM2.A, new PointM(x, y, intersectionPt.Z));
				SegmentM segmentM4 = new SegmentM(new PointM(x, y, intersectionPt.Z), segmentM2.B);
				segIdxToAAndBSegs[ordinal] = new Tuple<SegmentM, SegmentM>(segmentM3, segmentM4);
				break;
			}
		}
	}

	private static PointMs ConsolidateSegments(Polyline originalPL, Dictionary<int, Tuple<SegmentM, SegmentM>> segIdxToAAndBSegs)
	{
		PointMs pointMs = new PointMs();
		checked
		{
			int count = originalPL.SegmentCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (segIdxToAAndBSegs.ContainsKey(i))
				{
					if (i == 0)
					{
						pointMs.Add(segIdxToAAndBSegs[i].Item1.A);
					}
					pointMs.Add(segIdxToAAndBSegs[i].Item1.B);
					pointMs.Add(segIdxToAAndBSegs[i].Item2.B);
				}
				else
				{
					if (i == 0)
					{
						pointMs.Add(originalPL.Segment(i).A);
					}
					pointMs.Add(originalPL.Segment(i).B);
				}
			}
			return pointMs;
		}
	}

	private DevelopmentTin ComputeDevTin_OLD(int fid, [Optional][DefaultParameterValue(null)] ref Polygon boundingPgon)
	{
		_Closure_0024__66_002D0 capturedLocals = default;
		_Closure_0024__66_002D0 capturedLocals2 = new _Closure_0024__66_002D0(capturedLocals);
		Polyline polyline = Polyline(fid);
		if (!polyline.IsValid())
		{
			return null;
		}
		capturedLocals2.maxReach = GetMaxReach(fid);
		if (capturedLocals2.maxReach <= 0f)
		{
			return null;
		}
		capturedLocals2.topWidth = GetTopWidth(fid);
		if (capturedLocals2.topWidth <= 0f)
		{
			return null;
		}
		capturedLocals2.topElevation = GetTopElevation(fid);
		float width = capturedLocals2.maxReach * 2f;
		PointMs leftOffset = null;
		PointMs rightOffset = null;
		boundingPgon = polyline.GetOffsetPolygon(width, ref leftOffset, ref rightOffset);
		PointMs leftOffset2 = null;
		PointMs rightOffset2 = null;
		polyline.GetOffsetPolygon(capturedLocals2.topWidth, ref leftOffset2, ref rightOffset2);
		checked
		{
			int count = leftOffset2.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM leftOffset22 = leftOffset2.PointM(i);
				leftOffset22.Z = capturedLocals2.topElevation;
				leftOffset2[i] = leftOffset22;
			}
			int count2 = rightOffset2.Count - 1;
			for (int j = 0; j <= count2; j++)
			{
				PointM rightOffset22 = rightOffset2.PointM(j);
				rightOffset22.Z = capturedLocals2.topElevation;
				rightOffset2[j] = rightOffset22;
			}
			PointMs pointMs = polyline.CopyPoints();
			int count3 = pointMs.Count - 1;
			for (int k = 0; k <= count3; k++)
			{
				PointM pointMs4 = pointMs.PointM(k);
				pointMs4.Z = capturedLocals2.topElevation;
				pointMs[k] = pointMs4;
			}
			float capturedLocals3 = capturedLocals2.maxReach - capturedLocals2.topWidth / 2f;
			float leftSlope = GetLeftSlope(fid);
			if (GetElevationModificationType(fid) == ElevationModificationType.TakeLower)
			{
				leftSlope *= -1f;
			}
			capturedLocals2.baseElevation = capturedLocals2.topElevation - capturedLocals3 * leftSlope;
			PointMs pointMs2 = boundingPgon.CopyPoints();
			int count4 = pointMs2.Count - 1;
			for (int l = 0; l <= count4; l++)
			{
				PointM pointMs22 = pointMs2.PointM(l);
				pointMs22.Z = capturedLocals2.baseElevation;
				pointMs2[l] = pointMs22;
			}
			boundingPgon = new Polygon(pointMs2);
			capturedLocals2.breaklines = new List<Polyline>();
			capturedLocals2.breaklines.Add(new Polyline(pointMs));
			capturedLocals2.breaklines.Add(new Polyline(leftOffset2));
			capturedLocals2.breaklines.Add(new Polyline(rightOffset2));
			Action<PointM, PointM> action = [SpecialName] (PointM startPoint, PointM perpUnitVec) =>
			{
				SegmentM segmentM2 = new SegmentM(startPoint, startPoint.Add(perpUnitVec));
				SegmentM segmentM3 = new SegmentM(startPoint, startPoint.Add(perpUnitVec.NegateXY()));
				segmentM2 = segmentM2.PerpendicularSegment();
				segmentM3 = segmentM3.PerpendicularSegment();
				segmentM2.ExtendB(capturedLocals2.maxReach);
				segmentM3.ExtendB(capturedLocals2.maxReach);
				segmentM2.A.Z = capturedLocals2.topElevation;
				segmentM2.B.Z = capturedLocals2.baseElevation;
				segmentM3.A.Z = capturedLocals2.topElevation;
				segmentM3.B.Z = capturedLocals2.baseElevation;
				PointMs pointMs3 = new PointMs { segmentM2.B };
				PointM pointAtDistance = segmentM2.PointAtDistance(capturedLocals2.topWidth / 2f);
				pointAtDistance.Z = capturedLocals2.topElevation;
				pointMs3.Add(pointAtDistance);
				pointMs3.Add(segmentM2.A);
				PointM pointAtDistance2 = segmentM3.PointAtDistance(capturedLocals2.topWidth / 2f);
				pointAtDistance2.Z = capturedLocals2.topElevation;
				pointMs3.Add(pointAtDistance2);
				pointMs3.Add(segmentM3.B);
				capturedLocals2.breaklines.Add(new Polyline(pointMs3));
			};
			int count5 = polyline.SegmentCount() - 2;
			for (int ordinal = 0; ordinal <= count5; ordinal++)
			{
				SegmentM segmentM = polyline.Segment(ordinal);
				SegmentM segOther = polyline.Segment(ordinal + 1);
				PointM averagedUnitSegment = segmentM.AveragedUnitSegment(segOther);
				action(segmentM.B, averagedUnitSegment);
			}
			action(polyline.Segment(0).A, polyline.Segment(0).PerpendicularUnitVector());
			action(polyline.Segment(polyline.SegmentCount() - 1).B, polyline.Segment(polyline.SegmentCount() - 1).PerpendicularUnitVector());
			return new DelaunayTINFactory(0.001).ComputeTIN(boundingPgon, capturedLocals2.breaklines, null, null, removeDeletedTriangles: false);
		}
	}

	public Tin ComputeTin(int fid)
	{
		Profile centerProfile = null;
		return ComputeDevTin(fid, ref centerProfile).MakeCleanTin();
	}

	public override void OnStartEditSession()
	{
		base.OnStartEditSession();
		ControlPoints.OnStartEditSession();
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

	private CachedLevee GetLeveeCache(int fid)
	{
		return FeatureRow(fid)["Cached Levee Info"] as CachedLevee;
	}

	private void SetLeveeCache(int fid, CachedLevee cachedLevee)
	{
		FeatureRow(fid)["Cached Levee Info"] = cachedLevee;
	}

	public float GetTopElevation(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Top Elevation");
	}

	public void SetTopElevation(int fid, float topElevation)
	{
		FeatureRow(fid)["Top Elevation"] = topElevation;
	}

	public float GetTopWidth(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Top Width");
	}

	public void SetTopWidth(int fid, float topWidth)
	{
		FeatureRow(fid)["Top Width"] = topWidth;
	}

	public float GetLeftSlope(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Left Slope");
	}

	public void SetLeftSlope(int fid, float leftSlope)
	{
		FeatureRow(fid)["Left Slope"] = leftSlope;
	}

	public float GetRightSlope(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Right Slope");
	}

	public void SetRightSlope(int fid, float rightSlope)
	{
		FeatureRow(fid)["Right Slope"] = rightSlope;
	}

	public float GetUsableRightSlope(int fid)
	{
		float rightSlope = GetRightSlope(fid);
		if (float.IsNaN(rightSlope))
		{
			return GetLeftSlope(fid);
		}
		return rightSlope;
	}

	public float GetMaxReach(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Max Reach");
	}

	public void SetMaxReach(int fid, float maxReach)
	{
		FeatureRow(fid)["Max Reach"] = maxReach;
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

	public PointOnPolyline NearestPointOnPolyline(int idx, PointM pt)
	{
		Polyline polyline = Polyline(idx);
		double actualDistance = 0.0;
		return polyline.NearestPointOnPolyline(pt, ref actualDistance);
	}

	PointOnPolyline IContainsControlPointsLayer.NearestPointOnPolyline(int idx, PointM pt)
	{
		//ILSpy generated this explicit interface implementation from .override directive in NearestPointOnPolyline
		return this.NearestPointOnPolyline(idx, pt);
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

	public Dictionary<int, PointM> GetElevationChangingPointOnAllPerimeters(int crtlPtFID)
	{
		Dictionary<int, PointM> elevationChangingPointOnAllPerimeters = new Dictionary<int, PointM>();
		checked
		{
			if (crtlPtFID <= ControlPoints.FeatureCount() - 1)
			{
				List<int> elements = EnsureSpatialIndex().GetElements(ControlPoints[crtlPtFID]);
				int count = elements.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					int element = elements[i];
					PointOnPolyline pointOnPolyline = NearestPointOnPolyline(element, ControlPoints[crtlPtFID].PointM());
					double distanceTo = pointOnPolyline.PointM.DistanceTo(ControlPoints[crtlPtFID].PointM());
					float elevPtTolerance = GetElevPtTolerance(element);
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
}
