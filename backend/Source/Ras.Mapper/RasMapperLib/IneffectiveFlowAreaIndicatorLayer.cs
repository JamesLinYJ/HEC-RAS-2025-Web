using H5Assist.VB;

namespace RasMapperLib;

public class IneffectiveFlowAreaIndicatorLayer : PolylineFeatureLayer, IGeometryLayer, IVirtualFeatureLayer
{
	private RASXS _xs;

	public override bool CanEdit { get; }

	public override bool ForwardEditRequestsToParent { get; }

	public override string DefaultLayerName => "Ineffective";

	public RASGeometry Geometry => _xs?.Geometry;

	public FeatureLayer Layer => this;

	public FeatureLayer SourceLayer => _xs;

	public string HDFGroupName => "";

	public string CountAttributeName => "";

	public IneffectiveFlowAreaIndicatorLayer(RASXS xsLayer)
		: base("Ineffective")
	{
		CanEdit = false;
		ForwardEditRequestsToParent = true;
		_xs = xsLayer;
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
