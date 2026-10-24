using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Xml;
using H5Assist;
using H5Assist.Identifiers;
using H5Assist.VB;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Mapping;
using RasMapperLib.Names;
using RasMapperLib.Structures;
using RasMapperLib.TinHelpers;
using RasMapperLib.Utilities;
using Utility.Extensions;
using Utility.Progress;
using Utility.Systems;

namespace RasMapperLib;

public class StructureLayer : PolylineFeatureLayer, IGeometryLayer, IProvideWidth, IEnforceBreaklineLayer, IFlowHydrograph, IHasRRRSLayer, IHasProfileProperties, IStructureLayer
{
	int IHasRRRSLayer.RiverStationDecimalPlaces { get => _riverStationDecimalPlaces; set => _riverStationDecimalPlaces = value; }
	SharedData.EnumRSUnits IHasRRRSLayer.UnitsRiverStation { get => _unitsRiverStation; set => _unitsRiverStation = value; }

	public enum StructureType
	{
		None,
		Bridge,
		Inline,
		Lateral,
		Connection
	}

	internal class Cols
	{
		public class Weir
		{
			public const string Distance = "Upstream Distance";

			public const string Width = "Weir Width";

			public const string MaxSubmergence = "Weir Max Submergence";

			public const string MinElev = "Weir Min Elevation";

			public const string Coef = "Weir Coef";

			public const string Shape = "Weir Shape";

			public const string DesignEGHead = "Weir Design EG Head";

			public const string DesignSpillHT = "Weir Design Spillway HT";

			public const string UpstreamSlope = "Weir US Slope";

			public const string DownstreamSlope = "Weir DS Slope";
		}

		public class HTAB
		{
			public const string FFPoints = "HTAB FF Points";

			public const string RCCount = "HTAB RC Count";

			public const string RCPoints = "HTAB RC Points";

			public const string HWMax = "HTAB HW Max";

			public const string TWMax = "HTAB TW Max";

			public const string MaxFlow = "HTAB Max Flow";
		}

		public class LinearRouting
		{
			public const string PositiveCoef = "Linear Routing Positive Coef.";

			public const string NegativeCoef = "Linear Routing Negative Coef.";

			public const string Elevation = "Linear Routing Elev.";
		}

		public class LateralWeir
		{
			public class HeadWater
			{
				public const string Position = "LW HW Position";

				public const string Distance = "LW HW Distance";
			}

			public class TailWater
			{
				public const string Position = "LW TW Position";

				public const string Distance = "LW TW Distance";
			}

			public const string SpanMultiple = "Span Multiple";
		}

		public class Hagers
		{
			public const string WeirCoef = "Hagers Weir Coef";

			public const string Height = "Hagers Height";

			public const string Slope = "Hagers Slope";

			public const string Angle = "Hagers Angle";

			public const string Radius = "Hagers Radius";
		}

		public class BR
		{
			public class US
			{
				public const string LeftBank = "BR US Left Bank";

				public const string RightBank = "BR US Right Bank";

				public const string Profile = "Upstream BR Profile";

				public const string WeirProfile = "Upstream BR Weir Profile";

				public const string LidProfile = "Upstream BR Lid Profile";

				public const string Mann = "Upstream BR Mannings";
			}

			public class DS
			{
				public const string LeftBank = "BR DS Left Bank";

				public const string RightBank = "BR DS Right Bank";

				public const string Profile = "Downstream BR Profile";

				public const string WeirProfile = "Downstream BR Weir Profile";

				public const string LidProfile = "Downstream BR Lid Profile";

				public const string Mann = "Downstream BR Mannings";
			}
		}

		public class BR2D
		{
			public class US
			{
				public const string LeftIneffSta = "US Left Ineff Sta";

				public const string LeftIneffElev = "US Left Ineff Elev";

				public const string RightIneffSta = "US Right Ineff Sta";

				public const string RightIneffElev = "US Right Ineff Elev";
			}

			public class DS
			{
				public const string LeftIneffSta = "DS Left Ineff Sta";

				public const string LeftIneffElev = "DS Left Ineff Elev";

				public const string RightIneffSta = "DS Right Ineff Sta";

				public const string RightIneffElev = "DS Right Ineff Elev";
			}
		}

		public class XS
		{
			public class US
			{
				public const string LeftBank = "XS US Left Bank";

				public const string RightBank = "XS US Right Bank";

				public const string Profile = "Upstream XS Profile";

				public const string Mann = "Upstream XS Mannings";
			}

			public class DS
			{
				public const string LeftBank = "XS DS Left Bank";

				public const string RightBank = "XS DS Right Bank";

				public const string Profile = "Downstream XS Profile";

				public const string Mann = "Downstream XS Mannings";
			}
		}

		public const string Type = "Type";

		public const string StructureType = "Structure Type";

		public const string River = "River";

		public const string Reach = "Reach";

		public const string RiverStation = "River Station";

		public const string ConnectionName = "Connection Name";

		public const string GroupName = "Group Name";

		public const string USType = "US Type";

		public const string USRiver = "US River";

		public const string USReach = "US Reach";

		public const string USRiverStation = "US River Station";

		public const string USSA2D = "US SA/2D";

		public const string DSType = "DS Type";

		public const string DSRiver = "DS River";

		public const string DSReach = "DS Reach";

		public const string DSRiverStation = "DS River Station";

		public const string DSSA2D = "DS SA/2D";

		public const string NodeName = "Node Name";

		public const string NodeDescription = "Node Descriptions";

		public const string LastEdited = "Last Edited";

		public const string Culverts = "Culverts";

		public const string FlapGates = "Flap Gates";

		public const string Gates = "Gates";

		public const string FrictionInMomentum = "Friction in Momentum";

		public const string WeightInMomentum = "Weight in Momentum";

		public const string UseCriticalWS = "Use Critical WS";

		public const string PressCriteriaEG = "Press Criteria EG";

		public const string IceOption = "Ice Option";

		public const string WeirSkew = "Weir Skew";

		public const string PierSkew = "Pier Skew";

		public const string CenterlineProfile = "Centerline Profile";

		public const string StationOffset = "Station Offset";

		public const string RatingCurve = "Rating Curve";

		public const string Use2DForOverflow = "Use 2D for Overflow";

		public const string UseVelocityInto2D = "Use Velocity into 2D";

		public const string PilotFlow = "Pilot Flow";

		public const string UseOverrideHWConnectivity = "Use Override HW Connectivity";

		public const string UseOverrideTWConnectivity = "Use Override TW Connectivity";

		public const string UseWSCritia = "Use WS Critia";

		public const string SNNID = "SNET Node Index";

		public const string IsDefaultCenterline = "Default Centerline";

		public const string TSVariables = "Time Series Variables";

		public const string Abutments = "Abutments";

		public const string Piers = "Piers";

		public const string BridgeCoefficients = "Bridge Coefficients";
	}

	public class AttributeNames
	{
		public class BreachingVariables
		{
			public class VariableUnit
			{
				public const string StageHW = "Stage HW";

				public const string StageTW = "Stage TW";

				public const string BottomWidth = "Bottom-Width";

				public const string BottomElevation = "Bottom-Elevation";

				public const string LeftSideSlope = "Left Side Slope";

				public const string RightSideSlope = "Right Side Slope";

				public const string BreachFlow = "Breach Flow";

				public const string BreachVelocity = "Breach Velocity";

				public const string BreachFlowArea = "Breach Flow Area";

				public const string TopElevation = "Top-Elevation";
			}

			public const string BreachAtTime = "Breach at Time (Days)";

			public const string CenterlineBreach = "Centerline Breach";
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__77_002D0
	{
		public H5Assist.VB.H5Reader hr;

		public CompoundDataset compound;

		public StructureLayer structureLayer;

		public _Closure_0024__77_002D0(_Closure_0024__77_002D0 other)
		{
			if (other != null)
			{
				hr = other.hr;
				compound = other.compound;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(string prefix, string colName, string dset)
		{
			structureLayer.ReadSetProfiles(hr, dset, compound.GetColumn<int>(prefix + " (Index)"), compound.GetColumn<int>(prefix + " (Count)"), colName);
		}
	}

	[CompilerGenerated]
private sealed class _Closure_0024__79_002D0
	{
		public CompoundTypeDefinition ctd;

		public _Closure_0024__79_002D0(_Closure_0024__79_002D0 other)
		{
			if (other != null)
			{
				ctd = other.ctd;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(string prefix)
		{
			ctd.AddField<int>(prefix + " (Index)");
			ctd.AddField<int>(prefix + " (Count)");
		}
	}

	public const string sPlotWidth = "Plot Width";

	public const string sPlotBreach = "Plot Breach";

	public const string sInlineStructure = "Inline Structure";

	public const string sLateralStructure = "Lateral Structure";

	public const string sSA2DConnection = "SA/2D Connection";

	public const string sStructures = "Structures";

	public const string sWeirWidthColumn = "Weir Width";

	internal int IsEditingDataSemaphore;

	public const string HDFGroupName = "Geometry/Structures";

	private const string AttributesDset = "Geometry/Structures/Attributes";

	private const string TablesDset = "Geometry/Structures/Table Info";

	private const string ProfilesDset = "Geometry/Structures/Profile Data";

	private const string MannDset = "Geometry/Structures/Mannings Data";

	private const string RatingCurveDset = "Geometry/Structures/Rating Curve Data";

	public const string sWeirGatesCulvertsORCOTS = "Weir, Gates, Culverts, Outlet RC and Outlet TS";

	public const string sLinearRouting = "Linear Routing";

	public const string s2DBridge = "Bridge (Internal to 2D Flow Area)";

	public const string sModeGroup = "Mode";

	public const string sGeoSpatialRelevantGroup = "Geospatial Relevant";

	public const string sOtherStructureInformation = "Other Structure Information";

	public const string sUpstreamInfoGroup = "Upstream Information";

	public const string sDownstreamInfoGroup = "Downstream Information";

	public const string sWeirInfoGroup = "Weir Information";

	public const string sLSWeirInfoGroup = "LS Weir Information";

	private SharedData.EnumRSUnits _unitsRiverStation { get; set; }

	private int _riverStationDecimalPlaces { get; set; }

	public override string SimpleFeatureTypeName { get; set; }

	public RASGeometry Geometry { get; }
string IGeometryLayer.HDFGroupName=> "Geometry/Structures";

	public override bool ForwardEditRequestsToParent { get; }

	public override string DefaultLayerName => "Structures";

	public override bool CanEdit { get; }

	public string NearSpacingColumnName { get; }

	public string NearSpacingDiskName { get; }

	public string FarSpacingColumnName { get; }

	public string FarSpacingDiskName { get; }

	public string NearRepeatsColumnName { get; }

	public string NearRepeatsDiskName { get; }

	public override bool IsEditingData => IsEditingDataSemaphore > 0;

	public StructureLayer(RASGeometry geometry)
		: base("Structures", geometry.SourceFilename)
	{
		IsEditingDataSemaphore = 0;
		SimpleFeatureTypeName = "Structure";
		ForwardEditRequestsToParent = true;
		CanEdit = false;
		NearSpacingColumnName = "Near Spacing";
		NearSpacingDiskName = "Cell Spacing Near";
		FarSpacingColumnName = "Far Spacing";
		FarSpacingDiskName = "Cell Spacing Far";
		NearRepeatsColumnName = "Near Repeats";
		NearRepeatsDiskName = "Near Repeats";
		Geometry = geometry;
	}

	public StructureLayer(DataTable overrideDT)
		: base("Structures")
	{
		IsEditingDataSemaphore = 0;
		SimpleFeatureTypeName = "Structure";
		ForwardEditRequestsToParent = true;
		CanEdit = false;
		NearSpacingColumnName = "Near Spacing";
		NearSpacingDiskName = "Cell Spacing Near";
		FarSpacingColumnName = "Far Spacing";
		FarSpacingDiskName = "Cell Spacing Far";
		NearRepeatsColumnName = "Near Repeats";
		NearRepeatsDiskName = "Near Repeats";
		OverrideFeatureTable(overrideDT.Copy());
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		DataColumnViewState viewState = DataColumnViewState.Invisible;
		if (SharedData.DebugMode)
		{
			viewState = DataColumnViewState.GrayedOut;
		}
		DataColumnGroup dataColumnGroup = new DataColumnGroup("River, Reach, River Station", isVisible: true);
		AddManagedAttributeColumn("Type", "Type", typeof(string), null, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("River", "River", typeof(string), dataColumnGroup, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Reach", "Reach", typeof(string), dataColumnGroup, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("River Station", "RS", typeof(string), dataColumnGroup, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Node Name", "Node Name", typeof(string), dataColumnGroup);
		DataColumnGroup dataColumnGroup2 = new DataColumnGroup("Connection Information", isVisible: true);
		AddManagedAttributeColumn("Connection Name", "Connection", typeof(string), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Structure Type", "Mode", typeof(string), dataColumnGroup2, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Last Edited", "Last Edited", typeof(string), null, viewState);
		AddManagedAttributeColumn("Node Descriptions", "Description", typeof(string));
		AddManagedAttributeColumn("Group Name", "Groupname", typeof(string), null, DataColumnViewState.Invisible);
		AddManagedAttributeColumn("Weir Width", "Weir Width", typeof(float));
		AddManagedAttributeColumn("Upstream Distance", "Upstream Distance", typeof(float));
		DataColumnGroup dataColumnGroup3 = new DataColumnGroup("Upstream Information", isVisible: true);
		AddManagedAttributeColumn("US Type", "US Type", typeof(string), dataColumnGroup3, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("US River", "US River", typeof(string), dataColumnGroup3, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("US Reach", "US Reach", typeof(string), dataColumnGroup3, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("US River Station", "US RS", typeof(string), dataColumnGroup3, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("US SA/2D", "US SA/2D", typeof(string), dataColumnGroup3, DataColumnViewState.GrayedOut);
		DataColumnGroup dataColumnGroup4 = new DataColumnGroup("Downstream Information", isVisible: true);
		AddManagedAttributeColumn("DS Type", "DS Type", typeof(string), dataColumnGroup4, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("DS River", "DS River", typeof(string), dataColumnGroup4, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("DS Reach", "DS Reach", typeof(string), dataColumnGroup4, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("DS River Station", "DS RS", typeof(string), dataColumnGroup4, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("DS SA/2D", "DS SA/2D", typeof(string), dataColumnGroup4, DataColumnViewState.GrayedOut);
		DataColumnGroup dataColumnGroup5 = new DataColumnGroup("Weir Information", isVisible: true);
		AddManagedAttributeColumn("Weir Max Submergence", "Weir Max Submergence", typeof(float), dataColumnGroup5);
		AddManagedAttributeColumn("Weir Min Elevation", "Weir Min Elevation", typeof(float), dataColumnGroup5);
		AddManagedAttributeColumn("Weir Coef", "Weir Coef", typeof(float), dataColumnGroup5);
		AddManagedAttributeColumn("Weir Shape", "Weir Shape", typeof(string), dataColumnGroup5);
		AddManagedAttributeColumn("Weir Design EG Head", "Weir Design EG Head", typeof(float), dataColumnGroup5);
		AddManagedAttributeColumn("Weir Design Spillway HT", "Weir Design Spillway HT", typeof(float), dataColumnGroup5);
		AddManagedAttributeColumn("Weir US Slope", "Weir US Slope", typeof(float), dataColumnGroup5, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Weir DS Slope", "Weir DS Slope", typeof(float), dataColumnGroup5, DataColumnViewState.GrayedOut);
		DataColumnGroup dataColumnGroup6 = new DataColumnGroup("LS Weir Information", isVisible: true);
		AddManagedAttributeColumn("LW HW Position", "LW HW Position", typeof(int), dataColumnGroup6, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("LW TW Position", "LW TW Position", typeof(int), dataColumnGroup6, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("LW HW Distance", "LW HW Distance", typeof(float), dataColumnGroup6, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("LW TW Distance", "LW TW Distance", typeof(float), dataColumnGroup6, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Span Multiple", "LW Span Multiple", typeof(bool), dataColumnGroup6, DataColumnViewState.GrayedOut);
		DataColumnGroup dataColumnGroup7 = new DataColumnGroup("Compute Options", isVisible: true);
		AddManagedAttributeColumn("Use 2D for Overflow", "Use 2D for Overflow", typeof(bool), dataColumnGroup7, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Use Velocity into 2D", "Use Velocity into 2D", typeof(bool), dataColumnGroup7, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Use Override HW Connectivity", "Use Override HW Connectivity", typeof(bool), dataColumnGroup7, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Use Override TW Connectivity", "Use Override TW Connectivity", typeof(bool), dataColumnGroup7, DataColumnViewState.GrayedOut);
		DataColumnGroup dataColumnGroup8 = new DataColumnGroup("Pilot Flow", isVisible: true);
		AddManagedAttributeColumn("Pilot Flow", "Pilot Flow", typeof(float), dataColumnGroup8, DataColumnViewState.GrayedOut);
		DataColumnGroup dataColumnGroup9 = new DataColumnGroup("HTAB Options", isVisible: true);
		AddManagedAttributeColumn("HTAB FF Points", "HTAB FF Points", typeof(int), dataColumnGroup9);
		AddManagedAttributeColumn("HTAB RC Count", "HTAB RC Count", typeof(int), dataColumnGroup9);
		AddManagedAttributeColumn("HTAB RC Points", "HTAB RC Points", typeof(int), dataColumnGroup9);
		AddManagedAttributeColumn("HTAB HW Max", "HTAB HW Max", typeof(float), dataColumnGroup9);
		AddManagedAttributeColumn("HTAB TW Max", "HTAB TW Max", typeof(float), dataColumnGroup9);
		AddManagedAttributeColumn("HTAB Max Flow", "HTAB Max Flow", typeof(float), dataColumnGroup9);
		EnforceBreaklineLayerExtensions.InitializeBreaklineColumns(this);
		DataColumnGroup dataColumnGroup10 = new DataColumnGroup("Linear Routing", isVisible: true);
		AddManagedAttributeColumn("Linear Routing Positive Coef.", "Linear Routing Positive Coef", typeof(float), dataColumnGroup10);
		AddManagedAttributeColumn("Linear Routing Negative Coef.", "Linear Routing Negative Coef", typeof(float), dataColumnGroup10);
		AddManagedAttributeColumn("Linear Routing Elev.", "Linear Routing Elevation", typeof(float), dataColumnGroup10);
		DataColumnGroup dataColumnGroup11 = new DataColumnGroup("LS Weir Info", isVisible: true);
		AddManagedAttributeColumn("Hagers Weir Coef", "Hagers Weir Coef", typeof(float), dataColumnGroup11);
		AddManagedAttributeColumn("Hagers Height", "Hagers Height", typeof(float), dataColumnGroup11);
		AddManagedAttributeColumn("Hagers Slope", "Hagers Slope", typeof(float), dataColumnGroup11);
		AddManagedAttributeColumn("Hagers Angle", "Hagers Angle", typeof(float), dataColumnGroup11);
		AddManagedAttributeColumn("Hagers Radius", "Hagers Radius", typeof(float), dataColumnGroup11);
		AddManagedAttributeColumn("Use WS Critia", "Use WS for Weir Reference", typeof(bool), dataColumnGroup11);
		DataColumnGroup dataColumnGroup12 = new DataColumnGroup("Bridge Options", isVisible: true);
		AddManagedAttributeColumn("Friction in Momentum", "Use Friction in Momentum", typeof(bool), dataColumnGroup12);
		AddManagedAttributeColumn("Weight in Momentum", "Use Weight in Momentum", typeof(bool), dataColumnGroup12);
		AddManagedAttributeColumn("Use Critical WS", "Use Critical US", typeof(bool), dataColumnGroup12);
		AddManagedAttributeColumn("Press Criteria EG", "Use EG for Pressure Criteria", typeof(bool), dataColumnGroup12);
		AddManagedAttributeColumn("Ice Option", "Ice Option", typeof(int), dataColumnGroup12);
		AddManagedAttributeColumn("Weir Skew", "Weir Skew", typeof(float), dataColumnGroup12);
		AddManagedAttributeColumn("Pier Skew", "Pier Skew", typeof(float), dataColumnGroup12);
		DataColumnGroup dataColumnGroup13 = new DataColumnGroup("Bridge (2D) Bank Stations", isVisible: true);
		AddManagedAttributeColumn("BR US Left Bank", "BR US Left Bank", typeof(float), dataColumnGroup13);
		AddManagedAttributeColumn("BR US Right Bank", "BR US Right Bank", typeof(float), dataColumnGroup13);
		AddManagedAttributeColumn("BR DS Left Bank", "BR DS Left Bank", typeof(float), dataColumnGroup13);
		AddManagedAttributeColumn("BR DS Right Bank", "BR DS Right Bank", typeof(float), dataColumnGroup13);
		AddManagedAttributeColumn("XS US Left Bank", "XS US Left Bank", typeof(float), dataColumnGroup13);
		AddManagedAttributeColumn("XS US Right Bank", "XS US Right Bank", typeof(float), dataColumnGroup13);
		AddManagedAttributeColumn("XS DS Left Bank", "XS DS Left Bank", typeof(float), dataColumnGroup13);
		AddManagedAttributeColumn("XS DS Right Bank", "XS DS Right Bank", typeof(float), dataColumnGroup13);
		DataColumnGroup dataColumnGroup14 = new DataColumnGroup("Bridge (2D) Ineffective Areas", isVisible: true);
		AddManagedAttributeColumn("US Left Ineff Sta", "US Ineff Left Sta", typeof(float), dataColumnGroup14);
		AddManagedAttributeColumn("US Left Ineff Elev", "US Ineff Left Elev", typeof(float), dataColumnGroup14);
		AddManagedAttributeColumn("US Right Ineff Sta", "US Ineff Right Sta", typeof(float), dataColumnGroup14);
		AddManagedAttributeColumn("US Right Ineff Elev", "US Ineff Right Elev", typeof(float), dataColumnGroup14);
		AddManagedAttributeColumn("DS Left Ineff Sta", "DS Ineff Left Sta", typeof(float), dataColumnGroup14);
		AddManagedAttributeColumn("DS Left Ineff Elev", "DS Ineff Left Elev", typeof(float), dataColumnGroup14);
		AddManagedAttributeColumn("DS Right Ineff Sta", "DS Ineff Right Sta", typeof(float), dataColumnGroup14);
		AddManagedAttributeColumn("DS Right Ineff Elev", "DS Ineff Right Elev", typeof(float), dataColumnGroup14);
		AddManagedAttributeColumn("UseOverrideHTabIBCurves", "Use Override HTabIBCurves", typeof(bool), dataColumnGroup9);
		AddManagedAttributeColumn("SNET Node Index", "SNN ID", typeof(int), null, viewState);
		AddManagedAttributeColumn("Default Centerline", "Default Centerline", typeof(bool), null, DataColumnViewState.Invisible);
		DataColumnGroup dataColumnGroup15 = new DataColumnGroup("Terrain/Profile", isVisible: false);
		AddAttributeColumn("Centerline Profile", typeof(IProfileInfo), dataColumnGroup15, DataColumnViewState.GrayedOut);
		AddAttributeColumn("Track Terrain", typeof(bool), dataColumnGroup15, DataColumnViewState.Invisible);
		AddAttributeColumn("Auto Update", typeof(bool), dataColumnGroup15, DataColumnViewState.Invisible);
		AddAttributeColumn("Upstream BR Profile", typeof(IProfileInfo), null, DataColumnViewState.Invisible);
		AddAttributeColumn("Downstream BR Profile", typeof(IProfileInfo), null, DataColumnViewState.Invisible);
		AddAttributeColumn("Upstream BR Mannings", typeof(IProfileInfo), null, DataColumnViewState.Invisible);
		AddAttributeColumn("Downstream BR Mannings", typeof(IProfileInfo), null, DataColumnViewState.Invisible);
		AddAttributeColumn("Upstream BR Weir Profile", typeof(IProfileInfo), null, DataColumnViewState.Invisible);
		AddAttributeColumn("Downstream BR Weir Profile", typeof(IProfileInfo), null, DataColumnViewState.Invisible);
		AddAttributeColumn("Upstream BR Lid Profile", typeof(IProfileInfo), null, DataColumnViewState.Invisible);
		AddAttributeColumn("Downstream BR Lid Profile", typeof(IProfileInfo), null, DataColumnViewState.Invisible);
		AddAttributeColumn("Upstream XS Profile", typeof(IProfileInfo), null, DataColumnViewState.Invisible);
		AddAttributeColumn("Downstream XS Profile", typeof(IProfileInfo), null, DataColumnViewState.Invisible);
		AddAttributeColumn("Upstream XS Mannings", typeof(IProfileInfo), null, DataColumnViewState.Invisible);
		AddAttributeColumn("Downstream XS Mannings", typeof(IProfileInfo), null, DataColumnViewState.Invisible);
		AddAttributeColumn("Rating Curve", typeof(IProfileInfo), null, DataColumnViewState.Invisible);
		AddAttributeColumn("Abutments", typeof(List<AbutmentData>), null, DataColumnViewState.Invisible);
		AddAttributeColumn("Piers", typeof(List<PierData>), null, DataColumnViewState.Invisible);
		AddAttributeColumn("Bridge Coefficients", typeof(List<BridgeCoefficientData>), null, DataColumnViewState.Invisible);
		AddAttributeColumn("ComputedUnroundedRS", typeof(double), dataColumnGroup, viewState);
		AddAttributeColumn("Station Offset", typeof(float), null, viewState);
		AddAttributeColumn("Time Series Variables", typeof(List<TimeSeriesVariable>), null, DataColumnViewState.Invisible);
	}

	protected override void XMLLoadMetadata(XmlElement xmlLayer)
	{
		base.XMLLoadMetadata(xmlLayer);
		string stringAttribute = XML.StringAttribute(xmlLayer, "UnitsRiverStation");
		if (string.IsNullOrEmpty(stringAttribute))
		{
			_unitsRiverStation = SharedData.UnitsRiverStation;
		}
		else
		{
			SharedData.EnumRSUnits result = _unitsRiverStation;
			bool tryParse = Enum.TryParse<SharedData.EnumRSUnits>(stringAttribute, out result);
			_unitsRiverStation = result;
			if (!tryParse)
			{
				_unitsRiverStation = SharedData.UnitsRiverStation;
			}
		}
		_riverStationDecimalPlaces = XML.IntegerAttribute(xmlLayer, "RiverStationDecimalPlaces", -1);
		if (_riverStationDecimalPlaces == -1)
		{
			_riverStationDecimalPlaces = SharedData.RiverStationDecimalPlaces;
		}
	}

	protected override void XMLSaveMetadata(XmlDocument doc, XmlElement xmlLayer)
	{
		base.XMLSaveMetadata(doc, xmlLayer);
		xmlLayer.SetAttribute("UnitsRiverStation", _unitsRiverStation.ToString());
		xmlLayer.SetAttribute("RiverStationDecimalPlaces", _riverStationDecimalPlaces.ToString());
	}

	protected void HDFLoadFeatureTable(H5Assist.VB.H5Reader hr)
	{
		_Closure_0024__77_002D0 capturedLocals = default;
		_Closure_0024__77_002D0 capturedLocals2 = new _Closure_0024__77_002D0(capturedLocals);
		capturedLocals2.structureLayer = this;
		capturedLocals2.hr = hr;
		if (!capturedLocals2.hr.ExistsAsGroup("Geometry/Structures") || !capturedLocals2.hr.ExistsAsDataset("Geometry/Structures/Attributes"))
		{
			return;
		}
		HDF5Storage.ReadPolyFeatures(this, capturedLocals2.hr, "Geometry/Structures/Centerline");
		ReadManagedColumns(capturedLocals2.hr, "Geometry/Structures/Attributes");
		if (FeatureCount() == 0)
		{
			return;
		}
		if (!capturedLocals2.hr.ExistsAsDataset("Geometry/Structures/Table Info"))
		{
			ErrorMessage = "Error loading structures: Table data not found.";
			return;
		}
		capturedLocals2.compound = capturedLocals2.hr.ReadCompoundDataset("Geometry/Structures/Table Info");
		Action<string, string, string> action = [SpecialName] (string prefix, string colName, string dset) =>
		{
			capturedLocals2.structureLayer.ReadSetProfiles(capturedLocals2.hr, dset, capturedLocals2.compound.GetColumn<int>(prefix + " (Index)"), capturedLocals2.compound.GetColumn<int>(prefix + " (Count)"), colName);
		};
		action("Centerline Profile", "Centerline Profile", "Geometry/Structures/Profile Data");
		action("US BR Profile", "Upstream BR Profile", "Geometry/Structures/Profile Data");
		action("DS BR Profile", "Downstream BR Profile", "Geometry/Structures/Profile Data");
		action("US BR Mann", "Upstream BR Mannings", "Geometry/Structures/Mannings Data");
		action("DS BR Mann", "Downstream BR Mannings", "Geometry/Structures/Mannings Data");
		action("US BR Weir Profile", "Upstream BR Weir Profile", "Geometry/Structures/Profile Data");
		action("DS BR Weir Profile", "Downstream BR Weir Profile", "Geometry/Structures/Profile Data");
		action("US BR Lid Profile", "Upstream BR Lid Profile", "Geometry/Structures/Profile Data");
		action("DS BR Lid Profile", "Downstream BR Lid Profile", "Geometry/Structures/Profile Data");
		action("US XS Profile", "Upstream XS Profile", "Geometry/Structures/Profile Data");
		action("DS XS Profile", "Downstream XS Profile", "Geometry/Structures/Profile Data");
		action("US XS Mann", "Upstream XS Mannings", "Geometry/Structures/Mannings Data");
		action("DS XS Mann", "Downstream XS Mannings", "Geometry/Structures/Mannings Data");
		action("RC", "Rating Curve", "Geometry/Structures/Rating Curve Data");
		Dictionary<int, List<AbutmentData>> collection = AbutmentData.ReadCollection(capturedLocals2.hr, "Geometry/Structures");
		Dictionary<int, List<PierData>> collection2 = PierData.ReadCollection(capturedLocals2.hr, "Geometry/Structures");
		Dictionary<int, List<BridgeCoefficientData>> collection3 = BridgeCoefficientData.ReadCollection(capturedLocals2.hr, "Geometry/Structures");
		checked
		{
			int count = FeatureCount() - 1;
			for (int ordinal = 0; ordinal <= count; ordinal++)
			{
				List<AbutmentData> abutmentDatas = null;
				if (collection.TryGetValue(ordinal, out abutmentDatas))
				{
					SetAbutments(ordinal, abutmentDatas);
				}
				List<PierData> pierDatas = null;
				if (collection2.TryGetValue(ordinal, out pierDatas))
				{
					SetPiers(ordinal, pierDatas);
				}
				List<BridgeCoefficientData> bridgeCoefficientDatas = null;
				if (collection3.TryGetValue(ordinal, out bridgeCoefficientDatas))
				{
					SetBridgeCoefs(ordinal, bridgeCoefficientDatas);
				}
				SetTrackTerrain(ordinal, condition: false);
				SetAutoUpdate(ordinal, condition: false);
			}
		}
	}

	void IGeometryLayer.HDFLoadFeatureTable(H5Assist.VB.H5Reader hr)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFLoadFeatureTable
		this.HDFLoadFeatureTable(hr);
	}

	private void ReadSetProfiles(H5Assist.VB.H5Reader hr, string pointDataset, int[] starts, int[] counts, string outProfileColumn)
	{
		if (starts == null || counts == null || !hr.ExistsAsDataset(pointDataset))
		{
			return;
		}
		float[,] values = null;
		hr.ReadDataset(pointDataset, ref values);
		int featureCount = FeatureCount();
		List<Profile> fromInfoValues = Profile.FromInfoValues(values, starts, counts);
		if (featureCount != fromInfoValues.Count)
		{
			return;
		}
		checked
		{
			if (FeatureTable().Columns[outProfileColumn].DataType == typeof(IProfileInfo))
			{
				int count = featureCount - 1;
				for (int i = 0; i <= count; i++)
				{
					FeatureRow(i)[outProfileColumn] = new InMemoryProfileInfo(fromInfoValues[i]);
				}
			}
			else
			{
				int count2 = featureCount - 1;
				for (int j = 0; j <= count2; j++)
				{
					FeatureRow(j)[outProfileColumn] = fromInfoValues[j];
				}
			}
		}
	}

	protected void HDFSaveFeatureTable(H5Assist.VB.H5Writer hw)
	{
		if (FeatureCount() == 0)
		{
			hw.Delete("Geometry/Structures");
			return;
		}
		_Closure_0024__79_002D0 capturedLocals = default;
		_Closure_0024__79_002D0 capturedLocals2 = new _Closure_0024__79_002D0(capturedLocals);
		hw.CreateGroup("Geometry/Structures");
		HDF5Storage.WritePolylineFeatures(this, hw, "Geometry/Structures/Centerline");
		WriteManagedColumns(hw, "Geometry/Structures/Attributes");
		capturedLocals2.ctd = new CompoundTypeDefinition();
		Action<string> action = [SpecialName] (string prefix) =>
		{
			capturedLocals2.ctd.AddField<int>(prefix + " (Index)");
			capturedLocals2.ctd.AddField<int>(prefix + " (Count)");
		};
		action("Centerline Profile");
		action("US XS Profile");
		action("US BR Profile");
		action("US BR Weir Profile");
		action("US BR Lid Profile");
		action("DS XS Profile");
		action("DS BR Profile");
		action("DS BR Weir Profile");
		action("DS BR Lid Profile");
		action("US XS Mann");
		action("US BR Mann");
		action("DS XS Mann");
		action("DS BR Mann");
		action("RC");
		hw.Create1DCompoundDataset("Geometry/Structures/Table Info", 100, capturedLocals2.ctd);
		List<int> intList = new List<int>();
		List<int> intListList = new List<int>();
		List<int> intList2 = new List<int>();
		List<int> intList3 = new List<int>();
		List<int> intList4 = new List<int>();
		List<int> intList5 = new List<int>();
		List<float[,]> ptList = new List<float[,]>();
		List<float[,]> ptList2 = new List<float[,]>();
		List<float[,]> floatValuesList = new List<float[,]>();
		checked
		{
			int count = FeatureCount() - 1;
			for (int ordinal = 0; ordinal <= count; ordinal++)
			{
				List<object> objectList = new List<object>();
				Profile.AppendInfoValues(GetCenterlineProfile(ordinal), intList, intList3, ptList);
				objectList.Add(intList.Last());
				objectList.Add(intList3.Last());
				Profile.AppendInfoValues(GetCrossSectionUpstreamProfile(ordinal), intList, intList3, ptList);
				objectList.Add(intList.Last());
				objectList.Add(intList3.Last());
				Profile.AppendInfoValues(GetBridgeUpstreamProfile(ordinal), intList, intList3, ptList);
				objectList.Add(intList.Last());
				objectList.Add(intList3.Last());
				Profile.AppendInfoValues(GetBridgeUpstreamWeirProfile(ordinal), intList, intList3, ptList);
				objectList.Add(intList.Last());
				objectList.Add(intList3.Last());
				Profile.AppendInfoValues(GetBridgeUpstreamLidProfile(ordinal), intList, intList3, ptList);
				objectList.Add(intList.Last());
				objectList.Add(intList3.Last());
				Profile.AppendInfoValues(GetCrossSectionDownstreamProfile(ordinal), intList, intList3, ptList);
				objectList.Add(intList.Last());
				objectList.Add(intList3.Last());
				Profile.AppendInfoValues(GetBridgeDownstreamProfile(ordinal), intList, intList3, ptList);
				objectList.Add(intList.Last());
				objectList.Add(intList3.Last());
				Profile.AppendInfoValues(GetBridgeDownstreamWeirProfile(ordinal), intList, intList3, ptList);
				objectList.Add(intList.Last());
				objectList.Add(intList3.Last());
				Profile.AppendInfoValues(GetBridgeDownstreamLidProfile(ordinal), intList, intList3, ptList);
				objectList.Add(intList.Last());
				objectList.Add(intList3.Last());
				Profile.AppendInfoValues(GetCrossSectionUpstreamMannings(ordinal), intListList, intList4, ptList2);
				objectList.Add(intListList.Last());
				objectList.Add(intList4.Last());
				Profile.AppendInfoValues(GetBridgeUpstreamMannings(ordinal), intListList, intList4, ptList2);
				objectList.Add(intListList.Last());
				objectList.Add(intList4.Last());
				Profile.AppendInfoValues(GetCrossSectionDownstreamMannings(ordinal), intListList, intList4, ptList2);
				objectList.Add(intListList.Last());
				objectList.Add(intList4.Last());
				Profile.AppendInfoValues(GetBridgeDownstreamMannings(ordinal), intListList, intList4, ptList2);
				objectList.Add(intListList.Last());
				objectList.Add(intList4.Last());
				Profile.AppendInfoValues(GetRatingCurve(ordinal), intList2, intList5, floatValuesList);
				objectList.Add(intList2.Last());
				objectList.Add(intList5.Last());
				hw.AddCompoundElement("Geometry/Structures/Table Info", objectList.ToArray());
			}
			float[,] mergePointList = Profile.MergePointList(ptList);
			float[,] mergePointList2 = Profile.MergePointList(ptList2);
			float[,] floatValues = floatValuesList.First();
			if (mergePointList.Length > 0)
			{
				hw.WriteArray("Geometry/Structures/Profile Data", mergePointList);
			}
			if (mergePointList2.Length > 0)
			{
				hw.WriteArray("Geometry/Structures/Mannings Data", mergePointList2);
			}
			if (floatValues.Length > 0)
			{
				hw.WriteArray("Geometry/Structures/Rating Curve Data", floatValues);
			}
			AbutmentData.WriteCollection(hw, "Geometry/Structures", (from fid in Enumerable.Range(0, FeatureCount())
				select GetAbutments(fid)).ToArray());
			PierData.WriteCollection(hw, "Geometry/Structures", (from fid in Enumerable.Range(0, FeatureCount())
				select GetPiers(fid)).ToArray());
			BridgeCoefficientData.WriteCollection(hw, "Geometry/Structures", (from fid in Enumerable.Range(0, FeatureCount())
				select GetBridgeCoefs(fid)).ToArray());
		}
	}

	void IGeometryLayer.HDFSaveFeatureTable(H5Assist.VB.H5Writer hw)
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
				Polyline(i).Stream(bw);
				Pipe.WriteString(bw, GetStructureType(i));
				Pipe.WriteString(bw, IHasRRRSLayerExtensions.GetRiverName(this, i));
				Pipe.WriteString(bw, IHasRRRSLayerExtensions.GetReachName(this, i));
				Pipe.WriteString(bw, IHasRRRSLayerExtensions.GetRiverStationName(this, i));
				Pipe.WriteString(bw, GetConnectionName(i));
				bw.Write(GetWeirWidth(i));
				bw.Write(GetWeirUSSlope(i));
				bw.Write(GetWeirDSSlope(i));
				Pipe.WriteString(bw, GetUSType(i));
				Pipe.WriteString(bw, GetUSRiverName(i));
				Pipe.WriteString(bw, GetUSReachName(i));
				Pipe.WriteString(bw, GetUSRiverStationName(i));
				Pipe.WriteString(bw, GetUSSA2DName(i));
				Pipe.WriteString(bw, GetDSType(i));
				Pipe.WriteString(bw, GetDSRiverName(i));
				Pipe.WriteString(bw, GetDSReachName(i));
				Pipe.WriteString(bw, GetDSRiverStationName(i));
				Pipe.WriteString(bw, GetDSSA2DName(i));
			}
		}
	}

	public static StructureLayer Stream(BinaryReader br, RASGeometry geom)
	{
		StructureLayer structureLayer = new StructureLayer(geom);
		structureLayer.InitializeEmptyFeatureTable();
		structureLayer.SuppressTableReload(boolValue: true);
		structureLayer.SuppressFeatureChanged(boolValue: true);
		checked
		{
			int count = br.ReadInt32() - 1;
			for (int i = 0; i <= count; i++)
			{
				Polyline feature = RasMapperLib.Polyline.Stream(br);
				structureLayer.AddFeature(feature);
				structureLayer.SetStructureType(i, Pipe.ReadString(br));
				IHasRRRSLayerExtensions.SetRiverName(structureLayer, i, Pipe.ReadString(br));
				IHasRRRSLayerExtensions.SetReachName(structureLayer, i, Pipe.ReadString(br));
				IHasRRRSLayerExtensions.SetRiverStationName(structureLayer, i, Pipe.ReadString(br));
				structureLayer.SetConnectionName(i, Pipe.ReadString(br));
				structureLayer.SetWeirWidth(i, br.ReadSingle());
				structureLayer.SetWeirUSSlope(i, br.ReadSingle());
				structureLayer.SetWeirDSSlope(i, br.ReadSingle());
				structureLayer.SetUSType(i, Pipe.ReadString(br));
				structureLayer.SetUSRiverName(i, Pipe.ReadString(br));
				structureLayer.SetUSReachName(i, Pipe.ReadString(br));
				structureLayer.SetUSRiverStationName(i, Pipe.ReadString(br));
				structureLayer.SetUSSA2DName(i, Pipe.ReadString(br));
				structureLayer.SetDSType(i, Pipe.ReadString(br));
				structureLayer.SetDSRiverName(i, Pipe.ReadString(br));
				structureLayer.SetDSReachName(i, Pipe.ReadString(br));
				structureLayer.SetDSRiverStationName(i, Pipe.ReadString(br));
				structureLayer.SetDSSA2DName(i, Pipe.ReadString(br));
			}
			return structureLayer;
		}
	}

	public override List<PlotOption> GetStaticPlotOptions()
	{
		List<PlotOption> staticPlotOptions = base.GetStaticPlotOptions();
		staticPlotOptions.Add(new PlotOption("Plot Width"));
		return staticPlotOptions;
	}

	public override List<PlotOption> GetDefaultPlotOptions()
	{
		List<PlotOption> defaultPlotOptions = base.GetDefaultPlotOptions();
		defaultPlotOptions.Add(new PlotOption("Plot Width"));
		return defaultPlotOptions;
	}

	public bool ComputeSetConnectivity<T>(T lyr, ProgressReporter prog = null) where T : PolylineFeatureLayer
,IStructurePolylineLayer	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		bool condition = false;
		checked
		{
			int count = lyr.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				int structureIndex = lyr.TryGetStructureIndex(i);
				if (structureIndex == -1)
				{
					prog.ReportError("Structure Index not found when computing connectivity for: " + lyr.GetFeatureName(i));
					condition = true;
					continue;
				}
				if (!lyr.ShouldProcessGeospatialHwTwConnectivity(i))
				{
					IStructurePolylineLayerExtensions.SetUSCellList(lyr, i, null);
					IStructurePolylineLayerExtensions.SetDSCellList(lyr, i, null);
					continue;
				}
				if (!RasMapperLib.Polyline.IsValidPolyline(lyr.Polyline(i)))
				{
					IStructurePolylineLayerExtensions.SetUSCellList(lyr, i, null);
					IStructurePolylineLayerExtensions.SetDSCellList(lyr, i, null);
					prog.ReportError("Structure needs geospatial Hw Tw connectivity but no polyline was found for: " + lyr.GetFeatureName(i));
					condition = true;
					continue;
				}
				MeshFV2D structureUSMesh = GetStructureUSMesh(structureIndex);
				MeshFV2D structureDSMesh = GetStructureDSMesh(structureIndex);
				List<MeshFV2D.ProfileCellRange> cellsUnderSegment = null;
				if (structureUSMesh != null)
				{
					SegmentM uSSegment = IStructurePolylineLayerExtensions.GetUSSegment(lyr, i);
					cellsUnderSegment = structureUSMesh.CellsUnderSegment(uSSegment);
					if (System.Math.Abs(cellsUnderSegment.Sum([SpecialName] (MeshFV2D.ProfileCellRange a) => a.StationEnd - a.StationStart) - uSSegment.Length) > 0.001)
					{
						prog.ReportError(lyr.GetFeatureName(i) + " on structure " + GetFeatureName(structureIndex) + " does not entirely intersect the upstream area " + GetUSSA2DName(structureIndex));
						condition = true;
					}
				}
				List<MeshFV2D.ProfileCellRange> cellsUnderSegmentList = null;
				if (structureDSMesh != null)
				{
					SegmentM dSSegment = IStructurePolylineLayerExtensions.GetDSSegment(lyr, i);
					cellsUnderSegmentList = structureDSMesh.CellsUnderSegment(dSSegment);
					if (System.Math.Abs(cellsUnderSegmentList.Sum([SpecialName] (MeshFV2D.ProfileCellRange a) => a.StationEnd - a.StationStart) - dSSegment.Length) > 0.001)
					{
						prog.ReportError(lyr.GetFeatureName(i) + " on structure " + GetFeatureName(structureIndex) + " does not entirely intersect the downstream area " + GetDSSA2DName(structureIndex));
						condition = true;
					}
				}
				IStructurePolylineLayerExtensions.SetUSCellList(lyr, i, cellsUnderSegment);
				IStructurePolylineLayerExtensions.SetDSCellList(lyr, i, cellsUnderSegmentList);
			}
			return !condition;
		}
	}

	public float PipeArchSpan(float rise, long chrt)
	{
		Profile profile = new Profile();
		if ((chrt == 34) | (chrt == 35))
		{
			profile.Add(new Point2D(0f, 0f));
			profile.Add(new Point2D(0.916f, 1.5f));
			profile.Add(new Point2D(1.083f, 1.833f));
			profile.Add(new Point2D(1.333f, 2.0833f));
			profile.Add(new Point2D(1.5f, 2.417f));
			profile.Add(new Point2D(1.833f, 3f));
			profile.Add(new Point2D(2.25f, 3.583f));
			profile.Add(new Point2D(2.583f, 4.167f));
			profile.Add(new Point2D(3f, 4.833f));
			profile.Add(new Point2D(3.333f, 5.471f));
			profile.Add(new Point2D(3.666f, 6f));
			profile.Add(new Point2D(4.5833f, 6.0833f));
			profile.Add(new Point2D(4.75f, 6.3333f));
			profile.Add(new Point2D(4.9167f, 6.75f));
			profile.Add(new Point2D(5.0833f, 7f));
			profile.Add(new Point2D(5.25f, 7.25f));
			profile.Add(new Point2D(5.4167f, 7.6667f));
			profile.Add(new Point2D(5.5833f, 7.9167f));
			profile.Add(new Point2D(5.75f, 8.1667f));
			profile.Add(new Point2D(5.9167f, 8.5833f));
			profile.Add(new Point2D(6.0833f, 8.8333f));
			profile.Add(new Point2D(6.25f, 9.3333f));
			profile.Add(new Point2D(6.4167f, 9.5f));
			profile.Add(new Point2D(6.5833f, 9.75f));
			profile.Add(new Point2D(6.75f, 10.25f));
			profile.Add(new Point2D(6.9167f, 10.6667f));
			profile.Add(new Point2D(7.0833f, 10.9167f));
			profile.Add(new Point2D(7.25f, 11.4167f));
			profile.Add(new Point2D(7.4167f, 11.5833f));
			profile.Add(new Point2D(7.5833f, 11.8333f));
			profile.Add(new Point2D(7.75f, 12.3333f));
			profile.Add(new Point2D(7.9167f, 12.5f));
			profile.Add(new Point2D(8.0833f, 12.6667f));
			profile.Add(new Point2D(8.3333f, 12.8333f));
			profile.Add(new Point2D(8.4167f, 13.4167f));
			profile.Add(new Point2D(8.5833f, 13.9167f));
			profile.Add(new Point2D(8.75f, 14.0833f));
			profile.Add(new Point2D(8.9167f, 14.25f));
			profile.Add(new Point2D(9.0833f, 14.8333f));
			profile.Add(new Point2D(9.25f, 15.3333f));
			profile.Add(new Point2D(9.4167f, 15.5f));
			profile.Add(new Point2D(9.5833f, 15.6667f));
			profile.Add(new Point2D(9.8333f, 15.8333f));
			profile.Add(new Point2D(9.9167f, 16.4167f));
			profile.Add(new Point2D(10.0833f, 16.5833f));
		}
		else
		{
			profile.Add(new Point2D(0f, 0f));
			profile.Add(new Point2D(9.3333f, 13.25f));
			profile.Add(new Point2D(9.5f, 13.5f));
			profile.Add(new Point2D(9.6667f, 14f));
			profile.Add(new Point2D(9.8333f, 14.1667f));
			profile.Add(new Point2D(10f, 14.4167f));
			profile.Add(new Point2D(10.1667f, 14.9167f));
			profile.Add(new Point2D(10.3333f, 15.3333f));
			profile.Add(new Point2D(10.5f, 15.5833f));
			profile.Add(new Point2D(10.6667f, 15.8333f));
			profile.Add(new Point2D(10.8333f, 16.25f));
			profile.Add(new Point2D(11f, 16.5f));
			profile.Add(new Point2D(11.1667f, 17f));
			profile.Add(new Point2D(11.3333f, 17.1667f));
			profile.Add(new Point2D(11.5f, 17.4167f));
			profile.Add(new Point2D(11.6667f, 17.9167f));
			profile.Add(new Point2D(11.8333f, 18.0833f));
			profile.Add(new Point2D(12f, 18.5833f));
			profile.Add(new Point2D(12.1667f, 18.75f));
			profile.Add(new Point2D(12.3333f, 19.25f));
			profile.Add(new Point2D(12.5f, 19.5f));
			profile.Add(new Point2D(12.6667f, 19.6667f));
			profile.Add(new Point2D(12.8333f, 19.9167f));
			profile.Add(new Point2D(13f, 20.4167f));
			profile.Add(new Point2D(13.1667f, 20.5833f));
		}
		return profile.Query(rise);
	}

	public double FindBoundingRiverStationWithPoint(int fid, PointM pt, ref int usXSID, ref int dsXSID, string errorMessage = "", HashSet<int> xsIDsToIgnore = null)
	{
		if (!Geometry.XSInterpolationSurface.EnsureInterpolationSurface())
		{
			errorMessage += "Cannot generate interpolation surface.";
			return -1.0;
		}
		double maxValue = double.MaxValue;
		SegmentM segmentM = default;
		int elementsTerm = -1;
		RASXSInterpolationSurface xSInterpolationSurface = Geometry.XSInterpolationSurface;
		SpatialIndex<Tuple<int, int, int>> tinPerimeterSpatialIndex = xSInterpolationSurface.GetTinPerimeterSpatialIndex();
		checked
		{
			if (tinPerimeterSpatialIndex != null)
			{
				float computed = 1000f;
				if (SharedData.Units == SharedData.EnumUnits.SIUnits)
				{
					computed *= 0.3048f;
				}
				Polyline polyline = base[fid];
				float width = computed;
				PointMs leftOffset = null;
				PointMs rightOffset = null;
				List<Tuple<int, int, int>> elements = tinPerimeterSpatialIndex.GetElements((Polyline)polyline.GetOffsetPolygon(width, ref leftOffset, ref rightOffset));
				int count = elements.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					if (xsIDsToIgnore == null || (!xsIDsToIgnore.Contains(xSInterpolationSurface.GetUpstreamXSID(elements[i].Item1)) && !xsIDsToIgnore.Contains(xSInterpolationSurface.GetDownstreamXSID(elements[i].Item1))))
					{
						TriangleStruct triangleStruct = xSInterpolationSurface.Tin(elements[i].Item1).Triangle(elements[i].Item2);
						int index = triangleStruct.EdgePoint0(elements[i].Item3);
						int index2 = triangleStruct.EdgePoint1(elements[i].Item3);
						SegmentM segmentM2 = new SegmentM(xSInterpolationSurface.Tin(elements[i].Item1).PointM(index), xSInterpolationSurface.Tin(elements[i].Item1).PointM(index2));
						SegmentM.RelativePosition relativePosition = SegmentM.RelativePosition.Undefined;
						double distanceToPoint = segmentM2.DistanceToPoint(pt, ref relativePosition);
						if (distanceToPoint < maxValue)
						{
							elementsTerm = elements[i].Item1;
							maxValue = distanceToPoint;
							segmentM = segmentM2;
						}
					}
				}
			}
			if (elementsTerm != -1)
			{
				usXSID = xSInterpolationSurface.GetUpstreamXSID(elementsTerm);
				dsXSID = xSInterpolationSurface.GetDownstreamXSID(elementsTerm);
				double t = 0.0;
				double z = segmentM.NearestPoint(pt, ref t).Z;
				if (!double.TryParse(IHasRRRSLayerExtensions.GetRiverStationName(Geometry.XS, usXSID), out var result))
				{
					result = double.NaN;
				}
				if (!double.TryParse(IHasRRRSLayerExtensions.GetRiverStationName(Geometry.XS, dsXSID), out var result2))
				{
					result2 = double.NaN;
				}
				double riverStation = result - (result - result2) * (1.0 - z);
				riverStation = RASXS.ComputeAppropriateRiverStationRounding(riverStation, result, result2);
				if (riverStation == result)
				{
					riverStation -= 0.001;
				}
				if (riverStation == result2)
				{
					riverStation += 0.001;
				}
				return riverStation;
			}
			usXSID = -1;
			dsXSID = -1;
			return -1.0;
		}
	}

	public override string GetFeatureName(int fid)
	{
		string connectionName = GetConnectionName(fid);
		if (!string.IsNullOrEmpty(connectionName))
		{
			return connectionName;
		}
		return IHasRRRSLayerExtensions.GetRiverName(this, fid) + " " + IHasRRRSLayerExtensions.GetReachName(this, fid) + " " + IHasRRRSLayerExtensions.GetRiverStationName(this, fid);
	}

	public override void SetFeatureName(int fid, string newName)
	{
		if (Operators.CompareString(GetStructureType(fid), "Connection", TextCompare: false) == 0)
		{
			SetConnectionName(fid, newName);
		}
	}

	public override string GetFeatureDisplayLabel(int fid)
	{
		if (!string.IsNullOrEmpty(GetNodeName(fid)))
		{
			return GetNodeName(fid);
		}
		return base.GetFeatureDisplayLabel(fid);
	}

	protected internal override string GetCMSTitle(int fid)
	{
		StructureIdentifier structureIdentifier = GetStructureIdentifier(fid);
		return GetStructureType(fid) + ": " + (structureIdentifier.River + " " + structureIdentifier.Reach + " " + structureIdentifier.RiverStation + " " + structureIdentifier.Connection).Trim();
	}

	public void ComputeSetRRRSAll([Optional][DefaultParameterValue(null)] ref RiverMap rivMap)
	{
		Dictionary<string, UserStationingComputer> mappedUserStationing = Geometry.XS.GetMappedUserStationing(Enumerable.Range(0, FeatureCount()).ToList(), ref rivMap);
		checked
		{
			using (new WaitCursor(SharedData.RasMapper))
			{
				SharedData.ReportMessage("Computing River Stations...");
				int count = FeatureCount() - 1;
				for (int i = 0; i <= count; i++)
				{
					if (Operators.CompareString(GetStructureType(i), "Bridge", TextCompare: false) == 0 || Operators.CompareString(GetStructureType(i), "Culvert", TextCompare: false) == 0 || Operators.CompareString(GetStructureType(i), "Multiple Opening", TextCompare: false) == 0 || Operators.CompareString(GetStructureType(i), "Inline", TextCompare: false) == 0)
					{
						UserStationingComputer userStationingComputer = null;
						mappedUserStationing.TryGetValue(IHasRRRSLayerExtensions.GetRiverName(this, i), out userStationingComputer);
						RASXS.ComputeSetRRRSInternal(this, i, dontUpdateRSIfReachIdentical: false, userStationingComputer, ref rivMap);
					}
				}
				SharedData.ReportMessage("Computing River Stations... done", replacePreviousIfPossible: true);
			}
		}
	}

	private void ReadAllBreachesStartEnd(int profileIdx, ref float[] breachStartStation, ref float[] breachEndStation)
	{
		breachStartStation = null;
		breachEndStation = null;
		if (FeatureCount() == 0 || !Geometry.IsResultFile)
		{
			return;
		}
		checked
		{
			breachStartStation = new float[FeatureCount() - 1 + 1];
			breachEndStation = new float[FeatureCount() - 1 + 1];
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				breachStartStation[i] = float.NaN;
				breachEndStation[i] = float.NaN;
			}
			using H5Assist.VB.H5Reader h5Reader = new H5Assist.VB.H5Reader(SourceFilename);
			int count2 = FeatureCount() - 1;
			float computed = default;
			float computed2 = default;
			for (int j = 0; j <= count2; j++)
			{
				string structureVariablesDataset = GetStructureVariablesDataset(j);
				string breachingVariablesDataset = GetBreachingVariablesDataset(j);
				if (!RasMapperLib.Polyline.IsValidPolyline(Polyline(j)) || !h5Reader.ExistsAsDataset(structureVariablesDataset) || !h5Reader.ExistsAsDataset(breachingVariablesDataset) || !h5Reader.TryReadAttributeScalar(structureVariablesDataset, "Starting Weir Sta", ref computed) || !h5Reader.TryReadAttributeScalar(breachingVariablesDataset, "Centerline Breach", ref computed2) || float.IsNaN(computed2))
				{
					continue;
				}
				List<TimeSeriesVariable> breachingVariables = ReadBreachingVariables(j, h5Reader);
				if (breachingVariables.IsNullOrEmpty())
				{
					continue;
				}
				TimeSeriesVariable timeSeriesVariable = breachingVariables.FirstOrDefault([SpecialName] (TimeSeriesVariable tsv) => Operators.CompareString(tsv.VariableName, "Bottom-Width", TextCompare: false) == 0);
				if (timeSeriesVariable != null && !timeSeriesVariable.TimeSeries.IsNullOrEmpty() && timeSeriesVariable.TimeSeries.Count() > profileIdx)
				{
					float timeSery = timeSeriesVariable.TimeSeries[profileIdx];
					if (!float.IsNaN(timeSery) && !(timeSery <= 0f))
					{
						breachStartStation[j] = computed2 - computed - 0.5f * timeSery;
						breachEndStation[j] = computed2 - computed + 0.5f * timeSery;
					}
				}
			}
		}
	}

	internal bool TryReadBreachStartEnd(int fid, int profileIdx, ref float breachStartStation, ref float breachEndStation)
	{
		string structureVariablesDataset = GetStructureVariablesDataset(fid);
		string breachingVariablesDataset = GetBreachingVariablesDataset(fid);
		if (!RasMapperLib.Polyline.IsValidPolyline(Polyline(fid)))
		{
			return false;
		}
		using H5Assist.VB.H5Reader h5Reader = new H5Assist.VB.H5Reader(SourceFilename);
		if (!h5Reader.ExistsAsDataset(structureVariablesDataset))
		{
			return false;
		}
		if (!h5Reader.ExistsAsDataset(breachingVariablesDataset))
		{
			return false;
		}
		float computed = default;
		if (!h5Reader.TryReadAttributeScalar(structureVariablesDataset, "Starting Weir Sta", ref computed))
		{
			return false;
		}
		float computed2 = default;
		if (!h5Reader.TryReadAttributeScalar(breachingVariablesDataset, "Centerline Breach", ref computed2))
		{
			return false;
		}
		if (float.IsNaN(computed2))
		{
			computed2 = 0f;
		}
		List<TimeSeriesVariable> breachingVariables = ReadBreachingVariables(fid, h5Reader);
		if (breachingVariables.IsNullOrEmpty())
		{
			return false;
		}
		TimeSeriesVariable timeSeriesVariable = breachingVariables.FirstOrDefault([SpecialName] (TimeSeriesVariable tsv) => Operators.CompareString(tsv.VariableName, "Bottom-Elevation", TextCompare: false) == 0);
		if (timeSeriesVariable == null)
		{
			return false;
		}
		if (timeSeriesVariable.TimeSeries.IsNullOrEmpty() || timeSeriesVariable.TimeSeries.Count() <= profileIdx)
		{
			return false;
		}
		float timeSery = timeSeriesVariable.TimeSeries[profileIdx];
		if (float.IsNaN(timeSery))
		{
			return false;
		}
		if (timeSery <= 0f)
		{
			return false;
		}
		breachStartStation = computed2 - computed - 0.5f * timeSery;
		breachEndStation = computed2 - computed + 0.5f * timeSery;
		return true;
	}

	public float[,] ReadSegmentedFlowHydrograph(int fid, string outputBlock = "")
	{
		float[,] data = null;
		ReadSegmentedFlowHydrograph(fid, ref data, outputBlock);
		return data;
	}

	float[,] IFlowHydrograph.ReadSegmentedFlowHydrograph(int fid,string outputBlock )
	{
		//ILSpy generated this explicit interface implementation from .override directive in ReadSegmentedFlowHydrograph
		return this.ReadSegmentedFlowHydrograph(fid, outputBlock);
	}

	public float[] ReadAggregatedFlowHydrograph(int fid, string outputBlock = "")
	{
		if (!Geometry.IsResultFile)
		{
			return null;
		}
		if (!SourceFileExists)
		{
			return null;
		}
		List<TimeSeriesVariable> timeSeriesVariables = null;
		if (outputBlock.RoughlyEquals("Base Output") || string.IsNullOrEmpty(outputBlock))
		{
			timeSeriesVariables = ReadResultVariables(fid);
		}
		else if (outputBlock.RoughlyEquals("DSS Hydrograph Output"))
		{
			timeSeriesVariables = ReadResultVariables_DSSHydrograph(fid);
		}
		else
		{
			if (!outputBlock.RoughlyEquals("DSS Profile Output"))
			{
				return null;
			}
			timeSeriesVariables = ReadResultVariables_DSSProfile(fid);
		}
		return timeSeriesVariables?.FirstOrDefault([SpecialName] (TimeSeriesVariable tsv) => tsv.VariableName.RoughlyEquals("Total Flow"))?.TimeSeries;
	}

	float[] IFlowHydrograph.ReadAggregatedFlowHydrograph(int fid,string outputBlock )
	{
		//ILSpy generated this explicit interface implementation from .override directive in ReadAggregatedFlowHydrograph
		return this.ReadAggregatedFlowHydrograph(fid, outputBlock);
	}

	public float GetOutputTimestepInSeconds(string outputBlock = "")
	{
		if (Geometry == null || Geometry.Results == null)
		{
			return 0f;
		}
		return Geometry.Results.GetOutputTimestepInSeconds(outputBlock);
	}

	float IFlowHydrograph.GetOutputTimestepInSeconds(string outputBlock )
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetOutputTimestepInSeconds
		return this.GetOutputTimestepInSeconds(outputBlock);
	}

	private void ReadSegmentedFlowHydrograph(int fid, ref float[,] data, string outputBlock = "")
	{
		if (!Geometry.IsResultFile)
		{
			return;
		}
		using H5Assist.VB.H5Reader h5Reader = new H5Assist.VB.H5Reader(Geometry.Results.SourceFilename);
		string textValue = "";
		if (IsConnection(fid))
		{
			textValue = ((!IsConnectionInternal(fid)) ? ResultsDatasets.Unsteady.TimeSeriesOutput.SA2DConnections.FlowSegments(GetFeatureName(fid)) : ResultsDatasets.Unsteady.TimeSeriesOutput.SA2DConnections.FlowSegments(GetUSSA2DName(fid), GetFeatureName(fid)));
		}
		else
		{
			textValue = ResultsDatasets.Unsteady.TimeSeriesOutput.LateralStructures.FlowSegments(GetFeatureName(fid));
		}
		if (!string.IsNullOrEmpty(outputBlock))
		{
			textValue = textValue.Replace("Base Output", outputBlock);
		}
		if (h5Reader.ExistsAsDataset(textValue))
		{
			h5Reader.ReadDataset(textValue, ref data);
		}
	}

	public List<Tuple<float, float>[]> ReadSubmergedTW(int fid)
	{
		string groupName = GetGroupName(fid);
		if (string.IsNullOrEmpty(groupName))
		{
			return null;
		}
		checked
		{
			using H5Assist.VB.H5Reader h5Reader = new H5Assist.VB.H5Reader(Geometry.SourceFilename);
			string path = "Geometry/Structures/Property Tables";
			if (!h5Reader.ExistsAsGroup(path))
			{
				return null;
			}
			string pathGroupNameTerm = path + "/" + groupName;
			if (!h5Reader.ExistsAsGroup(pathGroupNameTerm))
			{
				return null;
			}
			string textValue = pathGroupNameTerm + "/Info";
			string textValue2 = pathGroupNameTerm + "/Values";
			if (!h5Reader.ExistsAsDataset(textValue) || !h5Reader.ExistsAsDataset(textValue2))
			{
				return null;
			}
			long[] datasetDimensions = h5Reader.GetDatasetDimensions(textValue);
			int[,] rows = h5Reader.ReadRows<int>(textValue, 0, (int)datasetDimensions[0]);
			long[] datasetDimensions2 = h5Reader.GetDatasetDimensions(textValue2);
			float[,] rows2 = h5Reader.ReadRows<float>(textValue2, 0, (int)datasetDimensions2[0]);
			List<Tuple<float, float>[]> valueTupleValuesList = new List<Tuple<float, float>[]>();
			int count = rows.GetLength(0) - 1;
			for (int i = 0; i <= count; i++)
			{
				Tuple<float, float>[] valueTupleArray = new Tuple<float, float>[rows[i, 1] - 1 + 1];
				int rowsTerm = rows[i, 0];
				int count2 = valueTupleArray.Length - 1;
				for (int j = 0; j <= count2; j++)
				{
					valueTupleArray[j] = new Tuple<float, float>(rows2[rowsTerm + j, 0], rows2[rowsTerm + j, 1]);
				}
				valueTupleValuesList.Add(valueTupleArray);
			}
			return valueTupleValuesList;
		}
	}

	public List<TimeSeriesVariable> ReadResultVariables(int fid)
	{
		if (!Geometry.IsResultFile)
		{
			return null;
		}
		return ReadVariableUnitDataset(Geometry.Results.SourceFilename, GetStructureVariablesDataset(fid));
	}

	public List<TimeSeriesVariable> ReadLeveeBreaching(int fid, H5Assist.VB.H5Reader h5r = null)
	{
		if (!Geometry.IsResultFile)
		{
			return null;
		}
		if (h5r == null)
		{
			return ReadVariableUnitDataset(Geometry.Results.SourceFilename, GetLeveeBreachingDataset(fid));
		}
		return ReadVariableUnitDataset(h5r, GetLeveeBreachingDataset(fid));
	}

	public List<TimeSeriesVariable> ReadBreachingVariables(int fid, H5Assist.VB.H5Reader h5r = null)
	{
		if (!Geometry.IsResultFile)
		{
			return null;
		}
		if (h5r == null)
		{
			return ReadVariableUnitDataset(Geometry.Results.SourceFilename, GetBreachingVariablesDataset(fid));
		}
		return ReadVariableUnitDataset(h5r, GetBreachingVariablesDataset(fid));
	}

	public List<TimeSeriesVariable> ReadWeirVariables(int fid)
	{
		if (!Geometry.IsResultFile)
		{
			return null;
		}
		return ReadVariableUnitDataset(Geometry.Results.SourceFilename, GetWeirVariablesDataset(fid));
	}

	public List<TimeSeriesVariable> ReadResultVariables_DSSHydrograph(int fid)
	{
		if (!Geometry.IsResultFile)
		{
			return null;
		}
		string resultVariablesDataset = GetResultVariablesDataset(fid);
		return ReadVariableUnitDataset(Geometry.Results.SourceFilename, resultVariablesDataset);
	}

	public List<TimeSeriesVariable> ReadLeveeBreaching_DSSHydrograph(int fid)
	{
		if (!Geometry.IsResultFile)
		{
			return null;
		}
		string dset = GetLeveeBreachingDataset(fid).Replace("Base Output", "DSS Hydrograph Output");
		return ReadVariableUnitDataset(Geometry.Results.SourceFilename, dset);
	}

	public List<TimeSeriesVariable> ReadBreachingVariables_DSSHydrograph(int fid)
	{
		if (!Geometry.IsResultFile)
		{
			return null;
		}
		string dset = GetBreachingVariablesDataset(fid).Replace("Base Output", "DSS Hydrograph Output");
		return ReadVariableUnitDataset(Geometry.Results.SourceFilename, dset);
	}

	public List<TimeSeriesVariable> ReadWeirVariables_DSSHydrograph(int fid)
	{
		if (!Geometry.IsResultFile)
		{
			return null;
		}
		string dset = GetWeirVariablesDataset(fid).Replace("Base Output", "DSS Hydrograph Output");
		return ReadVariableUnitDataset(Geometry.Results.SourceFilename, dset);
	}

	public List<TimeSeriesVariable> ReadResultVariables_DSSProfile(int fid)
	{
		if (!Geometry.IsResultFile)
		{
			return null;
		}
		string dset = GetStructureVariablesDataset(fid).Replace("Base Output", "DSS Profile Output");
		return ReadVariableUnitDataset(Geometry.Results.SourceFilename, dset);
	}

	public List<TimeSeriesVariable> ReadBreachingVariables_DSSProfile(int fid)
	{
		if (!Geometry.IsResultFile)
		{
			return null;
		}
		string dset = GetBreachingVariablesDataset(fid).Replace("Base Output", "DSS Profile Output");
		return ReadVariableUnitDataset(Geometry.Results.SourceFilename, dset);
	}

	public List<TimeSeriesVariable> ReadWeirVariables_DSSProfile(int fid)
	{
		if (!Geometry.IsResultFile)
		{
			return null;
		}
		string dset = GetWeirVariablesDataset(fid).Replace("Base Output", "DSS Profile Output");
		return ReadVariableUnitDataset(Geometry.Results.SourceFilename, dset);
	}

	public List<TimeSeriesVariable> ReadCulvertGroups_DSSHydrograph(int fid, int culvGroupFid)
	{
		if (!Geometry.IsResultFile)
		{
			return null;
		}
		string dset = GetCulvertGroupDataset(fid, culvGroupFid).Replace("Base Output", "DSS Hydrograph Output");
		return ReadVariableUnitDataset(Geometry.Results.SourceFilename, dset);
	}

	public List<TimeSeriesVariable> ReadCulvertGroups_DSSProfile(int fid, int culvGroupFid)
	{
		if (!Geometry.IsResultFile)
		{
			return null;
		}
		string dset = GetCulvertGroupDataset(fid, culvGroupFid).Replace("Base Output", "DSS Profile Output");
		return ReadVariableUnitDataset(Geometry.Results.SourceFilename, dset);
	}

	public List<TimeSeriesVariable> ReadGateGroups_DSSHydrograph(int fid, int gateGroupFid)
	{
		if (!Geometry.IsResultFile)
		{
			return null;
		}
		string dset = GetGateGroupDataset(fid, gateGroupFid).Replace("Base Output", "DSS Hydrograph Output");
		return ReadVariableUnitDataset(Geometry.Results.SourceFilename, dset);
	}

	public List<TimeSeriesVariable> ReadGateGroups_DSSProfile(int fid, int gateGroupFid)
	{
		if (!Geometry.IsResultFile)
		{
			return null;
		}
		string dset = GetGateGroupDataset(fid, gateGroupFid).Replace("Base Output", "DSS Profile Output");
		return ReadVariableUnitDataset(Geometry.Results.SourceFilename, dset);
	}

	public string GetResultVariablesDataset(int fid)
	{
		return GetStructureType(fid) switch
		{
			"Bridge" => GetBridgeVariablesDataset(fid).Replace("Base Output", "DSS Hydrograph Output"), 
			"Culvert" => GetCulvertVariablesDataset(fid).Replace("Base Output", "DSS Hydrograph Output"), 
			"Multiple Opening" => GetMultipleOpeningVariablesDataset(fid).Replace("Base Output", "DSS Hydrograph Output"), 
			_ => GetStructureVariablesDataset(fid).Replace("Base Output", "DSS Hydrograph Output"), 
		};
	}

	public string GetLeveeBreachingDataset(int fid)
	{
		if (IsConnection(fid))
		{
			if (IsConnectionInternal(fid))
			{
				return ResultsDatasets.Unsteady.TimeSeriesOutput.SA2DConnections.LeveeBreaching(GetUSSA2DName(fid), GetFeatureName(fid));
			}
			return ResultsDatasets.Unsteady.TimeSeriesOutput.SA2DConnections.LeveeBreaching(GetFeatureName(fid));
		}
		if (Operators.CompareString(GetStructureType(fid), "Inline", TextCompare: false) == 0)
		{
			return ResultsDatasets.Unsteady.TimeSeriesOutput.InlineStructures.LeveeBreaching(GetFeatureName(fid));
		}
		return ResultsDatasets.Unsteady.TimeSeriesOutput.LateralStructures.LeveeBreaching(GetFeatureName(fid));
	}

	public string GetStructureVariablesDataset(int fid)
	{
		if (IsConnection(fid))
		{
			if (IsConnectionInternal(fid))
			{
				return ResultsDatasets.Unsteady.TimeSeriesOutput.SA2DConnections.StructureVariables(GetUSSA2DName(fid), GetFeatureName(fid));
			}
			return ResultsDatasets.Unsteady.TimeSeriesOutput.SA2DConnections.StructureVariables(GetFeatureName(fid));
		}
		string structureType = GetStructureType(fid);
		if (Operators.CompareString(structureType, "Inline", TextCompare: false) == 0)
		{
			return ResultsDatasets.Unsteady.TimeSeriesOutput.InlineStructures.StructureVariables(GetFeatureName(fid));
		}
		if (Operators.CompareString(structureType, "Lateral", TextCompare: false) == 0)
		{
			return ResultsDatasets.Unsteady.TimeSeriesOutput.LateralStructures.StructureVariables(GetFeatureName(fid));
		}
		throw new NotImplementedException("GetStructureVariablesDataset Not Implemented for " + structureType);
	}

	public string GetBridgeVariablesDataset(int fid)
	{
		string structureType = GetStructureType(fid);
		if (Operators.CompareString(structureType, "Bridge", TextCompare: false) == 0)
		{
			return ResultsDatasets.Unsteady.TimeSeriesOutput.Bridges.BridgeVariables(GetFeatureName(fid));
		}
		throw new NotImplementedException("GetBridgeVariablesDataset Not Implemented for " + structureType);
	}

	public string GetCulvertVariablesDataset(int fid)
	{
		string structureType = GetStructureType(fid);
		if (Operators.CompareString(structureType, "Culvert", TextCompare: false) == 0)
		{
			return ResultsDatasets.Unsteady.TimeSeriesOutput.Culverts.CulvertVariables(GetFeatureName(fid));
		}
		throw new NotImplementedException("GetCulvertVariablesDataset Not Implemented for " + structureType);
	}

	public string GetMultipleOpeningVariablesDataset(int fid)
	{
		string structureType = GetStructureType(fid);
		if (Operators.CompareString(structureType, "Multiple Opening", TextCompare: false) == 0)
		{
			return ResultsDatasets.Unsteady.TimeSeriesOutput.MultipleOpenings.MultipleOpeningsVariables(GetFeatureName(fid));
		}
		throw new NotImplementedException("GetMultipleOpeningVariablesDataset Not Implemented for " + structureType);
	}

	public string GetBreachingVariablesDataset(int fid)
	{
		if (IsConnection(fid))
		{
			if (IsConnectionInternal(fid))
			{
				return ResultsDatasets.Unsteady.TimeSeriesOutput.SA2DConnections.BreachingVariables(GetUSSA2DName(fid), GetFeatureName(fid));
			}
			return ResultsDatasets.Unsteady.TimeSeriesOutput.SA2DConnections.BreachingVariables(GetFeatureName(fid));
		}
		if (Operators.CompareString(GetStructureType(fid), "Inline", TextCompare: false) == 0)
		{
			return ResultsDatasets.Unsteady.TimeSeriesOutput.InlineStructures.BreachingVariables(GetFeatureName(fid));
		}
		return ResultsDatasets.Unsteady.TimeSeriesOutput.LateralStructures.BreachingVariables(GetFeatureName(fid));
	}

	public string GetCulvertGroupDataset(int fid, int culvGroupFid)
	{
		if (IsConnection(fid))
		{
			if (IsConnectionInternal(fid))
			{
				return ResultsDatasets.Unsteady.TimeSeriesOutput.SA2DConnections.CulvertGroup(GetUSSA2DName(fid), GetFeatureName(fid), Geometry.CulvertGroups.GetFeatureName(culvGroupFid));
			}
			return ResultsDatasets.Unsteady.TimeSeriesOutput.SA2DConnections.CulvertGroup(GetFeatureName(fid), Geometry.CulvertGroups.GetFeatureName(culvGroupFid));
		}
		if (Operators.CompareString(GetStructureType(fid), "Inline", TextCompare: false) == 0)
		{
			return ResultsDatasets.Unsteady.TimeSeriesOutput.InlineStructures.CulvertGroup(GetFeatureName(fid), Geometry.CulvertGroups.GetFeatureName(culvGroupFid));
		}
		return ResultsDatasets.Unsteady.TimeSeriesOutput.LateralStructures.CulvertGroup(GetFeatureName(fid), Geometry.CulvertGroups.GetFeatureName(culvGroupFid));
	}

	public string GetGateGroupDataset(int fid, int gateGroupFid)
	{
		if (IsConnection(fid))
		{
			if (IsConnectionInternal(fid))
			{
				return ResultsDatasets.Unsteady.TimeSeriesOutput.SA2DConnections.GateGroup(GetUSSA2DName(fid), GetFeatureName(fid), Geometry.GateGroups.GetFeatureName(gateGroupFid));
			}
			return ResultsDatasets.Unsteady.TimeSeriesOutput.SA2DConnections.GateGroup(GetFeatureName(fid), Geometry.GateGroups.GetFeatureName(gateGroupFid));
		}
		if (Operators.CompareString(GetStructureType(fid), "Inline", TextCompare: false) == 0)
		{
			return ResultsDatasets.Unsteady.TimeSeriesOutput.InlineStructures.GateGroup(GetFeatureName(fid), Geometry.GateGroups.GetFeatureName(gateGroupFid));
		}
		return ResultsDatasets.Unsteady.TimeSeriesOutput.LateralStructures.GateGroup(GetFeatureName(fid), Geometry.GateGroups.GetFeatureName(gateGroupFid));
	}

	public string GetWeirVariablesDataset(int fid)
	{
		if (IsConnection(fid))
		{
			if (IsConnectionInternal(fid))
			{
				return ResultsDatasets.Unsteady.TimeSeriesOutput.SA2DConnections.WeirVariables(GetUSSA2DName(fid), GetFeatureName(fid));
			}
			return ResultsDatasets.Unsteady.TimeSeriesOutput.SA2DConnections.WeirVariables(GetFeatureName(fid));
		}
		return ResultsDatasets.Unsteady.TimeSeriesOutput.LateralStructures.WeirVariables(GetFeatureName(fid));
	}

	public string GetResultGroupName(int fid)
	{
		if (IsConnection(fid))
		{
			if (IsConnectionInternal(fid))
			{
				return ResultsDatasets.Unsteady.TimeSeriesOutput.SA2DConnections.GroupName(GetUSSA2DName(fid), GetFeatureName(fid));
			}
			return ResultsDatasets.Unsteady.TimeSeriesOutput.SA2DConnections.GroupName(GetFeatureName(fid));
		}
		string structureType = GetStructureType(fid);
		return structureType switch
		{
			"Inline" => ResultsDatasets.Unsteady.TimeSeriesOutput.InlineStructures.GroupName(GetFeatureName(fid)), 
			"Lateral" => ResultsDatasets.Unsteady.TimeSeriesOutput.LateralStructures.GroupName(GetFeatureName(fid)), 
			"Bridge" => ResultsDatasets.Unsteady.TimeSeriesOutput.Bridges.GroupName(GetFeatureName(fid)), 
			_ => throw new NotImplementedException("GetResultGroupName Not Implemented for " + structureType), 
		};
	}

	public Profile GetCenterlineProfile(int fid)
	{
		return GetCenterlineProfileProperties(fid)?.GetProfile();
	}

	Profile IHasProfileProperties.GetProfile(int fid)
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetCenterlineProfile
		return this.GetCenterlineProfile(fid);
	}

	public void SetCenterlineProfile(int fid, Profile pf)
	{
		InMemoryProfileInfo pfInfo = new InMemoryProfileInfo(pf);
		SetCenterlineProfileProperties(fid, pfInfo);
	}

	void IHasProfileProperties.SetProfile(int fid,Profile pf)
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetCenterlineProfile
		this.SetCenterlineProfile(fid, pf);
	}

	public IProfileInfo GetCenterlineProfileProperties(int xsid)
	{
		return FeatureRow(xsid)["Centerline Profile"] as IProfileInfo;
	}

	IProfileInfo IHasProfileProperties.GetProfileProperties(int xsid)
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetCenterlineProfileProperties
		return this.GetCenterlineProfileProperties(xsid);
	}

	public void SetCenterlineProfileProperties(int xsid, IProfileInfo pfInfo)
	{
		FeatureRow(xsid)["Centerline Profile"] = pfInfo;
	}

	void IHasProfileProperties.SetProfileProperties(int xsid,IProfileInfo pfInfo)
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetCenterlineProfileProperties
		this.SetCenterlineProfileProperties(xsid, pfInfo);
	}

	public Profile GetBridgeUpstreamProfile(int fid)
	{
		return GetBridgeUpstreamProfileProperties(fid)?.GetProfile();
	}

	public void SetBridgeUpstreamProfile(int fid, Profile pf)
	{
		InMemoryProfileInfo pfInfo = new InMemoryProfileInfo(pf);
		SetBridgeUpstreamProfileProperties(fid, pfInfo);
	}

	public IProfileInfo GetBridgeUpstreamProfileProperties(int xsid)
	{
		return FeatureRow(xsid)["Upstream BR Profile"] as IProfileInfo;
	}

	public void SetBridgeUpstreamProfileProperties(int xsid, IProfileInfo pfInfo)
	{
		FeatureRow(xsid)["Upstream BR Profile"] = pfInfo;
	}

	public Profile GetBridgeDownstreamProfile(int fid)
	{
		return GetBridgeDownstreamProfileProperties(fid)?.GetProfile();
	}

	public void SetBridgeDownstreamProfile(int fid, Profile pf)
	{
		InMemoryProfileInfo pfInfo = new InMemoryProfileInfo(pf);
		SetBridgeDownstreamProfileProperties(fid, pfInfo);
	}

	public IProfileInfo GetBridgeDownstreamProfileProperties(int xsid)
	{
		return FeatureRow(xsid)["Downstream BR Profile"] as IProfileInfo;
	}

	public void SetBridgeDownstreamProfileProperties(int xsid, IProfileInfo pfInfo)
	{
		FeatureRow(xsid)["Downstream BR Profile"] = pfInfo;
	}

	public Profile GetBridgeUpstreamMannings(int fid)
	{
		return GetBridgeUpstreamManningsProfileProperties(fid)?.GetProfile();
	}

	public void SetBridgeUpstreamMannings(int fid, Profile pf)
	{
		InMemoryProfileInfo pfInfo = new InMemoryProfileInfo(pf);
		SetBridgeUpstreamManningsProfileProperties(fid, pfInfo);
	}

	public IProfileInfo GetBridgeUpstreamManningsProfileProperties(int xsid)
	{
		return FeatureRow(xsid)["Upstream BR Mannings"] as IProfileInfo;
	}

	public void SetBridgeUpstreamManningsProfileProperties(int xsid, IProfileInfo pfInfo)
	{
		FeatureRow(xsid)["Upstream BR Mannings"] = pfInfo;
	}

	public Profile GetBridgeDownstreamMannings(int fid)
	{
		return GetBridgeDownstreamManningsProfileProperties(fid)?.GetProfile();
	}

	public void SetBridgeDownstreamMannings(int fid, Profile pf)
	{
		InMemoryProfileInfo pfInfo = new InMemoryProfileInfo(pf);
		SetBridgeDownstreamManningsProfileProperties(fid, pfInfo);
	}

	public IProfileInfo GetBridgeDownstreamManningsProfileProperties(int xsid)
	{
		return FeatureRow(xsid)["Downstream BR Mannings"] as IProfileInfo;
	}

	public void SetBridgeDownstreamManningsProfileProperties(int xsid, IProfileInfo pfInfo)
	{
		FeatureRow(xsid)["Downstream BR Mannings"] = pfInfo;
	}

	public Profile GetBridgeUpstreamWeirProfile(int fid)
	{
		return GetBridgeUpstreamWeirProfileProperties(fid)?.GetProfile();
	}

	public void SetBridgeUpstreamWeirProfile(int fid, Profile pf)
	{
		InMemoryProfileInfo pfInfo = new InMemoryProfileInfo(pf);
		SetBridgeUpstreamWeirProfileProperties(fid, pfInfo);
	}

	public IProfileInfo GetBridgeUpstreamWeirProfileProperties(int xsid)
	{
		return FeatureRow(xsid)["Upstream BR Weir Profile"] as IProfileInfo;
	}

	public void SetBridgeUpstreamWeirProfileProperties(int xsid, IProfileInfo pfInfo)
	{
		FeatureRow(xsid)["Upstream BR Weir Profile"] = pfInfo;
	}

	public Profile GetBridgeDownstreamWeirProfile(int fid)
	{
		return GetBridgeDownstreamWeirProfileProperties(fid)?.GetProfile();
	}

	public void SetBridgeDownstreamWeirProfile(int fid, Profile pf)
	{
		InMemoryProfileInfo pfInfo = new InMemoryProfileInfo(pf);
		SetBridgeDownstreamWeirProfileProperties(fid, pfInfo);
	}

	public IProfileInfo GetBridgeDownstreamWeirProfileProperties(int xsid)
	{
		return FeatureRow(xsid)["Downstream BR Weir Profile"] as IProfileInfo;
	}

	public void SetBridgeDownstreamWeirProfileProperties(int xsid, IProfileInfo pfInfo)
	{
		FeatureRow(xsid)["Downstream BR Weir Profile"] = pfInfo;
	}

	public Profile GetBridgeUpstreamLidProfile(int fid)
	{
		return GetBridgeUpstreamLidProfileProperties(fid)?.GetProfile();
	}

	public void SetBridgeUpstreamLidProfile(int fid, Profile pf)
	{
		InMemoryProfileInfo pfInfo = new InMemoryProfileInfo(pf);
		SetBridgeUpstreamLidProfileProperties(fid, pfInfo);
	}

	public IProfileInfo GetBridgeUpstreamLidProfileProperties(int xsid)
	{
		return FeatureRow(xsid)["Upstream BR Lid Profile"] as IProfileInfo;
	}

	public void SetBridgeUpstreamLidProfileProperties(int xsid, IProfileInfo pfInfo)
	{
		FeatureRow(xsid)["Upstream BR Lid Profile"] = pfInfo;
	}

	public Profile GetBridgeDownstreamLidProfile(int fid)
	{
		return GetBridgeDownstreamLidProfileProperties(fid)?.GetProfile();
	}

	public void SetBridgeDownstreamLidProfile(int fid, Profile pf)
	{
		InMemoryProfileInfo pfInfo = new InMemoryProfileInfo(pf);
		SetBridgeDownstreamLidProfileProperties(fid, pfInfo);
	}

	public IProfileInfo GetBridgeDownstreamLidProfileProperties(int xsid)
	{
		return FeatureRow(xsid)["Downstream BR Lid Profile"] as IProfileInfo;
	}

	public void SetBridgeDownstreamLidProfileProperties(int xsid, IProfileInfo pfInfo)
	{
		FeatureRow(xsid)["Downstream BR Lid Profile"] = pfInfo;
	}

	public Profile GetCrossSectionUpstreamProfile(int fid)
	{
		return GetCrossSectionUpstreamProfileProperties(fid)?.GetProfile();
	}

	public void SetCrossSectionUpstreamProfile(int fid, Profile pf)
	{
		InMemoryProfileInfo pfInfo = new InMemoryProfileInfo(pf);
		SetCrossSectionUpstreamProfileProperties(fid, pfInfo);
	}

	public IProfileInfo GetCrossSectionUpstreamProfileProperties(int xsid)
	{
		return FeatureRow(xsid)["Upstream XS Profile"] as IProfileInfo;
	}

	public void SetCrossSectionUpstreamProfileProperties(int xsid, IProfileInfo pfInfo)
	{
		FeatureRow(xsid)["Upstream XS Profile"] = pfInfo;
	}

	public Profile GetCrossSectionDownstreamProfile(int fid)
	{
		return GetCrossSectionDownstreamProfileProperties(fid)?.GetProfile();
	}

	public void SetCrossSectionDownstreamProfile(int fid, Profile pf)
	{
		InMemoryProfileInfo pfInfo = new InMemoryProfileInfo(pf);
		SetCrossSectionDownstreamProfileProperties(fid, pfInfo);
	}

	public IProfileInfo GetCrossSectionDownstreamProfileProperties(int xsid)
	{
		return FeatureRow(xsid)["Downstream XS Profile"] as IProfileInfo;
	}

	public void SetCrossSectionDownstreamProfileProperties(int xsid, IProfileInfo pfInfo)
	{
		FeatureRow(xsid)["Downstream XS Profile"] = pfInfo;
	}

	public Profile GetCrossSectionUpstreamMannings(int fid)
	{
		return GetCrossSectionUpstreamManningsProfileProperties(fid)?.GetProfile();
	}

	public void SetCrossSectionUpstreamMannings(int fid, Profile pf)
	{
		InMemoryProfileInfo pfInfo = new InMemoryProfileInfo(pf);
		SetCrossSectionUpstreamManningsProfileProperties(fid, pfInfo);
	}

	public IProfileInfo GetCrossSectionUpstreamManningsProfileProperties(int xsid)
	{
		return FeatureRow(xsid)["Upstream XS Mannings"] as IProfileInfo;
	}

	public void SetCrossSectionUpstreamManningsProfileProperties(int xsid, IProfileInfo pfInfo)
	{
		FeatureRow(xsid)["Upstream XS Mannings"] = pfInfo;
	}

	public Profile GetCrossSectionDownstreamMannings(int fid)
	{
		return GetCrossSectionDownstreamManningsProfileProperties(fid)?.GetProfile();
	}

	public void SetCrossSectionDownstreamMannings(int fid, Profile pf)
	{
		InMemoryProfileInfo pfInfo = new InMemoryProfileInfo(pf);
		SetCrossSectionDownstreamManningsProfileProperties(fid, pfInfo);
	}

	public IProfileInfo GetCrossSectionDownstreamManningsProfileProperties(int xsid)
	{
		return FeatureRow(xsid)["Downstream XS Mannings"] as IProfileInfo;
	}

	public void SetCrossSectionDownstreamManningsProfileProperties(int xsid, IProfileInfo pfInfo)
	{
		FeatureRow(xsid)["Downstream XS Mannings"] = pfInfo;
	}

	public Profile GetRatingCurve(int fid)
	{
		return GetRatingCurveProfileProperties(fid)?.GetProfile();
	}

	public void SetRatingCurve(int fid, Profile pf)
	{
		InMemoryProfileInfo pfInfo = new InMemoryProfileInfo(pf);
		SetRatingCurveProfileProperties(fid, pfInfo);
	}

	public IProfileInfo GetRatingCurveProfileProperties(int fid)
	{
		return FeatureRow(fid)["Rating Curve"] as IProfileInfo;
	}

	public void SetRatingCurveProfileProperties(int fid, IProfileInfo pfInfo)
	{
		FeatureRow(fid)["Rating Curve"] = pfInfo;
	}

	private Profile TryGetProfile(int fid, string colName)
	{
		object objectValue = RuntimeHelpers.GetObjectValue(FeatureRow(fid)[colName]);
		if (objectValue == null || Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)))
		{
			return null;
		}
		return objectValue as Profile;
	}

	public bool GetAutoUpdate(int xsid)
	{
		return DataTableExtensions.GetBoolean(FeatureTable(), xsid, "Auto Update");
	}

	private void SetAutoUpdate(int xsid, bool condition)
	{
		FeatureRow(xsid)["Auto Update"] = condition;
	}

	private bool GetTrackTerrain(int xsid)
	{
		return DataTableExtensions.GetBoolean(FeatureTable(), xsid, "Track Terrain");
	}

	private void SetTrackTerrain(int xsid, bool condition)
	{
		FeatureRow(xsid)["Track Terrain"] = condition;
	}

	public void SetAllProfilesTrackTerrain()
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				SetProfileTrackTerrain(i);
			}
		}
	}

	public void SetProfileTrackTerrain(IList<int> fids)
	{
		foreach (int fid in fids)
		{
			SetProfileTrackTerrain(fid);
		}
	}

	public void SetProfileTrackTerrain(int fid)
	{
		TerrainQueryProfileInfo terrainQueryProfileInfo = new TerrainQueryProfileInfo(Polyline(fid), Geometry);
		terrainQueryProfileInfo.AttachFilterer(SharedData.MaxNumberOfPointsOnXS);
		terrainQueryProfileInfo.AttachDecimalPlaces(SharedData.HorizontalDecimalPlaces, SharedData.VerticalDecimalPlaces);
		if (Operators.CompareString(GetStructureType(fid), "Bridge", TextCompare: false) == 0)
		{
			SetBridgeUpstreamWeirProfileProperties(fid, terrainQueryProfileInfo);
			SetBridgeDownstreamWeirProfileProperties(fid, terrainQueryProfileInfo);
		}
		else
		{
			SetCenterlineProfileProperties(fid, terrainQueryProfileInfo);
		}
		SetTrackTerrain(fid, condition: true);
	}

	public string GetConnectionName(int fID)
	{
		return DataTableExtensions.GetString(FeatureTable(), fID, "Connection Name");
	}

	public void SetConnectionName(int fID, string connName)
	{
		FeatureRow(fID)["Connection Name"] = connName;
	}

	public string GetStructureType(int fID)
	{
		return DataTableExtensions.GetString(FeatureTable(), fID, "Type");
	}

	public void SetStructureType(int fID, string typeName)
	{
		FeatureRow(fID)["Type"] = typeName;
	}

	public string GetLastEdited(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "Last Edited");
	}

	public void SetLastEdited(int fid, string lastEdited)
	{
		FeatureRow(fid)["Last Edited"] = lastEdited;
	}

	public string GetNodeName(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "Node Name");
	}

	public void SetNodeName(int fid, string name)
	{
		FeatureRow(fid)["Node Name"] = name;
	}

	public string GetGroupName(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "Group Name");
	}

	public string GetDescription(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "Node Descriptions");
	}

	public void SetDescription(int fid, string desc)
	{
		FeatureRow(fid)["Node Descriptions"] = desc;
	}

	public string GetUSType(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "US Type");
	}

	public void SetUSType(int fID, string typeName)
	{
		FeatureRow(fID)["US Type"] = typeName;
	}

	public string GetDSType(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "DS Type");
	}

	public void SetDSType(int fID, string typeName)
	{
		FeatureRow(fID)["DS Type"] = typeName;
	}

	public string GetUSRiverName(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "US River");
	}

	public void SetUSRiverName(int fID, string rivName)
	{
		FeatureRow(fID)["US River"] = rivName;
	}

	public string GetDSRiverName(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "DS River");
	}

	public void SetDSRiverName(int fID, string rivName)
	{
		FeatureRow(fID)["DS River"] = rivName;
	}

	public string GetUSReachName(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "US Reach");
	}

	public void SetUSReachName(int fID, string rchName)
	{
		FeatureRow(fID)["US Reach"] = rchName;
	}

	public string GetDSReachName(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "DS Reach");
	}

	public void SetDSReachName(int fID, string rchName)
	{
		FeatureRow(fID)["DS Reach"] = rchName;
	}

	public string GetUSRiverStationName(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "US River Station");
	}

	public void SetUSRiverStationName(int fID, string rsName)
	{
		FeatureRow(fID)["US River Station"] = rsName;
	}

	public string GetDSRiverStationName(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "DS River Station");
	}

	public void SetDSRiverStationName(int fID, string rsName)
	{
		FeatureRow(fID)["DS River Station"] = rsName;
	}

	public string GetUSSA2DName(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "US SA/2D");
	}

	public void SetUSSA2DName(int fID, string sa2DName)
	{
		FeatureRow(fID)["US SA/2D"] = sa2DName;
	}

	public string GetDSSA2DName(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "DS SA/2D");
	}

	public void SetDSSA2DName(int fID, string sa2DName)
	{
		FeatureRow(fID)["DS SA/2D"] = sa2DName;
	}

	public string GetMode(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "Structure Type");
	}

	public void SetMode(int fid, string mode)
	{
		FeatureRow(fid)["Structure Type"] = mode;
	}

	public int GetStructureID(string river, string reach, string rs, string connection)
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (Operators.CompareString(IHasRRRSLayerExtensions.GetRiverName(this, i), river, TextCompare: false) == 0 && Operators.CompareString(IHasRRRSLayerExtensions.GetReachName(this, i), reach, TextCompare: false) == 0 && Operators.CompareString(IHasRRRSLayerExtensions.GetRiverStationName(this, i), rs, TextCompare: false) == 0 && Operators.CompareString(GetConnectionName(i), connection, TextCompare: false) == 0)
				{
					return i;
				}
			}
			return -1;
		}
	}

	public float GetWeirWidth(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Weir Width");
	}

	float IProvideWidth.GetWidth(int fid)
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetWeirWidth
		return this.GetWeirWidth(fid);
	}

	public void SetWeirWidth(int fID, float weirWidth)
	{
		FeatureRow(fID)["Weir Width"] = weirWidth;
	}

	public float GetWeirUSSlope(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Weir US Slope");
	}

	public void SetWeirUSSlope(int fID, float slope)
	{
		FeatureRow(fID)["Weir US Slope"] = slope;
	}

	public float GetWeirDSSlope(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Weir DS Slope");
	}

	public void SetWeirDSSlope(int fID, float slope)
	{
		FeatureRow(fID)["Weir DS Slope"] = slope;
	}

	public List<TimeSeriesVariable> GetTimeSeriesVariables(int fid)
	{
		object objectValue = RuntimeHelpers.GetObjectValue(FeatureRow(fid)["Time Series Variables"]);
		if (objectValue == null || Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)))
		{
			List<TimeSeriesVariable> resultVariables = ReadResultVariables(fid);
			SetTimeSeriesVariables(fid, resultVariables);
			return resultVariables;
		}
		return objectValue as List<TimeSeriesVariable>;
	}

	public void SetTimeSeriesVariables(int fid, List<TimeSeriesVariable> vars)
	{
		FeatureRow(fid)["Time Series Variables"] = vars;
	}

	public StructureIdentifier GetStructureIdentifier(int fid)
	{
		return new StructureIdentifier(IHasRRRSLayerExtensions.GetRiverName(this, fid), IHasRRRSLayerExtensions.GetReachName(this, fid), IHasRRRSLayerExtensions.GetRiverStationName(this, fid), GetConnectionName(fid), "");
	}

	StructureIdentifier IStructureLayer.GetStructureIdentifier(int fid)
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetStructureIdentifier
		return this.GetStructureIdentifier(fid);
	}

	public int FindStructure(StructureIdentifier s)
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (GetStructureIdentifier(i).Matches(s))
				{
					return i;
				}
			}
			return -1;
		}
	}

	public MeshFV2D GetStructureUSMesh(int fid)
	{
		string uSType = GetUSType(fid);
		if (string.IsNullOrEmpty(uSType))
		{
			return null;
		}
		if (Operators.CompareString(uSType.Trim(), "2D", TextCompare: false) != 0)
		{
			return null;
		}
		string uSSA2DName = GetUSSA2DName(fid);
		RASD2FlowArea rASD2FlowArea = Geometry?.D2FlowArea;
		if (rASD2FlowArea == null)
		{
			return null;
		}
		int featureByName = rASD2FlowArea.GetFeatureByName(uSSA2DName);
		if (featureByName < 0)
		{
			return null;
		}
		return rASD2FlowArea.Mesh(featureByName);
	}

	public MeshFV2D GetStructureDSMesh(int fid)
	{
		string dSType = GetDSType(fid);
		if (string.IsNullOrEmpty(dSType))
		{
			return null;
		}
		if (Operators.CompareString(dSType.Trim(), "2D", TextCompare: false) != 0)
		{
			return null;
		}
		string dSSA2DName = GetDSSA2DName(fid);
		RASD2FlowArea rASD2FlowArea = Geometry?.D2FlowArea;
		if (rASD2FlowArea == null)
		{
			return null;
		}
		int featureByName = rASD2FlowArea.GetFeatureByName(dSSA2DName);
		if (featureByName < 0)
		{
			return null;
		}
		return rASD2FlowArea.Mesh(featureByName);
	}

	public float GetUSWeirDistance(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Upstream Distance");
	}

	public void SetUSWeirDistance(int fid, float dist)
	{
		FeatureRow(fid)["Upstream Distance"] = dist;
	}

	public List<AbutmentData> GetAbutments(int fid)
	{
		object objectValue = RuntimeHelpers.GetObjectValue(FeatureRow(fid)["Abutments"]);
		if (Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)))
		{
			return null;
		}
		return objectValue as List<AbutmentData>;
	}

	private void SetAbutments(int fid, List<AbutmentData> abutments)
	{
		FeatureRow(fid)["Abutments"] = abutments;
	}

	public List<PierData> GetPiers(int fid)
	{
		object objectValue = RuntimeHelpers.GetObjectValue(FeatureRow(fid)["Piers"]);
		if (Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)))
		{
			return null;
		}
		return objectValue as List<PierData>;
	}

	private void SetPiers(int fid, List<PierData> abutments)
	{
		FeatureRow(fid)["Piers"] = abutments;
	}

	private List<BridgeCoefficientData> GetBridgeCoefs(int fid)
	{
		object objectValue = RuntimeHelpers.GetObjectValue(FeatureRow(fid)["Bridge Coefficients"]);
		if (Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)))
		{
			return null;
		}
		return objectValue as List<BridgeCoefficientData>;
	}

	private void SetBridgeCoefs(int fid, List<BridgeCoefficientData> data)
	{
		FeatureRow(fid)["Bridge Coefficients"] = data;
	}

	public bool IsConnection(int fid)
	{
		return !string.IsNullOrEmpty(GetConnectionName(fid));
	}

	public bool IsConnectionInternal(int fid)
	{
		string uSSA2DName = GetUSSA2DName(fid);
		if (string.IsNullOrEmpty(uSSA2DName))
		{
			return false;
		}
		string dSSA2DName = GetDSSA2DName(fid);
		if (string.IsNullOrEmpty(dSSA2DName))
		{
			return false;
		}
		return string.Equals(uSSA2DName, dSSA2DName, StringComparison.CurrentCultureIgnoreCase);
	}

	public bool GetIsDefaultCenterline(int fid)
	{
		return DataTableExtensions.GetBoolean(FeatureTable(), fid, "Default Centerline");
	}

	public void SetIsDefaultCenterline(int fid, bool isDefaultCenterline)
	{
		FeatureRow(fid)["Default Centerline"] = isDefaultCenterline;
	}
}
