using System;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

public class RASPipeLinks : PolylineFeatureLayer, IGeometryLayer
{
	public const string DefaultHDFGroupName = "Geometry/Pipe Links";

	public const string PolylineDatasetName = "Geometry/Pipe Links/Polyline";

	public override bool CanAddOrRemoveAttributeColumns => true;

	public RASGeometry Geometry { get; }

	public override bool ForwardEditRequestsToParent { get; }

	public string HDFGroupName => "Geometry/Pipe Links";

	public RASPipeLinks(RASGeometry geometry)
		: base("Pipe Links", geometry.SourceFilename)
	{
		ForwardEditRequestsToParent = true;
		Geometry = geometry;
	}

	public void HDFLoadFeatureTable(H5Reader hr)
	{
		try
		{
			if (hr.ExistsAsGroup(HDFGroupName))
			{
				HDF5Storage.ReadPolyFeatures(this, hr, "Geometry/Pipe Links/Polyline");
				string datasetName = HDFGroupName + "/Attributes";
				ReadManagedColumnMetadata(hr, HDFGroupName + "/Managed Columns");
				ReadManagedColumns(hr, datasetName);
			}
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			ErrorMessage = ex2.Message;
			ProjectData.ClearProjectError();
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
			hw.Delete(HDFGroupName);
			return;
		}
		try
		{
			hw.CreateGroup(HDFGroupName);
			HDF5Storage.WritePolylineFeatures(this, hw, "Geometry/Pipe Links/Polyline");
			WriteManagedColumns(hw, HDFGroupName + "/Attributes");
			WriteManagedColumnMetadata(hw, HDFGroupName + "/Managed Columns");
		}
		catch (Exception projectError)
		{
			ProjectData.SetProjectError(projectError);
			ProjectData.ClearProjectError();
		}
	}

	void IGeometryLayer.HDFSaveFeatureTable(H5Writer hw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFSaveFeatureTable
		this.HDFSaveFeatureTable(hw);
	}

	public static void Migrate(H5Reader h5r, H5Writer h5w)
	{
		if (h5r.ExistsAsGroup("Geometry/Pipe Links"))
		{
			if (h5w.ExistsAsGroup("Geometry/Pipe Links"))
			{
				h5w.Delete("Geometry/Pipe Links");
			}
			h5r.CopyTo(h5w, "Geometry/Pipe Links", "Geometry/Pipe Links");
		}
	}
}
