using H5Assist.VB;

namespace RasMapperLib;

public class BankStationLayer : MultiPointFeatureLayer, IGeometryLayer, IVirtualFeatureLayer
{
	private RASXS _xs;

	public override bool CanEdit { get; }

	public override bool ForwardEditRequestsToParent { get; }

	public override string DefaultLayerName => "Bank Stations";

	public RASGeometry Geometry => _xs?.Geometry;

	public FeatureLayer Layer => this;

	public FeatureLayer SourceLayer => _xs;

	public string HDFGroupName => "";

	public string CountAttributeName => "";

	public BankStationLayer(RASXS xsLayer)
		: base("Bank Stations")
	{
		CanEdit = false;
		ForwardEditRequestsToParent = true;
		_xs = xsLayer;
	}

	public override Feature Feature(int fId)
	{
		PointMs pointMs = new PointMs();
		double distance = _xs.GetLeftBankStation(fId);
		double distance2 = _xs.GetRightBankStation(fId);
		Polyline polyline = _xs.Polyline(fId);
		if (polyline == null || polyline.Count < 2)
		{
			return new MultiPoint(pointMs);
		}
		pointMs.Add(polyline.PointAtDistance(distance).PointM);
		pointMs.Add(polyline.PointAtDistance(distance2).PointM);
		return new MultiPoint(pointMs);
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
