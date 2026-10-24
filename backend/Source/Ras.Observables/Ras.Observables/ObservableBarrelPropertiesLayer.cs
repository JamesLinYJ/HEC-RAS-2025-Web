using System;
using Ras.Hydraulics.Culverts;
using Ras.Layers;
using Utility.Interfaces;

namespace Ras.Observables;

public class ObservableBarrelPropertiesLayer : ObservableGeometryLayer<BarrelPropertiesLayer, BarrelProperties, ObservableBarrelProperties>, IReadOnlyDisplayName
{
	public string DisplayName => "Barrel Properties";

	public ObservableBarrelPropertiesLayer(ObservableGeometry geometry)
		: base(geometry, geometry.Geometry.BarrelPropertiesLayer, (Func<BarrelProperties, ObservableBarrelProperties>)((BarrelProperties barrelProps) => new ObservableBarrelProperties(barrelProps)))
	{
	}

	public ObservableBarrelProperties TryFindByName(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return null;
		}
		foreach (ObservableBarrelProperties feature in Features)
		{
			if (feature.Name == name)
			{
				return feature;
			}
		}
		return null;
	}
}
