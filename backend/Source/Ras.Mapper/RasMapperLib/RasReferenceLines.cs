using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using H5Assist.Identifiers;
using H5Assist.VB;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Names;
using Utility.Extensions;
using Utility.Progress;

namespace RasMapperLib;

public class RasReferenceLines : PolylineFeatureLayer, IGeometryLayer
{
	public const string HDFGroupName = "Geometry/Reference Lines";

	public const string PolylineDatasetName = "Geometry/Reference Lines/Polyline";

	public const string sCellList = "Cells Crossed";

	public const string sFaceList = "Faces Crossed";

	public const string HDFCellListDataset = "Geometry/Reference Lines/Internal Cells";

	public const string HDFFaceListDataset = "Geometry/Reference Lines/Internal Faces";

	public RASGeometry Geometry { get; }

	public override bool ForwardEditRequestsToParent { get; }

	public override string SimpleFeatureTypeName { get; set; }

	public override bool CanRename { get; }

	public override string DefaultLayerName => "Reference Lines";
string IGeometryLayer.HDFGroupName=> "Geometry/Reference Lines";

	public override bool FeaturesRequireUniqueNames { get; set; }

	public RasReferenceLines(RASGeometry geometry)
		: base("Reference Lines", geometry.SourceFilename)
	{
		ForwardEditRequestsToParent = true;
		SimpleFeatureTypeName = "Reference Line";
		CanRename = false;
		FeaturesRequireUniqueNames = true;
		Geometry = geometry;
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddManagedAttributeColumn("Name", "Name", typeof(string));
		AddManagedAttributeColumn("SA/2D Area", "SA-2D", typeof(string), null, DataColumnViewState.GrayedOut);
		AddAttributeColumn("Cells Crossed", typeof(List<MeshFV2D.ProfileCellRange>), null, DataColumnViewState.Invisible);
		AddAttributeColumn("Faces Crossed", typeof(List<MeshFV2D.ProfileFaceRange>), null, DataColumnViewState.Invisible);
		AddAttributeColumn("IsDefaultName", typeof(bool), null, DataColumnViewState.Invisible);
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
			SetupNewRefLine(featureIndicesChanged);
		}
	}

	private void SetupNewRefLine(int Idx)
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

	public void HDFLoadFeatureTable(H5Reader hr)
	{
		if (hr.ExistsAsGroup("Geometry/Reference Lines"))
		{
			HDF5Storage.ReadPolyFeatures(this, hr, "Geometry/Reference Lines/Polyline");
			string path = "Geometry/Reference Lines/Attributes";
			if (hr.ExistsAsDataset(path))
			{
				ReadManagedColumns(hr, path);
			}
		}
	}

	void IGeometryLayer.HDFLoadFeatureTable(H5Reader hr)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFLoadFeatureTable
		this.HDFLoadFeatureTable(hr);
	}

	public void HDFSaveFeatureTable(H5Writer hw)
	{
		if (FeatureCount() == 0)
		{
			hw.Delete("Geometry/Reference Lines");
			return;
		}
		hw.CreateGroup("Geometry/Reference Lines");
		HDF5Storage.WritePolylineFeatures(this, hw, "Geometry/Reference Lines/Polyline");
		WriteManagedColumns(hw, "Geometry/Reference Lines/Attributes");
	}

	void IGeometryLayer.HDFSaveFeatureTable(H5Writer hw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFSaveFeatureTable
		this.HDFSaveFeatureTable(hw);
	}

	public bool ComputeConnectivity(ProgressReporter prog = null)
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
				if (Geometry.D2FlowArea == null)
				{
					prog.ReportError("Mesh Feature Layer failed to load. Please recompute this Geometry.");
					result = false;
				}
				else
				{
					using (new FeatureTableReloadSuppressor(this))
					{
						using H5Writer hw = new H5Writer(SourceFilename);
						ComputeSetMeshConnectionElements();
						WriteCellsConnectivity(hw);
						WriteFacesConnectivity(hw);
					}
					result = true;
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
				if (featureByName != -1)
				{
					MeshFV2D meshFV2D = Geometry.D2FlowArea.Mesh(featureByName);
					if (meshFV2D != null && meshFV2D.IsComplete())
					{
						SetCellList(i, meshFV2D.CellsUnderPolyline(polyline));
						List<Tuple<int, bool>> faces = meshFV2D.FacesAlongPolyline(polyline);
						List<MeshFV2D.ProfileFaceRange> convertFaceTuplesToProfileFaceRange = meshFV2D.ConvertFaceTuplesToProfileFaceRange(faces);
						SetFaceList(i, convertFaceTuplesToProfileFaceRange);
					}
				}
			}
		}
	}

	private void WriteCellsConnectivity(H5Writer hw)
	{
		string path = "Geometry/Reference Lines/Internal Cells";
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

	private void WriteFacesConnectivity(H5Writer hw)
	{
		string path = "Geometry/Reference Lines/Internal Faces";
		int faceListCountSum = 0;
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				List<MeshFV2D.ProfileFaceRange> faceList = GetFaceList(i);
				if (faceList != null)
				{
					faceListCountSum += faceList.Count;
				}
			}
			if (faceListCountSum == 0)
			{
				if (hw.ExistsAsDataset(path))
				{
					hw.Delete(path);
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
			hw.Create1DCompoundDataset(path, faceListCountSum, compoundTypeDefinition);
			using H5Assist.VB.CompoundWriter compoundWriter = new H5Assist.VB.CompoundWriter(hw, path, faceListCountSum);
			compoundWriter.WriteColumn(0, intValues);
			compoundWriter.WriteColumn(1, intValuesArray);
			compoundWriter.WriteColumn(2, intValues2);
			compoundWriter.WriteColumn(3, intValues3);
			compoundWriter.WriteColumn(4, floatValues);
			compoundWriter.WriteColumn(5, floatValuesArray);
		}
	}

	public string GetSA2DName(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "SA/2D Area");
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

	public override void StopEditing(bool save)
	{
		base.StopEditing(save);
	}

	public List<TimeSeriesVariable> ReadRL(int fid, string outputBlock = "Base Output")
	{
		RASResults results = Geometry.Results;
		if (results == null)
		{
			return null;
		}
		List<TimeSeriesVariable> timeSeriesVariables = new List<TimeSeriesVariable>();
		string replace = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Reference Lines/Water Surface".Replace("Base Output", outputBlock);
		string replace2 = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Reference Lines/Flow".Replace("Base Output", outputBlock);
		string textValue = "";
		string textValue2 = "";
		using (H5Reader h5Reader = new H5Reader(Geometry.Results.SourceFilename))
		{
			if (h5Reader.ExistsAsDataset(replace) && h5Reader.ExistsAsAttribute(replace, "Units"))
			{
				h5Reader.ReadAttributeScalar(replace, "Units", ref textValue);
			}
			if (h5Reader.ExistsAsDataset(replace2) && h5Reader.ExistsAsAttribute(replace2, "Units"))
			{
				h5Reader.ReadAttributeScalar(replace2, "Units", ref textValue2);
			}
			float[] values = null;
			results.ReadUnsteadyTimeSeries(replace, fid, ref values);
			timeSeriesVariables.Add(new TimeSeriesVariable("Stage", textValue, values));
			float[] values2 = null;
			results.ReadUnsteadyTimeSeries(replace2, fid, ref values2);
			timeSeriesVariables.Add(new TimeSeriesVariable("Flow", textValue2, values2));
			string replace3 = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Reference Lines/".Replace("Base Output", outputBlock);
			List<string> datasetNames = h5Reader.GetDatasetNames("Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Reference Lines/".Replace("Base Output", outputBlock));
			string right = ResultsDatasets.Unsteady.TimeSeriesOutput.ReferenceLines.VariableName("Name").Replace("Base Output", outputBlock);
			foreach (string datasetName in datasetNames)
			{
				string replace3PlusDatasetName = replace3 + datasetName;
				if (Operators.CompareString(replace3PlusDatasetName, replace, TextCompare: false) != 0 && Operators.CompareString(replace3PlusDatasetName, replace2, TextCompare: false) != 0 && Operators.CompareString(replace3PlusDatasetName, right, TextCompare: false) != 0)
				{
					float[] values3 = null;
					string textValue3 = "";
					if (h5Reader.ExistsAsDataset(replace3PlusDatasetName) && h5Reader.ExistsAsAttribute(replace3PlusDatasetName, "Units"))
					{
						h5Reader.ReadAttributeScalar(replace3PlusDatasetName, "Units", ref textValue3);
					}
					results.ReadUnsteadyTimeSeries(replace3PlusDatasetName, fid, ref values3);
					timeSeriesVariables.Add(new TimeSeriesVariable(datasetName, textValue3, values3));
				}
			}
		}
		return timeSeriesVariables;
	}

	public List<TimeSeriesVariable> ReadRL_DSSHydrograph(int fid)
	{
		return ReadRL(fid, "DSS Hydrograph Output");
	}

	public List<TimeSeriesVariable> ReadRL_DSSProfile(int fid)
	{
		return ReadRL(fid, "DSS Profile Output");
	}
}
