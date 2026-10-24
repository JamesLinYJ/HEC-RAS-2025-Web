using H5Assist.VB;

namespace RasMapperLib;

public class GeometryGroupLayer : GroupLayer, IGeometryLayer
{
	public override bool CanEdit { get; }

	public override bool CanRemove { get; }

	public override bool CanRename { get; }

	public override bool ForwardEditRequestsToParent { get; }

	public RASGeometry Geometry { get; }

	public string HDFGroupName => "";

	public GeometryGroupLayer(RASGeometry geometry, string lyrName)
		: base(lyrName)
	{
		CanEdit = false;
		CanRemove = false;
		CanRename = false;
		ForwardEditRequestsToParent = true;
		Geometry = geometry;
	}

	public override void OnStartEditSession()
	{
		base.OnStartEditSession();
		if (IsSelected)
		{
			Expand();
		}
	}

	public void HDFLoadFeatureTable(H5Reader hr)
	{
	}

	void IGeometryLayer.HDFLoadFeatureTable(H5Reader hr)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFLoadFeatureTable
		this.HDFLoadFeatureTable(hr);
	}

	public void HDFSaveFeatureTable(H5Writer hw)
	{
	}

	void IGeometryLayer.HDFSaveFeatureTable(H5Writer hw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFSaveFeatureTable
		this.HDFSaveFeatureTable(hw);
	}
}
