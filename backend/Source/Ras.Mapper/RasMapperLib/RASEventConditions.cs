using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Utilities;
using Utility.Extensions;
using Utility.Progress;
using Utility.Systems;

namespace RasMapperLib;

public class RASEventConditions : Layer, IStreamableObject
{
	public class MetTypeNames
	{
		public const string WindVx = "Wind Velocity X";

		public const string WindVy = "Wind Velocity Y";

		public const string WindSpeed = "Wind Speed";

		public const string WindDirection = "Wind Direction";

		public const string AirDensity = "Air Density";

		public const string AirPressure = "Air Pressure";

		public const string AirTemperature = "Air Temperature";

		public const string Evapotranspiration = "Evapotranspiration";

		public const string Humidity = "Humidity";

		public const string Precipitation = "Precipitation";
	}

	public const string HDFGroupName = "/Event Conditions";

	public const string MetGroupName = "/Event Conditions/Meteorology";

	public const string DefaultTimeFormat = "ddMMMyyyy HH:mm:ss.fff";

	public const string sEventConditions = "Event Conditions";

	public const string sEventCompletedAttribute = "Completed Successfully";

	public const string sEventProcessedTimestamp = "Date Processed";

	public const string sModeAttribute = "Mode";

	public const string sProjectionOverrideAttribute = "Projection Override";

	public RASResults Results;

	public IWindLayer WindLayer;

	public IPrecipitationLayer PrecipLayer;

	public IMeteorologyLayer EvapoLayer;

	public IMeteorologyLayer AirDensityLayer;

	public ObservedDataLayer ObservedData;

	public MeteorologicalStationLayer MetStations;

	public AccumulatedPrecipitationLayer AccumulatedPrecipLayer;

	public SharedData.PlanStructure PlanStructure;

	private bool _hasLoaded;

	public override bool CanRename { get; }

	public string FlowExtension => System.IO.Path.GetExtension(System.IO.Path.GetFileNameWithoutExtension(SourceFilename));

	public bool IsResult => Results != null;

	public DateTime StartTime
	{
		get
		{
			if (PlanStructure == null)
			{
				return DateTime.MinValue;
			}
			return PlanStructure.StartDate;
		}
	}

	public DateTime EndTime
	{
		get
		{
			if (PlanStructure == null)
			{
				return DateTime.MaxValue;
			}
			return PlanStructure.EndDate;
		}
	}

	public static string GetImportedMetRasterDatasetName(string metType)
	{
		return GetMetFolderName(metType) + "/Imported Raster Data/Values";
	}

	public static string GetMetFolderName(string metType)
	{
		return "/Event Conditions/Meteorology/" + metType;
	}

	public RASEventConditions()
	{
		CanRename = false;
		_hasLoaded = false;
	}

	public RASEventConditions(string fn)
		: base("Event Conditions", fn)
	{
		CanRename = false;
		_hasLoaded = false;
		InitializeMandatoryChildren();
	}

	public RASEventConditions(RASResults res)
		: base("Event Conditions", res.SourceFilename)
	{
		CanRename = false;
		_hasLoaded = false;
		Results = res;
		InitializeMandatoryChildren();
	}

	private void InitializeMandatoryChildren()
	{
		if (_hasLoaded)
		{
			return;
		}
		EnsureDataLoaded();
		if (WindLayer != null)
		{
			Nodes.Add(WindLayer as Layer);
		}
		if (PrecipLayer != null)
		{
			Nodes.Add(PrecipLayer as Layer);
			if (PrecipLayer != null)
			{
				AccumulatedPrecipLayer = new AccumulatedPrecipitationLayer(PrecipLayer);
				Nodes.Add(AccumulatedPrecipLayer);
			}
		}
		if (EvapoLayer != null)
		{
			Nodes.Add(EvapoLayer as Layer);
		}
		if (AirDensityLayer != null)
		{
			Nodes.Add(AirDensityLayer as Layer);
		}
	}

	public void ChangeActivePlanForVisualization(SharedData.PlanStructure ps)
	{
		PlanStructure = ps;
		PrecipLayer?.AlertTimeWindowChanged(ps.StartDate, ps.EndDate);
	}

	protected override void XMLLoadChildren(XmlElement xmlEl)
	{
		InitializeMandatoryChildren();
		if (xmlEl == null)
		{
			return;
		}
		XmlNodeList subLayerList = Layer.GetSubLayerList(xmlEl);
		foreach (XmlElement subLayerList2 in subLayerList)
		{
			try
			{
				XMLLoadChild(subLayerList2);
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				ProjectData.ClearProjectError();
			}
		}
	}

	protected override bool XMLLoadChild(XmlElement xmlEl)
	{
		LoadXmlIntoExistingNodes(xmlEl, recurse: false);
		return true;
	}

	public bool ResultsCompleted()
	{
		if (Results == null)
		{
			return false;
		}
		return Results.HasBeenRun();
	}

	public void EnsureDataLoaded()
	{
		if (!_hasLoaded)
		{
			LoadData(selectPlanTimeWindow: true);
		}
		_hasLoaded = true;
	}

	private bool LoadData(bool selectPlanTimeWindow, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		bool result;
		if (!File.Exists(SourceFilename))
		{
			result = false;
		}
		else
		{
			try
			{
				using H5Reader h5Reader = new H5Reader(SourceFilename);
				if (selectPlanTimeWindow && !IsResult)
				{
					List<SharedData.PlanStructure> associatedPlans = GetAssociatedPlans();
					if (associatedPlans.IsNullOrEmpty())
					{
						PlanStructure = null;
					}
					else
					{
						PlanStructure = associatedPlans.FirstOrDefault([SpecialName] (SharedData.PlanStructure ps) => ps.IsActivePlan);
						if (PlanStructure == null)
						{
							PlanStructure = associatedPlans.First();
						}
					}
				}
				MetStations = MeteorologicalStationLayer.TryParseMetLayer(h5Reader);
				if (!WindLayerNew.TryParseWindLayer(h5Reader, Results, MetStations, ref WindLayer, prog))
				{
					result = false;
				}
				else
				{
					if (h5Reader.ExistsAsGroup("/Event Conditions/Observed Data"))
					{
						ObservedData = new ObservedDataLayer(this);
					}
					MetParser.TryParseGenericMetLayer(h5Reader, Results, this, "Evapotranspiration", ref EvapoLayer, prog);
					MetParser.TryParseGenericMetLayer(h5Reader, Results, this, "Air Density", ref AirDensityLayer, prog);
					result = true;
				}
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				prog.ReportError("Unknown error: " + ex2.Message);
				result = false;
				ProjectData.ClearProjectError();
			}
		}
		return result;
	}

	private List<SharedData.PlanStructure> GetAssociatedPlans()
	{
		return SharedData.PlanStructures.Where([SpecialName] (SharedData.PlanStructure ps) => ps.FlowExtension.RoughlyEquals(FlowExtension)).ToList();
	}

	public bool CompleteForComputations(ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		if (Results == null)
		{
			prog.ReportError("Result data not loaded " + SourceFilename.InDoubleQuotes());
			return false;
		}
		ChangeActivePlanForVisualization(new SharedData.PlanStructure(Results));
		if (!LoadData(selectPlanTimeWindow: false, prog))
		{
			return false;
		}
		if (WindLayer != null)
		{
			prog.ReportMessage("Processing Wind data...");
			Stopwatch stopwatch = Stopwatch.StartNew();
			if (!WindLayer.RasterizeSaveData(SourceFilename, "Event Conditions/Meteorology/Wind", Results, prog))
			{
				prog.ReportMessage("Error processing Wind data, exiting...");
				WriteMetaAttributes(complete: false);
				return false;
			}
			stopwatch.Stop();
			prog.ReportMessage("Finished Processing Wind data " + (System.Math.Round((float)stopwatch.ElapsedMilliseconds / 1000f, 3) + "s").InParentheses());
		}
		else
		{
			if (SharedData.DebugMode)
			{
				prog.ReportMessage("DEBUG: No Wind data found to process.");
			}
			using H5Writer h5w = new H5Writer(SourceFilename);
			RasMapperLib.WindLayer.EnsureClean(h5w);
		}
		if (PrecipLayer != null)
		{
			prog.ReportMessage("Processing Precipitation data...");
			prog.ReportMessage("   (assumes geometry data is geo-referenced)");
			Stopwatch stopwatch2 = Stopwatch.StartNew();
			stopwatch2.Stop();
			prog.ReportMessage("Finished Processing Precipitation data " + (System.Math.Round((float)stopwatch2.ElapsedMilliseconds / 1000f, 3) + "s").InParentheses());
		}
		else if (SharedData.DebugMode)
		{
			prog.ReportMessage("DEBUG: No Precip data found to process.");
		}
		if (EvapoLayer != null)
		{
			prog.ReportMessage("Processing Evapotranspiration data...");
			prog.ReportMessage("   (assumes geometry data is geo-referenced)");
			if (!EvapoLayer.RasterizeSaveData(SourceFilename, GetMetFolderName("Evapotranspiration"), Results, prog))
			{
				prog.ReportMessage("Error processing Evapotranspiration data, exiting...");
				WriteMetaAttributes(complete: false);
				return false;
			}
		}
		else
		{
			using H5Writer h5Writer = new H5Writer(SourceFilename);
			string metFolderName = GetMetFolderName("Evapotranspiration");
			if (h5Writer.ExistsAsGroup(metFolderName))
			{
				h5Writer.Delete(metFolderName);
			}
		}
		if (AirDensityLayer != null)
		{
			prog.ReportMessage("Processing Air Density data...");
			if (!AirDensityLayer.RasterizeSaveData(SourceFilename, GetMetFolderName("Air Density"), Results, prog))
			{
				prog.ReportMessage("Error processing Air Density data, exiting...");
				WriteMetaAttributes(complete: false);
				return false;
			}
		}
		else
		{
			using H5Writer h5Writer2 = new H5Writer(SourceFilename);
			string metFolderName2 = GetMetFolderName("Air Density");
			if (h5Writer2.ExistsAsGroup(metFolderName2))
			{
				h5Writer2.Delete(metFolderName2);
			}
		}
		using (H5Writer h5Writer3 = new H5Writer(SourceFilename))
		{
			string metFolderName3 = GetMetFolderName("Air Pressure");
			if (h5Writer3.ExistsAsGroup(metFolderName3))
			{
				h5Writer3.Delete(metFolderName3);
			}
			metFolderName3 = GetMetFolderName("Air Temperature");
			if (h5Writer3.ExistsAsGroup(metFolderName3))
			{
				h5Writer3.Delete(metFolderName3);
			}
			metFolderName3 = GetMetFolderName("Humidity");
			if (h5Writer3.ExistsAsGroup(metFolderName3))
			{
				h5Writer3.Delete(metFolderName3);
			}
		}
		if (ObservedData != null)
		{
			prog.ReportMessage("Processing Observed data...");
			if (!ObservedData.PrepareForComputations(prog))
			{
				prog.ReportMessage("Error processing Observed data, exiting...");
				WriteMetaAttributes(complete: false);
				return false;
			}
		}
		else if (SharedData.DebugMode)
		{
			prog.ReportMessage("DEBUG: No Observed data found to process.");
		}
		string path = "Event Conditions/Unsteady/Initial Conditions/";
		checked
		{
			using (H5Writer h5Writer4 = new H5Writer(Results.SourceFilename))
			{
				bool condition = true;
				if (!h5Writer4.ExistsAsGroup(path))
				{
					condition = false;
				}
				else if (!h5Writer4.ExistsAsAttribute(path, "Prior WS Filename") || !h5Writer4.ExistsAsAttribute(path, "Prior WS Profile") || !h5Writer4.ExistsAsAttribute(path, "Startup Mode"))
				{
					condition = false;
				}
				else if (Operators.CompareString(h5Writer4.TryReadStringAttributeScalar(path, "Startup Mode"), "Prior WS", TextCompare: false) != 0)
				{
					condition = false;
				}
				if (condition)
				{
					string profileName = h5Writer4.TryReadStringAttributeScalar(path, "Prior WS Profile");
					string tryReadStringAttributeScalar = h5Writer4.TryReadStringAttributeScalar(path, "Prior WS Filename");
					if (!System.IO.Path.IsPathRooted(tryReadStringAttributeScalar))
					{
						tryReadStringAttributeScalar = System.IO.Path.GetDirectoryName(Results.SourceFilename) + "\\" + tryReadStringAttributeScalar;
					}
					RASResults rASResults = new RASResults(tryReadStringAttributeScalar);
					int pfIdx = rASResults.ClosestProfileIndex(profileName);
					InitialConditionState initialConditionState = InitialConditionState.ComputeFromExistingResult(rASResults, pfIdx, Results.Geometry);
					if (initialConditionState == null)
					{
						prog.ReportMessage("Error Setting Flow Distribution From Results, exiting...");
						WriteMetaAttributes(complete: false);
						return false;
					}
					initialConditionState.Save(h5Writer4, path);
				}
				if (!condition && h5Writer4.ExistsAsGroup(path))
				{
					string left = h5Writer4.TryReadStringAttributeScalar(path, "Startup Mode");
					if (Operators.CompareString(left, "Computed", TextCompare: false) == 0)
					{
						string pathTerm = path + "/IC Point Names";
						string pathTerm2 = path + "/IC Point Elevations";
						if (h5Writer4.ExistsAsDataset(pathTerm) & h5Writer4.ExistsAsDataset(pathTerm2))
						{
							string[] values = null;
							float[] values2 = null;
							h5Writer4.ReadDataset(pathTerm, ref values);
							h5Writer4.ReadDataset(pathTerm2, ref values2);
							RASGeometry geometry = Results.Geometry;
							RASInitialConditionsPoints initialConditionPoints = geometry.InitialConditionPoints;
							int count = geometry.D2FlowArea.FeatureCount() - 1;
							for (int i = 0; i <= count; i++)
							{
								MeshFV2D meshFV2D = geometry.D2FlowArea.Mesh(i);
								string featureName = geometry.D2FlowArea.GetFeatureName(i);
								List<MeshFV2D.PointCellElev> pointCellElevs = new List<MeshFV2D.PointCellElev>();
								int count2 = values.Count() - 1;
								for (int j = 0; j <= count2; j++)
								{
									int featureByName = initialConditionPoints.GetFeatureByName(values[j]);
									if (featureByName != -1 && Operators.CompareString(initialConditionPoints.GetSAD2Name(featureByName), featureName, TextCompare: false) == 0)
									{
										initialConditionPoints.GetCellIndex(featureByName);
										pointCellElevs.Add(new MeshFV2D.PointCellElev(initialConditionPoints.Point(featureByName).PointM(), initialConditionPoints.GetCellIndex(featureByName), values2[j]));
									}
								}
								if (pointCellElevs.Count > 0)
								{
									float[] initialElevations = meshFV2D.ComputeInitialElevations(geometry, pointCellElevs);
									if (initialElevations != null)
									{
										h5Writer4.WriteArray(path + "/2D Flow Areas/" + featureName, initialElevations);
									}
								}
							}
						}
					}
				}
			}
			WriteMetaAttributes(complete: true);
			prog.ReportMessage("LAYER=COMPLETE");
			prog.ReportProgress(100);
			return true;
		}
	}

	public static bool EventFileComplete(string hdfFilename)
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
					if (h5Reader.ExistsAsAttribute("Event Conditions", "Completed Successfully"))
					{
						h5Reader.ReadAttributeScalar("Event Conditions", "Completed Successfully", ref textValue);
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

	private void WriteMetaAttributes(bool complete)
	{
		using H5Writer h5Writer = new H5Writer(SourceFilename);
		h5Writer.WriteAttribute("Event Conditions", "Completed Successfully", General.BooleanToString(complete));
		h5Writer.WriteAttribute("Event Conditions", "Date Processed", DateTime.Now.ToString());
	}

	public void StreamOut(BinaryWriter bw)
	{
		Pipe.WriteString(bw, SourceFilename);
	}

	void IStreamableObject.StreamOut(BinaryWriter bw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in StreamOut
		this.StreamOut(bw);
	}

	public void StreamIn(BinaryReader br)
	{
		SourceFilename = Pipe.ReadString(br);
	}

	void IStreamableObject.StreamIn(BinaryReader br)
	{
		//ILSpy generated this explicit interface implementation from .override directive in StreamIn
		this.StreamIn(br);
	}
}
