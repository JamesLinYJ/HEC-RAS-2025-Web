using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial;
using Ras.Layers;
using Utility.Hashing;

namespace Ras.Observables;

public static class ObservableFeatureLayerExtensions
{
	public static ObsFt AddNamedFeature<GeomLyr, GeomFt, ObsFt>(this ObservableFeatureLayer<GeomLyr, GeomFt, ObsFt> layer, string namePrefix) where GeomLyr : FeatureLayerBase<GeomFt>, IDataHash where GeomFt : IFeatureRow, new() where ObsFt : ObservableObject, IObservableFeature<GeomFt>, INamedObservableFeature, new()
	{
		if (layer == null)
		{
			return null;
		}
		ObsFt obsFt = new ObsFt
		{
			Name = layer.MakeUniqueName(namePrefix)
		};
		layer.AddFeature(obsFt);
		return obsFt;
	}

	public static void DeleteNamedFeatures<GeomLyr, GeomFt, ObsFt>(this ObservableFeatureLayer<GeomLyr, GeomFt, ObsFt> layer, IEnumerable<INamedObservableFeature> features) where GeomLyr : FeatureLayerBase<GeomFt>, IDataHash where GeomFt : IFeatureRow, new() where ObsFt : ObservableObject, IObservableFeature<GeomFt>, INamedObservableFeature
	{
		if (layer == null || features == null)
		{
			return;
		}
		foreach (INamedObservableFeature feature in features)
		{
			if (feature is ObsFt ft)
			{
				layer.DeleteFeature(ft);
			}
		}
	}

	public static string MakeUniqueName<GeomLyr, GeomFt, ObsFt>(this ObservableFeatureLayer<GeomLyr, GeomFt, ObsFt> layer, string namePrefix) where GeomLyr : FeatureLayerBase<GeomFt>, IDataHash where GeomFt : IFeatureRow, new() where ObsFt : ObservableObject, IObservableFeature<GeomFt>, INamedObservableFeature
	{
		HashSet<string> ordinalIgnoreCase = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (ObsFt layer2 in layer)
		{
			if (!string.IsNullOrEmpty(layer2.Name))
			{
				ordinalIgnoreCase.Add(layer2.Name);
			}
		}
		int unity = 1;
		string message;
		while (true)
		{
			message = $"{namePrefix} {unity}";
			if (!ordinalIgnoreCase.Contains(message))
			{
				break;
			}
			unity++;
		}
		return message;
	}
}
