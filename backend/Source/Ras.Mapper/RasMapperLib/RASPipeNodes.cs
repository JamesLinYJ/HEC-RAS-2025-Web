using System;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

public class RASPipeNodes : PointFeatureLayer, IGeometryLayer
{
	public const string HDFGroupName = "Geometry/Pipe Nodes";

	public const string PointDatasetName = "Geometry/Pipe Nodes/Points";

	public override bool CanAddOrRemoveAttributeColumns => true;

	public RASGeometry Geometry { get; }

	public override bool ForwardEditRequestsToParent { get; }
string IGeometryLayer.HDFGroupName=> "Geometry/Pipe Nodes";

	public RASPipeNodes(RASGeometry geometry)
		: base("Pipe Nodes", geometry.SourceFilename)
	{
		ForwardEditRequestsToParent = true;
		Geometry = geometry;
	}

	public void HDFLoadFeatureTable(H5Reader hr)
	{
		if (hr.ExistsAsGroup("Geometry/Pipe Nodes"))
		{
			HDF5Storage.ReadPointFeatures(this, hr, "Geometry/Pipe Nodes/Points");
			string path = "Geometry/Pipe Nodes/Attributes";
			ReadManagedColumnMetadata(hr, "Geometry/Pipe Nodes/Managed Columns");
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
			hw.Delete("Geometry/Pipe Nodes");
			return;
		}
		try
		{
			hw.CreateGroup("Geometry/Pipe Nodes");
			HDF5Storage.WritePointFeatures(this, hw, "Geometry/Pipe Nodes/Points");
			WriteManagedColumns(hw, "Geometry/Pipe Nodes/Attributes");
			WriteManagedColumnMetadata(hw, "Geometry/Pipe Nodes/Managed Columns");
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
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
		if (h5r.ExistsAsGroup("Geometry/Pipe Nodes"))
		{
			if (h5w.ExistsAsGroup("Geometry/Pipe Nodes"))
			{
				h5w.Delete("Geometry/Pipe Nodes");
			}
			h5r.CopyTo(h5w, "Geometry/Pipe Nodes", "Geometry/Pipe Nodes");
		}
	}
}
