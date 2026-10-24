using H5Assist.VB;

namespace RasMapperLib;

public class InterpolationSurfaceMLayer : TinFeatureLayer, IGeometryLayer, IVirtualFeatureLayer
{
	private static readonly double[] InterpolationMDefaultValues = new double[5] { -2.0, -1.0, 0.0, 1.0, 2.0 };

	private RASXSInterpolationSurface _interpSurface;

	public override bool CanEdit { get; }

	public RASGeometry Geometry => _interpSurface?.Geometry;

	public override string DefaultLayerName => "Interpolation M";

	public FeatureLayer Layer => this;

	public FeatureLayer SourceLayer => _interpSurface;

	public string HDFGroupName => "";

	public string CountAttributeName => "";

	public InterpolationSurfaceMLayer(RASXSInterpolationSurface xsSurface)
		: base("Interpolation M")
	{
		CanEdit = false;
		_interpSurface = xsSurface;
		SelectedPlotOptions["TIN Plot M Surface"] = true;
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
