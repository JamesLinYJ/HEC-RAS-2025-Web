using System;
using System.Collections;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Geospatial.Rasters;
using Ras.Layers.Terrains;
using Ras.Observables.Layers;
using Utility.Validation;

namespace Ras.Observables;

public class ObservableEllipseRasterModificationLayer : ObservableFeatureLayer<EllipseRasterModificationLayer, EllipseRasterMod, ObservableEllipseRasterMod>, IObservableTerrainInput, IMutableNotifyDataErrorInfo, INotifyDataErrorInfo
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

	public bool HasErrors
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged
	{
		add
		{
		}
		remove
		{
		}
	}

	public ObservableEllipseRasterModificationLayer(ObservableTerrain tr, EllipseRasterModificationLayer lyr)
		: base(lyr, (Func<EllipseRasterMod, ObservableEllipseRasterMod>)((EllipseRasterMod mod) => new ObservableEllipseRasterMod(mod)))
	{
		_ = Features.Count;
		_terrain = tr;
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
