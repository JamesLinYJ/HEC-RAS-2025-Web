using System.Collections.Generic;
using Geospatial;
using H5Assist;

namespace Ras.Layers;

public abstract class GeometryLayerBase<T> : FeatureLayerBase<T>, IGeometryLayer, IFeatureLayer where T : IFeatureRow, new()
{
	public override bool IsReadOnly => Geometry.IsResult;

	public Geometry Geometry { get; }

	public GeometryLayerBase(Geometry geom)
	{
		Geometry = geom;
	}

	protected override List<T> LoadFeaturesInternal()
	{
		return Geometry.CheckInitializeData(ref _loadedFeatures, _readAction);
	}

	protected override int PeekCountInternal()
	{
		if (Geometry.IsInMemory)
		{
			return Count;
		}
		return Geometry.UseReader((H5Reader hr) =>
		{
			string hdF5GroupAttrDatasetNameTerm = HDF5Group + "/" + _attrDatasetName;
			return (int)(hr.ExistsAsDataset(hdF5GroupAttrDatasetNameTerm) ? hr.GetDatasetDimensions(hdF5GroupAttrDatasetNameTerm)[0] : 0);
		});
	}
}
