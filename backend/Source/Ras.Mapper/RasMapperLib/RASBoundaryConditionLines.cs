using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using H5Assist.Identifiers;
using H5Assist.VB;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Mesh;
using RasMapperLib.Names;
using Utility.Extensions;
using Utility.Progress;

namespace RasMapperLib;

public class RASBoundaryConditionLines : PolylineFeatureLayer, IGeometryLayer, IFlowHydrograph
{
	public const string HDFGroupName = "Geometry/Boundary Condition Lines";

	public const string HDFCellListDataset = "Geometry/Boundary Condition Lines/Internal Cells";

	public const string HDFExternalFaceListDataset = "Geometry/Boundary Condition Lines/External Faces";

	public const string HDFInternalFaceListDataset = "Geometry/Boundary Condition Lines/Internal Faces";

	public const string sConnectionType = "Connection Type";

	public const string sCellList = "Cells Crossed";

	public const string sFaceList = "Faces Crossed";

	public const string sMeshPerimPolyline = "Mesh Perimeter Polyline";


	public override string SimpleFeatureTypeName { get; set; }

	public RASGeometry Geometry { get; }

	public override bool ForwardEditRequestsToParent { get; }

	public override bool CanRename { get; }

	public override string DefaultLayerName => "Boundary Condition Lines";
string IGeometryLayer.HDFGroupName=> "Geometry/Boundary Condition Lines";

	public override bool FeaturesRequireUniqueNames { get; set; }

	public RASBoundaryConditionLines(RASGeometry geometry)
		: base("Boundary Condition Lines", geometry.SourceFilename)
	{
		SimpleFeatureTypeName = "BC Line";
		ForwardEditRequestsToParent = true;
		CanRename = false;
		FeaturesRequireUniqueNames = true;
		Geometry = geometry;
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddManagedAttributeColumn("Name", "Name", typeof(string));
		AddManagedAttributeColumn("SA/2D Area", "SA-2D", typeof(string), null, DataColumnViewState.GrayedOut);
		AddManagedAttributeColumn("Connection Type", "Type", typeof(string), null, DataColumnViewState.GrayedOut);
		AddAttributeColumn("Cells Crossed", typeof(List<MeshFV2D.ProfileCellRange>), null, DataColumnViewState.Invisible);
		AddAttributeColumn("Faces Crossed", typeof(List<MeshFV2D.ProfileFaceRange>), null, DataColumnViewState.Invisible);
		AddAttributeColumn("Mesh Perimeter Polyline", typeof(Polyline), null, DataColumnViewState.Invisible);
		AddAttributeColumn("IsDefaultName", typeof(bool), null, DataColumnViewState.Invisible);
	}

	protected void HDFLoadFeatureTable(H5Reader hr)
	{
		if (hr.ExistsAsGroup("Geometry/Boundary Condition Lines"))
		{
			HDF5Storage.ReadPolyFeatures(this, hr, "Geometry/Boundary Condition Lines/Polyline");
			string path = "Geometry/Boundary Condition Lines/Attributes";
			if (hr.ExistsAsDataset(path))
			{
				ReadManagedColumns(hr, path);
				HDFLoadConnectivityMetadata(hr);
			}
		}
	}

	void IGeometryLayer.HDFLoadFeatureTable(H5Reader hr)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFLoadFeatureTable
		this.HDFLoadFeatureTable(hr);
	}

	protected void HDFSaveFeatureTable(H5Writer hw)
	{
		hw.Delete("Geometry/Boundary Condition Lines/Internal Cells");
		hw.Delete("Geometry/Boundary Condition Lines/External Faces");
		hw.Delete("Geometry/Boundary Condition Lines/Internal Faces");
		if (FeatureCount() == 0)
		{
			hw.Delete("Geometry/Boundary Condition Lines");
			return;
		}
		hw.CreateGroup("Geometry/Boundary Condition Lines");
		HDF5Storage.WritePolylineFeatures(this, hw, "Geometry/Boundary Condition Lines/Polyline");
		WriteManagedColumns(hw, "Geometry/Boundary Condition Lines/Attributes");
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
				Polyline(i).Stream(bw);
				bw.WriteStringA(GetFeatureName(i));
				bw.WriteStringA(GetSA2DName(i));
				bw.WriteStringA(GetConnectionType(i));
			}
		}
	}

	public static RASBoundaryConditionLines Stream(BinaryReader br, RASGeometry geom)
	{
		RASBoundaryConditionLines rASBoundaryConditionLines = new RASBoundaryConditionLines(geom);
		rASBoundaryConditionLines.InitializeEmptyFeatureTable();
		rASBoundaryConditionLines.SuppressTableReload(boolValue: true);
		rASBoundaryConditionLines.SuppressFeatureChanged(boolValue: true);
		checked
		{
			int count = br.ReadInt32() - 1;
			for (int i = 0; i <= count; i++)
			{
				rASBoundaryConditionLines.AddFeature(RasMapperLib.Polyline.Stream(br));
				rASBoundaryConditionLines.SetFeatureName(i, br.ReadStringA());
				rASBoundaryConditionLines.SetSA2DName(i, br.ReadStringA());
				rASBoundaryConditionLines.SetConnectionType(i, br.ReadStringA());
			}
			return rASBoundaryConditionLines;
		}
	}

	private void HDFLoadConnectivityMetadata(H5Reader hr)
	{
		checked
		{
			try
			{
				if (hr.ExistsAsDataset("Geometry/Boundary Condition Lines/Internal Cells"))
				{
					Dictionary<CompoundFieldDefinition, Array> source = hr.ReadCompoundElements("Geometry/Boundary Condition Lines/Internal Cells");
					Dictionary<int, List<MeshFV2D.ProfileCellRange>> profileCellRangeListByInt = new Dictionary<int, List<MeshFV2D.ProfileCellRange>>();
					int length = source.First().Value.Length;
					int[] intValues = (int[])source.First([SpecialName] (KeyValuePair<CompoundFieldDefinition, Array> kvp) => Operators.CompareString(kvp.Key.Name, "BC Line ID", TextCompare: false) == 0).Value;
					int[] intValuesArray = (int[])source.First([SpecialName] (KeyValuePair<CompoundFieldDefinition, Array> kvp) => Operators.CompareString(kvp.Key.Name, "Cell Index", TextCompare: false) == 0).Value;
					float[] floatValues = (float[])source.First([SpecialName] (KeyValuePair<CompoundFieldDefinition, Array> kvp) => Operators.CompareString(kvp.Key.Name, "Station Start", TextCompare: false) == 0).Value;
					float[] floatValuesArray = (float[])source.First([SpecialName] (KeyValuePair<CompoundFieldDefinition, Array> kvp) => Operators.CompareString(kvp.Key.Name, "Station End", TextCompare: false) == 0).Value;
					int count = length - 1;
					for (int intervalIndex = 0; intervalIndex <= count; intervalIndex++)
					{
						int key = intValues[intervalIndex];
						int c = intValuesArray[intervalIndex];
						float computed = floatValues[intervalIndex];
						float computed2 = floatValuesArray[intervalIndex];
						List<MeshFV2D.ProfileCellRange> profileCellRanges = null;
						if (!profileCellRangeListByInt.TryGetValue(key, out profileCellRanges))
						{
							profileCellRanges = (profileCellRangeListByInt[key] = new List<MeshFV2D.ProfileCellRange>());
						}
						profileCellRanges.Add(new MeshFV2D.ProfileCellRange(c, computed, computed2));
					}
					foreach (KeyValuePair<int, List<MeshFV2D.ProfileCellRange>> localValue in profileCellRangeListByInt)
					{
						SetCellList(localValue.Key, localValue.Value);
					}
				}
				TryLoadFaceConnectivity(hr, "Geometry/Boundary Condition Lines/External Faces");
				TryLoadFaceConnectivity(hr, "Geometry/Boundary Condition Lines/Internal Faces");
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				ProjectData.ClearProjectError();
			}
		}
	}

	private void TryLoadFaceConnectivity(H5Reader hr, string dataset)
	{
		if (!hr.ExistsAsDataset(dataset))
		{
			return;
		}
		Dictionary<CompoundFieldDefinition, Array> source = hr.ReadCompoundElements(dataset);
		Dictionary<int, List<MeshFV2D.ProfileFaceRange>> profileFaceRangeListByInt = new Dictionary<int, List<MeshFV2D.ProfileFaceRange>>();
		int length = source.First().Value.Length;
		int[] intValues = (int[])source.First([SpecialName] (KeyValuePair<CompoundFieldDefinition, Array> kvp) => Operators.CompareString(kvp.Key.Name, "BC Line ID", TextCompare: false) == 0).Value;
		int[] intValuesArray = (int[])source.First([SpecialName] (KeyValuePair<CompoundFieldDefinition, Array> kvp) => Operators.CompareString(kvp.Key.Name, "Face Index", TextCompare: false) == 0).Value;
		int[] intValues2 = (int[])source.First([SpecialName] (KeyValuePair<CompoundFieldDefinition, Array> kvp) => Operators.CompareString(kvp.Key.Name, "FP Start Index", TextCompare: false) == 0).Value;
		int[] intValues3 = (int[])source.First([SpecialName] (KeyValuePair<CompoundFieldDefinition, Array> kvp) => Operators.CompareString(kvp.Key.Name, "FP End Index", TextCompare: false) == 0).Value;
		float[] floatValues = (float[])source.First([SpecialName] (KeyValuePair<CompoundFieldDefinition, Array> kvp) => Operators.CompareString(kvp.Key.Name, "Station Start", TextCompare: false) == 0).Value;
		float[] floatValuesArray = (float[])source.First([SpecialName] (KeyValuePair<CompoundFieldDefinition, Array> kvp) => Operators.CompareString(kvp.Key.Name, "Station End", TextCompare: false) == 0).Value;
		checked
		{
			int count = length - 1;
			for (int intervalIndex = 0; intervalIndex <= count; intervalIndex++)
			{
				int key = intValues[intervalIndex];
				int f = intValuesArray[intervalIndex];
				int fpStart = intValues2[intervalIndex];
				int fpEnd = intValues3[intervalIndex];
				float computed = floatValues[intervalIndex];
				float computed2 = floatValuesArray[intervalIndex];
				List<MeshFV2D.ProfileFaceRange> profileFaceRanges = null;
				if (!profileFaceRangeListByInt.TryGetValue(key, out profileFaceRanges))
				{
					profileFaceRanges = (profileFaceRangeListByInt[key] = new List<MeshFV2D.ProfileFaceRange>());
				}
				profileFaceRanges.Add(new MeshFV2D.ProfileFaceRange(f, fpStart, fpEnd, computed, computed2));
			}
			foreach (KeyValuePair<int, List<MeshFV2D.ProfileFaceRange>> localValue in profileFaceRangeListByInt)
			{
				SetFaceList(localValue.Key, localValue.Value);
			}
		}
	}

	protected override void OnFeatureChanged(FeatureChangedEventArgs args)
	{
		base.OnFeatureChanged(args);
		if (args.ChangeType != FeatureChangedType.FeatureAdded)
		{
			return;
		}
		foreach (int featureIndicesChanged in args.FeatureIndicesChanged)
		{
			SetupNewBCLine(featureIndicesChanged);
		}
	}

	private void SetupNewBCLine(int Idx)
	{
		SetUniqueFeatureName(Idx);
		SetIsDefaultName(Idx, isDefaultName: true);
	}

	public override void SetFeatureName(int fid, string newName)
	{
		FeatureRow(fid)["Name"] = newName;
		SetIsDefaultName(fid, isDefaultName: false);
	}

	public bool GetIsDefaultName(int fid)
	{
		return DataTableExtensions.GetBoolean(FeatureTable(), fid, "IsDefaultName");
	}

	public void SetIsDefaultName(int fid, bool isDefaultName)
	{
		FeatureRow(fid)["IsDefaultName"] = isDefaultName;
	}

	public RASBoundaryConditionLines(DataTable overrideDT)
		: base("Boundary Condition Lines")
	{
		SimpleFeatureTypeName = "BC Line";
		ForwardEditRequestsToParent = true;
		CanRename = false;
		FeaturesRequireUniqueNames = true;
		OverrideFeatureTable(overrideDT.Copy());
	}

	public override object Clone()
	{
		RASBoundaryConditionLines rASBoundaryConditionLines = new RASBoundaryConditionLines(FeatureTable());
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Feature feature = Feature(i);
				if (feature is IEditableFeature)
				{
					rASBoundaryConditionLines.SetFeature(i, ((IEditableFeature)feature)?.Save());
				}
			}
			return rASBoundaryConditionLines;
		}
	}

	protected override void OnFeatureTableLoaded()
	{
		base.OnFeatureTableLoaded();
	}

	public MeshFV2D TryGetAssociatedMesh(int fid)
	{
		int featureByName = Geometry.D2FlowArea.GetFeatureByName(GetSA2DName(fid));
		if (featureByName == -1)
		{
			return null;
		}
		return Geometry.D2FlowArea.Mesh(featureByName);
	}

	public bool EnsureConnectivity(ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		if (NeedToRecompute())
		{
			return ComputeConnectivity(prog);
		}
		return true;
	}

	private bool NeedToRecompute()
	{
		if (FeatureCount() == 0)
		{
			return false;
		}
		if (Geometry.IsResultFile)
		{
			return false;
		}
		if (Geometry.D2FlowArea.FeatureCount() == 0)
		{
			return false;
		}
		return true;
	}

	public float[,] ReadSegmentedFlowHydrograph(int fid, string outputBlock = "")
	{
		float[,] data = null;
		int[] cells = null;
		ReadFlowHydrographPerCell(fid, ref data, ref cells, outputBlock);
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
		float[,] segmentedFlowHydrograph = ReadSegmentedFlowHydrograph(fid, outputBlock);
		if (segmentedFlowHydrograph == null)
		{
			return null;
		}
		checked
		{
			float[] floatValues = new float[segmentedFlowHydrograph.GetLength(0) - 1 + 1];
			int count = floatValues.Length - 1;
			for (int i = 0; i <= count; i++)
			{
				float computed = 0f;
				int count2 = segmentedFlowHydrograph.GetLength(1) - 1;
				for (int j = 0; j <= count2; j++)
				{
					computed += segmentedFlowHydrograph[i, j];
				}
				floatValues[i] = computed;
			}
			return floatValues;
		}
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

	private bool ReadFlowHydrographPerCell(int fid, ref float[,] data, ref int[] cells, string outputBlock = "")
	{
		if (!Geometry.IsResultFile)
		{
			return false;
		}
		using H5Reader h5Reader = new H5Reader(Geometry.Results.SourceFilename);
		string textValue = "";
		if (!string.IsNullOrEmpty(outputBlock))
		{
			textValue = ResultsDatasets.Unsteady.TimeSeriesOutput.FlowAreas.FlowBoundaryConditionPerCell(GetFeatureName(fid)).Replace("Base Output", outputBlock);
		}
		else
		{
			textValue = ResultsDatasets.Unsteady.TimeSeriesOutput.FlowAreas.FlowBoundaryConditionPerCell(GetFeatureName(fid));
			if (!h5Reader.ExistsAsDataset(textValue))
			{
				textValue = ResultsDatasets.Unsteady.TimeSeriesOutput.FlowAreas.FlowBoundaryConditionPerCell_Old(GetSA2DName(fid), GetFeatureName(fid));
			}
		}
		if (h5Reader.ExistsAsDataset(textValue))
		{
			h5Reader.ReadDataset(textValue, ref data);
			if (h5Reader.ExistsAsAttribute(textValue, "Cells"))
			{
				h5Reader.ReadAttribute(textValue, "Cells", ref cells);
			}
			return true;
		}
		return false;
	}

	private void ReadStageHydrographPerCell(int fid, ref float[,] data, ref int[] cells, string outputBlock = "")
	{
		if (!Geometry.IsResultFile)
		{
			return;
		}
		using H5Reader h5Reader = new H5Reader(Geometry.Results.SourceFilename);
		string textValue = "";
		if (!string.IsNullOrEmpty(outputBlock))
		{
			textValue = ResultsDatasets.Unsteady.TimeSeriesOutput.FlowAreas.StageBoundaryConditionPerCell(GetFeatureName(fid)).Replace("Base Output", outputBlock);
		}
		else
		{
			textValue = ResultsDatasets.Unsteady.TimeSeriesOutput.FlowAreas.StageBoundaryConditionPerCell(GetFeatureName(fid));
			if (!h5Reader.ExistsAsDataset(textValue))
			{
				textValue = ResultsDatasets.Unsteady.TimeSeriesOutput.FlowAreas.StageBoundaryConditionPerCell_Old(GetSA2DName(fid), GetFeatureName(fid));
			}
		}
		if (h5Reader.ExistsAsDataset(textValue))
		{
			h5Reader.ReadDataset(textValue, ref data);
			if (h5Reader.ExistsAsAttribute(textValue, "Cells"))
			{
				h5Reader.ReadAttribute(textValue, "Cells", ref cells);
			}
		}
	}

	private bool TryReadStageHydrograph(int fid, ref float[] data, ref string units, string outputBlock = "")
	{
		if (!Geometry.IsResultFile)
		{
			return false;
		}
		using H5Reader h5Reader = new H5Reader(Geometry.Results.SourceFilename);
		string isNullOrEmptyOutputBlockTerm = "";
		isNullOrEmptyOutputBlockTerm = (string.IsNullOrEmpty(outputBlock) ? ResultsDatasets.Unsteady.TimeSeriesOutput.FlowAreas.BoundaryConditionBase(GetFeatureName(fid)) : ResultsDatasets.Unsteady.TimeSeriesOutput.FlowAreas.BoundaryConditionBase(GetFeatureName(fid)).Replace("Base Output", outputBlock));
		if (h5Reader.ExistsAsDataset(isNullOrEmptyOutputBlockTerm))
		{
			h5Reader.ReadColumn(isNullOrEmptyOutputBlockTerm, 0, ref data);
			units = h5Reader.TryReadStringAttributeScalar(isNullOrEmptyOutputBlockTerm, "Stage");
			return true;
		}
		return false;
	}

	private void ReadFlowHydrograph(int fid, ref float[] data, ref string units, string outputBlock = "")
	{
		if (!Geometry.IsResultFile)
		{
			return;
		}
		using H5Reader h5Reader = new H5Reader(Geometry.Results.SourceFilename);
		string isNullOrEmptyOutputBlockTerm = "";
		isNullOrEmptyOutputBlockTerm = (string.IsNullOrEmpty(outputBlock) ? ResultsDatasets.Unsteady.TimeSeriesOutput.FlowAreas.BoundaryConditionBase(GetFeatureName(fid)) : ResultsDatasets.Unsteady.TimeSeriesOutput.FlowAreas.BoundaryConditionBase(GetFeatureName(fid)).Replace("Base Output", outputBlock));
		if (h5Reader.ExistsAsDataset(isNullOrEmptyOutputBlockTerm))
		{
			h5Reader.ReadColumn(isNullOrEmptyOutputBlockTerm, 1, ref data);
			units = h5Reader.TryReadStringAttributeScalar(isNullOrEmptyOutputBlockTerm, "Flow");
		}
	}

	public List<TimeSeriesVariable> ReadBCL(int fid, string outputBlock = "Base Output")
	{
		if (Geometry.Results == null)
		{
			return null;
		}
		List<TimeSeriesVariable> timeSeriesVariables = new List<TimeSeriesVariable>();
		float[] data = null;
		float[] data2 = null;
		string units = "";
		string units2 = "";
		TryReadStageHydrograph(fid, ref data, ref units, outputBlock);
		ReadFlowHydrograph(fid, ref data2, ref units2, outputBlock);
		TimeSeriesVariable timeSeriesVariable = new TimeSeriesVariable("Stage", units, data);
		timeSeriesVariables.Add(timeSeriesVariable);
		TimeSeriesVariable timeSeriesVariable2 = new TimeSeriesVariable("Flow", units2, data2);
		timeSeriesVariables.Add(timeSeriesVariable2);
		return timeSeriesVariables;
	}

	public List<TimeSeriesVariable> ReadBCL_DSSHydrograph(int fid)
	{
		return ReadBCL(fid, "DSS Hydrograph Output");
	}

	public List<TimeSeriesVariable> ReadBCL_DSSProfile(int fid)
	{
		return ReadBCL(fid, "DSS Profile Output");
	}

	public string GetSA2DName(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "SA/2D Area");
	}

	public void SetSA2DName(int fid, string name)
	{
		FeatureRow(fid)["SA/2D Area"] = name;
	}

	public string GetConnectionType(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "Connection Type");
	}

	public void SetConnectionType(int fid, string type)
	{
		FeatureRow(fid)["Connection Type"] = type;
	}

	public List<MeshFV2D.ProfileCellRange> GetCellList(int fid)
	{
		object objectValue = RuntimeHelpers.GetObjectValue(FeatureRow(fid)["Cells Crossed"]);
		if (objectValue == null || Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)))
		{
			return null;
		}
		return objectValue as List<MeshFV2D.ProfileCellRange>;
	}

	public void SetCellList(int fID, List<MeshFV2D.ProfileCellRange> profileCellRanges)
	{
		FeatureRow(fID)["Cells Crossed"] = profileCellRanges;
	}

	public List<MeshFV2D.ProfileFaceRange> GetFaceList(int fid)
	{
		object objectValue = RuntimeHelpers.GetObjectValue(FeatureRow(fid)["Faces Crossed"]);
		if (objectValue == null || Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)))
		{
			return null;
		}
		return objectValue as List<MeshFV2D.ProfileFaceRange>;
	}

	public void SetFaceList(int fID, List<MeshFV2D.ProfileFaceRange> profileFaceRanges)
	{
		FeatureRow(fID)["Faces Crossed"] = profileFaceRanges;
	}

	public Polyline GetMeshPerimPolyline(int fid)
	{
		object objectValue = RuntimeHelpers.GetObjectValue(FeatureRow(fid)["Mesh Perimeter Polyline"]);
		if (objectValue == null || Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)))
		{
			return null;
		}
		return objectValue as Polyline;
	}

	public void SetMeshPerimPolyline(int fID, Polyline pline)
	{
		FeatureRow(fID)["Mesh Perimeter Polyline"] = pline;
	}

	private void ComputeConnectivity(object sender, EventArgs e)
	{
		using (new WaitCursor(SharedData.RasMapper))
		{
			ComputeConnectivity(ProgressReporter.DebugPrint());
		}
		SharedData.RASMapperReplot();
	}

	public bool ComputeConnectivity(ProgressReporter prog = null)
	{
		checked
		{
			bool result;
			try
			{
				if (prog == null)
				{
					prog = ProgressReporter.None();
				}
				if (FeatureCount() == 0)
				{
					result = true;
				}
				else
				{
					Console.WriteLine("LAYER=" + Name);
					RASD2FlowArea d2FlowArea = Geometry.D2FlowArea;
					if (d2FlowArea == null)
					{
						prog.ReportError("Mesh Feature Layer failed to load. Please recompute this Geometry.");
						result = false;
					}
					else
					{
						RASStorageArea storageArea = Geometry.StorageArea;
						if (storageArea == null)
						{
							prog.ReportError("Storage Area Feature Layer failed to load. Please recompute this Geometry.");
							result = false;
						}
						else if (storageArea.FeatureCount() + d2FlowArea.FeatureCount() == 0)
						{
							prog.ReportError("There are no 2D Flow Areas or Storage Areas in this Geometry. Boundary Condition Lines are invalid if they cannot connect to one of these hydraulic elements.");
							result = false;
						}
						else
						{
							bool condition = true;
							int count = FeatureCount() - 1;
							for (int i = 0; i <= count; i++)
							{
								Polyline polyline = Polyline(i);
								string inSingleQuotesGetFeatureNameTerm = "BC Line " + GetFeatureName(i).InSingleQuotes();
								if (!RasMapperLib.Polyline.IsValidPolyline(polyline))
								{
									prog.ReportError(inSingleQuotesGetFeatureNameTerm + " is not a valid Polyline.");
									condition = false;
									continue;
								}
								string connectionType = GetConnectionType(i);
								if (string.IsNullOrEmpty(connectionType) || (!connectionType.RoughlyEquals("External") && !connectionType.RoughlyEquals("Internal")))
								{
									prog.ReportError(inSingleQuotesGetFeatureNameTerm + " has an associated connection type of " + connectionType.InSingleQuotes() + ", which is invalid.");
									condition = false;
									continue;
								}
								string sA2DName = GetSA2DName(i);
								int featureByName = d2FlowArea.GetFeatureByName(sA2DName);
								if (featureByName == -1)
								{
									if (storageArea.GetFeatureByName(sA2DName) == -1)
									{
										prog.ReportError(inSingleQuotesGetFeatureNameTerm + " is associated with " + sA2DName.InSingleQuotes() + ", which is not a valid 2D Flow Area or Storage Area.");
										condition = false;
									}
									continue;
								}
								MeshFV2D meshFV2D = d2FlowArea.Mesh(featureByName);
								if (meshFV2D == null || !meshFV2D.IsComplete())
								{
									prog.ReportError(inSingleQuotesGetFeatureNameTerm + " is associated with 2D Flow Area " + sA2DName.InSingleQuotes() + ", which was not successfully created or saved.");
									condition = false;
									continue;
								}
								Polygon perimeter = meshFV2D.Perimeter;
								DevelopmentTin devTin = PolygonFeatureLayer.ComputeDevelopmentTIN(perimeter);
								List<Tuple<int, bool>> facesAlongPolyline = meshFV2D.FacesAlongPolyline(polyline);
								double cellPolygonAreaSum = 0.0;
								int ordinal = 0;
								foreach (Tuple<int, bool> facesAlongPolyline2 in facesAlongPolyline)
								{
									Face face = meshFV2D.Faces[facesAlongPolyline2.Item1];
									int cellA = face.cellA;
									int cellB = face.cellB;
									if (cellA != -1)
									{
										cellPolygonAreaSum += meshFV2D.CellPolygonArea(cellA);
										ordinal++;
									}
									if (cellB != -1)
									{
										cellPolygonAreaSum += meshFV2D.CellPolygonArea(cellB);
										ordinal++;
									}
								}
								if (ordinal == 0)
								{
									prog.ReportError(inSingleQuotesGetFeatureNameTerm + " could not find any nearby faces on 2D Flow Area " + d2FlowArea.GetFeatureName(featureByName).InSingleQuotes());
									condition = false;
									continue;
								}
								double cellPolygonAreaSumTerm = 0.1 * Math.Sqrt(cellPolygonAreaSum / (double)ordinal);
								if (polyline.RoughlyFollowsOrIsOutside(perimeter, cellPolygonAreaSumTerm, devTin))
								{
									if (connectionType.RoughlyEquals("Internal"))
									{
										prog.ReportError(inSingleQuotesGetFeatureNameTerm + " appears to be an External boundary condition for 2D Flow Area " + d2FlowArea.GetFeatureName(featureByName).InSingleQuotes() + " but is listed as Internal. If this BC Line is intended to be Internal, please move it inside the boundary.");
										condition = false;
									}
								}
								else if (polyline.IsStrictlyInside(perimeter, cellPolygonAreaSumTerm, devTin))
								{
									if (connectionType.RoughlyEquals("External"))
									{
										prog.ReportError(inSingleQuotesGetFeatureNameTerm + " appears to be an Internal boundary condition for 2D Flow Area " + d2FlowArea.GetFeatureName(featureByName).InSingleQuotes() + " but is listed as External.. If this BC Line is intended to be External, please move it outside the boundary.");
										condition = false;
									}
								}
								else
								{
									prog.ReportError(inSingleQuotesGetFeatureNameTerm + " intersects the perimeter of 2D Flow Area " + d2FlowArea.GetFeatureName(featureByName).InSingleQuotes() + ". This is not allowed in HEC-RAS: if this is an external BC Line, then it must be entirely outside the 2D Area Perimeter.If this is an internal BC Line, it must be entirely inside the Perimeter.");
									condition = false;
								}
							}
							if (!condition)
							{
								result = false;
							}
							else
							{
								using (new FeatureTableReloadSuppressor(this))
								{
									using H5Writer hw = new H5Writer(SourceFilename);
									ComputeSetMeshConnectionElements();
									WriteCellsConnectivity(hw);
									WriteExternalFacesConnectivity(hw);
									WriteInternalFacesConnectivity(hw);
									AppendLengthsToAttributeTable(hw);
								}
								result = condition;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				SharedData.ReportMessage("Error computing BC Lines connectivity");
				prog.ReportError("Error computing BC Lines connectivity");
				result = false;
				ProjectData.ClearProjectError();
			}
			return result;
		}
	}

	private void ComputeSetMeshConnectionElements()
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Polyline polyline = Polyline(i);
				if (!polyline.IsValid())
				{
					continue;
				}
				string sA2DName = GetSA2DName(i);
				int featureByName = Geometry.D2FlowArea.GetFeatureByName(sA2DName);
				if (featureByName == -1)
				{
					int featureByName2 = Geometry.StorageArea.GetFeatureByName(sA2DName);
					if (featureByName2 == -1)
					{
						continue;
					}
					try
					{
						Polygon polygon = Geometry.StorageArea.Polygon(featureByName2);
						if (polygon != null && polygon.IsValid())
						{
							PointM ptM = polyline.FirstPointM();
							double actualDistance = 0.0;
							PointOnPolyline pointOnPolyline = polygon.NearestPointOnPolyline(ptM, ref actualDistance);
							PointM pointM = polyline.Midpoint().PointM;
							actualDistance = 0.0;
							PointOnPolyline midPoP = polygon.NearestPointOnPolyline(pointM, ref actualDistance);
							PointM ptM2 = polyline.LastPointM();
							actualDistance = 0.0;
							PointOnPolyline pointOnPolyline2 = polygon.NearestPointOnPolyline(ptM2, ref actualDistance);
							if (!polygon.IsPointBetweenCCW(pointOnPolyline, midPoP, pointOnPolyline2))
							{
								SetMeshPerimPolyline(i, polygon.PolylineSubsection(pointOnPolyline2, pointOnPolyline).Reverse());
							}
							else
							{
								SetMeshPerimPolyline(i, polygon.PolylineSubsection(pointOnPolyline, pointOnPolyline2));
							}
						}
					}
					catch (Exception ex)
					{
						ProjectData.SetProjectError(ex);
						Exception ex2 = ex;
						ProjectData.ClearProjectError();
					}
					continue;
				}
				MeshFV2D meshFV2D = Geometry.D2FlowArea.Mesh(featureByName);
				if (meshFV2D != null && meshFV2D.IsComplete())
				{
					if (ConnectionTypeIsInternal(i))
					{
						SetCellList(i, meshFV2D.CellsUnderPolyline(polyline));
						List<Tuple<int, bool>> faces = meshFV2D.FacesAlongPolyline(polyline);
						List<MeshFV2D.ProfileFaceRange> convertFaceTuplesToProfileFaceRange = meshFV2D.ConvertFaceTuplesToProfileFaceRange(faces);
						SetFaceList(i, convertFaceTuplesToProfileFaceRange);
					}
					else if (ConnectionTypeIsExternal(i))
					{
						Polyline magnetizedPline = null;
						SetFaceList(i, meshFV2D.PerimeterFacesAlongPolyline(polyline, ref magnetizedPline));
						SetMeshPerimPolyline(i, magnetizedPline);
					}
				}
			}
		}
	}

	public bool TryGetNearestSA2D(int fid, ref FeatureLayer layer, ref int layerFid)
	{
		layer = null;
		PointM ptM = Polyline(fid).FirstPointM();
		double maxValue = double.MaxValue;
		checked
		{
			int count = Geometry.StorageArea.FeatureCount() - 1;
			double actualDistance = default;
			for (int i = 0; i <= count; i++)
			{
				Geometry.StorageArea.Polygon(i).NearestPointOnPolyline(ptM, ref actualDistance);
				if (actualDistance < maxValue)
				{
					maxValue = actualDistance;
					layer = Geometry.StorageArea;
					layerFid = i;
				}
			}
			int count2 = Geometry.MeshPerimeters.FeatureCount() - 1;
			double actualDistance2 = default;
			for (int j = 0; j <= count2; j++)
			{
				Geometry.MeshPerimeters.Polygon(j).NearestPointOnPolyline(ptM, ref actualDistance2);
				if (actualDistance2 < maxValue)
				{
					maxValue = actualDistance2;
					layer = Geometry.MeshPerimeters;
					layerFid = j;
				}
			}
			return layer != null;
		}
	}

	public bool TryComputeInternalOrExternal(int fid, ref string connectionType)
	{
		connectionType = "";
		Polyline polyline = Polyline(fid);
		if (!polyline.IsValid())
		{
			return false;
		}
		string sA2DName = GetSA2DName(fid);
		int featureByName = Geometry.D2FlowArea.GetFeatureByName(sA2DName);
		if (featureByName == -1)
		{
			if (Geometry.StorageArea.GetFeatureByName(sA2DName) == -1)
			{
				return false;
			}
			connectionType = "External";
			return true;
		}
		MeshFV2D meshFV2D = Geometry.D2FlowArea.Mesh(featureByName);
		if (meshFV2D == null || !meshFV2D.IsComplete())
		{
			return false;
		}
		List<MeshFV2D.ProfileCellRange> cellsUnderPolyline = meshFV2D.CellsUnderPolyline(polyline);
		if (cellsUnderPolyline.Count == 0)
		{
			connectionType = "External";
			return true;
		}
		connectionType = "External";
		checked
		{
			int count = cellsUnderPolyline.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				if (!meshFV2D.CellIsPerimeter(cellsUnderPolyline[i].Cell))
				{
					connectionType = "Internal";
					return true;
				}
			}
			return true;
		}
	}

	public bool ConnectionTypeIsInternal(int fid)
	{
		return GetConnectionType(fid).RoughlyEquals("Internal");
	}

	public bool ConnectionTypeIsExternal(int fid)
	{
		return GetConnectionType(fid).RoughlyEquals("External");
	}

	private void WriteCellsConnectivity(H5Writer hw)
	{
		string path = "Geometry/Boundary Condition Lines/Internal Cells";
		int cellListCountSum = 0;
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				List<MeshFV2D.ProfileCellRange> cellList = GetCellList(i);
				if (cellList != null)
				{
					cellListCountSum += cellList.Count;
				}
			}
			if (cellListCountSum == 0)
			{
				if (hw.ExistsAsDataset(path))
				{
					hw.Delete(path);
				}
				return;
			}
			int[] intValues = new int[cellListCountSum - 1 + 1];
			int[] intValuesArray = new int[cellListCountSum - 1 + 1];
			float[] floatValues = new float[cellListCountSum - 1 + 1];
			float[] floatValuesArray = new float[cellListCountSum - 1 + 1];
			int ordinal = 0;
			int count2 = FeatureCount() - 1;
			for (int j = 0; j <= count2; j++)
			{
				List<MeshFV2D.ProfileCellRange> cellList2 = GetCellList(j);
				if (cellList2.IsNullOrEmpty())
				{
					continue;
				}
				foreach (MeshFV2D.ProfileCellRange cellList22 in cellList2)
				{
					intValues[ordinal] = j;
					intValuesArray[ordinal] = cellList22.Cell;
					floatValues[ordinal] = (float)cellList22.StationStart;
					floatValuesArray[ordinal] = (float)cellList22.StationEnd;
					ordinal++;
				}
			}
			CompoundTypeDefinition compoundTypeDefinition = new CompoundTypeDefinition();
			compoundTypeDefinition.AddField<int>("BC Line ID");
			compoundTypeDefinition.AddField<int>("Cell Index");
			compoundTypeDefinition.AddField<float>("Station Start");
			compoundTypeDefinition.AddField<float>("Station End");
			hw.Create1DCompoundDataset(path, cellListCountSum, compoundTypeDefinition);
			using H5Assist.VB.CompoundWriter compoundWriter = new H5Assist.VB.CompoundWriter(hw, path, cellListCountSum);
			compoundWriter.WriteColumn(0, intValues);
			compoundWriter.WriteColumn(1, intValuesArray);
			compoundWriter.WriteColumn(2, floatValues);
			compoundWriter.WriteColumn(3, floatValuesArray);
		}
	}

	private void WriteExternalFacesConnectivity(H5Writer hw)
	{
		string dataset = "Geometry/Boundary Condition Lines/External Faces";
		WriteFacesConnectivity(hw, dataset, isExternal: true);
	}

	private void WriteInternalFacesConnectivity(H5Writer hw)
	{
		string dataset = "Geometry/Boundary Condition Lines/Internal Faces";
		WriteFacesConnectivity(hw, dataset, isExternal: false);
	}

	private void WriteFacesConnectivity(H5Writer hw, string dataset, bool isExternal)
	{
		int faceListCountSum = 0;
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (ConnectionTypeIsExternal(i) == isExternal)
				{
					List<MeshFV2D.ProfileFaceRange> faceList = GetFaceList(i);
					if (faceList != null)
					{
						faceListCountSum += faceList.Count;
					}
				}
			}
			if (faceListCountSum == 0)
			{
				if (hw.ExistsAsDataset(dataset))
				{
					hw.Delete(dataset);
				}
				return;
			}
			int[] intValues = new int[faceListCountSum - 1 + 1];
			int[] intValuesArray = new int[faceListCountSum - 1 + 1];
			int[] intValues2 = new int[faceListCountSum - 1 + 1];
			int[] intValues3 = new int[faceListCountSum - 1 + 1];
			float[] floatValues = new float[faceListCountSum - 1 + 1];
			float[] floatValuesArray = new float[faceListCountSum - 1 + 1];
			int ordinal = 0;
			int count2 = FeatureCount() - 1;
			for (int j = 0; j <= count2; j++)
			{
				if (ConnectionTypeIsExternal(j) != isExternal)
				{
					continue;
				}
				List<MeshFV2D.ProfileFaceRange> faceList2 = GetFaceList(j);
				if (faceList2.IsNullOrEmpty())
				{
					continue;
				}
				foreach (MeshFV2D.ProfileFaceRange faceList22 in faceList2)
				{
					intValues[ordinal] = j;
					intValuesArray[ordinal] = faceList22.Face;
					intValues2[ordinal] = faceList22.FPStartIdx;
					intValues3[ordinal] = faceList22.FPEndIdx;
					floatValues[ordinal] = (float)faceList22.StationStart;
					floatValuesArray[ordinal] = (float)faceList22.StationEnd;
					ordinal++;
				}
			}
			CompoundTypeDefinition compoundTypeDefinition = new CompoundTypeDefinition();
			compoundTypeDefinition.AddField<int>("BC Line ID");
			compoundTypeDefinition.AddField<int>("Face Index");
			compoundTypeDefinition.AddField<int>("FP Start Index");
			compoundTypeDefinition.AddField<int>("FP End Index");
			compoundTypeDefinition.AddField<float>("Station Start");
			compoundTypeDefinition.AddField<float>("Station End");
			hw.Create1DCompoundDataset(dataset, faceListCountSum, compoundTypeDefinition);
			using H5Assist.VB.CompoundWriter compoundWriter = new H5Assist.VB.CompoundWriter(hw, dataset, faceListCountSum);
			compoundWriter.WriteColumn(0, intValues);
			compoundWriter.WriteColumn(1, intValuesArray);
			compoundWriter.WriteColumn(2, intValues2);
			compoundWriter.WriteColumn(3, intValues3);
			compoundWriter.WriteColumn(4, floatValues);
			compoundWriter.WriteColumn(5, floatValuesArray);
		}
	}

	private void AppendLengthsToAttributeTable(H5Writer hw)
	{
		string path = "Geometry/Boundary Condition Lines/Attributes";
		if (!hw.ExistsAsDataset(path))
		{
			return;
		}
		checked
		{
			float[] floatValues = new float[FeatureCount() - 1 + 1];
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Polyline polyline = GetMeshPerimPolyline(i);
				if (polyline == null || !polyline.IsValid())
				{
					polyline = Polyline(i);
				}
				floatValues[i] = (float)polyline.Length;
			}
			Dictionary<CompoundFieldDefinition, Array> compoundElements = hw.ReadCompoundElements(path);
			List<KeyValuePair<CompoundFieldDefinition, Array>> compoundElementsList = new List<KeyValuePair<CompoundFieldDefinition, Array>>();
			foreach (KeyValuePair<CompoundFieldDefinition, Array> compoundElement in compoundElements)
			{
				if (!string.Equals(compoundElement.Key.Name, "Length", StringComparison.CurrentCultureIgnoreCase))
				{
					compoundElementsList.Add(compoundElement);
				}
			}
			compoundElementsList = compoundElementsList.OrderBy([SpecialName] (KeyValuePair<CompoundFieldDefinition, Array> kvp) => kvp.Key.Offset).ToList();
			CompoundTypeDefinition compoundTypeDefinition = new CompoundTypeDefinition();
			foreach (KeyValuePair<CompoundFieldDefinition, Array> localValue in compoundElementsList)
			{
				compoundTypeDefinition.AddField(localValue.Key);
			}
			compoundTypeDefinition.AddField<float>("Length");
			hw.Delete(path);
			hw.Create1DCompoundDataset(path, 1024, compoundTypeDefinition);
			using H5Assist.VB.CompoundWriter compoundWriter = new H5Assist.VB.CompoundWriter(hw, path, FeatureCount());
			int ordinal = 0;
			foreach (KeyValuePair<CompoundFieldDefinition, Array> localValue2 in compoundElementsList)
			{
				compoundWriter.WriteColumn(ordinal, localValue2.Value);
				ordinal++;
			}
			compoundWriter.WriteColumn(ordinal, floatValues);
		}
	}
}
