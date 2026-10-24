using H5Assist.VB;

namespace RasMapperLib;

public class InterpretationOverrideGroupLayer : GeometryGroupLayer
{
	private PolygonFeatureLayer _regionsLayer;

	public InterpretationOverrideGroupLayer(RASGeometry geometry, string lyrName, PolygonFeatureLayer regionsLayer)
		: base(geometry, lyrName)
	{
		_regionsLayer = regionsLayer;
	}

	public override bool LayerHasData(H5Reader hr = null)
	{
		if (_regionsLayer is InterpretationOverrideLayer)
		{
			return ((InterpretationOverrideLayer)_regionsLayer).ClassificationLayer != null;
		}
		if (_regionsLayer is RasLandCoverRegions)
		{
			return ((RasLandCoverRegions)_regionsLayer).Geometry.NValueAssociation.Layer != null;
		}
		return base.LayerHasData(hr);
	}
}
