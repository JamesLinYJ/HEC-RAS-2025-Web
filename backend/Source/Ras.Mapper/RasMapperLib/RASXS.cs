using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Xml;
using H5Assist.Identifiers;
using H5Assist.VB;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Mapping;
using RasMapperLib.Utilities;
using Utility;
using Utility.Extensions;
using Utility.Progress;
using Utility.Systems;

namespace RasMapperLib;

public class RASXS : PolylineFeatureLayer, IGeometryLayer, IHasRRRSLayer, IHasProfileProperties
{
	int IHasRRRSLayer.RiverStationDecimalPlaces { get => _riverStationDecimalPlaces; set => _riverStationDecimalPlaces = value; }
	SharedData.EnumRSUnits IHasRRRSLayer.UnitsRiverStation { get => _unitsRiverStation; set => _unitsRiverStation = value; }

	public enum GenerateExportMode
	{
		XSIntersections,
		XSLevees,
		XSIneffective,
		XSBlockedObstructions
	}

	public struct RiverReachXSData
	{
		public int FirstXS;

		public int LastXS;

		public int Count;
	}

	public struct XSRiverStations
	{
		public int fid;

		public double computedRS;

		public string roundedRS;
	}

	public struct IneffectiveBlock
	{
		public float LeftStation;

		public float RightStation;

		public float Elevation;

		public bool Permanent;

		public IneffectiveBlock(float ls, float rs, float elev, bool perm)
		{
			this = default;
			LeftStation = ls;
			RightStation = rs;
			Elevation = elev;
			Permanent = perm;
		}

		public override bool Equals(object obj)
		{
			if (obj is IneffectiveBlock)
			{
				IneffectiveBlock ineffectiveBlock = ((obj != null) ? ((IneffectiveBlock)obj) : default(IneffectiveBlock));
				return (double)System.Math.Abs(LeftStation - ineffectiveBlock.LeftStation) < 0.01 && (double)System.Math.Abs(RightStation - ineffectiveBlock.RightStation) < 0.01 && (double)System.Math.Abs(Elevation - ineffectiveBlock.Elevation) < 0.01;
			}
			return false;
		}

		public override int GetHashCode()
		{
			return MathHelpers.CombineHashCodes<float>((float)System.Math.Round(LeftStation, 2), (float)System.Math.Round(RightStation, 2), (float)System.Math.Round(Elevation, 2));
		}
	}

	public struct Obstruction
	{
		public float LeftStation;

		public float RightStation;

		public float Elevation;

		public Obstruction(float ls, float rs, float elev)
		{
			this = default;
			LeftStation = ls;
			RightStation = rs;
			Elevation = elev;
		}

		public override bool Equals(object obj)
		{
			if (obj is Obstruction)
			{
				Obstruction obstruction = ((obj != null) ? ((Obstruction)obj) : default(Obstruction));
				return (double)System.Math.Abs(LeftStation - obstruction.LeftStation) < 0.01 && (double)System.Math.Abs(RightStation - obstruction.RightStation) < 0.01 && (double)System.Math.Abs(Elevation - obstruction.Elevation) < 0.01;
			}
			return false;
		}

		public override int GetHashCode()
		{
			return MathHelpers.CombineHashCodes<float>((float)System.Math.Round(LeftStation, 2), (float)System.Math.Round(RightStation, 2), (float)System.Math.Round(Elevation, 2));
		}
	}

	public class Indexer
	{
		private DataTable _dt;

		private Dictionary<string, List<int>> _RSDictionary;

		private int _rivCol;

		private int _rchCol;

		private int _rsCol;

		public Indexer(RASXS xs)
		{
			_RSDictionary = new Dictionary<string, List<int>>();
			_dt = xs.FeatureTable();
			_rivCol = _dt.Columns.IndexOf("River");
			_rchCol = _dt.Columns.IndexOf("Reach");
			_rsCol = _dt.Columns.IndexOf("River Station");
			checked
			{
				int count = _dt.Rows.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					string key = Form.DataRowItemToString(_dt.Rows[i], _rsCol);
					if (!_RSDictionary.ContainsKey(key))
					{
						_RSDictionary[key] = new List<int>();
					}
					_RSDictionary[key].Add(i);
				}
			}
		}

		public int GetXSFID(string River, string Reach, string RS)
		{
			List<int> localValue = null;
			if (_RSDictionary.TryGetValue(RS, out localValue))
			{
				foreach (int ordinal in localValue)
				{
					DataRow row = _dt.Rows[ordinal];
					if (Operators.CompareString(Form.DataRowItemToString(row, _rivCol), River, TextCompare: false) == 0 && Operators.CompareString(Form.DataRowItemToString(row, _rchCol), Reach, TextCompare: false) == 0)
					{
						return ordinal;
					}
				}
			}
			return -1;
		}
	}

	public const string sRiverStationComputed = "ComputedUnroundedRS";

	public const string sLengthLOB = "Length LOB";

	public const string sLengthChan = "Length Channel";

	public const string sLengthROB = "Length ROB";

	public const string sContractionCoef = "Steady Contr. Coef.";

	public const string sExpansionCoef = "Steady Expan. Coef.";

	public const string sUSFContractionCoef = "Unsteady Contr. Coef.";

	public const string sUSFExpansionCoef = "Unsteady Expan. Coef.";

	public const string sMinorLoss = "Minor Loss";

	public const string sSkew = "Skew";

	public const string sLBank = "Left Bank";

	public const string sRBank = "Right Bank";

	public const string sLLevee = "Levee Left Sta.";

	public const string sLLeveeElev = "Levee Left Elev.";

	public const string sRLevee = "Levee Right Sta.";

	public const string sRLeveeElev = "Levee Right Elev.";

	public const string sProfile = "Profile";

	public const string sBankProperties = "Bank Properties";

	public const string sLeveeProperties = "Levee Properties";

	public const string sManningN = "Manning Data";

	public const string sFrictionMode = "Friction Mode";

	public const string sBasicMannFricMode = "Basic Mann n";

	public const string sHorizontalMannFricMode = "Horiz Mann n";

	public const string sHPVertSlices = "HP Vert. Slices";

	public const string sHPStartElev = "HP Start Elev.";

	public const string sHPVertIncr = "HP Vert. Incr.";

	public const string sHPLOBSlices = "HP ROB Slices";

	public const string sHPChanSlices = "HP Chan. Slices";

	public const string sHPROBSlices = "HP LOB Slices";

	public const string sPilotChannelInvert = "Pilot Channel Invert";

	public const string sPilotChannelWidth = "Pilot Channel Width";

	public const string sPilotChannelMann = "Pilot Channel Mann.";

	public const string sDeckPreissmanSlot = "Deck Preissman Slot";

	public const string sDeckLowStaElev = "Low Deck Sta/Elev";

	public const string sDeckHighStaElev = "High Deck Sta/Elev";

	public const string sRatingCurveHWCheck = "Rating Curve HW Check";

	public const string sRatingCurve = "Rating Curve";

	public const string sIneffectiveRegions = "Ineffective Regions";

	public const string sBlockedObstructions = "Blocked Obstructions";

	public const string sIceJamChannel = "Ice Jam (Channel)";

	public const string sIceJamOB = "Ice Jam (Overbank)";

	public const string sIceJamFixedMann = "Ice Fixed Mann. n";

	public const string sIceThicknessLeftOB = "Ice Thickness (Left OB)";

	public const string sIceThicknessChannel = "Ice Thickness (Channel)";

	public const string sIceThicknessRightOB = "Ice Thickness (Right OB)";

	public const string sIceMannLeftOB = "Ice Mann. n (Left OB)";

	public const string sIceMannChannel = "Ice Mann. n (Channel)";

	public const string sIceMannRightOB = "Ice Mann. n (Right OB)";

	public const string sIceSpecificGravity = "Ice Specific Gravity";

	public const string sIceFrictionAngle = "Ice Friction Angle";

	public const string sIcePorosity = "Ice Porosity";

	public const string sIceK1 = "Ice K1";

	public const string sIceMaxMeanVel = "Ice Max Mean Velocity";

	public const string sIceCohesion = "Ice Cohesion";

	private const string sStationOffset = "Stationing Offset";

	public const string HDFGroupName = "Geometry/Cross Sections";

	private const string HDFGroupStationElevation = "Geometry/Cross Sections/Station Elevation";

	private const string HDFGroupStationElevationInfo = "Geometry/Cross Sections/Station Elevation Info";

	private const string HDFGroupStationElevationValues = "Geometry/Cross Sections/Station Elevation Values";

	private const string HDFGroupDeckLow = "Geometry/Cross Sections/Deck Low";

	private const string HDFGroupDeckHigh = "Geometry/Cross Sections/Deck High";

	private const string HDFGroupRatingCurve = "Geometry/Cross Sections/Rating Curve";

	private const string HDFGroupObstructions = "Geometry/Cross Sections/Obstruction";

	private const string HDFGroupObstructionInfo = "Geometry/Cross Sections/Obstruction Info";

	private const string HDFGroupObstructionValues = "Geometry/Cross Sections/Obstruction Blocks";

	private const string HDFGroupIneffective = "Geometry/Cross Sections/Ineffective";

	private const string HDFGroupIneffectiveInfo = "Geometry/Cross Sections/Ineffective Info";

	private const string HDFGroupIneffectiveValues = "Geometry/Cross Sections/Ineffective Blocks";

	private const string sPlotBankStationsOption = "Bank Stations";

	private const string sPlotIneffectiveAreasOption = "Ineffective Areas";

	private const string sPlotBlockedObstructionsOption = "Blocked Obstructions";

	public const string sPlotManningsNOption = "Manning's n Values";

	private const string sPlotReachLengthsOption = "Reach Lengths";

	private const string sPlotRValueOption = "Ratio of Cut Line to XS Line";


	internal const string sUnitsRiverStation = "UnitsRiverStation";

	internal const string sRiverStationDecimalPlaces = "RiverStationDecimalPlaces";

	private const string IsDefaultCenterline = "Default Centerline";

	private Dictionary<string, RiverReachXSData> _riverReachXSData;

	private Dictionary<string, Dictionary<int, int>> _outputTypeToBaseFIDToLocalFID;

	public const string sRColName = "Cutline to XS Ratio";

	public const string sRColNameValid = "Cutline to XS Ratio Valid";

	private SharedData.EnumRSUnits _unitsRiverStation { get; set; }

	private int _riverStationDecimalPlaces { get; set; }

	public RASGeometry Geometry { get; }
string IGeometryLayer.HDFGroupName=> "Geometry/Cross Sections";

	public override bool ForwardEditRequestsToParent { get; }

	public override string SimpleFeatureTypeName { get; set; }

	public override bool CanRename { get; }

	public override string DefaultLayerName => "Cross Sections";

	public RASXS(RASGeometry geometry)
		: base("Cross Sections", geometry.SourceFilename)
	{
		_riverReachXSData = null;
		ForwardEditRequestsToParent = true;
		SimpleFeatureTypeName = "Cross Sections";
		CanRename = false;
		Geometry = geometry;
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		DataColumnGroup dataColumnGroup = new DataColumnGroup("River, Reach, River Station", isVisible: true);
		AddManagedAttributeColumn("River", "River", typeof(string), dataColumnGroup, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Reach", "Reach", typeof(string), dataColumnGroup, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("River Station", "RS", typeof(string), dataColumnGroup);
		AddManagedAttributeColumn("Node Name", "Name", typeof(string), dataColumnGroup);
		AddManagedAttributeColumn("Node Descriptions", "Description", typeof(string), dataColumnGroup);
		DataColumnGroup dataColumnGroup2 = new DataColumnGroup("Bank Parameters", isVisible: true);
		AddManagedAttributeColumn("Left Bank", "Left Bank", typeof(float), dataColumnGroup2);
		AddManagedAttributeColumn("Right Bank", "Right Bank", typeof(float), dataColumnGroup2);
		AddAttributeColumn("Bank Properties", typeof(BankProperties), dataColumnGroup2, DataColumnViewState.Invisible);
		DataColumnGroup dataColumnGroup3 = new DataColumnGroup("Reach Lengths", isVisible: true);
		AddManagedAttributeColumn("Length LOB", "Len Left", typeof(float), dataColumnGroup3);
		AddManagedAttributeColumn("Length Channel", "Len Channel", typeof(float), dataColumnGroup3);
		AddManagedAttributeColumn("Length ROB", "Len Right", typeof(float), dataColumnGroup3);
		AddAttributeColumn("ComputedUnroundedRS", typeof(double), dataColumnGroup3, DataColumnViewState.Invisible);
		DataColumnGroup dataColumnGroup4 = new DataColumnGroup("Manning's n", isVisible: false);
		AddAttributeColumn("Manning Data", typeof(Point2DCollection), dataColumnGroup4, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Friction Mode", "Friction Mode", typeof(string), dataColumnGroup4, DataColumnViewState.GrayedOut);
		DataColumnGroup dataColumnGroup5 = new DataColumnGroup("Ineffective Areas", isVisible: false);
		AddAttributeColumn("Ineffective Regions", typeof(List<IneffectiveBlock>), dataColumnGroup5, DataColumnViewState.GrayedOut);
		DataColumnGroup dataColumnGroup6 = new DataColumnGroup("Blocked Obstructions", isVisible: false);
		AddAttributeColumn("Blocked Obstructions", typeof(List<Obstruction>), dataColumnGroup6, DataColumnViewState.GrayedOut);
		DataColumnGroup dataColumnGroup7 = new DataColumnGroup("Contraction/Expansion/Loss Parameters", isVisible: false);
		AddManagedAttributeColumn("Steady Contr. Coef.", "Contr", typeof(float), dataColumnGroup7);
		AddManagedAttributeColumn("Steady Expan. Coef.", "Expan", typeof(float), dataColumnGroup7);
		AddManagedAttributeColumn("Unsteady Contr. Coef.", "Contr (USF)", typeof(float), dataColumnGroup7);
		AddManagedAttributeColumn("Unsteady Expan. Coef.", "Expan (USF)", typeof(float), dataColumnGroup7);
		AddManagedAttributeColumn("Minor Loss", "Minor Loss", typeof(float), dataColumnGroup7);
		AddManagedAttributeColumn("Skew", "Skew", typeof(float), dataColumnGroup7);
		DataColumnGroup dataColumnGroup8 = new DataColumnGroup("Levee Parameters", isVisible: false);
		AddManagedAttributeColumn("Levee Left Sta.", "Left Levee Sta", typeof(float), dataColumnGroup8);
		AddManagedAttributeColumn("Levee Left Elev.", "Left Levee Elev", typeof(float), dataColumnGroup8);
		AddManagedAttributeColumn("Levee Right Sta.", "Right Levee Sta", typeof(float), dataColumnGroup8);
		AddManagedAttributeColumn("Levee Right Elev.", "Right Levee Elev", typeof(float), dataColumnGroup8);
		AddAttributeColumn("Levee Properties", typeof(LeveeProperties), dataColumnGroup8, DataColumnViewState.Invisible);
		DataColumnGroup dataColumnGroup9 = new DataColumnGroup("Hydraulic Properties", isVisible: false);
		AddManagedAttributeColumn("HP Vert. Slices", "HP Count", typeof(int), dataColumnGroup9);
		AddManagedAttributeColumn("HP Start Elev.", "HP Start Elev", typeof(float), dataColumnGroup9);
		AddManagedAttributeColumn("HP Vert. Incr.", "HP Vert Incr", typeof(float), dataColumnGroup9);
		AddManagedAttributeColumn("HP ROB Slices", "HP LOB Slices", typeof(int), dataColumnGroup9);
		AddManagedAttributeColumn("HP Chan. Slices", "HP Chan Slices", typeof(int), dataColumnGroup9);
		AddManagedAttributeColumn("HP LOB Slices", "HP ROB Slices", typeof(int), dataColumnGroup9);
		DataColumnGroup dataColumnGroup10 = new DataColumnGroup("Terrain/Profile", isVisible: false);
		AddAttributeColumn("Profile", typeof(IProfileInfo), dataColumnGroup10, DataColumnViewState.GrayedOut);
		AddAttributeColumn("Track Terrain", typeof(bool), dataColumnGroup10);
		AddAttributeColumn("Auto Update", typeof(bool), dataColumnGroup10, DataColumnViewState.Invisible);
		DataColumnGroup dataColumnGroup11 = new DataColumnGroup("Pilot Channel", isVisible: false);
		AddManagedAttributeColumn("Pilot Channel Invert", "PC Invert", typeof(float), dataColumnGroup11);
		AddManagedAttributeColumn("Pilot Channel Width", "PC Width", typeof(float), dataColumnGroup11);
		AddManagedAttributeColumn("Pilot Channel Mann.", "PC Mann", typeof(float), dataColumnGroup11);
		DataColumnGroup dataColumnGroup12 = new DataColumnGroup("Deck Data", isVisible: false);
		AddManagedAttributeColumn("Deck Preissman Slot", "Deck Preissman Slot", typeof(bool), dataColumnGroup12, DataColumnViewState.GrayedOut);
		AddAttributeColumn("Low Deck Sta/Elev", typeof(IProfileInfo), dataColumnGroup12, DataColumnViewState.GrayedOut);
		AddAttributeColumn("High Deck Sta/Elev", typeof(IProfileInfo), dataColumnGroup12, DataColumnViewState.GrayedOut);
		DataColumnGroup dataColumnGroup13 = new DataColumnGroup("Rating Curves", isVisible: false);
		AddManagedAttributeColumn("Rating Curve HW Check", "RC HW Check", typeof(bool), dataColumnGroup13, DataColumnViewState.GrayedOut);
		AddAttributeColumn("Rating Curve", typeof(IProfileInfo), dataColumnGroup13, DataColumnViewState.GrayedOut);
		DataColumnGroup dataColumnGroup14 = new DataColumnGroup("Ice Info", isVisible: false);
		AddManagedAttributeColumn("Ice Jam (Channel)", "Ice Jam Chan", typeof(int), dataColumnGroup14, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Ice Jam (Overbank)", "Ice Jam OB", typeof(int), dataColumnGroup14, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Ice Fixed Mann. n", "Ice Fix Mann", typeof(int), dataColumnGroup14, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Ice Thickness (Left OB)", "Ice Thick L", typeof(float), dataColumnGroup14, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Ice Thickness (Channel)", "Ice Thick C", typeof(float), dataColumnGroup14, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Ice Thickness (Right OB)", "Ice Thick R", typeof(float), dataColumnGroup14, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Ice Mann. n (Left OB)", "Ice Mann L", typeof(float), dataColumnGroup14, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Ice Mann. n (Channel)", "Ice Mann C", typeof(float), dataColumnGroup14, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Ice Mann. n (Right OB)", "Ice Mann R", typeof(float), dataColumnGroup14, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Ice Specific Gravity", "Ice SG", typeof(float), dataColumnGroup14, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Ice Friction Angle", "Ice FA", typeof(float), dataColumnGroup14, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Ice Porosity", "Ice Porosity", typeof(float), dataColumnGroup14, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Ice K1", "Ice K1", typeof(float), dataColumnGroup14, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Ice Max Mean Velocity", "Ice MaxVel", typeof(float), dataColumnGroup14, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Ice Cohesion", "Ice Cohesion", typeof(float), dataColumnGroup14, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Default Centerline", "Default Centerline", typeof(bool), null, DataColumnViewState.Invisible);
		AddAttributeColumn("Stationing Offset", typeof(float), null, DataColumnViewState.GrayedOut);
	}

	public void Stream(BinaryWriter bw)
	{
		StreamInternal(bw);
	}

	private void StreamInternal(BinaryWriter bw)
	{
		bw.Write(FeatureCount());
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Polyline(i).Stream(bw);
				Pipe.WriteString(bw, IHasRRRSLayerExtensions.GetRiverName(this, i));
				Pipe.WriteString(bw, IHasRRRSLayerExtensions.GetReachName(this, i));
				Pipe.WriteString(bw, IHasRRRSLayerExtensions.GetRiverStationName(this, i));
				Pipe.WriteString(bw, GetNodeName(i));
				Pipe.WriteString(bw, GetNodeDescription(i));
				bw.Write(GetLeftOverbankLength(i));
				bw.Write(GetChannelLength(i));
				bw.Write(GetRightOverbankLength(i));
				bw.Write(GetLeftBankStation(i));
				bw.Write(GetRightBankStation(i));
				bw.Write(GetLeftLevee(i));
				bw.Write(GetLeftLeveeElev(i));
				bw.Write(GetRightLevee(i));
				bw.Write(GetRightLeveeElev(i));
				bw.Write(GetContractionCoef(i));
				bw.Write(GetExpansionCoef(i));
				bw.Write(GetUnsteadyContractionCoef(i));
				bw.Write(GetUnsteadyExpansionCoef(i));
				bw.Write(GetMinorLossCoef(i));
				bw.Write(GetSkewCoef(i));
				bw.Write(GetHPVertSliceCount(i));
				bw.Write(GetHPStartElev(i));
				bw.Write(GetHPVertIncr(i));
				bw.Write(GetHPLOBSlices(i));
				bw.Write(GetHPChanSlices(i));
				bw.Write(GetHPROBSlices(i));
			}
		}
	}

	public static RASXS Stream(BinaryReader br, RASGeometry geom)
	{
		return StreamInternal(br, geom);
	}

	private static RASXS StreamInternal(BinaryReader br, RASGeometry geom)
	{
		RASXS rASXS = new RASXS(geom);
		rASXS.InitializeEmptyFeatureTable();
		rASXS.SuppressTableReload(boolValue: true);
		rASXS.SuppressFeatureChanged(boolValue: true);
		checked
		{
			using (new FeatureTableReloadSuppressor(rASXS))
			{
				int count = br.ReadInt32() - 1;
				for (int i = 0; i <= count; i++)
				{
					Polyline feature = RasMapperLib.Polyline.Stream(br);
					rASXS.AddFeature(feature);
					IHasRRRSLayerExtensions.SetRiverName(rASXS, i, Pipe.ReadString(br));
					IHasRRRSLayerExtensions.SetReachName(rASXS, i, Pipe.ReadString(br));
					IHasRRRSLayerExtensions.SetRiverStationName(rASXS, i, Pipe.ReadString(br));
					rASXS.SetNodeName(i, Pipe.ReadString(br));
					rASXS.SetNodeDescription(i, Pipe.ReadString(br));
					rASXS.SetLeftOverbankLength(i, br.ReadSingle());
					rASXS.SetChannelLength(i, br.ReadSingle());
					rASXS.SetRightOverbankLength(i, br.ReadSingle());
					rASXS.SetLeftBankStation(i, br.ReadSingle());
					rASXS.SetRightBankStation(i, br.ReadSingle());
					rASXS.SetLeftLeveeStation(i, br.ReadSingle());
					rASXS.SetLeftLeveeElev(i, br.ReadSingle());
					rASXS.SetRightLeveeStation(i, br.ReadSingle());
					rASXS.SetRightLeveeElev(i, br.ReadSingle());
					rASXS.SetContractionCoef(i, br.ReadSingle());
					rASXS.SetExpansionCoef(i, br.ReadSingle());
					rASXS.SetUnsteadyContractionCoef(i, br.ReadSingle());
					rASXS.SetUnsteadyExpansionCoef(i, br.ReadSingle());
					rASXS.SetMinorLossCoef(i, br.ReadSingle());
					rASXS.SetSkewCoef(i, br.ReadSingle());
					rASXS.SetHPVertSliceCount(i, br.ReadInt32());
					rASXS.SetHPStartElev(i, br.ReadSingle());
					rASXS.SetHPVertIncr(i, br.ReadSingle());
					rASXS.SetHPLOBSlices(i, br.ReadInt32());
					rASXS.SetHPChanSlices(i, br.ReadInt32());
					rASXS.SetHPROBSlices(i, br.ReadInt32());
					rASXS.SetProfileProperties(i, new EmptyProfileInfo());
				}
				rASXS.ProcessBankStationData();
				rASXS.ProcessLeveeProperties();
				return rASXS;
			}
		}
	}

	public override List<PlotOption> GetStaticPlotOptions()
	{
		List<PlotOption> plotOptions = new List<PlotOption>();
		plotOptions.Add(new PlotOption("Bank Stations"));
		plotOptions.Add(new PlotOption("Manning's n Values"));
		plotOptions.Add(new PlotOption("Reach Lengths"));
		plotOptions.Add(new PlotOption("Ineffective Areas"));
		plotOptions.Add(new PlotOption("Blocked Obstructions"));
		plotOptions.Add(new PlotOption("Ratio of Cut Line to XS Line"));
		plotOptions.AddRange(base.GetStaticPlotOptions());
		return plotOptions;
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

	protected void HDFLoadFeatureTable(H5Reader hr)
	{
		if (!hr.ExistsAsGroup("Geometry/Cross Sections"))
		{
			return;
		}
		HDF5Storage.ReadPolyFeatures(this, hr, "Geometry/Cross Sections/Polyline");
		string path = "Geometry/Cross Sections/Attributes";
		checked
		{
			if (hr.ExistsAsDataset(path))
			{
				ReadManagedColumns(hr, path);
				List<List<Point2D>> pointCollection = HDF5Storage.ReadPointCollection(hr, "Geometry/Cross Sections/Manning's n");
				int count = pointCollection.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					SetManningsN(i, new Point2DCollection(pointCollection[i]));
				}
				int count2 = FeatureCount() - 1;
				for (int j = 0; j <= count2; j++)
				{
					SetRatingCurveHWCheck(j, hwCheck: false);
					SetStationingOffset(j, 0f);
				}
				LoadProfileMetadataFromDisk(hr, "Geometry/Cross Sections/Station Elevation", SetProfileProperties);
				LoadProfileMetadataFromDisk(hr, "Geometry/Cross Sections/Deck Low", SetDeckLowProperties);
				LoadProfileMetadataFromDisk(hr, "Geometry/Cross Sections/Deck High", SetDeckHighProperties);
				LoadProfileMetadataFromDisk(hr, "Geometry/Cross Sections/Rating Curve", SetRatingCurveProperties);
				LoadIneffectiveAreas(hr);
				LoadBlockedObstructions(hr);
				int count3 = FeatureCount() - 1;
				for (int k = 0; k <= count3; k++)
				{
					IProfileInfo profileProperties = GetProfileProperties(k);
					if (profileProperties == null)
					{
						SetStationingOffset(k, 0f);
						continue;
					}
					SetStationingOffset(k, -1f * profileProperties.FirstStation);
					float leftBankStation = GetLeftBankStation(k);
					float rightBankStation = GetRightBankStation(k);
					if (float.IsNaN(leftBankStation))
					{
						SetLeftBankStation(k, profileProperties.FirstStation);
					}
					if (float.IsNaN(rightBankStation))
					{
						SetRightBankStation(k, profileProperties.LastStation);
					}
				}
				SetZeroBasedStationing();
			}
			else
			{
				HDFLoadFeatureTable_503(hr);
			}
			int count4 = FeatureCount() - 1;
			for (int l = 0; l <= count4; l++)
			{
				SetTrackTerrain(l, condition: false);
				SetAutoUpdate(l, condition: false);
			}
		}
	}

	void IGeometryLayer.HDFLoadFeatureTable(H5Reader hr)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFLoadFeatureTable
		this.HDFLoadFeatureTable(hr);
	}

	private void HDFLoadFeatureTable_503(H5Reader hr)
	{
		HDF5Storage.ReadStringColumn(this, hr, "Geometry/Cross Sections/River Names", "River");
		HDF5Storage.ReadStringColumn(this, hr, "Geometry/Cross Sections/Reach Names", "Reach");
		HDF5Storage.ReadStringColumn(this, hr, "Geometry/Cross Sections/River Stations", "River Station");
		HDF5Storage.ReadStringColumn(this, hr, "Geometry/Cross Sections/Node Names", "Node Name");
		HDF5Storage.ReadStringColumn(this, hr, "Geometry/Cross Sections/Node Descriptions", "Node Descriptions");
		HDF5Storage.ReadColumns(this, hr, "Geometry/Cross Sections/Lengths", "Length LOB", "Length Channel", "Length ROB");
		HDF5Storage.ReadColumns(this, hr, "Geometry/Cross Sections/Bank Stations", "Left Bank", "Right Bank");
		HDF5Storage.ReadColumns(this, hr, "Geometry/Cross Sections/Levees", "Levee Left Sta.", "Levee Left Elev.", "Levee Right Sta.", "Levee Right Elev.");
		HDF5Storage.ReadColumns(this, hr, "Geometry/Cross Sections/XS Coefficients", "Steady Contr. Coef.", "Steady Expan. Coef.", "Unsteady Contr. Coef.", "Unsteady Expan. Coef.", "Minor Loss", "Skew");
		HDF5Storage.ReadStringColumn(this, hr, "Geometry/Cross Sections/Friction Mode", "Friction Mode");
		HDF5Storage.ReadColumns(this, hr, "Geometry/Cross Sections/Ice Parameters (integer)", "Ice Jam (Channel)", "Ice Jam (Overbank)", "Ice Fixed Mann. n");
		HDF5Storage.ReadColumns(this, hr, "Geometry/Cross Sections/Ice Parameters (single)", "Ice Thickness (Left OB)", "Ice Thickness (Channel)", "Ice Thickness (Right OB)", "Ice Mann. n (Left OB)", "Ice Mann. n (Channel)", "Ice Mann. n (Right OB)", "Ice Specific Gravity", "Ice Friction Angle", "Ice Porosity", "Ice K1", "Ice Max Mean Velocity", "Ice Cohesion");
		List<List<Point2D>> pointCollection = HDF5Storage.ReadPointCollection(hr, "Geometry/Cross Sections/Manning's n");
		checked
		{
			int count = pointCollection.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				SetManningsN(i, new Point2DCollection(pointCollection[i]));
			}
			LoadProfileMetadataFromDisk(hr, "Geometry/Cross Sections/Station Elevation", SetProfileProperties);
			ProcessBankStationData();
			ProcessLeveeProperties();
		}
	}

	private void SetZeroBasedStationing()
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				float stationingOffset = GetStationingOffset(i);
				if (stationingOffset == 0f || float.IsNaN(stationingOffset))
				{
					continue;
				}
				GetProfileProperties(i)?.SetStationingOffset(stationingOffset);
				GetDeckHighProperties(i)?.SetStationingOffset(stationingOffset);
				GetDeckLowProperties(i)?.SetStationingOffset(stationingOffset);
				SetLeftBankStation(i, GetLeftBankStation(i) + stationingOffset);
				SetRightBankStation(i, GetRightBankStation(i) + stationingOffset);
				SetLeftLeveeStation(i, GetLeftLevee(i) + stationingOffset);
				SetRightLeveeStation(i, GetRightLevee(i) + stationingOffset);
				GetManningsN(i)?.Translate(stationingOffset, 0f);
				List<IneffectiveBlock> ineffectives = GetIneffectives(i);
				if (!ineffectives.IsNullOrEmpty())
				{
					int count2 = ineffectives.Count - 1;
					for (int j = 0; j <= count2; j++)
					{
						IneffectiveBlock ineffective = ineffectives[j];
						ineffective.LeftStation += stationingOffset;
						ineffective.RightStation += stationingOffset;
						ineffectives[j] = ineffective;
					}
				}
				List<Obstruction> obstructions = GetObstructions(i);
				if (!obstructions.IsNullOrEmpty())
				{
					int count3 = obstructions.Count - 1;
					for (int k = 0; k <= count3; k++)
					{
						Obstruction obstruction = obstructions[k];
						obstruction.LeftStation += stationingOffset;
						obstruction.RightStation += stationingOffset;
						obstructions[k] = obstruction;
					}
				}
			}
			ProcessBankStationData();
			ProcessLeveeProperties();
		}
	}

	private void SetTrueStationing()
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				GetProfileProperties(i)?.SetStationingOffset(0f);
				GetDeckHighProperties(i)?.SetStationingOffset(0f);
				GetDeckLowProperties(i)?.SetStationingOffset(0f);
				float stationingOffset = GetStationingOffset(i);
				if (stationingOffset == 0f || float.IsNaN(stationingOffset))
				{
					continue;
				}
				stationingOffset *= -1f;
				SetLeftBankStation(i, GetLeftBankStation(i) + stationingOffset);
				SetRightBankStation(i, GetRightBankStation(i) + stationingOffset);
				SetLeftLeveeStation(i, GetLeftLevee(i) + stationingOffset);
				SetRightLeveeStation(i, GetRightLevee(i) + stationingOffset);
				GetManningsN(i)?.Translate(stationingOffset, 0f);
				List<IneffectiveBlock> ineffectives = GetIneffectives(i);
				if (!ineffectives.IsNullOrEmpty())
				{
					int count2 = ineffectives.Count - 1;
					for (int j = 0; j <= count2; j++)
					{
						IneffectiveBlock ineffective = ineffectives[j];
						ineffective.LeftStation += stationingOffset;
						ineffective.RightStation += stationingOffset;
						ineffectives[j] = ineffective;
					}
				}
				List<Obstruction> obstructions = GetObstructions(i);
				if (!obstructions.IsNullOrEmpty())
				{
					int count3 = obstructions.Count - 1;
					for (int k = 0; k <= count3; k++)
					{
						Obstruction obstruction = obstructions[k];
						obstruction.LeftStation += stationingOffset;
						obstruction.RightStation += stationingOffset;
						obstructions[k] = obstruction;
					}
				}
			}
			ProcessBankStationData();
			ProcessLeveeProperties();
		}
	}

	private void LoadIneffectiveAreas(H5Reader hr)
	{
		string path = "Geometry/Cross Sections/Ineffective Info";
		string path2 = "Geometry/Cross Sections/Ineffective Blocks";
		if (!hr.ExistsAsDataset(path) || !hr.ExistsAsDataset(path2))
		{
			return;
		}
		int[,] values = null;
		hr.ReadDataset(path, ref values);
		int featureCount = FeatureCount();
		if (values.GetLength(0) != featureCount || values.GetLength(1) != 2)
		{
			return;
		}
		bool condition = true;
		checked
		{
			int count = featureCount - 1;
			for (int i = 0; i <= count; i++)
			{
				if (values[i, 1] > 0)
				{
					condition = false;
					break;
				}
			}
			if (condition)
			{
				return;
			}
			Dictionary<CompoundFieldDefinition, Array> compoundElements = hr.ReadCompoundElements(path2);
			float[] floatValues = null;
			float[] floatValuesArray = null;
			float[] floatValues2 = null;
			bool[] boolValues = null;
			foreach (KeyValuePair<CompoundFieldDefinition, Array> compoundElement in compoundElements)
			{
				if (Operators.CompareString(compoundElement.Key.Name.ToLower(), "left sta", TextCompare: false) == 0)
				{
					floatValues = compoundElement.Value as float[];
				}
				else if (Operators.CompareString(compoundElement.Key.Name.ToLower(), "right sta", TextCompare: false) == 0)
				{
					floatValuesArray = compoundElement.Value as float[];
				}
				else if (Operators.CompareString(compoundElement.Key.Name.ToLower(), "elevation", TextCompare: false) == 0)
				{
					floatValues2 = compoundElement.Value as float[];
				}
				else if (Operators.CompareString(compoundElement.Key.Name.ToLower(), "permanent", TextCompare: false) == 0)
				{
					boolValues = compoundElement.Value as bool[];
				}
			}
			if (floatValues == null || floatValuesArray == null || floatValues2 == null || boolValues == null)
			{
				return;
			}
			int count2 = featureCount - 1;
			for (int j = 0; j <= count2; j++)
			{
				int ordinal = values[j, 0];
				int ordinal2 = values[j, 1];
				if (ordinal2 != 0)
				{
					List<IneffectiveBlock> ineffectiveBlocks = new List<IneffectiveBlock>();
					int count3 = ordinal + ordinal2 - 1;
					for (int k = ordinal; k <= count3; k++)
					{
						ineffectiveBlocks.Add(new IneffectiveBlock(floatValues[k], floatValuesArray[k], floatValues2[k], boolValues[k]));
					}
					SetIneffectives(j, ineffectiveBlocks);
				}
			}
		}
	}

	private void LoadBlockedObstructions(H5Reader hr)
	{
		string path = "Geometry/Cross Sections/Obstruction Info";
		string path2 = "Geometry/Cross Sections/Obstruction Blocks";
		if (!hr.ExistsAsDataset(path) || !hr.ExistsAsDataset(path2))
		{
			return;
		}
		int[,] values = null;
		hr.ReadDataset(path, ref values);
		int featureCount = FeatureCount();
		if (values.GetLength(0) != featureCount || values.GetLength(1) != 2)
		{
			return;
		}
		bool condition = true;
		checked
		{
			int count = featureCount - 1;
			for (int i = 0; i <= count; i++)
			{
				if (values[i, 1] > 0)
				{
					condition = false;
					break;
				}
			}
			if (condition)
			{
				return;
			}
			Dictionary<CompoundFieldDefinition, Array> compoundElements = hr.ReadCompoundElements(path2);
			float[] floatValues = null;
			float[] floatValuesArray = null;
			float[] floatValues2 = null;
			foreach (KeyValuePair<CompoundFieldDefinition, Array> compoundElement in compoundElements)
			{
				if (Operators.CompareString(compoundElement.Key.Name.ToLower(), "left sta", TextCompare: false) == 0)
				{
					floatValues = compoundElement.Value as float[];
				}
				else if (Operators.CompareString(compoundElement.Key.Name.ToLower(), "right sta", TextCompare: false) == 0)
				{
					floatValuesArray = compoundElement.Value as float[];
				}
				else if (Operators.CompareString(compoundElement.Key.Name.ToLower(), "elevation", TextCompare: false) == 0)
				{
					floatValues2 = compoundElement.Value as float[];
				}
			}
			if (floatValues == null || floatValuesArray == null || floatValues2 == null)
			{
				return;
			}
			int count2 = featureCount - 1;
			for (int j = 0; j <= count2; j++)
			{
				int ordinal = values[j, 0];
				int ordinal2 = values[j, 1];
				if (ordinal2 != 0)
				{
					List<Obstruction> obstructions = new List<Obstruction>();
					int count3 = ordinal + ordinal2 - 1;
					for (int k = ordinal; k <= count3; k++)
					{
						obstructions.Add(new Obstruction(floatValues[k], floatValuesArray[k], floatValues2[k]));
					}
					SetObstructions(j, obstructions);
				}
			}
		}
	}

	private void LoadProfileMetadataFromDisk(H5Reader hr, string hdfPrefix, Action<int, IProfileInfo> setter)
	{
		List<SavedProfileInfo> profileMetadata = HDF5Storage.ReadProfileMetadata(hr, hdfPrefix);
		checked
		{
			if (profileMetadata.Count != FeatureCount())
			{
				int count = FeatureCount() - 1;
				for (int i = 0; i <= count; i++)
				{
					setter(i, new EmptyProfileInfo());
				}
			}
			int count2 = System.Math.Min(profileMetadata.Count, FeatureCount()) - 1;
			for (int j = 0; j <= count2; j++)
			{
				setter(j, profileMetadata[j]);
			}
		}
	}

	protected void HDFSaveFeatureTable(H5Writer hw)
	{
		if (FeatureCount() == 0)
		{
			hw.Delete("Geometry/Cross Sections");
			return;
		}
		SetTrueStationing();
		try
		{
			hw.CreateGroup("Geometry/Cross Sections");
			HDF5Storage.WritePolylineFeatures(this, hw, "Geometry/Cross Sections/Polyline");
			WriteManagedColumns(hw, "Geometry/Cross Sections/Attributes");
			SaveProfilesInjectBankStationPoints(hw);
			SaveProfileData(hw, "Geometry/Cross Sections/Deck Low", DeckLowProfileProperties());
			SaveProfileData(hw, "Geometry/Cross Sections/Deck High", DeckHighProfileProperties());
			SaveProfileData(hw, "Geometry/Cross Sections/Rating Curve", RatingCurveProfileProperties());
			SaveIneffectiveAreas(hw);
			SaveBlockedObstructions(hw);
			SaveManningsProfileData(hw);
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			ProjectData.ClearProjectError();
		}
		SetZeroBasedStationing();
	}

	void IGeometryLayer.HDFSaveFeatureTable(H5Writer hw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFSaveFeatureTable
		this.HDFSaveFeatureTable(hw);
	}

	private void SaveIneffectiveAreas(H5Writer hw)
	{
		string path = "Geometry/Cross Sections/Ineffective Info";
		string path2 = "Geometry/Cross Sections/Ineffective Blocks";
		hw.Delete(path);
		hw.Delete(path2);
		checked
		{
			int[,] intValues = new int[FeatureCount() - 1 + 1, 2];
			List<float> floatList = new List<float>();
			List<float> floatListList = new List<float>();
			List<float> floatList2 = new List<float>();
			List<bool> boolList = new List<bool>();
			int ineffectiveCountSum = 0;
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				intValues[i, 0] = ineffectiveCountSum;
				List<IneffectiveBlock> ineffectives = GetIneffectives(i);
				if (ineffectives.IsNullOrEmpty())
				{
					intValues[i, 1] = 0;
					continue;
				}
				ineffectiveCountSum += ineffectives.Count;
				intValues[i, 1] = ineffectives.Count;
				int count2 = ineffectives.Count - 1;
				for (int j = 0; j <= count2; j++)
				{
					IneffectiveBlock ineffectiveBlock = ineffectives[j];
					floatList.Add(ineffectiveBlock.LeftStation);
					floatListList.Add(ineffectiveBlock.RightStation);
					floatList2.Add(ineffectiveBlock.Elevation);
					boolList.Add(ineffectiveBlock.Permanent);
				}
			}
			if (ineffectiveCountSum != 0)
			{
				CompoundTypeDefinition compoundTypeDefinition = new CompoundTypeDefinition();
				compoundTypeDefinition.AddField<float>("Left Sta");
				compoundTypeDefinition.AddField<float>("Right Sta");
				compoundTypeDefinition.AddField<float>("Elevation");
				compoundTypeDefinition.AddField<bool>("Permanent");
				hw.WriteArray(path, intValues);
				hw.WriteAttribute(path, "Column", new string[2] { "Starting Index", "Count" });
				hw.WriteAttribute(path, "Row", "Feature");
				hw.Create1DCompoundDataset(path2, 100, compoundTypeDefinition);
				using H5Assist.VB.CompoundWriter compoundWriter = new H5Assist.VB.CompoundWriter(hw, path2, floatList.Count);
				compoundWriter.WriteColumn(0, floatList.ToArray());
				compoundWriter.WriteColumn(1, floatListList.ToArray());
				compoundWriter.WriteColumn(2, floatList2.ToArray());
				compoundWriter.WriteColumn(3, boolList.ToArray());
			}
		}
	}

	private void SaveBlockedObstructions(H5Writer hw)
	{
		string path = "Geometry/Cross Sections/Obstruction Info";
		string path2 = "Geometry/Cross Sections/Obstruction Blocks";
		hw.Delete(path);
		hw.Delete(path2);
		checked
		{
			int[,] intValues = new int[FeatureCount() - 1 + 1, 2];
			List<float> floatList = new List<float>();
			List<float> floatListList = new List<float>();
			List<float> floatList2 = new List<float>();
			int obstructionCountSum = 0;
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				intValues[i, 0] = obstructionCountSum;
				List<Obstruction> obstructions = GetObstructions(i);
				if (obstructions.IsNullOrEmpty())
				{
					intValues[i, 1] = 0;
					continue;
				}
				obstructionCountSum += obstructions.Count;
				intValues[i, 1] = obstructions.Count;
				int count2 = obstructions.Count - 1;
				for (int j = 0; j <= count2; j++)
				{
					Obstruction obstruction = obstructions[j];
					floatList.Add(obstruction.LeftStation);
					floatListList.Add(obstruction.RightStation);
					floatList2.Add(obstruction.Elevation);
				}
			}
			if (obstructionCountSum != 0)
			{
				hw.WriteArray(path, intValues);
				CompoundTypeDefinition compoundTypeDefinition = new CompoundTypeDefinition();
				compoundTypeDefinition.AddField<float>("Left Sta");
				compoundTypeDefinition.AddField<float>("Right Sta");
				compoundTypeDefinition.AddField<float>("Elevation");
				hw.Create1DCompoundDataset(path2, 100, compoundTypeDefinition);
				using H5Assist.VB.CompoundWriter compoundWriter = new H5Assist.VB.CompoundWriter(hw, path2, floatList.Count);
				compoundWriter.WriteColumn(0, floatList.ToArray());
				compoundWriter.WriteColumn(1, floatListList.ToArray());
				compoundWriter.WriteColumn(2, floatList2.ToArray());
			}
		}
	}

	private void SaveProfilesInjectBankStationPoints(H5Writer hw)
	{
		List<Profile> profilesList = null;
		Stopwatch stopwatch = Stopwatch.StartNew();
		ProfileProperties().ToList();
		profilesList = new List<Profile>();
		List<int> intList = new List<int>();
		List<TerrainQueryProfileInfo> terrainQueryProfileInfos = new List<TerrainQueryProfileInfo>();
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				IProfileInfo profileProperties = GetProfileProperties(i);
				if (profileProperties is TerrainQueryProfileInfo)
				{
					TerrainQueryProfileInfo profileProperties2 = profileProperties as TerrainQueryProfileInfo;
					profilesList.Add(null);
					intList.Add(i);
					terrainQueryProfileInfos.Add(profileProperties2);
				}
				else
				{
					profilesList.Add(profileProperties.GetProfile());
				}
			}
			if (terrainQueryProfileInfos.Count > 0)
			{
				List<Profile> profiles = TerrainQueryProfileInfo.GetProfiles(terrainQueryProfileInfos, Geometry.Terrain);
				int count2 = intList.Count - 1;
				for (int j = 0; j <= count2; j++)
				{
					profilesList[intList[j]] = profiles[j];
				}
			}
			stopwatch.Stop();
			int count3 = profilesList.Count - 1;
			for (int k = 0; k <= count3; k++)
			{
				Profile profile = profilesList[k];
				if (profile == null || profile.Count < 2)
				{
					SetProfile(k, profile);
					continue;
				}
				float leftBankStation = GetLeftBankStation(k);
				float rightBankStation = GetRightBankStation(k);
				if (float.IsNaN(leftBankStation) || float.IsNaN(rightBankStation) || leftBankStation < 0f || rightBankStation <= leftBankStation || profile.IsNullOrEmpty())
				{
					continue;
				}
				float polylineLength = (float)Polyline(k).Length;
				if ((double)System.Math.Abs(rightBankStation - polylineLength) < 0.1 && (double)System.Math.Abs(profile.Last().X - rightBankStation) < 1.0 && rightBankStation > profile[profile.Count - 2].X)
				{
					profile[profile.Count - 1] = new Point2D(rightBankStation, profile[profile.Count - 1].Y);
				}
				bool condition = false;
				int ordinal = -1;
				bool condition2 = false;
				int ordinal2 = -1;
				int count4 = profile.Count - 1;
				for (int l = 0; l <= count4; l++)
				{
					float x = profile[l].X;
					if (!condition)
					{
						if ((double)System.Math.Abs(x - leftBankStation) < 0.001)
						{
							condition = true;
						}
						else if (x > leftBankStation)
						{
							ordinal = l;
							condition = true;
						}
					}
					if (condition && !condition2)
					{
						if ((double)System.Math.Abs(x - rightBankStation) < 0.001)
						{
							condition2 = true;
							break;
						}
						if (x > rightBankStation)
						{
							ordinal2 = l;
							condition2 = true;
							break;
						}
					}
				}
				if (ordinal > -1)
				{
					profile.Insert(ordinal, new Point2D(leftBankStation, (float)profile.InterpolateY(leftBankStation)));
				}
				if (ordinal2 > -1)
				{
					if (ordinal > -1)
					{
						ordinal2++;
					}
					profile.Insert(ordinal2, new Point2D(rightBankStation, (float)profile.InterpolateY(rightBankStation)));
				}
				InMemoryProfileInfo pfInfo = new InMemoryProfileInfo(profile);
				SetProfileProperties(k, pfInfo);
			}
			HDF5Storage.WritePointCollection(hw, "Geometry/Cross Sections/Station Elevation", profilesList, "Station", "Elevation");
		}
	}

	private void SaveProfileData(H5Writer hw, string hdfDatasetPrefix, IEnumerable<IProfileInfo> profileInfo)
	{
		try
		{
			List<Profile> profiles = profileInfo.Select([SpecialName] (IProfileInfo pinfo) => pinfo?.GetProfile()).ToList();
			if (profiles.Any([SpecialName] (Profile p) => p != null && p.Count > 0))
			{
				HDF5Storage.WritePointCollection(hw, hdfDatasetPrefix, profiles);
			}
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			ProjectData.ClearProjectError();
		}
	}

	private void SaveManningsProfileData(H5Writer hw)
	{
		List<List<Point2D>> point2DListList = new List<List<Point2D>>();
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				string frictionMode = GetFrictionMode(i);
				if (Operators.CompareString(frictionMode, "Basic Mann n", TextCompare: false) != 0)
				{
					if (Operators.CompareString(frictionMode, "Horiz Mann n", TextCompare: false) == 0)
					{
						point2DListList.Add(GetManningsN(i));
					}
					else
					{
						point2DListList.Add(GetManningsN(i));
					}
					continue;
				}
				List<Point2D> manningsN = GetManningsN(i);
				if (manningsN.IsNullOrEmpty())
				{
					point2DListList.Add(null);
					continue;
				}
				float y = manningsN[0].Y;
				float manningsNTerm = ((manningsN.Count < 2) ? y : manningsN[1].Y);
				float y2 = ((manningsN.Count < 3) ? manningsNTerm : manningsN[2].Y);
				List<Point2D> point2Ds = new List<Point2D>();
				Profile profile = GetProfile(i);
				if (profile.IsNullOrEmpty())
				{
					point2Ds.Add(new Point2D(0f, y));
					point2Ds.Add(new Point2D(GetLeftBankStation(i), manningsNTerm));
					point2Ds.Add(new Point2D(GetRightBankStation(i), y2));
				}
				else
				{
					point2Ds.Add(new Point2D(profile.First().X, y));
					point2Ds.Add(new Point2D(GetLeftBankStation(i), manningsNTerm));
					point2Ds.Add(new Point2D(GetRightBankStation(i), y2));
				}
				point2DListList.Add(point2Ds);
				SetManningsN(i, new Point2DCollection(point2Ds));
			}
			HDF5Storage.WritePointCollection(hw, "Geometry/Cross Sections/Manning's n", point2DListList, "Station", "Mann n");
		}
	}

	protected override void OnFeatureChanged(FeatureChangedEventArgs args)
	{
		base.OnFeatureChanged(args);
		foreach (int orderBy in args.FeatureIndicesChanged.OrderBy([SpecialName] (int a) => a))
		{
			if (args.ChangeType == FeatureChangedType.FeatureAdded)
			{
				SetupNewCrossSection(orderBy);
			}
			else if (args.ChangeType != FeatureChangedType.FeatureDeleted)
			{
				_ = args.ChangeType;
			}
		}
	}

	private void SetupNewCrossSection(int xsid)
	{
		SetProfileTrackTerrain(xsid);
		SetBankStations(xsid, CreateBankStationPropertiesFromRow(xsid));
		ResetLeveeProperties(xsid);
		SetTrackTerrain(xsid, condition: true);
		SetAutoUpdate(xsid, condition: true);
		SetContractionCoef(xsid, 0.1f);
		SetExpansionCoef(xsid, 0.3f);
		SetUnsteadyContractionCoef(xsid, 0f);
		SetUnsteadyExpansionCoef(xsid, 0f);
		SetDeckPreissman(xsid, deckPreissman: false);
		SetRatingCurveHWCheck(xsid, hwCheck: false);
		SetFrictionMode(xsid, "Basic Mann n");
		SetHPLOBSlices(xsid, 5);
		SetHPChanSlices(xsid, 5);
		SetHPROBSlices(xsid, 5);
	}

	private void ProcessBankStationData()
	{
		DataTable dataTable = FeatureTable();
		int lBankCol = dataTable.Columns.IndexOf("Left Bank");
		int rBankCol = dataTable.Columns.IndexOf("Right Bank");
		checked
		{
			int count = dataTable.Rows.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				SetBankPropertiesInternal(i, CreateBankStationPropertiesFromRow(i, dataTable.Rows[i], lBankCol, rBankCol));
			}
		}
	}

	private BankProperties CreateBankStationPropertiesFromRow(int xsid)
	{
		DataTable dataTable = FeatureTable();
		int lBankCol = dataTable.Columns.IndexOf("Left Bank");
		int rBankCol = dataTable.Columns.IndexOf("Right Bank");
		return CreateBankStationPropertiesFromRow(xsid, dataTable.Rows[xsid], lBankCol, rBankCol);
	}

	private BankProperties CreateBankStationPropertiesFromRow(int xsid, DataRow row, int lBankCol, int rBankCol)
	{
		return CreateBankStationProperties(xsid, Form.DataRowItemToSingle(row, lBankCol, 0f), Form.DataRowItemToSingle(row, rBankCol, 0f));
	}

	private BankProperties CreateBankStationProperties(int xsid, float leftSta, float rightSta)
	{
		Polyline polyline = Polyline(xsid);
		if (polyline == null)
		{
			return RasMapperLib.BankProperties.Empty();
		}
		float fraction = ComputeFraction(xsid, leftSta);
		PointOnPolyline lBPoint = ((!double.IsNaN(fraction) && !double.IsNaN(fraction)) ? polyline.PointAtFraction(fraction) : polyline.FirstPointOnPolyline());
		float fraction2 = ComputeFraction(xsid, rightSta);
		PointOnPolyline rBPoint = ((!double.IsNaN(fraction2) && !double.IsNaN(fraction2)) ? polyline.PointAtFraction(fraction2) : polyline.LastPointOnPolyline());
		return new BankProperties(leftSta, fraction, lBPoint, rightSta, fraction2, rBPoint);
	}

	public void EnsureBanklineCenterStationValues()
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				BankProperties bankProperties = GetBankProperties(i);
				float leftBankStation = bankProperties.LeftBankStation;
				float rightBankStation = bankProperties.RightBankStation;
				List<PolylineIntersection> xsIntersectRiver = Geometry.XSIntersectRiver(i);
				if (xsIntersectRiver.Count == 1)
				{
					PolylineIntersection polylineIntersection = xsIntersectRiver.First();
					float centerlineStation = GetProfileProperties(i).FirstStation + (float)Polyline(i).DistanceAlong(polylineIntersection.BPointOnPolyline);
					if (centerlineStation > leftBankStation && centerlineStation < rightBankStation)
					{
						bankProperties.CenterlineStation = centerlineStation;
						SetBankPropertiesInternal(i, bankProperties);
					}
				}
			}
		}
	}

	private void ProcessLeveeProperties()
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				ResetLeveeProperties(i);
			}
		}
	}

	public override void MD5HashAppend(Hash hash)
	{
		base.MD5HashAppend(hash);
		hash.Add((from bs in BankProperties()
			select bs.LeftBankStation).ToArray());
		hash.Add((from bs in BankProperties()
			select bs.RightBankStation).ToArray());
		hash.Add((from pp in ProfileProperties()
			select pp.FirstStation).ToArray());
		hash.Add((from pp in ProfileProperties()
			select pp.LastStation).ToArray());
	}

	private float ComputeFraction(int xsID, float station)
	{
		if (float.IsNaN(station))
		{
			return float.NaN;
		}
		float firstStation = FirstStation(xsID);
		float lastStationXsIDTerm = LastStation(xsID) - firstStation;
		if (lastStationXsIDTerm <= 0f)
		{
			return float.NaN;
		}
		return (station - firstStation) / lastStationXsIDTerm;
	}

	public Point PointOnCutline(int xsID, float station)
	{
		float fraction = ComputeFraction(xsID, station);
		Polyline polyline = Polyline(xsID);
		if (polyline == null)
		{
			return null;
		}
		return new Point(polyline.PointAtFraction(fraction).PointM);
	}

	public float ComputeM(int xsID, float station)
	{
		BankProperties bankProperties = GetBankProperties(xsID);
		float firstStation = FirstStation(xsID);
		float lastStation = LastStation(xsID);
		if (station <= firstStation)
		{
			return -2f;
		}
		if (station < bankProperties.LeftBankStation)
		{
			return -2f + (station - firstStation) / (bankProperties.LeftBankStation - firstStation);
		}
		if (station < bankProperties.RightBankStation)
		{
			return -1f + 2f * (station - bankProperties.LeftBankStation) / (bankProperties.RightBankStation - bankProperties.LeftBankStation);
		}
		if (station < lastStation)
		{
			return 1f + (station - bankProperties.RightBankStation) / (lastStation - bankProperties.RightBankStation);
		}
		return 2f;
	}

	public float ComputeXSStationFromFraction(int xsID, float frac)
	{
		IProfileInfo profileProperties = GetProfileProperties(xsID);
		if (profileProperties == null)
		{
			return float.NaN;
		}
		return frac * (profileProperties.LastStation - profileProperties.FirstStation);
	}

	public float[] ComputeXSStation()
	{
		if (_riverReachXSData == null)
		{
			ComputeRiverReachXSData(FeatureTable());
		}
		checked
		{
			float[] xsStation = new float[FeatureCount() - 1 + 1];
			foreach (KeyValuePair<string, RiverReachXSData> riverReachXSDatum in _riverReachXSData)
			{
				RiverReachXSData riverReachXSData = riverReachXSDatum.Value;
				float xsStationChannelLengthTerm = 0f;
				int lastXSOffset = riverReachXSData.LastXS - 1;
				int firstXS = riverReachXSData.FirstXS;
				for (int i = lastXSOffset; i >= firstXS; i += -1)
				{
					float channelLength = GetChannelLength(i);
					xsStationChannelLengthTerm = (xsStation[i] = xsStationChannelLengthTerm + channelLength);
				}
			}
			return xsStation;
		}
	}

	public float ComputeStation(int xsID, float m)
	{
		IProfileInfo profileProperties = GetProfileProperties(xsID);
		float firstStation = profileProperties.FirstStation;
		float lastStation = profileProperties.LastStation;
		BankProperties bankProperties = GetBankProperties(xsID);
		if (m <= -2f)
		{
			return firstStation;
		}
		if (m < -1f)
		{
			return firstStation + (bankProperties.LeftBankStation - firstStation) * (2f + m);
		}
		if (m < 1f)
		{
			return bankProperties.LeftBankStation + (bankProperties.RightBankStation - bankProperties.LeftBankStation) * (m + 1f) * 0.5f;
		}
		if (m < 2f)
		{
			return bankProperties.RightBankStation + (lastStation - bankProperties.RightBankStation) * (m - 1f);
		}
		return lastStation;
	}

	public float ComputeStationWithCenterlineZeroValue(int xsID, float m)
	{
		IProfileInfo profileProperties = GetProfileProperties(xsID);
		float firstStation = profileProperties.FirstStation;
		float lastStation = profileProperties.LastStation;
		BankProperties bankProperties = GetBankProperties(xsID);
		if (m <= -2f)
		{
			return firstStation;
		}
		if (m < -1f)
		{
			return firstStation + (bankProperties.LeftBankStation - firstStation) * (2f + m);
		}
		if (m < 0f)
		{
			return bankProperties.LeftBankStation + (bankProperties.CenterlineStation - bankProperties.LeftBankStation) * (m + 1f);
		}
		if (m < 1f)
		{
			return bankProperties.CenterlineStation + (bankProperties.RightBankStation - bankProperties.CenterlineStation) * m;
		}
		if (m < 2f)
		{
			return bankProperties.RightBankStation + (lastStation - bankProperties.RightBankStation) * (m - 1f);
		}
		return lastStation;
	}

	public Func<float, float> GetStationComputer(int xsID)
	{
		IProfileInfo profileProperties = GetProfileProperties(xsID);
		float firstStation = profileProperties.FirstStation;
		float lastStation = profileProperties.LastStation;
		BankProperties bankProperties = GetBankProperties(xsID);
		float leftBankStation = bankProperties.LeftBankStation;
		float rightBankStation = bankProperties.RightBankStation;
		float leftBankStationAboveFirstStation = leftBankStation - firstStation;
		float rightBankStationLeftBankStationTerm = (rightBankStation - leftBankStation) * 0.5f;
		float lastStationAboveRightBankStation = lastStation - rightBankStation;
		return [SpecialName] (float m) =>
		{
			if (m <= -2f)
			{
				return firstStation;
			}
			if (m < -1f)
			{
				return firstStation + leftBankStationAboveFirstStation * (2f + m);
			}
			if (m < 1f)
			{
				return leftBankStation + rightBankStationLeftBankStationTerm * (m + 1f);
			}
			return (m < 2f) ? (rightBankStation + lastStationAboveRightBankStation * (m - 1f)) : lastStation;
		};
	}

	public float GetMinChannelElevation(int xs)
	{
		Profile profile = GetProfile(xs);
		BankProperties bankProperties = GetBankProperties(xs);
		float leftBankStation = bankProperties.LeftBankStation;
		float rightBankStation = bankProperties.RightBankStation;
		float maxValue = float.MaxValue;
		checked
		{
			int count = profile.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Point2D point2D = profile[i];
				if (!(point2D.X < leftBankStation))
				{
					if (point2D.X > rightBankStation)
					{
						break;
					}
					maxValue = System.Math.Min(maxValue, point2D.Y);
				}
			}
			return maxValue;
		}
	}

	public static bool IsBackwards<T>(T layer, int xsid) where T : PolylineFeatureLayer
,IHasRRRSLayer,IGeometryLayer	{
		List<PolylineIntersection> unknownIntersectRiver = layer.Geometry.UnknownIntersectRiver(layer, xsid);
		if (unknownIntersectRiver.Count != 1)
		{
			return false;
		}
		PolylineIntersection polylineIntersection = unknownIntersectRiver.First();
		Polyline polyline = layer.Geometry.River.Polyline(polylineIntersection.APolylineIndex);
		Polyline polyline2 = layer.Polyline(polylineIntersection.BPolylineIndex);
		SegmentM segmentM = polyline.Segment(polylineIntersection.APointOnPolyline.SegmentIndex);
		SegmentM segmentM2 = polyline2.Segment(polylineIntersection.BPointOnPolyline.SegmentIndex);
		if (Vector.CrossProduct(segmentM.A, segmentM.B, segmentM2.A) >= 0.0)
		{
			return false;
		}
		return true;
	}

	private void GenerateExportLayerAddFeature(FeatureLayer layer, Feature feature, DataRow xsrow)
	{
		DataRow dataRow = layer.AddFeature(feature);
		dataRow["River"] = RuntimeHelpers.GetObjectValue(xsrow["River"]);
		dataRow["Reach"] = RuntimeHelpers.GetObjectValue(xsrow["Reach"]);
		dataRow["River Station"] = RuntimeHelpers.GetObjectValue(xsrow["River Station"]);
	}

	public PointM GetCenterChannelPoint(int xsid)
	{
		if (!RasMapperLib.Polyline.IsValidPolyline(Polyline(xsid)))
		{
			return PointM.Undefined();
		}
		GetProfileProperties(xsid);
		GetBankProperties(xsid);
		throw new NotImplementedException();
	}

	private void ComputeRiverReachXSData(DataTable dt)
	{
		_riverReachXSData = new Dictionary<string, RiverReachXSData>();
		checked
		{
			int count = dt.Rows.Count - 1;
			RiverReachXSData riverReachXSData = default;
			for (int i = 0; i <= count; i++)
			{
				string riverName = IHasRRRSLayerExtensions.GetRiverName(this, i);
				string reachName = IHasRRRSLayerExtensions.GetReachName(this, i);
				string key = riverName + reachName;
				if (_riverReachXSData.ContainsKey(key))
				{
					RiverReachXSData riverReachXSData2 = _riverReachXSData[key];
					riverReachXSData2.Count++;
					riverReachXSData2.LastXS = i;
					_riverReachXSData[key] = riverReachXSData2;
				}
				else
				{
					riverReachXSData.FirstXS = i;
					riverReachXSData.LastXS = i;
					riverReachXSData.Count = 1;
					_riverReachXSData[key] = riverReachXSData;
				}
			}
		}
	}

	public RiverReachXSData GetRiverReachXSData(string river, string reach)
	{
		if (_riverReachXSData == null)
		{
			ComputeRiverReachXSData(FeatureTable());
		}
		string key = river + reach;
		if (_riverReachXSData.TryGetValue(key, out var riverReachXSData))
		{
			return riverReachXSData;
		}
		throw new Exception("River: '" + river + "' Reach: '" + reach + "' not found in the XS layer.");
	}

	public void ComputeSetRRRSAll([Optional][DefaultParameterValue(null)] ref RiverMap rivMap)
	{
		Dictionary<string, UserStationingComputer> mappedUserStationing = GetMappedUserStationing(Enumerable.Range(0, FeatureCount()).ToList(), ref rivMap);
		using (new WaitCursor(SharedData.RasMapper))
		{
			SharedData.ReportMessage("Computing River Stations...");
			if (rivMap == null)
			{
				rivMap = Geometry.CreateRiverMap();
			}
			List<RiverMap.CrossSection> orderBy = rivMap.CrossSections.OrderBy([SpecialName] (RiverMap.CrossSection xs) => xs.RiverStationComputed_WithRMM).ToList();
			foreach (RiverMap.CrossSection orderBy2 in orderBy)
			{
				UserStationingComputer userStationingComputer = null;
				mappedUserStationing.TryGetValue(IHasRRRSLayerExtensions.GetRiverName(this, orderBy2.ID), out userStationingComputer);
				ComputeSetRRRSInternal(orderBy2.ID, dontUpdateRSIfReachIdentical: false, userStationingComputer, ref rivMap);
			}
			SharedData.ReportMessage("Computing River Stations... done", replacePreviousIfPossible: true);
		}
	}

	public void ComputeSetRRRSAllIfReachChanges([Optional][DefaultParameterValue(null)] ref RiverMap rivMap)
	{
		Dictionary<string, UserStationingComputer> mappedUserStationing = GetMappedUserStationing(Enumerable.Range(0, FeatureCount()).ToList(), ref rivMap);
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				UserStationingComputer userStationingComputer = null;
				mappedUserStationing.TryGetValue(IHasRRRSLayerExtensions.GetRiverName(this, i), out userStationingComputer);
				ComputeSetRRRSInternal(i, dontUpdateRSIfReachIdentical: true, userStationingComputer, ref rivMap);
			}
		}
	}

	public void ComputeSetRRRS(int fID, [Optional][DefaultParameterValue(null)] ref RiverMap rivMap)
	{
		Dictionary<string, UserStationingComputer> mappedUserStationing = GetMappedUserStationing(new int[1] { fID }, ref rivMap);
		UserStationingComputer userStationingComputer = null;
		mappedUserStationing.TryGetValue(IHasRRRSLayerExtensions.GetRiverName(this, fID), out userStationingComputer);
		ComputeSetRRRSInternal(fID, dontUpdateRSIfReachIdentical: false, userStationingComputer, ref rivMap);
	}

	public void ComputeSetRRRS(IList<int> fIDs, [Optional][DefaultParameterValue(null)] ref RiverMap rivMap)
	{
		Dictionary<string, UserStationingComputer> mappedUserStationing = GetMappedUserStationing(fIDs, ref rivMap);
		foreach (int fID in fIDs)
		{
			UserStationingComputer userStationingComputer = null;
			mappedUserStationing.TryGetValue(IHasRRRSLayerExtensions.GetRiverName(this, fID), out userStationingComputer);
			ComputeSetRRRSInternal(fID, dontUpdateRSIfReachIdentical: false, userStationingComputer, ref rivMap);
		}
	}

	public bool CompleteForComputations(ProgressReporter prog = null)
	{
		int featureCount = FeatureCount();
		if (featureCount == 0)
		{
			return true;
		}
		checked
		{
			float[,] floatValues = new float[featureCount - 1 + 1, 2];
			int count = featureCount - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM pointM = ComputeOrthogonalVector(i);
				floatValues[i, 0] = (float)pointM.X;
				floatValues[i, 1] = (float)pointM.Y;
			}
			using (H5Writer h5Writer = new H5Writer(SourceFilename))
			{
				string datasetName = "Geometry/Cross Sections/Orthogonal Vectors";
				h5Writer.WriteArray(datasetName, floatValues);
			}
			return true;
		}
	}

	public PointM ComputeOrthogonalVector(int xsid)
	{
		PointM leftBankPoint = GetLeftBankPoint(xsid);
		PointM rightBankPoint = GetRightBankPoint(xsid);
		if (leftBankPoint.IsUndefined())
		{
			return PointM.Undefined();
		}
		if (rightBankPoint.IsUndefined())
		{
			return PointM.Undefined();
		}
		return new SegmentM(leftBankPoint, rightBankPoint).PerpendicularUnitVector();
	}

	public PointM GetLeftBankPoint(int xsid)
	{
		double distance = GetLeftBankStation(xsid);
		Polyline polyline = Polyline(xsid);
		if (polyline == null || polyline.Count < 2)
		{
			return PointM.Undefined();
		}
		return polyline.PointAtDistance(distance).PointM;
	}

	public PointM GetRightBankPoint(int xsid)
	{
		double distance = GetRightBankStation(xsid);
		Polyline polyline = Polyline(xsid);
		if (polyline == null || polyline.Count < 2)
		{
			return PointM.Undefined();
		}
		return polyline.PointAtDistance(distance).PointM;
	}

	internal Dictionary<string, UserStationingComputer> GetMappedUserStationing(IList<int> xsIDsChanging, [Optional][DefaultParameterValue(null)] ref RiverMap rivMap)
	{
		if (rivMap == null)
		{
			rivMap = Geometry.CreateRiverMap();
		}
		HashSet<int> intSet = new HashSet<int>();
		foreach (int xsIDsChanging2 in xsIDsChanging)
		{
			intSet.Add(xsIDsChanging2);
		}
		Dictionary<RiverMap.River, HashSet<int>> intSetByRiver = new Dictionary<RiverMap.River, HashSet<int>>();
		foreach (RiverMap.River river in rivMap.Rivers)
		{
			foreach (RiverMap.CrossSection crossSection4 in river.GetCrossSections())
			{
				if (intSet.Contains(crossSection4.ID))
				{
					RiverMap.CrossSection crossSection = crossSection4.UpstreamCrossSection();
					RiverMap.CrossSection crossSection2 = crossSection4.DownstreamCrossSection();
					HashSet<int> riverTerm = null;
					if (!intSetByRiver.TryGetValue(river, out riverTerm))
					{
						riverTerm = (intSetByRiver[river] = new HashSet<int>());
					}
					if (crossSection != null && !intSet.Contains(crossSection.ID))
					{
						riverTerm.Add(crossSection.ID);
					}
					if (crossSection2 != null && !intSet.Contains(crossSection2.ID))
					{
						riverTerm.Add(crossSection2.ID);
					}
				}
			}
		}
		Dictionary<string, UserStationingComputer> mappedUserStationing = new Dictionary<string, UserStationingComputer>();
		foreach (KeyValuePair<RiverMap.River, HashSet<int>> localValue in intSetByRiver)
		{
			RiverMap.River key = localValue.Key;
			HashSet<int> localValueSet = localValue.Value;
			Polyline combinedPolyline = key.CombinedPolyline;
			List<Tuple<double, double>> valueTupleList = new List<Tuple<double, double>>();
			foreach (int ordinal in localValueSet)
			{
				RiverMap.CrossSection crossSection3 = rivMap.GetCrossSection(ordinal);
				valueTupleList.Add(new Tuple<double, double>(combinedPolyline.Length - crossSection3.DistanceFromEndOfRiver, crossSection3.RiverStationUser));
			}
			mappedUserStationing[key.Name] = UserStationingComputer.LoadRiverStationing(Geometry, key.Name, valueTupleList, rivMap);
		}
		return mappedUserStationing;
	}

	public bool SetRiverReachGeospatially(int xsID, bool nullifyRS = true)
	{
		RASGeometry geometry = Geometry;
		RASRiver river = geometry.River;
		string riverName = IHasRRRSLayerExtensions.GetRiverName(this, xsID);
		string reachName = IHasRRRSLayerExtensions.GetReachName(this, xsID);
		if (river == null || river.FeatureCount() == 0)
		{
			if (string.IsNullOrEmpty(riverName) && string.IsNullOrEmpty(reachName))
			{
				ClearRRRSColumns(xsID);
				return false;
			}
			ClearRRRSColumns(xsID);
			return true;
		}
		List<PolylineIntersection> xsIntersectRiver = geometry.XSIntersectRiver(xsID);
		if (xsIntersectRiver.Count == 1)
		{
			int aPolylineIndex = xsIntersectRiver.First().APolylineIndex;
			string riverName2 = river.GetRiverName(aPolylineIndex);
			string reachName2 = river.GetReachName(aPolylineIndex);
			if (string.Equals(riverName, riverName2, StringComparison.CurrentCultureIgnoreCase) && string.Equals(reachName, reachName2, StringComparison.CurrentCultureIgnoreCase))
			{
				return false;
			}
			IHasRRRSLayerExtensions.SetRiverName(this, xsID, riverName2);
			IHasRRRSLayerExtensions.SetReachName(this, xsID, reachName2);
			if (nullifyRS)
			{
				IHasRRRSLayerExtensions.SetRiverStationName(this, xsID, "");
				IHasRRRSLayerExtensions.SetRiverStationComputed(this, xsID, double.NaN);
			}
			return true;
		}
		if (string.IsNullOrEmpty(riverName) && string.IsNullOrEmpty(reachName))
		{
			return false;
		}
		ClearRRRSColumns(xsID);
		return true;
	}

	internal static void ComputeSetRRRSInternal<T>(T layer, int fid, bool dontUpdateRSIfReachIdentical, [Optional][DefaultParameterValue(null)] UserStationingComputer userSta, [Optional][DefaultParameterValue(null)] ref RiverMap rivMap) where T : PolylineFeatureLayer
,IHasRRRSLayer,IGeometryLayer	{
		layer.Polyline(fid);
		RASRiver river = layer.Geometry.River;
		if (river == null || river.FeatureCount() == 0)
		{
			return;
		}
		_ = layer.Geometry.RiverStationing;
		List<PolylineIntersection> unknownIntersectRiver = layer.Geometry.UnknownIntersectRiver(layer, fid);
		if (unknownIntersectRiver.Count == 1)
		{
			PolylineIntersection polylineIntersection = unknownIntersectRiver.First();
			int aPolylineIndex = polylineIntersection.APolylineIndex;
			string riverName = river.GetRiverName(aPolylineIndex);
			string reachName = river.GetReachName(aPolylineIndex);
			if (dontUpdateRSIfReachIdentical && Operators.CompareString(riverName, IHasRRRSLayerExtensions.GetRiverName(layer, fid), TextCompare: false) == 0 && Operators.CompareString(reachName, IHasRRRSLayerExtensions.GetReachName(layer, fid), TextCompare: false) == 0)
			{
				return;
			}
			if (rivMap == null)
			{
				rivMap = layer.Geometry.CreateRiverMap();
			}
			RiverMap.River river2 = rivMap.GetRiver(riverName);
			Polyline combinedPolyline = river2.CombinedPolyline;
			if (combinedPolyline == null || combinedPolyline.Count <= 1)
			{
				return;
			}
			PointM pointM = polylineIntersection.APointOnPolyline.PointM;
			double actualDistance = 0.0;
			PointOnPolyline pointOnPolyline = combinedPolyline.NearestPointOnPolyline(pointM, ref actualDistance);
			RiverMap.CrossSection usXS = null;
			RiverMap.CrossSection dsXS = null;
			RiverMap.CrossSection crossSection = null;
			if (layer is RASXS)
			{
				crossSection = rivMap.GetCrossSection(fid);
				usXS = crossSection.UpstreamCrossSection();
				dsXS = crossSection.DownstreamCrossSection();
			}
			else
			{
				if (!(layer is StructureLayer))
				{
					throw new NotImplementedException();
				}
				double distanceFromTopOfReach = combinedPolyline.DistanceAlong(pointOnPolyline);
				rivMap.GetReach(river2, reachName).UpstreamAndDownstreamCrossSection(distanceFromTopOfReach, ref usXS, ref dsXS);
			}
			if (userSta == null)
			{
				List<Tuple<double, double>> valueTupleList = new List<Tuple<double, double>>();
				if (usXS != null)
				{
					valueTupleList.Add(new Tuple<double, double>(combinedPolyline.Length - usXS.DistanceFromEndOfRiver, usXS.RiverStationUser));
				}
				if (dsXS != null)
				{
					valueTupleList.Add(new Tuple<double, double>(combinedPolyline.Length - dsXS.DistanceFromEndOfRiver, dsXS.RiverStationUser));
				}
				userSta = UserStationingComputer.LoadRiverStationing(layer.Geometry, riverName, valueTupleList, rivMap);
			}
			double riverStation = ComputeRSUnrounded(combinedPolyline, pointOnPolyline, userSta);
			double usXSRS = double.MaxValue;
			double dsXSRS = double.MinValue;
			if (usXS != null)
			{
				usXSRS = usXS.RiverStationUser;
			}
			if (dsXS != null)
			{
				dsXSRS = dsXS.RiverStationUser;
			}
			riverStation = ComputeAppropriateRiverStationRounding(riverStation, usXSRS, dsXSRS);
			if (crossSection != null)
			{
				crossSection.RiverStationUser = riverStation;
			}
			IHasRRRSLayerExtensions.SetRiverStationComputed(layer, fid, riverStation);
			IHasRRRSLayerExtensions.SetRiverStationName(layer, fid, riverStation.ToString());
			IHasRRRSLayerExtensions.SetRiverName(layer, fid, river.GetRiverName(aPolylineIndex));
			IHasRRRSLayerExtensions.SetReachName(layer, fid, river.GetReachName(aPolylineIndex));
		}
		else if (unknownIntersectRiver.Count > 1)
		{
			IHasRRRSLayerExtensions.ClearRRRSColumns(layer, fid);
		}
		else
		{
			IHasRRRSLayerExtensions.ClearRRRSColumns(layer, fid);
		}
	}

	public static double ComputeAppropriateRiverStationRounding(double riverStation, double usXSRS, double dsXSRS)
	{
		int riverStationDecimalPlaces = SharedData.RiverStationDecimalPlaces;
		checked
		{
			int count = SharedData.RiverStationDecimalPlaces + 5;
			for (int i = riverStationDecimalPlaces; i <= count; i++)
			{
				double riverStation2 = System.Math.Round(riverStation, i);
				if (riverStation2 < usXSRS && riverStation2 > dsXSRS)
				{
					riverStation = riverStation2;
					break;
				}
			}
			return riverStation;
		}
	}

	private void ComputeSetRRRSInternal(int fID, bool dontUpdateRSIfReachIdentical, [Optional][DefaultParameterValue(null)] UserStationingComputer userSta, [Optional][DefaultParameterValue(null)] ref RiverMap rivMap)
	{
		ComputeSetRRRSInternal(this, fID, dontUpdateRSIfReachIdentical, userSta, ref rivMap);
	}

	public static double ComputeRSUnrounded(Polyline completeRiverLine, PointOnPolyline rivPop, UserStationingComputer userSta, double[] cumulativeSegLengths = null)
	{
		if (userSta == null)
		{
			return ComputeRSUnroundedDefault(completeRiverLine, rivPop, cumulativeSegLengths);
		}
		double interpolateUserStation = userSta.InterpolateUserStation(completeRiverLine.DistanceAlong(rivPop));
		if (double.IsNaN(interpolateUserStation))
		{
			return ComputeRSUnroundedDefault(completeRiverLine, rivPop, cumulativeSegLengths);
		}
		return interpolateUserStation;
	}

	private static double ComputeRSUnroundedDefault(Polyline completeRiverLine, PointOnPolyline rivPop, double[] cumulativeSegLengths = null)
	{
		double isNullOrEmptyCumulativeSegLengthsTerm = 0.0;
		isNullOrEmptyCumulativeSegLengthsTerm = ((!cumulativeSegLengths.IsNullOrEmpty()) ? (completeRiverLine.Length - completeRiverLine.DistanceAlongFast(rivPop, ref cumulativeSegLengths)) : (completeRiverLine.Length - completeRiverLine.DistanceAlong(rivPop)));
		return isNullOrEmptyCumulativeSegLengthsTerm * SharedData.RSConversionFactor();
	}

	private void RecomputeRiverStationsOnRiver(string rivName, [Optional][DefaultParameterValue(null)] ref RiverMap rivMap)
	{
		if (FeatureTable().Rows.Count < 1)
		{
			return;
		}
		if (rivMap == null)
		{
			rivMap = Geometry.CreateRiverMap();
		}
		List<RiverMap.CrossSection> crossSectionsOnRiver = rivMap.GetCrossSectionsOnRiver(rivName);
		checked
		{
			int count = crossSectionsOnRiver.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				RiverMap.CrossSection crossSection = crossSectionsOnRiver[i];
				if (i == 0)
				{
					continue;
				}
				RiverMap.CrossSection crossSection2 = crossSectionsOnRiver[i - 1];
				crossSection2.RiverStationUser = System.Math.Round(crossSection2.RiverStationComputed_WithRMM, SharedData.RiverStationDecimalPlaces);
				crossSection.RiverStationUser = System.Math.Round(crossSection.RiverStationComputed_WithRMM, SharedData.RiverStationDecimalPlaces);
				int riverStationDecimalPlacesOffset = SharedData.RiverStationDecimalPlaces + 1;
				int count2 = SharedData.RiverStationDecimalPlaces + 5;
				for (int j = riverStationDecimalPlacesOffset; j <= count2; j++)
				{
					if (crossSection.RiverStationUser < crossSection2.RiverStationUser)
					{
						break;
					}
					crossSection.RiverStationUser = System.Math.Round(crossSection.RiverStationComputed_WithRMM, j);
					crossSection2.RiverStationUser = System.Math.Round(crossSection2.RiverStationComputed_WithRMM, j);
				}
			}
			foreach (RiverMap.CrossSection crossSectionsOnRiver2 in crossSectionsOnRiver)
			{
				string rsName = crossSectionsOnRiver2.RiverStationUser.ToString();
				IHasRRRSLayerExtensions.SetRiverStationName(this, crossSectionsOnRiver2.ID, rsName);
			}
		}
	}

	private void ReadIneffectiveBlocks(ref int[,] info, ref float[,] values)
	{
		using H5Reader h5Reader = new H5Reader(SourceFilename);
		string path = "Geometry/Cross Sections/Blocked Ineffective";
		if (h5Reader.ExistsAsDataset(path))
		{
			h5Reader.ReadDataset(path + " Info", ref info);
			h5Reader.ReadDataset(path + " Values", ref values);
		}
		else
		{
			info = null;
			values = null;
		}
	}

	private void ReadObstructionBlocks(ref int[,] info, ref float[,] values)
	{
		using H5Reader h5Reader = new H5Reader(SourceFilename);
		string path = "Geometry/Cross Sections/Blocked Obstructions";
		if (h5Reader.ExistsAsDataset(path))
		{
			h5Reader.ReadDataset(path + " Info", ref info);
			h5Reader.ReadDataset(path + " Values", ref values);
		}
		else
		{
			info = null;
			values = null;
		}
	}

	public HydraulicTableParameters GetHydraulicTableParameters(int xsid)
	{
		return new HydraulicTableParameters
		{
			LOBSlices = GetHPLOBSlices(xsid),
			ChannelSlices = GetHPChanSlices(xsid),
			ROBSlices = GetHPROBSlices(xsid),
			VerticalSlices = GetHPVertSliceCount(xsid),
			StartingElevation = GetHPStartElev(xsid),
			ElevationIncrement = GetHPVertIncr(xsid)
		};
	}

	public List<TimeSeriesVariable> ReadXS(int fid)
	{
		RASResults results = Geometry.Results;
		if (results == null)
		{
			return null;
		}
		List<TimeSeriesVariable> timeSeriesVariables = new List<TimeSeriesVariable>();
		float[] values = null;
		results.ReadUnsteadyTimeSeries("Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/Water Surface", fid, ref values);
		timeSeriesVariables.Add(new TimeSeriesVariable("Stage", "ft", values));
		float[] values2 = null;
		results.ReadUnsteadyTimeSeries("Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/Flow", fid, ref values2);
		timeSeriesVariables.Add(new TimeSeriesVariable("Flow", "cfs", values2));
		return timeSeriesVariables;
	}

	public List<TimeSeriesVariable> ReadXS_DSSHydrograph(int fid)
	{
		EnsureBaseFIDToLocalFIDDictionary();
		checked
		{
			if (_outputTypeToBaseFIDToLocalFID["Results/Unsteady/Output/Output Blocks/DSS Hydrograph Output"].ContainsKey(fid))
			{
				int col = _outputTypeToBaseFIDToLocalFID["Results/Unsteady/Output/Output Blocks/DSS Hydrograph Output"][fid];
				RASResults results = Geometry.Results;
				if (results == null)
				{
					return null;
				}
				List<TimeSeriesVariable> timeSeriesVariables = new List<TimeSeriesVariable>();
				float[] values = null;
				string replace = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/Water Surface".Replace("Base Output", "DSS Hydrograph Output");
				results.ReadUnsteadyTimeSeries(replace, col, ref values);
				string units = "";
				float computed = default;
				float computed2 = default;
				using (H5Reader h5Reader = new H5Reader(results.SourceFilename))
				{
					units = h5Reader.TryReadStringAttributeScalar(replace, "Variable Units");
					h5Reader.ReadAttributeScalar(replace, "Maximum Value of Data Set", ref computed);
					h5Reader.ReadAttributeScalar(replace, "Minimum Value of Data Set", ref computed2);
				}
				timeSeriesVariables.Add(new TimeSeriesVariable("Stage", units, values));
				float[] floatValues = new float[values.Length - 1 + 1];
				Buffers.ArraySetAllVB(floatValues, computed);
				timeSeriesVariables.Add(new TimeSeriesVariable("Stage-Max", units, floatValues));
				float[] floatValuesArray = new float[values.Length - 1 + 1];
				Buffers.ArraySetAllVB(floatValuesArray, computed2);
				timeSeriesVariables.Add(new TimeSeriesVariable("Stage-Min", units, floatValuesArray));
				float[] values2 = null;
				string replace2 = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/Flow".Replace("Base Output", "DSS Hydrograph Output");
				results.ReadUnsteadyTimeSeries(replace2, col, ref values2);
				string units2 = "";
				float computed3 = default;
				float computed4 = default;
				using (H5Reader h5Reader2 = new H5Reader(results.SourceFilename))
				{
					units2 = h5Reader2.TryReadStringAttributeScalar(replace2, "Variable Units");
					h5Reader2.ReadAttributeScalar(replace2, "Maximum Value of Data Set", ref computed3);
					h5Reader2.ReadAttributeScalar(replace2, "Minimum Value of Data Set", ref computed4);
				}
				timeSeriesVariables.Add(new TimeSeriesVariable("Flow", units2, values2));
				float[] floatValues2 = new float[values.Length - 1 + 1];
				Buffers.ArraySetAllVB(floatValues2, computed3);
				timeSeriesVariables.Add(new TimeSeriesVariable("Flow-Max", units2, floatValues2));
				float[] floatValues3 = new float[values.Length - 1 + 1];
				Buffers.ArraySetAllVB(floatValues3, computed4);
				timeSeriesVariables.Add(new TimeSeriesVariable("Flow-Min", units2, floatValues3));
				return timeSeriesVariables;
			}
			return null;
		}
	}

	public List<int> GetDSSHydrographXSFIDs()
	{
		EnsureBaseFIDToLocalFIDDictionary();
		return _outputTypeToBaseFIDToLocalFID["Results/Unsteady/Output/Output Blocks/DSS Hydrograph Output"].Keys.ToList();
	}

	private string[] GetOutputTypeXSNames(string outputType)
	{
		checked
		{
			switch (outputType)
			{
			default:
				throw new ArgumentException("outputType does not match any known output types.  The known output types are: Results/Unsteady/Output/Output Blocks/Base Output, Results/Unsteady/Output/Output Blocks/DSS Hydrograph Output, Results/Unsteady/Output/Output Blocks/DSS Profile Output");
			case "Results/Unsteady/Output/Output Blocks/Base Output":
			case "Results/Unsteady/Output/Output Blocks/DSS Hydrograph Output":
			case "Results/Unsteady/Output/Output Blocks/DSS Profile Output":
			{
				string[] outputTypeXSNames = null;
				using (H5Reader h5Reader = new H5Reader(Geometry.Results.SourceFilename))
				{
					bool condition = false;
					string outputTypeTerm = outputType + "/Unsteady Time Series/Cross Sections/Cross Section Attributes";
					if (h5Reader.ExistsAsDataset(outputTypeTerm))
					{
						try
						{
							if (h5Reader.GetDatasetType(outputTypeTerm) == typeof(CompoundFieldDefinition))
							{
								condition = true;
								Dictionary<CompoundFieldDefinition, Array> compoundElements = h5Reader.ReadCompoundElements(outputTypeTerm);
								if (compoundElements.Count > 1)
								{
									outputTypeXSNames = new string[compoundElements.First().Value.Length - 1 + 1];
									foreach (KeyValuePair<CompoundFieldDefinition, Array> compoundElement in compoundElements)
									{
										string[] strings = (string[])compoundElement.Value;
										int count = compoundElement.Value.Length - 1;
										for (int i = 0; i <= count; i++)
										{
											if (outputTypeXSNames[i] == null)
											{
												outputTypeXSNames[i] = "";
											}
											if (!string.IsNullOrWhiteSpace(strings[i]))
											{
												ref string reference = ref outputTypeXSNames[i];
												reference = reference + strings[i].TrimEnd() + " - ";
											}
										}
										if (Operators.CompareString(compoundElement.Key.Name, "Station", TextCompare: false) == 0)
										{
											break;
										}
									}
									int count2 = outputTypeXSNames.Length - 1;
									for (int j = 0; j <= count2; j++)
									{
										if (!string.IsNullOrEmpty(outputTypeXSNames[j]))
										{
											outputTypeXSNames[j] = outputTypeXSNames[j].Substring(0, outputTypeXSNames[j].Length - " - ".Length);
										}
									}
								}
							}
						}
						catch (Exception projectError)
						{
							ProjectData.SetProjectError(projectError);
							ProjectData.ClearProjectError();
						}
					}
					if (!condition)
					{
						string outputTypeTerm2 = outputType + "/Unsteady Time Series/Cross Sections/";
						if (h5Reader.ExistsAsGroup(outputTypeTerm2))
						{
							int[] allAttribute = (int[])h5Reader.ReadAllAttributes(outputTypeTerm2)["Node Pointers"];
							string datasetName = "Results/Unsteady/Geometry Info/Node Info";
							string[] values = null;
							h5Reader.ReadDataset(datasetName, ref values);
							string datasetName2 = "Results/Unsteady/Geometry Info/Cross Section Only";
							string[] values2 = null;
							h5Reader.ReadDataset(datasetName2, ref values2);
							int k = 0;
							Dictionary<int, int> intByInt = new Dictionary<int, int>();
							int count3 = values2.Length - 1;
							for (int l = 0; l <= count3; l++)
							{
								for (; k < values.Length; k++)
								{
									string trimEnd = values[k].TrimEnd();
									string trimEnd2 = values2[l].ReduceWhitespace().TrimEnd();
									if (trimEnd.Contains(trimEnd2) && trimEnd.EndsWith(" XS"))
									{
										int index = trimEnd.IndexOf(trimEnd2) + trimEnd2.Length;
										if (!Versioned.IsNumeric(trimEnd[index]))
										{
											intByInt[k] = l;
											break;
										}
									}
								}
							}
							List<string> stringList = new List<string>();
							int count4 = allAttribute.Length - 1;
							for (int m = 0; m <= count4; m++)
							{
								if (intByInt.ContainsKey(allAttribute[m]))
								{
									stringList.Add(GetFeatureName(intByInt[allAttribute[m]]));
								}
							}
							outputTypeXSNames = stringList.ToArray();
						}
					}
				}
				return outputTypeXSNames;
			}
			}
		}
	}

	public float TryGetWSEL(int xsID, string profileName, [Optional][DefaultParameterValue(true)] bool replaceUnknownTimestepWithMaxProfile, [Optional][DefaultParameterValue(float.NaN)] ref float encrLeft, [Optional][DefaultParameterValue(float.NaN)] ref float encrRight)
	{
		RASGeometry geometry = Geometry;
		if (geometry == null)
		{
			return float.NaN;
		}
		RASResults results = geometry.Results;
		if (results == null)
		{
			return float.NaN;
		}
		int profileIndex = results.ProfileIndex(profileName);
		if (profileIndex == -1)
		{
			if (!replaceUnknownTimestepWithMaxProfile || !results.UnsteadyHydraulics)
			{
				return float.NaN;
			}
			profileIndex = int.MaxValue;
		}
		Tuple<float, float>[] xsEncroachments = results.ReadXSEncroachments(profileIndex);
		if (!xsEncroachments.IsNullOrEmpty())
		{
			encrLeft = xsEncroachments[xsID].Item1;
			encrRight = xsEncroachments[xsID].Item2;
		}
		return results.ReadXS_WSEL(xsID, profileIndex);
	}

	public List<Polygon> GetFilledWSPolygon(int xsID, string profileName, [Optional][DefaultParameterValue(true)] bool replaceUnknownTimestepWithMaxProfile, [Optional][DefaultParameterValue(float.NaN)] ref float encrLeft, [Optional][DefaultParameterValue(float.NaN)] ref float encrRight)
	{
		float wsel = TryGetWSEL(xsID, profileName, replaceUnknownTimestepWithMaxProfile, ref encrLeft, ref encrRight);
		List<Polygon> polygons = new List<Polygon>();
		checked
		{
			if (!float.IsNaN(wsel))
			{
				bool condition = default;
				float getLeftLeveeXsIDTerm = default;
				if (GetLeftLeveeElev(xsID) >= wsel)
				{
					condition = true;
					getLeftLeveeXsIDTerm = GetLeftLevee(xsID);
				}
				bool condition2 = default;
				float getRightLeveeXsIDTerm = default;
				if (GetRightLeveeElev(xsID) >= wsel)
				{
					condition2 = true;
					getRightLeveeXsIDTerm = GetRightLevee(xsID);
				}
				if (!float.IsNaN(encrLeft))
				{
					condition = true;
					if (encrLeft > getLeftLeveeXsIDTerm)
					{
						getLeftLeveeXsIDTerm = encrLeft;
					}
				}
				if (!float.IsNaN(encrRight))
				{
					condition2 = true;
					if (encrRight > getRightLeveeXsIDTerm)
					{
						getRightLeveeXsIDTerm = encrRight;
					}
				}
				Profile profile = GetProfile(xsID);
				float profileTerm = default;
				if (wsel >= profile[0].Y)
				{
					profileTerm = profile[0].X;
				}
				int count = profile.Count - 1;
				float profileWselTerm = default;
				for (int i = 1; i <= count; i++)
				{
					bool condition3 = false;
					bool condition4 = false;
					PointMs pointMs = new PointMs();
					if ((profile[i - 1].Y > wsel) & (profile[i].Y <= wsel))
					{
						profileTerm = profile[i - 1].X + (profile[i].X - profile[i - 1].X) / (profile[i].Y - profile[i - 1].Y) * (wsel - profile[i - 1].Y);
					}
					else if ((profile[i - 1].Y < wsel) & (profile[i].Y >= wsel))
					{
						profileWselTerm = profile[i - 1].X + (profile[i].X - profile[i - 1].X) / (profile[i].Y - profile[i - 1].Y) * (wsel - profile[i - 1].Y);
						condition3 = true;
					}
					if ((i == profile.Count - 1) & (wsel > profile[profile.Count - 1].Y))
					{
						profileWselTerm = profile[profile.Count - 1].X;
						condition3 = true;
					}
					if (!condition3)
					{
						continue;
					}
					condition3 = false;
					condition4 = true;
					if (condition)
					{
						if (profileWselTerm < getLeftLeveeXsIDTerm)
						{
							condition4 = false;
						}
						if (profileTerm < getLeftLeveeXsIDTerm)
						{
							profileTerm = getLeftLeveeXsIDTerm;
							float query = profile.Query(profileTerm);
							pointMs.Add(new PointM(profileTerm, query));
						}
					}
					if (condition4)
					{
						pointMs.Add(new PointM(profileTerm, wsel));
					}
					if (condition2)
					{
						if (profileTerm > getRightLeveeXsIDTerm)
						{
							condition4 = false;
						}
						if (profileWselTerm > getRightLeveeXsIDTerm)
						{
							profileWselTerm = getRightLeveeXsIDTerm;
							pointMs.Add(new PointM(profileWselTerm, wsel));
							float query2 = profile.Query(profileWselTerm);
							pointMs.Add(new PointM(profileWselTerm, query2));
						}
						else
						{
							pointMs.Add(new PointM(profileWselTerm, wsel));
						}
					}
					else if (condition4)
					{
						pointMs.Add(new PointM(profileWselTerm, wsel));
					}
					if (!condition4)
					{
						continue;
					}
					for (int j = profile.Count - 1; j >= 0; j += -1)
					{
						if (!((profile[j].X == profileWselTerm) & (profile[j].Y > wsel)) && !(profile[j].X > profileWselTerm))
						{
							if (((profile[j].X == profileTerm) & (profile[j].Y > wsel)) || profile[j].X < profileTerm)
							{
								break;
							}
							pointMs.Add(new PointM(profile[j].X, profile[j].Y));
						}
					}
					polygons.Add(new Polygon(pointMs));
				}
			}
			return polygons;
		}
	}

	private void EnsureBaseFIDToLocalFIDDictionary()
	{
		if (_outputTypeToBaseFIDToLocalFID != null)
		{
			return;
		}
		_outputTypeToBaseFIDToLocalFID = new Dictionary<string, Dictionary<int, int>>();
		_outputTypeToBaseFIDToLocalFID.Add("Results/Unsteady/Output/Output Blocks/DSS Hydrograph Output", new Dictionary<int, int>());
		_outputTypeToBaseFIDToLocalFID.Add("Results/Unsteady/Output/Output Blocks/DSS Profile Output", new Dictionary<int, int>());
		string[] outputTypeXSNames = GetOutputTypeXSNames("Results/Unsteady/Output/Output Blocks/DSS Hydrograph Output");
		if (outputTypeXSNames == null)
		{
			return;
		}
		Dictionary<string, int> intByString = new Dictionary<string, int>();
		checked
		{
			int count = outputTypeXSNames.Length - 1;
			for (int i = 0; i <= count; i++)
			{
				intByString[outputTypeXSNames[i]] = i;
			}
			string[] outputTypeXSNames2 = GetOutputTypeXSNames("Results/Unsteady/Output/Output Blocks/DSS Profile Output");
			if (outputTypeXSNames2 == null)
			{
				return;
			}
			Dictionary<string, int> intByString2 = new Dictionary<string, int>();
			int count2 = outputTypeXSNames2.Length - 1;
			for (int j = 0; j <= count2; j++)
			{
				intByString2[outputTypeXSNames2[j]] = j;
			}
			int count3 = FeatureCount() - 1;
			for (int k = 0; k <= count3; k++)
			{
				string featureName = GetFeatureName(k);
				if (intByString.ContainsKey(featureName))
				{
					_outputTypeToBaseFIDToLocalFID["Results/Unsteady/Output/Output Blocks/DSS Hydrograph Output"][k] = intByString[featureName];
				}
				if (intByString2.ContainsKey(featureName))
				{
					_outputTypeToBaseFIDToLocalFID["Results/Unsteady/Output/Output Blocks/DSS Profile Output"][k] = intByString2[featureName];
				}
			}
		}
	}

	public string GetNodeName(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "Node Name");
	}

	public void SetNodeName(int fid, string nodeName)
	{
		FeatureRow(fid)["Node Name"] = nodeName;
	}

	public string GetNodeDescription(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "Node Descriptions");
	}

	public void SetNodeDescription(int fid, string desc)
	{
		FeatureRow(fid)["Node Descriptions"] = desc;
	}

	public List<IneffectiveBlock> GetIneffectives(int fid)
	{
		object objectValue = RuntimeHelpers.GetObjectValue(FeatureRow(fid)["Ineffective Regions"]);
		if (Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)) || objectValue == null)
		{
			return null;
		}
		return objectValue as List<IneffectiveBlock>;
	}

	public void SetIneffectives(int fid, List<IneffectiveBlock> ineffectives)
	{
		FeatureRow(fid)["Ineffective Regions"] = ineffectives;
	}

	public List<Obstruction> GetObstructions(int fid)
	{
		object objectValue = RuntimeHelpers.GetObjectValue(FeatureRow(fid)["Blocked Obstructions"]);
		if (Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)) || objectValue == null)
		{
			return null;
		}
		return objectValue as List<Obstruction>;
	}

	public void SetObstructions(int fid, List<Obstruction> obstructions)
	{
		FeatureRow(fid)["Blocked Obstructions"] = obstructions;
	}

	public override string GetFeatureName(int fid)
	{
		return FormNameUsingRRRS(IHasRRRSLayerExtensions.GetRiverName(this, fid), IHasRRRSLayerExtensions.GetReachName(this, fid), IHasRRRSLayerExtensions.GetRiverStationName(this, fid));
	}

	public static string FormNameUsingRRRS(string river, string reach, string riverStation)
	{
		return river + " - " + reach + " - " + riverStation;
	}

	public override string GetFeatureDisplayLabel(int fid)
	{
		return IHasRRRSLayerExtensions.GetRiverStationName(this, fid);
	}

	private void SetAutoUpdate(int xsid, bool condition)
	{
		FeatureRow(xsid)["Auto Update"] = condition;
	}

	public bool GetAutoUpdate(int xsid)
	{
		return DataTableExtensions.GetBoolean(FeatureTable(), xsid, "Auto Update");
	}

	private bool GetTrackTerrain(int xsid)
	{
		return DataTableExtensions.GetBoolean(FeatureTable(), xsid, "Track Terrain");
	}

	private void SetTrackTerrain(int xsid, bool condition)
	{
		FeatureRow(xsid)["Track Terrain"] = condition;
	}

	public float GetStationingOffset(int xsid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), xsid, "Stationing Offset");
	}

	private void SetStationingOffset(int xsid, float computed)
	{
		FeatureRow(xsid)["Stationing Offset"] = computed;
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

	public void SetProfileTrackTerrain(IList<int> xsids)
	{
		foreach (int xsid in xsids)
		{
			SetProfileTrackTerrain(xsid);
		}
	}

	public void SetProfileTrackTerrain(int xsid)
	{
		TerrainQueryProfileInfo terrainQueryProfileInfo = new TerrainQueryProfileInfo(Polyline(xsid), Geometry);
		terrainQueryProfileInfo.AttachFilterer(SharedData.MaxNumberOfPointsOnXS);
		terrainQueryProfileInfo.AttachDecimalPlaces(SharedData.HorizontalDecimalPlaces, SharedData.VerticalDecimalPlaces);
		SetProfileProperties(xsid, terrainQueryProfileInfo);
		SetTrackTerrain(xsid, condition: true);
		SetStationingOffset(xsid, 0f);
	}

	public void SetAllProfilesPreserveChannel()
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				SetProfilePreserveChannel(i);
			}
		}
	}

	public void SetProfilePreserveChannel(IList<int> xsids)
	{
		foreach (int xsid in xsids)
		{
			SetProfilePreserveChannel(xsid);
		}
	}

	public void SetProfilePreserveChannel(int xsid)
	{
		LinePreserverProfileInfo pfInfo = new LinePreserverProfileInfo(this, xsid, XSSubSet.ChannelOnly);
		SetProfileProperties(xsid, pfInfo);
		SetStationingOffset(xsid, 0f);
	}

	public void SetAllProfilesPreserveOverBanks()
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				SetProfilePreserveOverbanks(i);
			}
		}
	}

	public void SetProfilePreserveOverbanks(IList<int> xsids)
	{
		foreach (int xsid in xsids)
		{
			SetProfilePreserveOverbanks(xsid);
		}
	}

	public void SetProfilePreserveOverbanks(int xsid)
	{
		LinePreserverProfileInfo pfInfo = new LinePreserverProfileInfo(this, xsid, XSSubSet.OverBanksOnly);
		SetProfileProperties(xsid, pfInfo);
		SetStationingOffset(xsid, 0f);
	}

	public Profile GetProfile(int xsid)
	{
		return GetProfileProperties(xsid)?.GetProfile();
	}

	Profile IHasProfileProperties.GetProfile(int xsid)
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetProfile
		return this.GetProfile(xsid);
	}

	public void SetProfile(int xsid, Profile pf)
	{
		InMemoryProfileInfo inMemoryProfileInfo = new InMemoryProfileInfo(pf);
		float stationingOffset = GetStationingOffset(xsid);
		if (!float.IsNaN(stationingOffset))
		{
			inMemoryProfileInfo.SetStationingOffset(stationingOffset);
		}
		SetProfileProperties(xsid, inMemoryProfileInfo);
	}

	void IHasProfileProperties.SetProfile(int xsid,Profile pf)
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetProfile
		this.SetProfile(xsid, pf);
	}

	public IProfileInfo GetProfileProperties(int xsid)
	{
		return FeatureRow(xsid)["Profile"] as IProfileInfo;
	}

	IProfileInfo IHasProfileProperties.GetProfileProperties(int xsid)
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetProfileProperties
		return this.GetProfileProperties(xsid);
	}

	public void SetProfileProperties(int xsid, IProfileInfo pfInfo)
	{
		FeatureRow(xsid)["Profile"] = pfInfo;
	}

	void IHasProfileProperties.SetProfileProperties(int xsid,IProfileInfo pfInfo)
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetProfileProperties
		this.SetProfileProperties(xsid, pfInfo);
	}

	public IEnumerable<IProfileInfo> ProfileProperties()
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				yield return GetProfileProperties(i);
			}
		}
	}

	public string GetFrictionMode(int xsid)
	{
		return FeatureRow(xsid)["Friction Mode"] as string;
	}

	public void SetFrictionMode(int xsid, string fMode)
	{
		FeatureRow(xsid)["Friction Mode"] = fMode;
	}

	public IProfileInfo GetDeckLowProperties(int xsid)
	{
		return FeatureRow(xsid)["Low Deck Sta/Elev"] as IProfileInfo;
	}

	public void SetDeckLowProperties(int xsid, IProfileInfo pfInfo)
	{
		FeatureRow(xsid)["Low Deck Sta/Elev"] = pfInfo;
	}

	public IEnumerable<IProfileInfo> DeckLowProfileProperties()
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				yield return GetDeckLowProperties(i);
			}
		}
	}

	public IProfileInfo GetDeckHighProperties(int xsid)
	{
		return FeatureRow(xsid)["High Deck Sta/Elev"] as IProfileInfo;
	}

	public void SetDeckHighProperties(int xsid, IProfileInfo pfInfo)
	{
		FeatureRow(xsid)["High Deck Sta/Elev"] = pfInfo;
	}

	public IEnumerable<IProfileInfo> DeckHighProfileProperties()
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				yield return GetDeckHighProperties(i);
			}
		}
	}

	public IProfileInfo GetRatingCurveProperties(int xsid)
	{
		return FeatureRow(xsid)["Rating Curve"] as IProfileInfo;
	}

	public void SetRatingCurveProperties(int xsid, IProfileInfo pfInfo)
	{
		FeatureRow(xsid)["Rating Curve"] = pfInfo;
	}

	public IEnumerable<IProfileInfo> RatingCurveProfileProperties()
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				yield return GetRatingCurveProperties(i);
			}
		}
	}

	private float FirstStation(int xsID)
	{
		return GetProfileProperties(xsID).FirstStation;
	}

	private float LastStation(int xsID)
	{
		return GetProfileProperties(xsID).LastStation;
	}

	public BankProperties GetBankProperties(int xsid)
	{
		BankProperties bankProperties = FeatureRow(xsid)["Bank Properties"] as BankProperties;
		if (bankProperties != null && (bankProperties.LeftBankStation != GetLeftBankStation(xsid) || bankProperties.RightBankStation != GetRightBankStation(xsid)))
		{
			bankProperties = CreateBankStationProperties(xsid, GetLeftBankStation(xsid), GetRightBankStation(xsid));
			SetBankPropertiesInternal(xsid, bankProperties);
		}
		return bankProperties;
	}

	public IEnumerable<BankProperties> BankProperties()
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				yield return GetBankProperties(i);
			}
		}
	}

	public float GetLeftBankStation(int xsid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), xsid, "Left Bank");
	}

	private void SetLeftBankStation(int xsid, float sta)
	{
		FeatureRow(xsid)["Left Bank"] = sta;
	}

	public float GetRightBankStation(int xsid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), xsid, "Right Bank");
	}

	private void SetRightBankStation(int xsid, float sta)
	{
		FeatureRow(xsid)["Right Bank"] = sta;
	}

	public void SetBankStations(int xsid, BankProperties bprops)
	{
		SetBankPropertiesInternal(xsid, bprops);
		SetLeftBankStation(xsid, bprops.LeftBankStation);
		SetRightBankStation(xsid, bprops.RightBankStation);
	}

	public void SetBankStations(int xsid, float leftBankSta, float rightBankSta)
	{
		SetBankStations(xsid, CreateBankStationProperties(xsid, leftBankSta, rightBankSta));
	}

	private void SetBankPropertiesInternal(int xsid, BankProperties bprops)
	{
		FeatureRow(xsid)["Bank Properties"] = bprops;
	}

	public LeveeProperties GetLeveeProperties(int xsid)
	{
		return FeatureRow(xsid)["Levee Properties"] as LeveeProperties;
	}

	public void ResetLeveeProperties(int xsid)
	{
		FeatureRow(xsid)["Levee Properties"] = CreateLeveeProperties(xsid);
	}

	private float GetLeftLevee(int xsid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), xsid, "Levee Left Sta.");
	}

	private void SetLeftLeveeStation(int xsid, float station)
	{
		FeatureRow(xsid)["Levee Left Sta."] = station;
	}

	private float GetLeftLeveeElev(int xsid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), xsid, "Levee Left Elev.");
	}

	private void SetLeftLeveeElev(int xsid, float elev)
	{
		FeatureRow(xsid)["Levee Left Elev."] = elev;
	}

	private float GetRightLevee(int xsid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), xsid, "Levee Right Sta.");
	}

	private void SetRightLeveeStation(int xsid, float station)
	{
		FeatureRow(xsid)["Levee Right Sta."] = station;
	}

	private float GetRightLeveeElev(int xsid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), xsid, "Levee Right Elev.");
	}

	private void SetRightLeveeElev(int xsid, float elev)
	{
		FeatureRow(xsid)["Levee Right Elev."] = elev;
	}

	public IEnumerable<LeveeProperties> LeveeProperties()
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				yield return GetLeveeProperties(i);
			}
		}
	}

	private LeveeProperties CreateLeveeProperties(int xsIdx)
	{
		LeveeProperties leveeProperties = new LeveeProperties();
		FeatureTable();
		float leftLevee = GetLeftLevee(xsIdx);
		float leftLeveeElev = GetLeftLeveeElev(xsIdx);
		if (float.IsNaN(leftLevee) || float.IsNaN(leftLeveeElev))
		{
			leveeProperties.LeftExists = false;
		}
		else
		{
			leveeProperties.LeftExists = true;
			leveeProperties.LeftM = ComputeM(xsIdx, leftLevee);
			leveeProperties.LeftStation = leftLevee;
			leveeProperties.LeftElevation = leftLeveeElev;
		}
		float rightLevee = GetRightLevee(xsIdx);
		float rightLeveeElev = GetRightLeveeElev(xsIdx);
		if (float.IsNaN(rightLevee) || float.IsNaN(rightLeveeElev))
		{
			leveeProperties.RightExists = false;
		}
		else
		{
			leveeProperties.RightExists = true;
			leveeProperties.RightM = ComputeM(xsIdx, rightLevee);
			leveeProperties.RightStation = rightLevee;
			leveeProperties.RightElevation = rightLeveeElev;
		}
		return leveeProperties;
	}

	public float GetLeftOverbankLength(int xsid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), xsid, "Length LOB");
	}

	public void SetLeftOverbankLength(int xsid, float len)
	{
		FeatureRow(xsid)["Length LOB"] = len;
	}

	public float GetRightOverbankLength(int xsid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), xsid, "Length ROB");
	}

	public void SetRightOverbankLength(int xsid, float len)
	{
		FeatureRow(xsid)["Length ROB"] = len;
	}

	public float GetChannelLength(int xsid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), xsid, "Length Channel");
	}

	public void SetChannelLength(int xsid, float len)
	{
		FeatureRow(xsid)["Length Channel"] = len;
	}

	public float GetContractionCoef(int xsid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), xsid, "Steady Contr. Coef.");
	}

	public void SetContractionCoef(int xsid, float coef)
	{
		FeatureRow(xsid)["Steady Contr. Coef."] = coef;
	}

	public float GetExpansionCoef(int xsid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), xsid, "Steady Expan. Coef.");
	}

	public void SetExpansionCoef(int xsid, float coef)
	{
		FeatureRow(xsid)["Steady Expan. Coef."] = coef;
	}

	public float GetUnsteadyContractionCoef(int xsid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), xsid, "Unsteady Contr. Coef.");
	}

	public void SetUnsteadyContractionCoef(int xsid, float coef)
	{
		FeatureRow(xsid)["Unsteady Contr. Coef."] = coef;
	}

	public float GetUnsteadyExpansionCoef(int xsid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), xsid, "Unsteady Expan. Coef.");
	}

	public void SetUnsteadyExpansionCoef(int xsid, float coef)
	{
		FeatureRow(xsid)["Unsteady Expan. Coef."] = coef;
	}

	public float GetMinorLossCoef(int xsid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), xsid, "Minor Loss");
	}

	public void SetMinorLossCoef(int xsid, float coef)
	{
		FeatureRow(xsid)["Minor Loss"] = coef;
	}

	public float GetSkewCoef(int xsid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), xsid, "Skew");
	}

	public void SetSkewCoef(int xsid, float coef)
	{
		FeatureRow(xsid)["Skew"] = coef;
	}

	public bool GetRatingCurveHWCheck(int xsid)
	{
		return DataTableExtensions.GetBoolean(FeatureTable(), xsid, "Rating Curve HW Check");
	}

	public void SetRatingCurveHWCheck(int xsid, bool hwCheck)
	{
		FeatureRow(xsid)["Rating Curve HW Check"] = hwCheck;
	}

	public Point2DCollection GetManningsN(int xsid)
	{
		return FeatureRow(xsid)["Manning Data"] as Point2DCollection;
	}

	public void SetManningsN(int xsid, Point2DCollection p2d)
	{
		FeatureRow(xsid)["Manning Data"] = p2d;
	}

	public int GetHPVertSliceCount(int xsid)
	{
		return DataTableExtensions.GetInteger(FeatureTable(), xsid, "HP Vert. Slices");
	}

	public void SetHPVertSliceCount(int xsid, int hpVertSliceCount)
	{
		FeatureRow(xsid)["HP Vert. Slices"] = hpVertSliceCount;
	}

	public float GetHPStartElev(int xsid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), xsid, "HP Start Elev.");
	}

	public void SetHPStartElev(int xsid, float hpStartElev)
	{
		FeatureRow(xsid)["HP Start Elev."] = hpStartElev;
	}

	public float GetHPVertIncr(int xsid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), xsid, "HP Vert. Incr.");
	}

	public void SetHPVertIncr(int xsid, float hpVertIncr)
	{
		FeatureRow(xsid)["HP Vert. Incr."] = hpVertIncr;
	}

	public int GetHPLOBSlices(int xsid)
	{
		return DataTableExtensions.GetInteger(FeatureTable(), xsid, "HP ROB Slices");
	}

	public void SetHPLOBSlices(int xsid, int hplobSlices)
	{
		FeatureRow(xsid)["HP ROB Slices"] = hplobSlices;
	}

	public int GetHPChanSlices(int xsid)
	{
		return DataTableExtensions.GetInteger(FeatureTable(), xsid, "HP Chan. Slices");
	}

	public void SetHPChanSlices(int xsid, int hpChanSlices)
	{
		FeatureRow(xsid)["HP Chan. Slices"] = hpChanSlices;
	}

	public int GetHPROBSlices(int xsid)
	{
		return DataTableExtensions.GetInteger(FeatureTable(), xsid, "HP LOB Slices");
	}

	public void SetHPROBSlices(int xsid, int hprobSlices)
	{
		FeatureRow(xsid)["HP LOB Slices"] = hprobSlices;
	}

	public bool GetDeckPreissman(int xsid)
	{
		return DataTableExtensions.GetBoolean(FeatureTable(), xsid, "Deck Preissman Slot");
	}

	public void SetDeckPreissman(int xsid, bool deckPreissman)
	{
		FeatureRow(xsid)["Deck Preissman Slot"] = deckPreissman;
	}

	public float GetRValueCached(int fid)
	{
		object objectValue = RuntimeHelpers.GetObjectValue(FeatureRow(fid)["Cutline to XS Ratio"]);
		if (objectValue == null || Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)))
		{
			return 0f;
		}
		return Conversions.ToSingle(objectValue);
	}

	public void SetRValueCached(int fid, float rVal)
	{
		FeatureRow(fid)["Cutline to XS Ratio"] = rVal;
	}

	public bool GetRValValid(int fid)
	{
		object objectValue = RuntimeHelpers.GetObjectValue(FeatureRow(fid)["Cutline to XS Ratio Valid"]);
		if (objectValue == null || Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)))
		{
			return false;
		}
		return Conversions.ToBoolean(objectValue);
	}

	public void SetRValValid(int fid, bool valid)
	{
		FeatureRow(fid)["Cutline to XS Ratio Valid"] = valid;
	}

	public float GetRValueQuick(int fid)
	{
		Polyline polyline = Polyline(fid);
		if (!RasMapperLib.Polyline.IsValidPolyline(polyline))
		{
			return float.NaN;
		}
		IProfileInfo profileInfo = ProfileProperties().ElementAtOrDefault(fid);
		float lengthOverLastStationAboveFirstStation = (float)(polyline.Length / (double)(profileInfo.LastStation - profileInfo.FirstStation));
		if (float.IsNaN(lengthOverLastStationAboveFirstStation))
		{
			return float.NaN;
		}
		return lengthOverLastStationAboveFirstStation;
	}

	public float GetRValue(int fid)
	{
		if (!GetRValValid(fid))
		{
			Polyline polyline = Polyline(fid);
			if (!RasMapperLib.Polyline.IsValidPolyline(polyline))
			{
				return float.NaN;
			}
			Profile profile = GetProfile(fid);
			float polylineProfileTerm = (float)(polyline.Length / (double)profile.Length());
			if (float.IsNaN(polylineProfileTerm))
			{
				return float.NaN;
			}
			SetRValueCached(fid, polylineProfileTerm);
			SetRValValid(fid, valid: true);
			return polylineProfileTerm;
		}
		return GetRValueCached(fid);
	}

	public DataRow GetXSRow(string river, string reach, string rs, [Optional][DefaultParameterValue(0)] ref int index)
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (Operators.CompareString(IHasRRRSLayerExtensions.GetRiverName(this, i), river, TextCompare: false) == 0 && Operators.CompareString(IHasRRRSLayerExtensions.GetReachName(this, i), reach, TextCompare: false) == 0 && Operators.CompareString(IHasRRRSLayerExtensions.GetRiverStationName(this, i), rs, TextCompare: false) == 0)
				{
					index = i;
					return FeatureRow(i);
				}
			}
			return null;
		}
	}

	public int FindXS(string river, string reach, string rs)
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (Operators.CompareString(IHasRRRSLayerExtensions.GetRiverName(this, i), river, TextCompare: false) == 0 && Operators.CompareString(IHasRRRSLayerExtensions.GetReachName(this, i), reach, TextCompare: false) == 0 && Operators.CompareString(IHasRRRSLayerExtensions.GetRiverStationName(this, i), rs, TextCompare: false) == 0)
				{
					return i;
				}
			}
			return -1;
		}
	}

	public bool GetIsDefaultCenterline(int fid)
	{
		return DataTableExtensions.GetBoolean(FeatureTable(), fid, "Default Centerline");
	}

	public void SetIsDefaultCenterline(int fid, bool isDefaultCenterline)
	{
		FeatureRow(fid)["Default Centerline"] = isDefaultCenterline;
	}

	public int FindXSIndexed(string river, string reach, string rs, ref Indexer cachedIndexer)
	{
		if (cachedIndexer == null)
		{
			cachedIndexer = new Indexer(this);
		}
		return cachedIndexer.GetXSFID(river, reach, rs);
	}

	public void ClearRRRSColumns(int fID)
	{
		IHasRRRSLayerExtensions.SetRiverName(this, fID, "");
		IHasRRRSLayerExtensions.SetReachName(this, fID, "");
		IHasRRRSLayerExtensions.SetRiverStationName(this, fID, "");
		IHasRRRSLayerExtensions.SetRiverStationComputed(this, fID, double.NaN);
	}

	public string GetRiverStationName(string river, string reach, int xsIDWithinReach)
	{
		return IHasRRRSLayerExtensions.GetRiverStationName(this, checked(xsIDWithinReach + GetRiverReachXSData(river, reach).FirstXS));
	}
}
