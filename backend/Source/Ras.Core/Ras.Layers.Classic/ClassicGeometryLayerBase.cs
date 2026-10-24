using System.Collections.Generic;
using Geospatial;

namespace Ras.Layers.Classic;

public abstract class ClassicGeometryLayerBase<T> : FeatureLayerBase<T>, IClassicGeometryLayer, IFeatureLayer where T : IFeatureRow, new()
{
	public override bool IsReadOnly => Geometry.IsResult;

	public ClassicGeometry Geometry { get; }

	public ClassicGeometryLayerBase(ClassicGeometry geom)
	{
		Geometry = geom;
	}

	protected override List<T> LoadFeaturesInternal()
	{
		return Geometry.CheckInitializeData(ref _loadedFeatures, _readAction);
	}

	protected override int PeekCountInternal()
	{
		return Count;
	}
}
