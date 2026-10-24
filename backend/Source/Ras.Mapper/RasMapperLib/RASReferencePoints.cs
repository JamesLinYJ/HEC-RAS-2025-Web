using System.Collections.Generic;
using H5Assist.VB;
using Utility.Progress;

namespace RasMapperLib;

public class RASReferencePoints : PointFeatureLayer, IGeometryLayer
{
	public const string HDFGroupName = "Geometry/Reference Points";

	public const string PointDatasetName = "Geometry/Reference Points/Points";

	public RASGeometry Geometry { get; }

	public override bool ForwardEditRequestsToParent { get; }

	public override string SimpleFeatureTypeName { get; set; }

	public override string DefaultLayerName => "Reference Points";
string IGeometryLayer.HDFGroupName=> "Geometry/Reference Points";

	public override bool FeaturesRequireUniqueNames { get; set; }

	public RASReferencePoints(RASGeometry geometry)
		: base("Reference Points", geometry.SourceFilename)
	{
		ForwardEditRequestsToParent = true;
		SimpleFeatureTypeName = "Reference Point";
		FeaturesRequireUniqueNames = true;
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
		if (hr.ExistsAsGroup("Geometry/Reference Points"))
		{
			HDF5Storage.ReadPointFeatures(this, hr, "Geometry/Reference Points/Points");
			string path = "Geometry/Reference Points/Attributes";
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
			hw.Delete("Geometry/Reference Points");
			return;
		}
		hw.CreateGroup("Geometry/Reference Points");
		HDF5Storage.WritePointFeatures(this, hw, "Geometry/Reference Points/Points");
		WriteManagedColumns(hw, "Geometry/Reference Points/Attributes");
	}

	void IGeometryLayer.HDFSaveFeatureTable(H5Writer hw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFSaveFeatureTable
		this.HDFSaveFeatureTable(hw);
	}

	public bool ComputeConnectivity(ProgressReporter prog = null)
	{
		return false;
	}

	public List<TimeSeriesVariable> ReadRP(int fid, string outputBlock = "Base Output")
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

	public List<TimeSeriesVariable> ReadRP_DSSHydrograph(int fid)
	{
		return ReadRP(fid, "DSS Hydrograph Output");
	}

	public List<TimeSeriesVariable> ReadRP_DSSProfile(int fid)
	{
		return ReadRP(fid, "DSS Profile Output");
	}
}
