using System;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Functional;

namespace RasMapperLib;

public class GaugeLayer : PointFeatureLayer
{
	public const string HDFGroupName = "//Gauges";

	public override bool ForwardEditRequestsToParent { get; }

	public GaugeLayer()
		: base("Gauge Layer")
	{
		ForwardEditRequestsToParent = true;
	}

	public GaugeLayer(string name)
		: base(name)
	{
		ForwardEditRequestsToParent = true;
	}

	public GaugeLayer(string name, string filename)
		: base(name, filename)
	{
		ForwardEditRequestsToParent = true;
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddAttributeColumn("Time Series Data", typeof(TimeSeriesCollection));
	}

	protected override void LoadFeatureTable()
	{
		base.LoadFeatureTable();
		if (!SourceFileExists)
		{
			return;
		}
		try
		{
			using H5Reader h5Reader = new H5Reader(SourceFilename);
			if (h5Reader.ExistsAsGroup("//Gauges"))
			{
				HDF5Storage.ReadPointFeatures(this, h5Reader, "//Gauges/Point");
			}
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			ProjectData.ClearProjectError();
		}
	}

	public override bool SaveFeatureTable()
	{
		using (H5Writer h5Writer = new H5Writer(SourceFilename))
		{
			h5Writer.CreateGroup("//Gauges");
			HDF5Storage.WritePointFeatures(this, h5Writer, "//Gauges/Point");
		}
		return true;
	}
}
