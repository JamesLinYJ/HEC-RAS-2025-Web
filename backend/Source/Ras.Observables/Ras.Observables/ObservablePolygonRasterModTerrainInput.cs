using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Geospatial.Rasters;
using Ras.Layers.Terrains;
using Ras.Observables.Layers;
using Utility.Validation;

namespace Ras.Observables;

public class ObservablePolygonRasterModTerrainInput : ObservableFeatureLayer<PolygonRasterModTerrainInput, PolygonRasterMod, ObservablePolygonRasterMod>, IObservableTerrainInput, IMutableNotifyDataErrorInfo, INotifyDataErrorInfo
{
	private ObservableTerrain _terrain;

	public ObservableTerrain Terrain => _terrain;

	public ITerrainInput TerrainInput => Layer;

	public string Name
	{
		get
		{
			return Layer.Name;
		}
		set
		{
			Layer.Name = value;
		}
	}

	public bool HasErrors => false;

	public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged
	{
		add
		{
		}
		remove
		{
		}
	}

	public ObservablePolygonRasterModTerrainInput(ObservableTerrain tr, PolygonRasterModTerrainInput lyr)
		: base(lyr, (Func<PolygonRasterMod, ObservablePolygonRasterMod>)((PolygonRasterMod mod) => new ObservablePolygonRasterMod(mod)))
	{
		_ = Features.Count;
		_terrain = tr;
	}

	public void AlertNewMods(IEnumerable<int> featureIdxs)
	{
		foreach (int featureIdx in featureIdxs)
		{
			AlertNewFeature(new ObservablePolygonRasterMod(Layer[featureIdx]));
		}
	}

	public void AlertModsRemoved(IEnumerable<int> featureIdxs)
	{
		foreach (int orderByDescending in featureIdxs.OrderByDescending((int i) => i))
		{
			DeleteFeatureAt(orderByDescending);
		}
	}

	public void ValidateProperty(object localValue, [CallerMemberName] string propertyName = null)
	{
		throw new NotImplementedException();
	}

	public IEnumerable GetErrors(string propertyName)
	{
		throw new NotImplementedException();
	}
}
