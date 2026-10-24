using System;
using Ras.Hydraulics.Culverts;
using Ras.Layers;
using Utility.Interfaces;

namespace Ras.Observables;

public class ObservableOpeningPropertiesLayer : ObservableGeometryLayer<OpeningPropertiesLayer, OpeningProperties, ObservableOpeningProperties>, IReadOnlyDisplayName
{
	public string DisplayName => "Opening Properties";

	public ObservableOpeningPropertiesLayer(ObservableGeometry geometry)
		: base(geometry, geometry.Geometry.OpeningPropertiesLayer, (Func<OpeningProperties, ObservableOpeningProperties>)((OpeningProperties openingProps) => new ObservableOpeningProperties(openingProps)))
	{
	}

	public ObservableOpeningProperties TryFindByName(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return null;
		}
		foreach (ObservableOpeningProperties feature in Features)
		{
			if (feature.Name == name)
			{
				return feature;
			}
		}
		return null;
	}
}
