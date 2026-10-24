using System;
using System.Data;
using H5Assist.VB;

namespace RasMapperLib;

public class VirtualGeometryFeatureLayer : VirtualFeatureLayer, IGeometryLayer
{
	public RASGeometry Geometry => ((IGeometryLayer)SourceLayer).Geometry;

	public string HDFGroupName => ((IGeometryLayer)SourceLayer).HDFGroupName;

	public VirtualGeometryFeatureLayer(IGeometryLayer sourceLayer, string countAttributeName, Predicate<DataRow> featureRowFilter = null)
		: base((FeatureLayer)sourceLayer, countAttributeName, featureRowFilter)
	{
	}

	public void HDFLoadFeatureTable(H5Reader hr)
	{
		((IGeometryLayer)SourceLayer).HDFLoadFeatureTable(hr);
	}

	void IGeometryLayer.HDFLoadFeatureTable(H5Reader hr)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFLoadFeatureTable
		this.HDFLoadFeatureTable(hr);
	}

	public void HDFSaveFeatureTable(H5Writer hw)
	{
		((IGeometryLayer)SourceLayer).HDFSaveFeatureTable(hw);
	}

	void IGeometryLayer.HDFSaveFeatureTable(H5Writer hw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFSaveFeatureTable
		this.HDFSaveFeatureTable(hw);
	}
}
