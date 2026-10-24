using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial;
using Ras.Layers;
using Utility.Hashing;

namespace Ras.Observables;

public abstract class ObservableGeometryLayer<GeometryLayer, Feature, ObservableFeature> : ObservableFeatureLayer<GeometryLayer, Feature, ObservableFeature> where GeometryLayer : GeometryLayerBase<Feature>, IDataHash where Feature : IFeatureRow, new() where ObservableFeature : ObservableObject, IObservableFeature<Feature>
{
	private ObservableGeometry _geometry;

	public ObservableGeometry Geometry
	{
		get
		{
			return _geometry;
		}
		private set
		{
			SetProperty(ref _geometry, value, "Geometry");
		}
	}

	public ObservableGeometryLayer(ObservableGeometry geometry, GeometryLayer lyr, Func<Feature, ObservableFeature> makeFeatureObservable)
		: base(lyr, makeFeatureObservable)
	{
		Geometry = geometry;
	}
}
