using System;
using System.Collections.Generic;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

public class RASInitialConditionsPoints : PointFeatureLayer, IGeometryLayer
{
	public const string PointDatasetName = "Geometry/IC Points/Points";

	public const string HDFGroupName = "Geometry/IC Points";

	public override string SimpleFeatureTypeName { get; set; }

	public RASGeometry Geometry { get; }

	public override bool ForwardEditRequestsToParent { get; }

	public override bool CanRename { get; }
string IGeometryLayer.HDFGroupName=> "Geometry/IC Points";

	public RASInitialConditionsPoints(RASGeometry geometry)
		: base("Initial Condition Points", geometry.SourceFilename)
	{
		SimpleFeatureTypeName = "IC Points";
		ForwardEditRequestsToParent = true;
		CanRename = false;
		Geometry = geometry;
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddManagedAttributeColumn("Name", "Name", typeof(string));
		AddManagedAttributeColumn("SA/2D Area", "SA/2D", typeof(string));
		AddManagedAttributeColumn("Cell Index", "Cell Index", typeof(int));
	}

	public void HDFLoadFeatureTable(H5Reader hr)
	{
		if (hr.ExistsAsGroup("Geometry/IC Points"))
		{
			HDF5Storage.ReadPointFeatures(this, hr, "Geometry/IC Points/Points");
			string path = "Geometry/IC Points/Attributes";
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
			hw.Delete("Geometry/IC Points");
			return;
		}
		hw.CreateGroup("Geometry/IC Points");
		HDF5Storage.WritePointFeatures(this, hw, "Geometry/IC Points/Points");
		WriteManagedColumns(hw, "Geometry/IC Points/Attributes");
	}

	void IGeometryLayer.HDFSaveFeatureTable(H5Writer hw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFSaveFeatureTable
		this.HDFSaveFeatureTable(hw);
	}

	public bool EnsureInitialConditions()
	{
		if (NeedToRecompute())
		{
			return ComputeInitialConditions();
		}
		return true;
	}

	public List<TimeSeriesVariable> ReadICP(int fid, string outputBlock = "Base Output")
	{
		RASResults results = Geometry.Results;
		if (results == null)
		{
			return null;
		}
		List<TimeSeriesVariable> timeSeriesVariables = new List<TimeSeriesVariable>();
		string replace = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Reference Points/Water Surface".Replace("Base Output", outputBlock);
		string textValue = "";
		using (H5Reader h5Reader = new H5Reader(Geometry.Results.SourceFilename))
		{
			if (h5Reader.ExistsAsDataset(replace) && h5Reader.ExistsAsAttribute(replace, "Units"))
			{
				h5Reader.ReadAttributeScalar(replace, "Units", ref textValue);
			}
		}
		float[] values = null;
		results.ReadUnsteadyTimeSeries(replace, fid, ref values);
		timeSeriesVariables.Add(new TimeSeriesVariable("Stage", textValue, values));
		return timeSeriesVariables;
	}

	public List<TimeSeriesVariable> ReadICP_DSSHydrograph(int fid)
	{
		return ReadICP(fid, "DSS Hydrograph Output");
	}

	public List<TimeSeriesVariable> ReadICP_DSSProfile(int fid)
	{
		return ReadICP(fid, "DSS Profile Output");
	}

	private bool NeedToRecompute()
	{
		RASGeometry geometry = Geometry;
		if (geometry.IsResultFile)
		{
			return false;
		}
		if (geometry.D2FlowArea.FeatureCount() == 0 && geometry.StorageArea.FeatureCount() == 0)
		{
			return false;
		}
		return FeatureCount() > 0;
	}

	private void ComputeInitialConditions(object sender, EventArgs e)
	{
		using (new WaitCursor(SharedData.RasMapper))
		{
			ComputeInitialConditions();
		}
		SharedData.RASMapperReplot();
	}

	public bool ComputeInitialConditions()
	{
		bool result;
		try
		{
			Console.WriteLine("LAYER=" + Name);
			result = true;
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			SharedData.ReportMessage("Error computing BC Lines connectivity");
			result = false;
			ProjectData.ClearProjectError();
		}
		return result;
	}

	public string GetSAD2Name(int fID)
	{
		return DataTableExtensions.GetString(FeatureTable(), fID, "SA/2D Area");
	}

	public void SetSA2DName(int fid, string name)
	{
		FeatureRow(fid)["SA/2D Area"] = name;
	}

	public int GetCellIndex(int fID)
	{
		return DataTableExtensions.GetInteger(FeatureTable(), fID, "Cell Index");
	}

	public void SetCellIndex(int fid, int cellIdx)
	{
		FeatureRow(fid)["Cell Index"] = cellIdx;
	}

	public override void StopEditing(bool save)
	{
		base.StopEditing(save);
	}
}
