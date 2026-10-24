using System;
using Geospatial.Rasters.Modifiers;
using Ras.Layers.Terrains;
using Ras.Observables.Layers;
using Utility.Interfaces;

namespace Ras.Observables;

public class ObservableLeveeModPropertiesLayer : ObservableFeatureLayer<LeveeModPropertiesLayer, LeveeModProperties, ObservableLeveeModProperties>, IReadOnlyDisplayName
{
	public string DisplayName => "Levee Properties";

	public ObservableLeveeModPropertiesLayer(LeveeModPropertiesLayer lyr)
		: base(lyr, (Func<LeveeModProperties, ObservableLeveeModProperties>)((LeveeModProperties props) => new ObservableLeveeModProperties(props)))
	{
	}

	public ObservableLeveeModProperties TryFindByName(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return null;
		}
		foreach (ObservableLeveeModProperties feature in Features)
		{
			if (feature.Name == name)
			{
				return feature;
			}
		}
		return null;
	}
}
