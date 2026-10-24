using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml;
using Geospatial.GDALAssist;
using H5Assist.Caching.VB;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Mapping;
using RasMapperLib.Names;
using RasMapperLib.Render;
using RasMapperLib.Utilities;
using Utility.Extensions;
using Utility.Systems;

namespace RasMapperLib;

public class RASResults : Layer, IStreamableObject
{
	public enum HydraulicsComputeMethod
	{
		Steady,
		Unsteady,
		Quasi,
		Unknown
	}

	public class SourceFileChangedSuppressor : IDisposable
	{
		public static int SuppressedSemaphore = 0;

		public SourceFileChangedSuppressor()
		{
			checked
			{
				SuppressedSemaphore++;
			}
		}

		protected virtual void Dispose(bool disposing)
		{
			checked
			{
				SuppressedSemaphore--;
			}
		}

		public void Dispose()
		{
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}

		void IDisposable.Dispose()
		{
			//ILSpy generated this explicit interface implementation from .override directive in Dispose
			this.Dispose();
		}
	}

	public enum TreeViewGeometryMode
	{
		Reaches,
		XS
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__193_002D0
	{
		public H5Reader hr;

		public _Closure_0024__193_002D0(_Closure_0024__193_002D0 other)
		{
			if (other != null)
			{
				hr = other.hr;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__193_002D1
	{
		public string meshName;

		public _Closure_0024__193_002D0 outerClosure;

		public _Closure_0024__193_002D1(_Closure_0024__193_002D1 other)
		{
			if (other != null)
			{
				meshName = other.meshName;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__1(string b)
		{
			return Generic2DMapParameters.HasGenericDatasets(outerClosure.hr, b, meshName);
		}
	}

	public const string sWaterSurface = "/Water Surface";

	public const string sMaxWaterSurface = "/Maximum Water Surface";

	public const string sMinWaterSurface = "/Minimum Water Surface";

	public const string sMaxChannelVelocity = "/Maximum Channel Velocity";

	public const string sIncreasingValues = "Increasing Regions Values";

	public const string sIncreasingInfo = "Increasing Regions Info";

	public const string sIncreasingMinMaxWS = "Increasing Regions Min Max WS";

	public const string sDepth = "/Depth";

	public const string sFlow = "/Flow";

	public const string sFaceVelocity = "/Face Velocity";

	public const string sMaxFaceVelocity = "/Maximum Face Velocity";

	public const string sMinFaceVelocity = "/Minimum Face Velocity";

	public const string sNodeXVelocity = "/Node X Vel";

	public const string sNodeYVelocity = "/Node Y Vel";

	public const string sCrossSections = "/Cross Sections";

	public const string sSedXS = "/Cross Section SE";

	public const string sStorageAreas = "/Storage Areas";

	public const string sD2FlowAreas = "/2D Flow Areas";

	public const string sResults = "Results";

	public const string sResultsUnsteady = "Results/Unsteady";

	public const string sResultsSteady = "Results/Steady";

	public const string sResultsQuasi = "Results/Sediment";

	public const string sResultsUnsteadySummary = "Results/Unsteady/Summary";

	public const string sResultsSteadySummary = "Results/Steady/Summary";

	public const string sUnsteadyGeometryInfo = "Results/Unsteady/Geometry Info";

	public const string sSteadyGeometryInfo = "Results/Steady/Output/Geometry Info";

	public const string sUnsteadyOutputBlocks = "Results/Unsteady/Output/Output Blocks";

	public const string sUnsteadyBaseOutput = "Results/Unsteady/Output/Output Blocks/Base Output";

	public const string sUnsteadyDSSHydrographOutput = "Results/Unsteady/Output/Output Blocks/DSS Hydrograph Output";

	public const string sUnsteadyDSSProfileOutput = "Results/Unsteady/Output/Output Blocks/DSS Profile Output";

	public const string sSteadyBaseGroup = "Results/Steady/Output/Output Blocks/Base Output";

	public const string sUnsteadySedimentOutputBlocksGroup = "Results/Unsteady/Output/Output Blocks";

	public const string sUnsteadySedimentBaseGroup = "Results/Unsteady/Output/Output Blocks/Sediment/Sediment Time Series";

	public const string sUnsteadySedimentCrossSectionsAll = "Results/Unsteady/Output/Output Blocks/Sediment/Sediment Time Series/Cross Sections";

	public const string sUnsteadySedimentXSBaseGroup = "Results/Unsteady/Output/Output Blocks/Sediment SE/Sediment Time Series";

	public const string sUnsteadySedimentXSOutput = "Results/Unsteady/Output/Output Blocks/Sediment SE/Sediment Time Series/Cross Section SE";

	public const string sQuasiSedimentOutputBlockGroup = "Results/Sediment/Output Blocks";

	public const string sQuasiSedimentBaseGroup = "Results/Sediment/Output Blocks/Sediment/Sediment Time Series";

	public const string sQuasiSedimentCrossSectionsAll = "Results/Sediment/Output Blocks/Sediment SE/Sediment Time Series";

	public const string sQuasiSedimentXSOutput = "Results/Sediment/Output Blocks/Sediment SE/Sediment Time Series/Cross Section SE";

	public const string sUnsteadySummaryGroup = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output";

	public const string sUnsteadySummaryXSMaxWaterSurface = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/Maximum Water Surface";

	public const string sUnsteadySummaryXSMinWaterSurface = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/Minimum Water Surface";

	public const string sUnsteadySummaryXSMaxVelocity = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/Maximum Channel Velocity";

	public const string sUnsteadySummarySAMaxWaterSurface = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Storage Areas/Maximum Water Surface";

	public const string sUnsteadySummarySAMinWaterSurface = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Storage Areas/Minimum Water Surface";

	public const string sUnsteadySummaryD2Group = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/2D Flow Areas";

	public const string sUnsteadyProfileGroup = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series";

	public const string sUnsteadyProfileXSWaterSurface = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/Water Surface";

	public const string sUnsteadyProfileSAWaterSurface = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Storage Areas/Water Surface";

	public const string sUnsteadyProfileSAFlow = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Storage Areas/Flow";

	public const string sUnsteadyProfileD2Group = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/2D Flow Areas";

	public const string sUnsteadyProfileXSFlow = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/Flow";

	public const string sUncertaintyAnalysisGroup = "Results/Uncertainty Analysis";

	public const string sUncertaintyAnalysisXSWaterSurface = "Results/Uncertainty Analysis/Cross Sections";

	public const string sSteadyProfileGroup = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles";

	public const string sSteadyProfileXSWaterSurface = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Water Surface";

	public const string sSteadyProfileSAWaterSurface = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Storage Areas/Water Surface";

	public const string sSteadyProfileD2Group = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/2D Flow Areas";

	public const string sSteadyProfileXSFlow = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Flow";

	public const string sSedimentGrainClassNames = "Event Conditions/Sediment/Grain Class Names";

	private const string sPlanInformationGroupName = "Plan Data/Plan Information";

	private const string MinimumCrossSectionWSAttribute = "Minimum Value of Data Set";

	private const string MaximumCrossSectionWSAttribute = "Maximum Value of Data Set";

	public bool UseInitialConditions;

	public float ApproachFactor;

	public int NumberIterations;

	public static float MinWSPlotTolerance = 0.001f;

	public static bool UseFaceCentroidAdjustment = false;

	public static bool UsePixelByPixelDV = true;

	private bool _loadedSuccessfully;

	private DateTime _structureTime;

	private HydraulicsComputeMethod _hydraulicsComputeMethod;

	private bool _unsteadySediment;

	private OutputBlockCollection _outputBlocks;

	private PlanAttributes _planAttributes;

	private bool _SIUnits;

	private string _UnitsSystemLabel;

	private FlowDistributionData _flowDistribution;

	private string[] _sedimentGrainClasses;

	private PostProcessor _postProcessor;

	public HydraulicsComputeMethod HydraulicComputeMethod => _hydraulicsComputeMethod;

	public RASGeometry Geometry { get; set; }

	public RASEventConditions EventConditions { get; set; }

	public GroupLayer UserDefinedMaps { get; set; }

	public bool LoadedSuccessfully => _loadedSuccessfully;

	public OutputBlockCollection OutputBlocks => _outputBlocks;

	public TimeProfile BaseOutputTimes => _outputBlocks?.BaseOutput;

	public override string LayerPropertiesText { get; }

	public PlanAttributes PlanAttributes
	{
		get
		{
			if (_planAttributes == null)
			{
				_planAttributes = ReadPlanAttributes(SourceFilename);
			}
			return _planAttributes;
		}
	}

	public bool RanSuccessfully { get; set; }

	public override string DefaultLayerName { get; }

	public string OutputFolder
	{
		get
		{
			string planShortID = PlanAttributes.PlanShortID;
			if (string.IsNullOrEmpty(planShortID))
			{
				return "";
			}
			char[] invalidFileNameChars = System.IO.Path.GetInvalidFileNameChars();
			foreach (char oldChar in invalidFileNameChars)
			{
				planShortID = planShortID.Replace(oldChar, '_');
			}
			return System.IO.Path.GetDirectoryName(SourceFilename) + Conversions.ToString(System.IO.Path.DirectorySeparatorChar) + planShortID;
		}
	}

	public override bool CanMoveToAnotherLevel { get; }

	public override bool CanRename { get; }

	public override Type BaseGroupType { get; }

	public string UnitsSystemLabel
	{
		get
		{
			if (Operators.CompareString(_UnitsSystemLabel, "", TextCompare: false) == 0)
			{
				ReadUnitsSystem();
			}
			return _UnitsSystemLabel;
		}
	}

	public bool SIUnits
	{
		get
		{
			if (Operators.CompareString(_UnitsSystemLabel, "", TextCompare: false) == 0)
			{
				ReadUnitsSystem();
			}
			return _SIUnits;
		}
	}

	public override Extent Extent
	{
		get
		{
			if (_extent == null)
			{
				_extent = Geometry.Extent;
			}
			return _extent;
		}
	}

	public FlowDistributionData FlowDistribution
	{
		get
		{
			FlowDistributionData result;
			try
			{
				if (_flowDistribution == null && Geometry.XS.FeatureCount() > 0)
				{
					if (!File.Exists(SourceFilename))
					{
						throw new Exception("Results file '" + System.IO.Path.GetFileName(SourceFilename) + "' doesn't exist.");
					}
					_flowDistribution = new FlowDistributionData(this);
				}
				result = _flowDistribution;
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				ErrorMessage = ex2.Message;
				result = null;
				ProjectData.ClearProjectError();
			}
			return result;
		}
	}

	public bool SteadyHydraulics => _hydraulicsComputeMethod == HydraulicsComputeMethod.Steady;

	public bool QuasiUnsteadyHydraulics => _hydraulicsComputeMethod == HydraulicsComputeMethod.Quasi;

	public bool UnsteadyHydraulics => _hydraulicsComputeMethod == HydraulicsComputeMethod.Unsteady;

	public bool UnsteadySediment => _unsteadySediment;

	public bool UncertaintyAnalysis => false;

	public string[] ProfileNames => _outputBlocks?.BaseOutput?.ProfileNames;

	public string[] ProfileSedNames => _outputBlocks?.SedimentProfiles?.ProfileNames;

	public string[] ProfileSedXSNames => _outputBlocks?.SedimentXSProfiles?.ProfileNames;

	public int ProfileCount
	{
		get
		{
			if (_outputBlocks == null)
			{
				return 0;
			}
			return _outputBlocks.BaseProfileCount;
		}
	}

	public double[] ProfileSimulationTimes => _outputBlocks?.BaseOutput?.SimulationTimes;

	public DateTime SimulationStart
	{
		get
		{
			string[] profileNames = ProfileNames;
			if (profileNames.IsNullOrEmpty())
			{
				return DateTime.MinValue;
			}
			return DSS.ConvertFromDSSDateTime(profileNames.First());
		}
	}

	public DateTime SimulationEnd
	{
		get
		{
			string[] profileNames = ProfileNames;
			if (profileNames.IsNullOrEmpty())
			{
				return DateTime.MinValue;
			}
			return DSS.ConvertFromDSSDateTime(profileNames.Last());
		}
	}

	public double[] ProfileDates => _outputBlocks?.BaseOutput?.ProfileDates;

	private DateTime[] _profileDateTimes { get; set; }

	public DateTime[] ProfileDateTimes
	{
		get
		{
			if (_profileDateTimes == null)
			{
				_profileDateTimes = _outputBlocks?.BaseOutput?.GetDateTimes(SimulationStart);
			}
			return _profileDateTimes;
		}
	}

	private DateTime[] _dssHydrographDateTimes { get; set; }

	public DateTime[] DSSHydrographDateTimes
	{
		get
		{
			if (_dssHydrographDateTimes == null)
			{
				_dssHydrographDateTimes = _outputBlocks?.DSSHydrographOutput?.GetDateTimes(SimulationStart);
			}
			return _dssHydrographDateTimes;
		}
	}

	private DateTime[] _dssProfileDateTimes { get; set; }

	public DateTime[] DSSProfileDateTimes
	{
		get
		{
			if (_dssProfileDateTimes == null)
			{
				_dssProfileDateTimes = _outputBlocks?.DSSProfileOutput?.GetDateTimes(SimulationStart);
			}
			return _dssProfileDateTimes;
		}
	}

	public float MappingDTSeconds => (float)((ProfileDates[1] - ProfileDates[0]) * 24.0 * 60.0 * 60.0);

	public float DssHydrographDTSeconds
	{
		get
		{
			DateTime[] dSSHydrographDateTimes = DSSHydrographDateTimes;
			if (dSSHydrographDateTimes == null || dSSHydrographDateTimes.Count() < 2)
			{
				return 0f;
			}
			return (float)(dSSHydrographDateTimes[1] - dSSHydrographDateTimes[0]).TotalSeconds;
		}
	}

	public float DssProfileDTSeconds
	{
		get
		{
			DateTime[] dSSProfileDateTimes = DSSProfileDateTimes;
			if (dSSProfileDateTimes == null || dSSProfileDateTimes.Count() < 2)
			{
				return 0f;
			}
			return (float)(dSSProfileDateTimes[1] - dSSProfileDateTimes[0]).TotalSeconds;
		}
	}

	public RASResults()
	{
		UseInitialConditions = false;
		ApproachFactor = 0.5f;
		NumberIterations = 1000;
		_loadedSuccessfully = true;
		_structureTime = DateTime.MinValue;
		_unsteadySediment = false;
		LayerPropertiesText = "RAS Results Information";
		RanSuccessfully = false;
		_UnitsSystemLabel = "";
		_flowDistribution = null;
		_sedimentGrainClasses = null;
		DefaultLayerName = "Result";
		CanMoveToAnotherLevel = false;
		CanRename = false;
		BaseGroupType = typeof(ResultsGroup);
		_profileDateTimes = null;
		_dssHydrographDateTimes = null;
		_dssProfileDateTimes = null;
	}

	public RASResults(string filename)
		: base("", filename)
	{
		UseInitialConditions = false;
		ApproachFactor = 0.5f;
		NumberIterations = 1000;
		_loadedSuccessfully = true;
		_structureTime = DateTime.MinValue;
		_unsteadySediment = false;
		LayerPropertiesText = "RAS Results Information";
		RanSuccessfully = false;
		_UnitsSystemLabel = "";
		_flowDistribution = null;
		_sedimentGrainClasses = null;
		DefaultLayerName = "Result";
		CanMoveToAnotherLevel = false;
		CanRename = false;
		BaseGroupType = typeof(ResultsGroup);
		_profileDateTimes = null;
		_dssHydrographDateTimes = null;
		_dssProfileDateTimes = null;
		SetDisplayName(PlanAttributes.PlanShortID);
		LoadFileStructure();
		EnsureGeometryAndEventConditions();
		ToolTipText = PlanAttributes.PlanTitle;
	}

	public void StreamOut(BinaryWriter bw)
	{
		Geometry.LoadAll();
		using BufferedBinaryWriter bufferedBinaryWriter = new BufferedBinaryWriter(bw);
		StreamOutInternal(bufferedBinaryWriter.Writer);
	}

	void IStreamableObject.StreamOut(BinaryWriter bw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in StreamOut
		this.StreamOut(bw);
	}

	public void StreamOutInternal(BinaryWriter bw)
	{
		Pipe.WriteString(bw, SourceFilename);
		bw.Write(RanSuccessfully);
		bw.Write(_SIUnits);
		PlanAttributes.StreamOut(bw);
		bw.Write(_hydraulicsComputeMethod == HydraulicsComputeMethod.Steady);
		bw.Write(_hydraulicsComputeMethod == HydraulicsComputeMethod.Quasi);
		bw.Write(_hydraulicsComputeMethod == HydraulicsComputeMethod.Unsteady);
		bw.Write(_unsteadySediment);
		bw.Write(_hydraulicsComputeMethod == HydraulicsComputeMethod.Unknown);
		if (_outputBlocks == null)
		{
			_outputBlocks = new OutputBlockCollection();
		}
		_outputBlocks.StreamOut(bw);
		Geometry.StreamOut(bw);
		EventConditions.StreamOut(bw);
	}

	public void StreamIn(BinaryReader br)
	{
		using BufferedBinaryReader bufferedBinaryReader = new BufferedBinaryReader(br);
		StreamInInternal(bufferedBinaryReader.Reader);
	}

	void IStreamableObject.StreamIn(BinaryReader br)
	{
		//ILSpy generated this explicit interface implementation from .override directive in StreamIn
		this.StreamIn(br);
	}

	private void StreamInInternal(BinaryReader br)
	{
		SourceFilename = Pipe.ReadString(br);
		RanSuccessfully = br.ReadBoolean();
		_SIUnits = br.ReadBoolean();
		_planAttributes = new PlanAttributes();
		_planAttributes.StreamIn(br);
		bool readBoolean = br.ReadBoolean();
		bool readBoolean2 = br.ReadBoolean();
		bool readBoolean3 = br.ReadBoolean();
		_unsteadySediment = br.ReadBoolean();
		br.ReadBoolean();
		if (readBoolean)
		{
			_hydraulicsComputeMethod = HydraulicsComputeMethod.Steady;
		}
		else if (readBoolean2)
		{
			_hydraulicsComputeMethod = HydraulicsComputeMethod.Quasi;
		}
		else if (readBoolean3)
		{
			_hydraulicsComputeMethod = HydraulicsComputeMethod.Unsteady;
		}
		else
		{
			_hydraulicsComputeMethod = HydraulicsComputeMethod.Unknown;
		}
		_outputBlocks = new OutputBlockCollection();
		_outputBlocks.StreamIn(br);
		Geometry = new RASGeometry();
		Geometry.Results = this;
		Geometry.StreamIn(br);
		EventConditions = new RASEventConditions();
		EventConditions.Results = this;
		EventConditions.StreamIn(br);
	}

	public PostProcessor GetPostProcessor()
	{
		if (_postProcessor == null)
		{
			_postProcessor = new PostProcessor(this);
		}
		return _postProcessor;
	}

	protected override void XMLLoadMetadata(XmlElement xmlLayer)
	{
		base.XMLLoadMetadata(xmlLayer);
		LoadFileStructure();
	}

	protected override void XMLLoadChildren(XmlElement xmlEl)
	{
		XmlNodeList xmlNodeList = xmlEl.SelectNodes("Layer");
		XmlElement geomXML = null;
		XmlElement eventXML = null;
		foreach (XmlElement xmlNodeList2 in xmlNodeList)
		{
			try
			{
				string typeNameFromXML = Layer.GetTypeNameFromXML(xmlNodeList2);
				if (typeNameFromXML.RoughlyEquals("RASResultsMap"))
				{
					RASResultsMap rASResultsMap = new RASResultsMap(this);
					rASResultsMap.XMLLoad(xmlNodeList2);
					Nodes.Add(rASResultsMap);
					continue;
				}
				if (typeNameFromXML.RoughlyEquals("rasgeometry"))
				{
					geomXML = xmlNodeList2;
					continue;
				}
				if (typeNameFromXML.RoughlyEquals("RASEventConditions"))
				{
					eventXML = xmlNodeList2;
					continue;
				}
				if (typeNameFromXML.RoughlyEquals("GroupLayer"))
				{
					RecurseLoadMapGroups(xmlNodeList2);
					continue;
				}
				Layer layer = Layer.LoadLayerNew(xmlNodeList2);
				if (layer != null)
				{
					Nodes.Add(layer);
				}
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				ProjectData.ClearProjectError();
			}
		}
		EnsureGeometryAndEventConditions(geomXML, eventXML);
		EnsureDefaultMaps();
		ToolTipText = PlanAttributes.PlanTitle;
	}

	private void RecurseLoadMapGroups(XmlElement xmlEl)
	{
		GroupLayer groupLayer = new GroupLayer();
		groupLayer.XMLLoadJustMetadata(xmlEl);
		Nodes.Add(groupLayer);
		XmlNodeList xmlNodeList = xmlEl.SelectNodes("Layer");
		foreach (XmlElement xmlNodeList2 in xmlNodeList)
		{
			string typeNameFromXML = Layer.GetTypeNameFromXML(xmlNodeList2);
			if (typeNameFromXML.RoughlyEquals("RASResultsMap"))
			{
				RASResultsMap rASResultsMap = new RASResultsMap(this);
				rASResultsMap.XMLLoad(xmlNodeList2);
				groupLayer.Nodes.Add(rASResultsMap);
			}
			else if (typeNameFromXML.RoughlyEquals("GroupLayer"))
			{
				RecurseLoadMapGroups(xmlNodeList2);
			}
		}
	}

	public void EnsureDefaultMaps()
	{
		bool condition = true;
		bool condition2 = true;
		bool condition3 = true;
		foreach (RASResultsMap rasResultsMap in ChildrenOfType<RASResultsMap>())
		{
			MapTypes mapType = rasResultsMap.MapType;
			if (mapType == MapTypes.Depth)
			{
				condition = false;
			}
			else if (mapType == MapTypes.Velocity)
			{
				condition2 = false;
			}
			else if (mapType == MapTypes.Elevation)
			{
				condition3 = false;
			}
		}
		if (condition)
		{
			AddMap(MapTypes.Depth);
		}
		if (condition2)
		{
			AddMap(MapTypes.Velocity);
		}
		if (condition3)
		{
			AddMap(MapTypes.Elevation);
		}
	}

	private void AddMap(MapTypes mt)
	{
		RASResultsMap rASResultsMap = new RASResultsMap(this, mt);
		if (SteadyHydraulics)
		{
			rASResultsMap.ProfileIndex = 0;
		}
		else
		{
			rASResultsMap.SetToMaxProfile();
		}
		Nodes.Add(rASResultsMap);
	}

	public void EnsureDataMatchesCurrentFileData()
	{
		Geometry.EnsureDataMatchesCurrentFileData();
		LoadFileStructure();
	}

	private void UnsetTimeProfiles()
	{
		_outputBlocks = new OutputBlockCollection();
	}

	private void LoadingError(string msg)
	{
		_structureTime = DateTime.MinValue;
		UnsetTimeProfiles();
		ErrorMessage = msg;
	}

	private void EnsureGeometryAndEventConditions(XmlElement geomXML = null, XmlElement eventXML = null)
	{
		if (Geometry == null)
		{
			Geometry = new RASGeometry(this);
			if (geomXML != null)
			{
				Geometry.XMLLoad(geomXML);
			}
		}
		if (EventConditions == null)
		{
			EventConditions = new RASEventConditions(this);
			if (eventXML != null)
			{
				EventConditions.XMLLoad(eventXML);
			}
		}
		if (!Nodes.Contains(EventConditions))
		{
			Nodes.Insert(0, EventConditions);
		}
		if (Operators.CompareString(EventConditions.ErrorMessage, "", TextCompare: false) != 0)
		{
			ErrorMessage = EventConditions.ErrorMessage;
		}
		if (!Nodes.Contains(Geometry))
		{
			Nodes.Insert(1, Geometry);
		}
		if (Operators.CompareString(Geometry.ErrorMessage, "", TextCompare: false) != 0)
		{
			ErrorMessage = Geometry.ErrorMessage;
		}
	}

	public bool LoadFileStructure()
	{
		bool result;
		if (!SourceFileExists)
		{
			LoadingError(SourceFilename + " does not exist.");
			result = false;
		}
		else if (DateTime.Compare(_structureTime, File.GetLastWriteTime(SourceFilename)) == 0)
		{
			result = true;
		}
		else
		{
			using H5Reader h5Reader = new H5Reader(SourceFilename);
			try
			{
				string planShortID = PlanAttributes.PlanShortID;
				if (string.IsNullOrEmpty(planShortID))
				{
					planShortID = "Unknown Result (Bad file)";
					_loadedSuccessfully = false;
				}
				SetDisplayName(planShortID);
				if (h5Reader.ExistsAsGroup("Results/Unsteady"))
				{
					_hydraulicsComputeMethod = HydraulicsComputeMethod.Unsteady;
					_outputBlocks = OutputBlockCollection.ParseUnsteady(h5Reader);
					string textValue = "";
					if (h5Reader.TryReadAttributeScalar("Results/Unsteady/Summary/", "Solution", ref textValue) && textValue.Contains("Finished Successfully"))
					{
						RanSuccessfully = true;
					}
				}
				else if (h5Reader.ExistsAsGroup("Results/Sediment"))
				{
					_hydraulicsComputeMethod = HydraulicsComputeMethod.Quasi;
					_outputBlocks = OutputBlockCollection.ParseQuasiUnsteady(h5Reader);
					RanSuccessfully = true;
				}
				else if (h5Reader.ExistsAsGroup("Results/Steady"))
				{
					_hydraulicsComputeMethod = HydraulicsComputeMethod.Steady;
					_outputBlocks = OutputBlockCollection.ParseSteady(h5Reader);
					string textValue2 = "";
					if (h5Reader.TryReadAttributeScalar("Results/Steady/Summary/", "Solution", ref textValue2) && textValue2.Contains("Finished Successfully"))
					{
						RanSuccessfully = true;
					}
				}
				else if (h5Reader.ExistsAsGroup("Results/Uncertainty Analysis"))
				{
					_hydraulicsComputeMethod = HydraulicsComputeMethod.Unknown;
				}
				else
				{
					_hydraulicsComputeMethod = HydraulicsComputeMethod.Unknown;
				}
				if (_outputBlocks == null || _outputBlocks.BaseOutput == null)
				{
					LoadingError("Malformed Results dataset.");
					result = false;
				}
				else
				{
					if (h5Reader.ExistsAsGroup("Results/Unsteady/Output/Output Blocks/Sediment/Sediment Time Series"))
					{
						_unsteadySediment = true;
						_sedimentGrainClasses = new string[1];
						if (h5Reader.ExistsAsDataset("Event Conditions/Sediment/Grain Class Names"))
						{
							H5ReadStringColumn(h5Reader, "Event Conditions/Sediment/Grain Class Names", ref _sedimentGrainClasses);
						}
					}
					else if (h5Reader.ExistsAsGroup("Results/Sediment/Output Blocks/Sediment/Sediment Time Series"))
					{
						_sedimentGrainClasses = new string[1];
						if (h5Reader.ExistsAsDataset("Event Conditions/Sediment/Grain Class Names"))
						{
							H5ReadStringColumn(h5Reader, "Event Conditions/Sediment/Grain Class Names", ref _sedimentGrainClasses);
						}
					}
					_planAttributes = ReadPlanAttributes(h5Reader);
					if (_hydraulicsComputeMethod != HydraulicsComputeMethod.Unknown)
					{
						_structureTime = File.GetLastWriteTime(SourceFilename);
						result = true;
					}
					else
					{
						LoadingError("Unknown file structure in " + SourceFilename);
						result = false;
					}
				}
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				LoadingError(ex2.Message);
				result = false;
				ProjectData.ClearProjectError();
			}
		}
		return result;
	}

	private void ReadUnitsSystem()
	{
		using (H5Reader h5Reader = new H5Reader(SourceFilename))
		{
			try
			{
				h5Reader.ReadAttributeScalar("/", "Units System", ref _UnitsSystemLabel);
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				ProjectData.ClearProjectError();
			}
		}
		if (Operators.CompareString(_UnitsSystemLabel, "SI", TextCompare: false) == 0 || Operators.CompareString(_UnitsSystemLabel, "SI Units", TextCompare: false) == 0)
		{
			_SIUnits = true;
		}
	}

	public Tuple<float, float>[] ReadXSEncroachments(int profile = 0)
	{
		checked
		{
			if (SteadyHydraulics)
			{
				string path = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Additional Variables/Encroachment Station Left";
				string path2 = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Additional Variables/Encroachment Station Right";
				using H5Reader h5Reader = new H5Reader(SourceFilename);
				if (!h5Reader.ExistsAsDataset(path))
				{
					return null;
				}
				if (!h5Reader.ExistsAsDataset(path2))
				{
					return null;
				}
				float[,] values = null;
				float[,] values2 = null;
				h5Reader.ReadRow(path, profile, ref values);
				h5Reader.ReadRow(path2, profile, ref values2);
				Tuple<float, float>[] valueTupleArray = new Tuple<float, float>[values.GetLength(1) - 1 + 1];
				int count = valueTupleArray.Length - 1;
				for (int i = 0; i <= count; i++)
				{
					float stationingOffset = Geometry.XS.GetStationingOffset(i);
					if (stationingOffset != 0f && !float.IsNaN(stationingOffset))
					{
						valueTupleArray[i] = new Tuple<float, float>(values[0, i] + stationingOffset, values2[0, i] + stationingOffset);
					}
					else
					{
						valueTupleArray[i] = new Tuple<float, float>(values[0, i], values2[0, i]);
					}
				}
				return valueTupleArray;
			}
			if (UnsteadyHydraulics)
			{
				string path3 = "Plan Data/Fixed Encroachments";
				using H5Reader h5Reader2 = new H5Reader(SourceFilename);
				if (!h5Reader2.ExistsAsDataset(path3))
				{
					return null;
				}
				float[,] values3 = null;
				h5Reader2.ReadDataset(path3, ref values3);
				Tuple<float, float>[] valueTupleArrayArray = new Tuple<float, float>[values3.GetLength(0) - 1 + 1];
				int count2 = values3.GetLength(0) - 1;
				for (int j = 0; j <= count2; j++)
				{
					float stationingOffset2 = Geometry.XS.GetStationingOffset(j);
					if (stationingOffset2 != 0f && !float.IsNaN(stationingOffset2))
					{
						valueTupleArrayArray[j] = new Tuple<float, float>(values3[j, 0] + stationingOffset2, values3[j, 1] + stationingOffset2);
					}
					else
					{
						valueTupleArrayArray[j] = new Tuple<float, float>(values3[j, 0], values3[j, 1]);
					}
				}
				return valueTupleArrayArray;
			}
			return null;
		}
	}

	public float[,] ReadFloatMatrix(string dataSetAbsolutePath)
	{
		using (H5Reader h5Reader = new H5Reader(SourceFilename))
		{
			if (h5Reader.ExistsAsDataset(dataSetAbsolutePath))
			{
				float[,] values = null;
				h5Reader.ReadDataset(dataSetAbsolutePath, ref values);
				return values;
			}
		}
		throw new Exception("Failed to read HDF5 data section '" + dataSetAbsolutePath + "' because it DOES NOT EXIST.");
	}

	public static RASResults TryIdentifyResultsFile(string resultIdentifier)
	{
		RASResults result;
		try
		{
			if (string.IsNullOrWhiteSpace(resultIdentifier))
			{
				result = null;
			}
			else if (File.Exists(resultIdentifier))
			{
				result = new RASResults(resultIdentifier);
			}
			else
			{
				if (!string.IsNullOrEmpty(SharedData.RasMapFolder))
				{
					throw new NotImplementedException();
				}
				result = null;
			}
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			result = null;
			ProjectData.ClearProjectError();
		}
		return result;
	}

	public bool HasBeenRun()
	{
		if (!SourceFileExists)
		{
			return false;
		}
		using H5Reader h5Reader = new H5Reader(SourceFilename);
		return h5Reader.ExistsAsGroup("/Results");
	}

	public float GetBaseComputationTimestepSeconds()
	{
		checked
		{
			using H5Reader h5Reader = new H5Reader(SourceFilename);
			string textValue = "";
			if (h5Reader.TryReadAttributeScalar("/Plan Data/Plan Information", "Computation Time Step Base", ref textValue))
			{
				float unity = 0f;
				string textValue2 = "";
				if (Operators.CompareString(textValue.Substring(textValue.Length - 3), "SEC", TextCompare: false) == 0)
				{
					textValue2 = textValue.Substring(0, textValue.Length - 3);
					unity = 1f;
				}
				else
				{
					if (Operators.CompareString(textValue.Substring(textValue.Length - 3), "MIN", TextCompare: false) != 0)
					{
						return 0f;
					}
					textValue2 = textValue.Substring(0, textValue.Length - 3);
					unity = 60f;
				}
				if (!float.TryParse(textValue2, out var result))
				{
					return 0f;
				}
				return result * unity;
			}
			return 0f;
		}
	}

	public DateTime GetBestAvailableComputeTimestamp()
	{
		if (!SourceFileExists)
		{
			return DateTime.MinValue;
		}
		Dictionary<string, object> allAttributes = null;
		using (H5Reader h5Reader = new H5Reader(SourceFilename))
		{
			if (UnsteadyHydraulics)
			{
				allAttributes = h5Reader.ReadAllAttributes("Results/Unsteady/Summary/");
			}
			else if (SteadyHydraulics)
			{
				allAttributes = h5Reader.ReadAllAttributes("Results/Steady/Summary/");
			}
		}
		if (allAttributes == null)
		{
			return SourceFileDate;
		}
		object localValue = null;
		if (allAttributes.TryGetValue("Run Time Window", out localValue))
		{
			string textValue = localValue as string;
			if (string.IsNullOrEmpty(textValue))
			{
				return SourceFileDate;
			}
			string[] parts = textValue.Split(new string[1] { " to " }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length != 2)
			{
				return SourceFileDate;
			}
			if (DateTime.TryParse(parts[1], out var result))
			{
				return result;
			}
		}
		return SourceFileDate;
	}

	public void ReadWSEL1DTimeSeries(int col, ref float[] values, bool showProgressBar = false)
	{
		if (SteadyHydraulics)
		{
			ReadUnsteadyTimeSeries("Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Water Surface", col, ref values, showProgressBar);
		}
		else
		{
			ReadUnsteadyTimeSeries("Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/Water Surface", col, ref values, showProgressBar);
		}
	}

	public void ReadWSELSATimeSeries(int col, ref float[] values, bool showProgressBar = false)
	{
		if (SteadyHydraulics)
		{
			ReadUnsteadyTimeSeries("Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Storage Areas/Water Surface", col, ref values, showProgressBar);
		}
		else
		{
			ReadUnsteadyTimeSeries("Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Storage Areas/Water Surface", col, ref values, showProgressBar);
		}
	}

	public void ReadFlow1DTimeSeries(int col, ref float[] values, bool showProgressBar = false)
	{
		if (SteadyHydraulics)
		{
			ReadUnsteadyTimeSeries("Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Flow", col, ref values, showProgressBar);
		}
		else
		{
			ReadUnsteadyTimeSeries("Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/Flow", col, ref values, showProgressBar);
		}
	}

	public void ReadUnsteadyTimeSeries(string datasetName, int col, ref float[,] values, bool showProgressBar = false)
	{
		PostProcessor postProcessor = GetPostProcessor();
		postProcessor.EnsurePostProcessPopup();
		bool condition = false;
		if (File.Exists(postProcessor.Filename))
		{
			using H5Reader h5Reader = new H5Reader(postProcessor.Filename);
			if (h5Reader.ExistsAsDataset(datasetName))
			{
				h5Reader.ReadColumn(datasetName, col, ref values);
				condition = true;
			}
			else
			{
				condition = false;
			}
		}
		if (condition)
		{
			return;
		}
		postProcessor.PostProcessWithVerticalChunking(datasetName, storeLocalExtrema: false);
		using H5Reader h5Reader2 = new H5Reader(postProcessor.Filename);
		if (h5Reader2.ExistsAsDataset(datasetName))
		{
			h5Reader2.ReadColumn(datasetName, col, ref values);
		}
	}

	public void ReadUnsteadyTimeSeries(string datasetName, int col, ref float[] values, bool showProgressBar = true)
	{
		float[,] values2 = null;
		ReadUnsteadyTimeSeries(datasetName, col, ref values2, showProgressBar);
		checked
		{
			values = new float[values2.GetLength(0) - 1 + 1];
			int count = values.Length - 1;
			for (int i = 0; i <= count; i++)
			{
				values[i] = values2[i, 0];
			}
		}
	}

	public void ReadUnsteadyTimeSeries(string datasetName, List<int> cols, ref float[,] values)
	{
		PostProcessor postProcessor = GetPostProcessor();
		postProcessor.EnsurePostProcessPopup();
		bool condition = false;
		if (File.Exists(postProcessor.Filename))
		{
			using H5Reader h5Reader = new H5Reader(postProcessor.Filename);
			if (h5Reader.ExistsAsDataset(datasetName))
			{
				h5Reader.ReadColumns(datasetName, cols, ref values);
				condition = true;
			}
			else
			{
				condition = false;
			}
		}
		if (condition)
		{
			return;
		}
		postProcessor.PostProcessWithVerticalChunking(datasetName, storeLocalExtrema: false);
		using H5Reader h5Reader2 = new H5Reader(postProcessor.Filename);
		if (h5Reader2.ExistsAsDataset(datasetName))
		{
			h5Reader2.ReadColumns(datasetName, cols, ref values);
		}
	}

	public float[] ReadFaceVelocityTimeSeries(int meshIdx, int faceIdx)
	{
		string faceVelocityDatasetName = Geometry.D2FlowArea.GetFaceVelocityDatasetName(meshIdx);
		float[] values = null;
		ReadUnsteadyTimeSeries(faceVelocityDatasetName, faceIdx, ref values, showProgressBar: false);
		return values;
	}

	public float[,] ReadFaceVelocityTimeSeries(int meshIdx, List<int> cols)
	{
		string faceVelocityDatasetName = Geometry.D2FlowArea.GetFaceVelocityDatasetName(meshIdx);
		float[,] values = null;
		ReadUnsteadyTimeSeries(faceVelocityDatasetName, cols, ref values);
		return values;
	}

	public float[] ReadCellWaterSurfaceTimeSeries(int meshIdx, int cellIdx)
	{
		string datasetName = ResultsDatasets.Unsteady.TimeSeriesOutput.FlowAreas.WaterSurface(Geometry.D2FlowArea.GetFeatureName(meshIdx));
		float[] values = null;
		ReadUnsteadyTimeSeries(datasetName, cellIdx, ref values, showProgressBar: false);
		return values;
	}

	public float[,] ReadCellWaterSurfaceTimeSeries(int meshIdx, List<int> cols)
	{
		string datasetName = ResultsDatasets.Unsteady.TimeSeriesOutput.FlowAreas.WaterSurface(Geometry.D2FlowArea.GetFeatureName(meshIdx));
		float[,] values = null;
		ReadUnsteadyTimeSeries(datasetName, cols, ref values);
		return values;
	}

	public List<string> XSOutputVariables()
	{
		List<string> stringList = new List<string>();
		int featureCount = Geometry.XS.FeatureCount();
		int profileNamesTerm = ProfileNames.Count();
		using (H5Reader h5Reader = new H5Reader(SourceFilename))
		{
			if (!SteadyHydraulics && UnsteadyHydraulics)
			{
				string path = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/";
				foreach (string datasetName in h5Reader.GetDatasetNames(path))
				{
					string current = System.IO.Path.Combine(path, datasetName);
					long[] datasetDimensions = h5Reader.GetDatasetDimensions(current);
					if (datasetDimensions != null && datasetDimensions.Length == 2 && datasetDimensions[0] == profileNamesTerm && datasetDimensions[1] == featureCount)
					{
						stringList.Add(current);
					}
				}
			}
		}
		return stringList;
	}

	public float ReadXS_WSEL(int xsID, int profileIdx)
	{
		switch (profileIdx)
		{
		case int.MaxValue:
			return Read("Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/Maximum Water Surface", 0, xsID);
		case int.MinValue:
			return Read("Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/Minimum Water Surface", 0, xsID);
		default:
			if (SteadyHydraulics)
			{
				return Read("Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Water Surface", profileIdx, xsID);
			}
			if (UnsteadyHydraulics)
			{
				return Read("Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/Water Surface", profileIdx, xsID);
			}
			if (QuasiUnsteadyHydraulics)
			{
				return Read(ResultsDatasets.Quasi.CrossSections.GetWSELDataset(), profileIdx, xsID);
			}
			return float.NaN;
		}
	}

	private float Read(string dsetName, int row, int col)
	{
		float result;
		if (!File.Exists(SourceFilename))
		{
			result = float.NaN;
		}
		else
		{
			float[,] values = null;
			try
			{
				using H5Reader h5Reader = new H5Reader(SourceFilename);
				if (h5Reader.ExistsAsDataset(dsetName))
				{
					h5Reader.ReadRow(dsetName, row, ref values);
					result = values[0, col];
				}
				else
				{
					result = float.NaN;
				}
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				result = float.NaN;
				ProjectData.ClearProjectError();
			}
		}
		return result;
	}

	public bool TryGetGlobalMinMaxWSEL(ref double min, ref double max)
	{
		if (!File.Exists(SourceFilename))
		{
			return false;
		}
		RASGeometry geometry = Geometry;
		if (geometry == null)
		{
			return false;
		}
		bool condition = false;
		bool condition2 = false;
		RASXS xS = geometry.XS;
		RASStorageArea storageArea = geometry.StorageArea;
		RASD2FlowArea d2FlowArea = geometry.D2FlowArea;
		double maxValue = double.MaxValue;
		double minValue = double.MinValue;
		checked
		{
			using (H5Reader h5Reader = new H5Reader(SourceFilename))
			{
				if (xS != null)
				{
					string path = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/Water Surface";
					if (h5Reader.ExistsAsAttribute(path, "Maximum Value of Data Set") && h5Reader.ExistsAsAttribute(path, "Minimum Value of Data Set"))
					{
						float tryReadSingleAttributeScalar = h5Reader.TryReadSingleAttributeScalar(path, "Minimum Value of Data Set");
						float tryReadSingleAttributeScalar2 = h5Reader.TryReadSingleAttributeScalar(path, "Maximum Value of Data Set");
						if (!double.IsNaN(tryReadSingleAttributeScalar))
						{
							maxValue = System.Math.Min(maxValue, tryReadSingleAttributeScalar);
							condition2 = true;
						}
						if (!double.IsNaN(tryReadSingleAttributeScalar2))
						{
							minValue = System.Math.Max(minValue, tryReadSingleAttributeScalar2);
							condition = true;
						}
					}
					else
					{
						string path2 = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/Maximum Water Surface";
						int row = 0;
						if (h5Reader.ExistsAsDataset(path2))
						{
							float[,] values = null;
							h5Reader.ReadRow(path2, row, ref values);
							int count = values.GetLength(1) - 1;
							for (int i = 0; i <= count; i++)
							{
								if (!double.IsNaN(values[0, i]))
								{
									minValue = System.Math.Max(minValue, values[0, i]);
									condition = true;
								}
							}
						}
						path2 = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/Minimum Water Surface";
						row = 0;
						if (h5Reader.ExistsAsDataset(path2))
						{
							float[,] values2 = null;
							h5Reader.ReadRow(path2, row, ref values2);
							int count2 = values2.GetLength(1) - 1;
							for (int j = 0; j <= count2; j++)
							{
								if (!double.IsNaN(values2[0, j]))
								{
									maxValue = System.Math.Min(maxValue, values2[0, j]);
									condition2 = true;
								}
							}
						}
					}
				}
				if (storageArea != null)
				{
					string path3 = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Storage Areas/Water Surface";
					if (h5Reader.ExistsAsAttribute(path3, "Maximum Value of Data Set") && h5Reader.ExistsAsAttribute(path3, "Minimum Value of Data Set"))
					{
						float tryReadSingleAttributeScalar3 = h5Reader.TryReadSingleAttributeScalar(path3, "Minimum Value of Data Set");
						float tryReadSingleAttributeScalar4 = h5Reader.TryReadSingleAttributeScalar(path3, "Maximum Value of Data Set");
						if (!double.IsNaN(tryReadSingleAttributeScalar3))
						{
							maxValue = System.Math.Min(maxValue, tryReadSingleAttributeScalar3);
							condition2 = true;
						}
						if (!double.IsNaN(tryReadSingleAttributeScalar4))
						{
							minValue = System.Math.Max(minValue, tryReadSingleAttributeScalar4);
							condition = true;
						}
					}
					else
					{
						string path4 = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Storage Areas/Maximum Water Surface";
						int row2 = 0;
						if (h5Reader.ExistsAsDataset(path4))
						{
							float[,] values3 = null;
							h5Reader.ReadRow(path4, row2, ref values3);
							int count3 = values3.GetLength(1) - 1;
							for (int k = 0; k <= count3; k++)
							{
								if (!double.IsNaN(values3[0, k]))
								{
									minValue = System.Math.Max(minValue, values3[0, k]);
									condition = true;
								}
							}
						}
						path4 = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Storage Areas/Minimum Water Surface";
						row2 = 0;
						if (h5Reader.ExistsAsDataset(path4))
						{
							float[,] values4 = null;
							h5Reader.ReadRow(path4, row2, ref values4);
							int count4 = values4.GetLength(1) - 1;
							for (int l = 0; l <= count4; l++)
							{
								if (!double.IsNaN(values4[0, l]))
								{
									maxValue = System.Math.Min(maxValue, values4[0, l]);
									condition2 = true;
								}
							}
						}
					}
				}
				if (d2FlowArea != null)
				{
					int featureCount = d2FlowArea.FeatureCount();
					if (featureCount != 0)
					{
						bool condition3 = false;
						bool condition4 = false;
						int count5 = featureCount - 1;
						for (int m = 0; m <= count5; m++)
						{
							string getFeatureNameD2FlowAreaTerm = "Results/Unsteady/Output/Output Blocks/Base Output//Unsteady Time Series/2D Flow Areas/" + d2FlowArea.GetFeatureName(m) + "/Water Surface";
							if (!h5Reader.ExistsAsAttribute(getFeatureNameD2FlowAreaTerm, "Maximum Value of Data Set") || !h5Reader.ExistsAsAttribute(getFeatureNameD2FlowAreaTerm, "Minimum Value of Data Set"))
							{
								break;
							}
							float tryReadSingleAttributeScalar5 = h5Reader.TryReadSingleAttributeScalar(getFeatureNameD2FlowAreaTerm, "Minimum Value of Data Set");
							float tryReadSingleAttributeScalar6 = h5Reader.TryReadSingleAttributeScalar(getFeatureNameD2FlowAreaTerm, "Maximum Value of Data Set");
							if (!double.IsNaN(tryReadSingleAttributeScalar5))
							{
								maxValue = System.Math.Min(maxValue, tryReadSingleAttributeScalar5);
								condition2 = true;
								condition3 = true;
							}
							if (!double.IsNaN(tryReadSingleAttributeScalar6))
							{
								minValue = System.Math.Max(minValue, tryReadSingleAttributeScalar6);
								condition = true;
								condition4 = true;
							}
						}
						if (!condition3 || !condition4)
						{
							List<string> stringList = new List<string>();
							int count6 = featureCount - 1;
							for (int n = 0; n <= count6; n++)
							{
								stringList.Add(d2FlowArea.GetFeatureName(n));
							}
							int row3 = 0;
							foreach (string textValue in ResultsDatasets.Unsteady.SummaryOutput.FlowAreas.MaxWaterSurface.Name(stringList))
							{
								if (!h5Reader.ExistsAsDataset(textValue))
								{
									continue;
								}
								float[,] values5 = null;
								h5Reader.ReadRow(textValue, row3, ref values5);
								int count7 = values5.GetLength(1) - 1;
								for (int intervalIndex = 0; intervalIndex <= count7; intervalIndex++)
								{
									if (!double.IsNaN(values5[0, intervalIndex]))
									{
										minValue = System.Math.Max(minValue, values5[0, intervalIndex]);
										condition = true;
									}
								}
							}
							row3 = 0;
							foreach (string textValue2 in ResultsDatasets.Unsteady.SummaryOutput.FlowAreas.MinWaterSurface.Name(stringList))
							{
								if (!h5Reader.ExistsAsDataset(textValue2))
								{
									continue;
								}
								float[,] values6 = null;
								h5Reader.ReadRow(textValue2, row3, ref values6);
								int count8 = values6.GetLength(1) - 1;
								for (int intervalIndex2 = 0; intervalIndex2 <= count8; intervalIndex2++)
								{
									if (!double.IsNaN(values6[0, intervalIndex2]))
									{
										maxValue = System.Math.Min(maxValue, values6[0, intervalIndex2]);
										condition2 = true;
									}
								}
							}
						}
					}
				}
			}
			min = maxValue;
			max = minValue;
			return condition2 && condition;
		}
	}

	private static void CapMinimumDataValuesTo(float[] values, float computed)
	{
		if (values == null)
		{
			return;
		}
		checked
		{
			int count = values.Length - 1;
			for (int i = 0; i <= count; i++)
			{
				if (values[i] != -9999f)
				{
					values[i] = System.Math.Max(values[i], computed);
				}
			}
		}
	}

	private static void ReplaceDataValuesWith(float[] values, float computed)
	{
		if (values == null)
		{
			return;
		}
		checked
		{
			int count = values.Length - 1;
			for (int i = 0; i <= count; i++)
			{
				if (values[i] != -9999f)
				{
					values[i] = computed;
				}
			}
		}
	}

	private static void ReplaceNoDataWithValue(float[] values, float computed)
	{
		if (values == null)
		{
			return;
		}
		checked
		{
			int count = values.Length - 1;
			for (int i = 0; i <= count; i++)
			{
				if (values[i] == -9999f)
				{
					values[i] = computed;
				}
			}
		}
	}

	private static void ConditionalReplaceWithValue(float[][] timeSeriesValues, float computed)
	{
		checked
		{
			int count = timeSeriesValues.Length - 1;
			for (int i = 0; i <= count; i++)
			{
				ReplaceNoDataWithValue(timeSeriesValues[i], computed);
			}
		}
	}

	private static void ReplaceNoDataWithTerrain(float[] values, float[] terrain, float fallBackValue = 0f)
	{
		if (values == null)
		{
			return;
		}
		checked
		{
			int count = values.Length - 1;
			for (int i = 0; i <= count; i++)
			{
				if (values[i] == -9999f)
				{
					if (terrain[i] == -9999f)
					{
						values[i] = fallBackValue;
					}
					else
					{
						values[i] = terrain[i];
					}
				}
			}
		}
	}

	private static void ReplaceNoDataWithNaN(float[] values)
	{
		if (values == null)
		{
			return;
		}
		checked
		{
			int count = values.Length - 1;
			for (int i = 0; i <= count; i++)
			{
				if (values[i] == -9999f)
				{
					values[i] = float.NaN;
				}
			}
		}
	}

	private static void ConditionalReplaceWithTerrain(float[][] timeSeriesValues, float[] terrain)
	{
		checked
		{
			int count = timeSeriesValues.Length - 1;
			for (int i = 0; i <= count; i++)
			{
				ReplaceNoDataWithValue(timeSeriesValues[i], terrain[i]);
			}
		}
	}

	public bool ComputeSwitch(RASResultsMap map, RASGeometryMapPoints mps, int profile, float[] terrain, CacheCollection cache, ref float[] values, bool convertToGraphableData = false)
	{
		MapTypes mapType = map.MapType;
		double depthThreshold = map.DepthThreshold;
		bool useHours = map.UseHours;
		double arrivalStartingSimulationTime = map.ArrivalStartingSimulationTime;
		bool arrivalAsDelta = map.ArrivalAsDelta;
		H5Reader.SetLogging(log: false);
		if (cache == null)
		{
			cache = new CacheCollection();
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		MapTypes mapTypes = mapType;
		if (mapTypes == MapTypes.Elevation)
		{
			WaterSurfaceRenderer.Compute(this, profile, mps, ref values, terrain, cache);
			if (convertToGraphableData)
			{
				ReplaceNoDataWithNaN(values);
			}
		}
		else if (mapTypes == MapTypes.Depth)
		{
			DepthRenderer.Compute(this, profile, mps, terrain, ref values, cache);
			if (convertToGraphableData)
			{
				ReplaceNoDataWithValue(values, 0f);
			}
		}
		else if (mapTypes == MapTypes.LaplacianU)
		{
			LaplaceRenderer.Compute(this, profile, mps, terrain, ref values, isLPU: true, cache);
			if (convertToGraphableData)
			{
				ReplaceNoDataWithValue(values, 0f);
			}
		}
		else if (mapTypes == MapTypes.LaplacianV)
		{
			LaplaceRenderer.Compute(this, profile, mps, terrain, ref values, isLPU: false, cache);
			if (convertToGraphableData)
			{
				ReplaceNoDataWithValue(values, 0f);
			}
		}
		else if (mapTypes == MapTypes.CellWet)
		{
			DepthRenderer.Compute(this, profile, mps, terrain, ref values, cache, allowNegativeDepths: true, (float)depthThreshold);
			CapMinimumDataValuesTo(values, (float)depthThreshold);
			if (convertToGraphableData)
			{
				ReplaceNoDataWithValue(values, 0f);
			}
		}
		else if (mapTypes == MapTypes.Velocity)
		{
			float[] outVelocityX = null;
			float[] outVelocityY = null;
			CacheCollection cache2 = cache;
			float[] outDepths = null;
			VelocityRenderer.Compute(this, profile, mps, terrain, ref outVelocityX, ref outVelocityY, ref values, cache2, ref outDepths);
			if (convertToGraphableData)
			{
				ReplaceNoDataWithValue(values, 0f);
			}
		}
		else if (mapTypes == MapTypes.DepthTimesVelocity)
		{
			DVRenderer.Compute(this, profile, mps, terrain, ref values, cache);
			if (convertToGraphableData)
			{
				ReplaceNoDataWithValue(values, 0f);
			}
		}
		else if (mapTypes == MapTypes.DepthTimesVelocitySquared)
		{
			DVSqRenderer.Compute(this, profile, mps, terrain, ref values, cache);
			if (convertToGraphableData)
			{
				ReplaceNoDataWithValue(values, 0f);
			}
		}
		else if (mapTypes == MapTypes.ArrivalTime)
		{
			ArrivalTimeRenderer.ComputeArrivalTime(this, mps, terrain, (float)depthThreshold, useHours, arrivalAsDelta, arrivalStartingSimulationTime, ref values, cache);
			float profileSimulationTimesTerm = (float)ProfileSimulationTimes.Last();
			float arrivalStartingSimulationTime2 = (float)arrivalStartingSimulationTime;
			float arrivalStartingSimulationTime2Term = profileSimulationTimesTerm - arrivalStartingSimulationTime2;
			if (useHours)
			{
				arrivalStartingSimulationTime2Term *= 24f;
			}
			if (convertToGraphableData)
			{
				ReplaceNoDataWithValue(values, arrivalStartingSimulationTime2Term);
			}
		}
		else if (mapTypes == MapTypes.ArrivalTimeMax)
		{
			ArrivalTimeRenderer.ComputeArrivalTimeMax(this, mps, terrain, (float)depthThreshold, useHours, arrivalStartingSimulationTime, ref values, cache);
			float profileSimulationTimesTerm2 = (float)ProfileSimulationTimes.Last();
			float arrivalStartingSimulationTime3 = (float)arrivalStartingSimulationTime;
			float arrivalStartingSimulationTime3Term = profileSimulationTimesTerm2 - arrivalStartingSimulationTime3;
			if (useHours)
			{
				arrivalStartingSimulationTime3Term *= 24f;
			}
			if (convertToGraphableData)
			{
				ReplaceNoDataWithValue(values, arrivalStartingSimulationTime3Term);
			}
		}
		else if (mapTypes == MapTypes.Recession)
		{
			ArrivalTimeRenderer.ComputeRecessionGrid(this, mps, terrain, (float)depthThreshold, useHours, arrivalAsDelta, ref values, cache);
			if (convertToGraphableData)
			{
				ReplaceNoDataWithValue(values, 0f);
			}
		}
		else if (mapTypes == MapTypes.Duration)
		{
			ArrivalTimeRenderer.ComputeDurationGrid(this, mps, terrain, (float)depthThreshold, useHours, arrivalStartingSimulationTime, ref values, cache);
			if (convertToGraphableData)
			{
				ReplaceNoDataWithValue(values, 0f);
			}
		}
		else if (mapTypes == MapTypes.Flow)
		{
			FlowRenderer.Compute(this, profile, mps, terrain, ref values, cache);
			if (convertToGraphableData)
			{
				ReplaceNoDataWithValue(values, 0f);
			}
		}
		else if (mapTypes == MapTypes.Courant)
		{
			CourantRenderer.Compute(this, profile, mps, terrain, ref values, cache);
			if (convertToGraphableData)
			{
				ReplaceNoDataWithValue(values, 0f);
			}
		}
		else if (mapTypes == MapTypes.Froude)
		{
			DepthAndVelocityRenderer.Compute(this, profile, mps, terrain, ref values, DepthVelocityMethod.Froude, cache);
			if (convertToGraphableData)
			{
				ReplaceNoDataWithValue(values, 0f);
			}
		}
		else if (mapTypes == MapTypes.EnergyDepth)
		{
			DepthAndVelocityRenderer.Compute(this, profile, mps, terrain, ref values, DepthVelocityMethod.EnergyDepth, cache);
			if (convertToGraphableData)
			{
				ReplaceNoDataWithValue(values, 0f);
			}
		}
		else if (mapTypes == MapTypes.EnergyElevation)
		{
			DepthAndVelocityRenderer.Compute(this, profile, mps, terrain, ref values, DepthVelocityMethod.EnergyElevation, cache);
			if (convertToGraphableData)
			{
				ReplaceNoDataWithValue(values, 0f);
			}
		}
		else if (mapTypes == MapTypes.ResidenceTime)
		{
			CourantRenderer.ComputeResidenceTime(this, profile, mps, terrain, ref values, cache);
			if (convertToGraphableData)
			{
				ReplaceNoDataWithValue(values, 0f);
			}
		}
		else if (mapTypes == MapTypes.ShearStress)
		{
			ShearRenderer.Compute(this, profile, mps, terrain, ref values, cache);
			if (convertToGraphableData)
			{
				ReplaceNoDataWithValue(values, 0f);
			}
		}
		else if (mapTypes == MapTypes.StreamPower)
		{
			StreamPowerRenderer.Compute(this, profile, mps, terrain, ref values, cache);
			if (convertToGraphableData)
			{
				ReplaceNoDataWithValue(values, 0f);
			}
		}
		else if (mapTypes == MapTypes.VelocityRange)
		{
			VelocityMC.Compute(this, profile, mps, terrain, ref values, cache);
			if (convertToGraphableData)
			{
				ReplaceNoDataWithValue(values, 0f);
			}
		}
		else if (mapTypes == MapTypes.FractionInundated)
		{
			FractionInundatedRenderer.Compute(this, mps, terrain, arrivalStartingSimulationTime, (float)depthThreshold, ref values, cache);
			if (convertToGraphableData)
			{
				ReplaceNoDataWithValue(values, 0f);
			}
		}
		else if (mapTypes == MapTypes.DepthRange)
		{
			DepthMC.ComputeDepthRange(this, profile, mps, terrain, ref values, cache);
			if (convertToGraphableData)
			{
				ReplaceNoDataWithValue(values, 0f);
			}
		}
		else if (!(mapTypes == MapTypes.ArrivalRange))
		{
			if (!(mapType is Generic2DMapType))
			{
				return false;
			}
			Generic2DMapType generic2DMapType = (Generic2DMapType)mapType;
			string hDF5Dataset = generic2DMapType.HDF5Dataset;
			float[] outDepths = null;
			float[] outVelocityY = null;
			AuxillaryRenderer.Compute(map, hDF5Dataset, profile, mps, terrain, ref values, ref outDepths, ref outVelocityY, cache);
			if (convertToGraphableData)
			{
				ReplaceNoDataWithValue(values, 0f);
			}
		}
		stopwatch.Stop();
		return true;
	}

	public bool ComputeSwitchTimeSeries(RASResultsMap map, RASGeometryMapPoints mps, float[] terrain, CacheCollection cache, ref float[][] pointTimeSeries, bool convertToGraphableData, bool UsePopupProgressIfComputeNeeded = false)
	{
		H5Reader.SetLogging(log: false);
		MapTypes mapType = map.MapType;
		MapTypes mapTypes = mapType;
		checked
		{
			if (mapTypes == MapTypes.Elevation)
			{
				WaterSurfaceTimeSeries.Compute(this, mps, ref pointTimeSeries, cache, terrain);
				if (convertToGraphableData && !terrain.IsNullOrEmpty())
				{
					ConditionalReplaceWithTerrain(pointTimeSeries, terrain);
				}
			}
			else if (mapTypes == MapTypes.Depth)
			{
				DepthTimeSeries.Compute(this, mps, terrain, ref pointTimeSeries, cache);
				if (convertToGraphableData)
				{
					ConditionalReplaceWithValue(pointTimeSeries, 0f);
				}
			}
			else if (mapTypes == MapTypes.Velocity)
			{
				float[][] outVelocityX = null;
				float[][] outVelocityY = null;
				float[][] outDepths = null;
				VelocityTimeSeries.Compute(this, mps, terrain, ref pointTimeSeries, ref outVelocityX, ref outVelocityY, cache, ref outDepths);
				if (convertToGraphableData)
				{
					ConditionalReplaceWithValue(pointTimeSeries, 0f);
				}
			}
			else if (mapTypes == MapTypes.Courant)
			{
				CourantTimeSeries.Compute(this, mps, terrain, ref pointTimeSeries, cache);
				if (convertToGraphableData)
				{
					ConditionalReplaceWithValue(pointTimeSeries, 0f);
				}
			}
			else
			{
				if (mapTypes == MapTypes.DepthTimesVelocity)
				{
					return false;
				}
				if (mapTypes == MapTypes.DepthTimesVelocitySquared)
				{
					return false;
				}
				if (mapTypes == MapTypes.Flow)
				{
					return false;
				}
				int profileCount = map.OutputBlock.ProfileCount;
				if ((object)mapType.GetType() != typeof(Generic2DMapType))
				{
					return false;
				}
				Generic2DMapType generic2DMapType = (Generic2DMapType)mapType;
				pointTimeSeries = new float[mps.InputCount - 1 + 1][];
				int count = mps.InputCount - 1;
				for (int i = 0; i <= count; i++)
				{
					float[] pointTimeSeriesArray = new float[profileCount - 1 + 1];
					pointTimeSeries[i] = pointTimeSeriesArray;
				}
				AuxillaryRenderer auxillaryRenderer = new AuxillaryRenderer(map, generic2DMapType.HDF5Dataset, cache);
				int count2 = profileCount - 1;
				for (int j = 0; j <= count2; j++)
				{
					float[] values = null;
					int profile = j;
					float[] vx = null;
					float[] vy = null;
					auxillaryRenderer.ComputeVariable(profile, mps, terrain, ref values, ref vx, ref vy);
					int count3 = values.Count() - 1;
					for (int k = 0; k <= count3; k++)
					{
						pointTimeSeries[k][j] = values[k];
					}
				}
				if (convertToGraphableData)
				{
					ConditionalReplaceWithValue(pointTimeSeries, 0f);
				}
			}
			return true;
		}
	}

	public bool CheckDatasetExists(string datasetName)
	{
		using H5Reader h5Reader = new H5Reader(SourceFilename);
		if (h5Reader.ExistsAsDataset(datasetName))
		{
			return true;
		}
		return false;
	}

	private static void H5ReadStringColumn(H5Reader hr, string ds, ref string[] col)
	{
		checked
		{
			switch (hr.GetDatasetNDims(ds))
			{
			case 1:
			{
				col = new string[1];
				hr.ReadDataset(ds, ref col);
				int count = col.Count() - 1;
				for (int j = 0; j <= count; j++)
				{
					col[j] = col[j].Trim();
				}
				break;
			}
			case 2:
			{
				string[,] values = new string[1, 1];
				hr.ReadDataset(ds, ref values);
				col = new string[values.GetUpperBound(1) + 1];
				int upperBound = values.GetUpperBound(1);
				for (int i = 0; i <= upperBound; i++)
				{
					col[i] = values[0, i].Trim();
				}
				break;
			}
			}
		}
	}

	public string ReadStringAttribute(string groupOrDatasetName, string attributeName)
	{
		using (H5Reader h5Reader = new H5Reader(SourceFilename))
		{
			if ((h5Reader.ExistsAsDataset(groupOrDatasetName) || h5Reader.ExistsAsGroup(groupOrDatasetName)) && h5Reader.GetAttributeNames(groupOrDatasetName).Contains(attributeName))
			{
				string textValue = "";
				h5Reader.ReadAttributeScalar(groupOrDatasetName, attributeName, ref textValue);
				return textValue;
			}
		}
		return "";
	}

	public string GetProjectionString()
	{
		string groupOrDatasetName = "/";
		string attributeName = "Projection";
		return ReadStringAttribute(groupOrDatasetName, attributeName);
	}

	public Projection GetProjection()
	{
		string projectionString = GetProjectionString();
		Projection result;
		if (string.IsNullOrEmpty(projectionString))
		{
			result = null;
		}
		else
		{
			try
			{
				result = new WKTProjection(projectionString);
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				result = null;
				ProjectData.ClearProjectError();
			}
		}
		return result;
	}

	public bool TryGetMinMaxWS(ref float min, ref float max)
	{
		if (!File.Exists(SourceFilename))
		{
			return false;
		}
		double min2 = min;
		double max2 = max;
		if (TryGetGlobalMinMaxWSEL(ref min2, ref max2))
		{
			min = (float)min2;
			max = (float)max2;
			if (float.IsNaN(min) || float.IsNaN(max) || min > max)
			{
				return false;
			}
			return true;
		}
		return false;
	}

	public bool MinMaxDatasetExists(RASResultsMap map, string dset)
	{
		string attributeName = "Minimum Value of Data Set";
		string attributeName2 = "Maximum Value of Data Set";
		string absolutePath = map.OutputBlock.Get2DDatasetPath(map.Results.HydraulicComputeMethod, Geometry.D2FlowArea.GetFeatureName(0), dset);
		using H5Reader h5Reader = new H5Reader(SourceFilename);
		return h5Reader.ExistsAsDataset(absolutePath) && h5Reader.ExistsAsAttribute(absolutePath, attributeName) && h5Reader.ExistsAsAttribute(absolutePath, attributeName2);
	}

	public bool TryGetMinMax(RASResultsMap map, string dset, ref float min, ref float max)
	{
		if (!File.Exists(SourceFilename))
		{
			return false;
		}
		string textValue = "Minimum Value of Data Set";
		string textValue2 = "Maximum Value of Data Set";
		if (Geometry == null)
		{
			return false;
		}
		string get2DDatasetPath = map.OutputBlock.Get2DDatasetPath(map.Results.HydraulicComputeMethod, Geometry.D2FlowArea.GetFeatureName(0), dset);
		using (H5Reader h5Reader = new H5Reader(SourceFilename))
		{
			if (h5Reader.ExistsAsDataset(get2DDatasetPath))
			{
				List<string> attributeNames = h5Reader.GetAttributeNames(get2DDatasetPath);
				if (attributeNames.Contains(textValue) && attributeNames.Contains(textValue2))
				{
					h5Reader.ReadAttributeScalar(get2DDatasetPath, textValue, ref min);
					h5Reader.ReadAttributeScalar(get2DDatasetPath, textValue2, ref max);
					if (float.IsNaN(min) || float.IsNaN(max) || min > max)
					{
						return false;
					}
					return true;
				}
			}
		}
		return false;
	}

	public static bool ComputeCompleted(string hdfFilename)
	{
		if (!File.Exists(hdfFilename))
		{
			return false;
		}
		using H5Reader hr = new H5Reader(hdfFilename);
		return ComputeCompleted(hr);
	}

	public static bool ComputeCompleted(H5Reader hr)
	{
		if (hr.ExistsAsGroup("Results/Unsteady/Summary"))
		{
			List<string> attributeNames = hr.GetAttributeNames("Results/Unsteady/Summary");
			string textValue = "Solution";
			if (attributeNames.Contains(textValue))
			{
				string textValue2 = "";
				hr.ReadAttributeScalar("Results/Unsteady/Summary", textValue, ref textValue2);
				return Operators.CompareString(textValue2.Trim(), "Unsteady Finished Successfully", TextCompare: false) == 0;
			}
		}
		else if (hr.ExistsAsGroup("Results/Steady/Summary"))
		{
			List<string> attributeNames2 = hr.GetAttributeNames("Results/Steady/Summary");
			string textValue3 = "Solution";
			if (attributeNames2.Contains(textValue3))
			{
				string textValue4 = "";
				hr.ReadAttributeScalar("Results/Steady/Summary", textValue3, ref textValue4);
				return Operators.CompareString(textValue4.Trim(), "Steady Finished Successfully", TextCompare: false) == 0;
			}
		}
		return false;
	}

	public static void ComputeMessagesRTF(string hdfFilename, ref string rtfText, ref string rtfError)
	{
		if (File.Exists(hdfFilename))
		{
			try
			{
				using H5Reader h5Reader = new H5Reader(hdfFilename);
				string path = "Results/Summary/Compute Messages (rtf)";
				if (h5Reader.ExistsAsDataset(path))
				{
					string[] values = new string[1];
					h5Reader.ReadDataset(path, ref values);
					rtfText = values[0];
				}
				else
				{
					rtfError = "Compute Messages do not exist in the results file: '" + hdfFilename + "'";
				}
				return;
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				rtfError = "Error loading compute messages from results file: '" + hdfFilename + "'";
				ProjectData.ClearProjectError();
				return;
			}
		}
		rtfError = "Results file: '" + hdfFilename + "' does not exist";
	}

	public static string PlanShortID(string hdfFilename)
	{
		string textValue = "";
		try
		{
			if (File.Exists(hdfFilename) && H5Reader.IsValid(hdfFilename))
			{
				using (H5Reader h5Reader = new H5Reader(hdfFilename))
				{
					if (h5Reader.ExistsAsGroup("Plan Data/Plan Information"))
					{
						textValue = h5Reader.TryReadStringAttributeScalar("Plan Data/Plan Information", "Plan ShortID");
					}
				}
				textValue = RasMapperLib.Utilities.Path.EnsureFileCharacters(textValue);
			}
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			ProjectData.ClearProjectError();
		}
		return textValue;
	}

	public static PlanAttributes ReadPlanAttributes(string filename)
	{
		try
		{
			if (File.Exists(filename) && H5Reader.IsValid(filename))
			{
				using (H5Reader hr = new H5Reader(filename))
				{
					return ReadPlanAttributes(hr);
				}
			}
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			ProjectData.ClearProjectError();
		}
		return new PlanAttributes();
	}

	public static PlanAttributes ReadPlanAttributes(H5Reader hr)
	{
		PlanAttributes planAttributes = new PlanAttributes();
		try
		{
			string path = "Plan Data/Plan Information";
			if (!hr.ExistsAsGroup(path))
			{
				return planAttributes;
			}
			if (hr.ExistsAsAttribute(path, "Plan Title"))
			{
				planAttributes.PlanTitle = hr.TryReadStringAttributeScalar(path, "Plan Title");
			}
			else if (hr.ExistsAsAttribute(path, "Plan Name"))
			{
				planAttributes.PlanTitle = hr.TryReadStringAttributeScalar(path, "Plan Name");
			}
			if (hr.ExistsAsAttribute(path, "Plan Filename"))
			{
				planAttributes.PlanFilename = hr.TryReadStringAttributeScalar(path, "Plan Filename");
			}
			else if (hr.ExistsAsAttribute(path, "Plan File"))
			{
				planAttributes.PlanFilename = hr.TryReadStringAttributeScalar(path, "Plan File");
			}
			planAttributes.PlanShortID = hr.TryReadStringAttributeScalar(path, "Plan ShortID");
			planAttributes.GeometryFilename = hr.TryReadStringAttributeScalar(path, "Geometry Filename");
			planAttributes.GeometryTitle = hr.TryReadStringAttributeScalar(path, "Geometry Title");
			planAttributes.FlowFilename = hr.TryReadStringAttributeScalar(path, "Flow Filename");
			planAttributes.FlowTitle = hr.TryReadStringAttributeScalar(path, "Flow Title");
			planAttributes.StartTimeString = hr.TryReadStringAttributeScalar(path, "Simulation Start Time");
			planAttributes.EndTimeString = hr.TryReadStringAttributeScalar(path, "Simulation End Time");
			if (!string.IsNullOrEmpty(planAttributes.StartTimeString))
			{
				planAttributes.StartTime = DSS.ConvertFromDSSDateTime(planAttributes.StartTimeString);
			}
			if (!string.IsNullOrEmpty(planAttributes.EndTimeString))
			{
				planAttributes.EndTime = DSS.ConvertFromDSSDateTime(planAttributes.EndTimeString);
			}
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			ProjectData.ClearProjectError();
		}
		return planAttributes;
	}

	public int ProfileIndex(string name)
	{
		if (Operators.CompareString(name, "Max", TextCompare: false) == 0)
		{
			return int.MaxValue;
		}
		if (Operators.CompareString(name, "Min", TextCompare: false) == 0)
		{
			return int.MinValue;
		}
		string[] profileNames = ProfileNames;
		checked
		{
			if (profileNames != null)
			{
				int count = profileNames.Count() - 1;
				for (int i = 0; i <= count; i++)
				{
					if (Operators.CompareString(name, profileNames[i], TextCompare: false) == 0)
					{
						return i;
					}
				}
			}
			return -1;
		}
	}

	public string ProfileName(int index)
	{
		checked
		{
			switch (index)
			{
			case int.MaxValue:
				return "Max";
			case int.MinValue:
				return "Min";
			default:
			{
				int count = ProfileCount - 1;
				for (int i = 0; i <= count; i++)
				{
					if (index == i)
					{
						return ProfileNames[i];
					}
				}
				return "";
			}
			}
		}
	}

	public int SelectBestProfileIndex(int indexFromXML, string nameFromXML)
	{
		checked
		{
			int result;
			if (ProfileNames == null)
			{
				result = indexFromXML;
			}
			else if (SteadyHydraulics)
			{
				result = IntegerExtensionMethods.Bound(indexFromXML, 0, ProfileNames.Count() - 1);
			}
			else if (UnsteadyHydraulics)
			{
				switch (indexFromXML)
				{
				case int.MaxValue:
					result = indexFromXML;
					break;
				case int.MinValue:
					result = indexFromXML;
					break;
				default:
					if (string.IsNullOrEmpty(nameFromXML))
					{
						result = IntegerExtensionMethods.Bound(indexFromXML, 0, ProfileNames.Count() - 1);
						break;
					}
					try
					{
						result = ((ProfileCount != 0 && ProfileNames.First() != null) ? DSS.NearestDSSDateIndex(ProfileNames, nameFromXML) : int.MaxValue);
					}
					catch (Exception ex)
					{
						ProjectData.SetProjectError(ex);
						Exception ex2 = ex;
						result = IntegerExtensionMethods.Bound(indexFromXML, 0, ProfileNames.Count() - 1);
						ProjectData.ClearProjectError();
					}
					break;
				}
			}
			else
			{
				result = IntegerExtensionMethods.Bound(indexFromXML, 0, ProfileNames.Count() - 1);
			}
			return result;
		}
	}

	public int ClosestProfileIndex(string profileName)
	{
		return _outputBlocks.BaseOutput?.ClosestProfileIndex(profileName) ?? (-1);
	}

	public void ReadProfileData(string datasetName, int row, ref float[,] values)
	{
		using H5Reader h5Reader = new H5Reader(SourceFilename);
		if (h5Reader.ExistsAsDataset(datasetName))
		{
			h5Reader.ReadRow(datasetName, row, ref values);
		}
	}

	public string GrainClassName(int gc)
	{
		return _sedimentGrainClasses[gc];
	}

	public int GrainClassIndex(string gc_called)
	{
		int ordinal = 0;
		do
		{
			if (_sedimentGrainClasses[ordinal].Equals(gc_called))
			{
				return ordinal;
			}
			ordinal = checked(ordinal + 1);
		}
		while (ordinal <= 19);
		return -1;
	}

	public void ReadSedXS(string datasetName, int XSIndex, string profile, ref float[,] values)
	{
		int[,] values2 = new int[1, 1];
		using H5Reader h5Reader = new H5Reader(SourceFilename);
		datasetName = datasetName + "/Station Elevation (" + profile + ")";
		if (!h5Reader.ExistsAsDataset(datasetName + " info"))
		{
			throw new ApplicationException("No Data Set By This name: " + datasetName);
		}
		h5Reader.ReadRow(datasetName + " info", XSIndex, ref values2);
		h5Reader.ReadRows(datasetName + " values", values2[0, 0], values2[0, 1], ref values);
	}

	public List<RASResultsMap> AnimatableMaps(bool includeUncheckedNodes)
	{
		List<RASResultsMap> rasResultsMaps = new List<RASResultsMap>();
		RecurseNodesForAnimateableMaps(Nodes, includeUncheckedNodes, rasResultsMaps);
		return rasResultsMaps;
	}

	private void RecurseNodesForAnimateableMaps(TreeNodeCollection nodes, bool includeUncheckedNodes, List<RASResultsMap> maps)
	{
		foreach (TreeNode node in nodes)
		{
			if (!includeUncheckedNodes && !node.Checked)
			{
				continue;
			}
			if (node is RASResultsMap)
			{
				RASResultsMap rASResultsMap = (RASResultsMap)node;
				if (rASResultsMap.OutputModeAnimateable)
				{
					maps.Add(rASResultsMap);
				}
			}
			if (includeUncheckedNodes || node.Checked)
			{
				RecurseNodesForAnimateableMaps(node.Nodes, includeUncheckedNodes, maps);
			}
		}
	}

	public List<RASResultsMap> FindAllMaps()
	{
		List<RASResultsMap> rasResultsMaps = new List<RASResultsMap>();
		FindMaps(Nodes, rasResultsMaps);
		return rasResultsMaps;
	}

	private void FindMaps(TreeNodeCollection treeNodes, List<RASResultsMap> maps)
	{
		foreach (TreeNode treeNode in treeNodes)
		{
			if (treeNode is RASResultsMap)
			{
				maps.Add((RASResultsMap)treeNode);
			}
			if (treeNode.Nodes.Count > 0)
			{
				FindMaps(treeNode.Nodes, maps);
			}
		}
	}

	public List<MapTypes> AvailableHydraulicsMapTypes()
	{
		new List<string>();
		if (UnsteadyHydraulics)
		{
			return MapTypes.UnsteadyTypes;
		}
		if (SteadyHydraulics)
		{
			return MapTypes.SteadyTypes;
		}
		return new List<MapTypes>();
	}

	public List<MapTypes> AllAvailableMapTypes()
	{
		new List<string>();
		if (UnsteadyHydraulics)
		{
			List<MapTypes> unsteadyTypes = new List<MapTypes>(MapTypes.UnsteadyTypes);
			if (Geometry.D2FlowArea.FeatureCount() > 0)
			{
				_Closure_0024__193_002D0 capturedLocals = new _Closure_0024__193_002D0(null);
				capturedLocals.hr = new H5Reader(SourceFilename);
				try
				{
					_Closure_0024__193_002D1 capturedLocals2 = default;
					_Closure_0024__193_002D1 capturedLocals3 = new _Closure_0024__193_002D1(capturedLocals2);
					capturedLocals3.outerClosure = capturedLocals;
					capturedLocals3.meshName = Geometry.D2FlowArea.GetFeatureName(0);
					List<string> stringList = (from b in OutputBlocks.Blocks
						select b.Key into b
						where Generic2DMapParameters.HasGenericDatasets(capturedLocals3.outerClosure.hr, b, capturedLocals3.meshName)
						select b).ToList();
					foreach (string textValue in stringList)
					{
						switch (textValue)
						{
						case "Base Output":
						case "Sediment Bed":
						case "Sediment Transport":
						{
							Dictionary<string, List<Generic2DMapParameters>> availableGenericDatasets = Generic2DMapParameters.AvailableGenericDatasets(capturedLocals3.outerClosure.hr, textValue, capturedLocals3.meshName);
							foreach (string key in availableGenericDatasets.Keys)
							{
								IOrderedEnumerable<Generic2DMapParameters> orderedEnumerable = availableGenericDatasets[key].OrderBy([SpecialName] (Generic2DMapParameters g2m) => g2m.Index);
								foreach (Generic2DMapParameters orderedEnumerable2 in orderedEnumerable)
								{
									if (!orderedEnumerable2.CanPlot || (Operators.CompareString(orderedEnumerable2.Name, "Water Surface", TextCompare: false) == 0 && orderedEnumerable2.Location == Location.Cell) || (Operators.CompareString(orderedEnumerable2.Name, "Velocity", TextCompare: false) == 0 && orderedEnumerable2.Location == Location.Face))
									{
										continue;
									}
									if ((orderedEnumerable2.Location == Location.SubCell || orderedEnumerable2.Location == Location.SubFace) && orderedEnumerable2.Name.Contains("Bed Change"))
									{
										Generic2DMapParameters generic2DMapParameters = new Generic2DMapParameters(orderedEnumerable2);
										generic2DMapParameters.Name = "Bed Elevation";
										bool orderedEnumerable2Term = orderedEnumerable2.Name.Contains("Hydraulic");
										string textValue2 = "";
										if (orderedEnumerable2Term)
										{
											textValue2 = "Hydraulic ";
										}
										generic2DMapParameters.HDFDataset = orderedEnumerable2.Location.ToString() + " " + textValue2 + "Bed Elevation";
										unsteadyTypes.Add(new Generic2DMapType(textValue, generic2DMapParameters.HDFDataset, generic2DMapParameters.Name, generic2DMapParameters.HasMinMax, generic2DMapParameters.Units));
									}
									unsteadyTypes.Add(new Generic2DMapType(textValue, orderedEnumerable2.HDFDataset, orderedEnumerable2.Name, orderedEnumerable2.HasMinMax, orderedEnumerable2.Units));
								}
							}
							break;
						}
						}
					}
				}
				finally
				{
					if (capturedLocals.hr != null)
					{
						((IDisposable)capturedLocals.hr).Dispose();
					}
				}
			}
			return unsteadyTypes;
		}
		if (SteadyHydraulics)
		{
			return MapTypes.SteadyTypes;
		}
		return new List<MapTypes>();
	}

	public float GetOutputTimestepInSeconds(string outputBlock = "")
	{
		if (!string.IsNullOrEmpty(outputBlock))
		{
			switch (outputBlock)
			{
			case "Base Output":
				break;
			case "DSS Hydrograph Output":
				return Geometry.Results.DssHydrographDTSeconds;
			case "DSS Profile Output":
				return Geometry.Results.DssProfileDTSeconds;
			default:
				return 0f;
			}
		}
		return Geometry.Results.MappingDTSeconds;
	}
}
