using System;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Geospatial.PairedData;
using Geospatial.Rasters.Modifiers;
using Geospatial.Vectors;
using Ras.Layers.Terrains;
using Ras.Observables.Layers;
using Utility;
using Utility.Interfaces;
using Utility.Validation;
using Visual.Observables.Interfaces;

namespace Ras.Observables;

public class ObservableLeveeModTerrainInput : ObservableFeatureLayer<LeveeModTerrainInput, LeveeMod, ObservableLeveeMod>, IObservableTerrainInput, IMutableNotifyDataErrorInfo, INotifyDataErrorInfo, IObservableDisplayName, IObservableReadOnlyDisplayName, IReadOnlyDisplayName, IDisplayName
{
	private ObservableTerrain _terrain;

	private readonly object _leveeModPropertiesLoadLock = new object();

	private ObservableLeveeModPropertiesLayer _leveeModProperties;

	private IPolylineCollection _plWrapper;

	public ObservableTerrain Terrain => _terrain;

	public ObservableLeveeModPropertiesLayer LeveeModProperties => LazyThreadsafe<ObservableLeveeModPropertiesLayer>.AllocateOnce(ref _leveeModProperties, _leveeModPropertiesLoadLock, () => new ObservableLeveeModPropertiesLayer(Layer.PropertiesTable));

	public ITerrainInput TerrainInput => Layer;

	public string Name
	{
		get
		{
			return Layer.Name;
		}
		set
		{
			if (SetProperty(Layer.Name, value, Layer, (LeveeModTerrainInput lyr, string textValue) =>
			{
				lyr.Name = textValue;
			}, "Name"))
			{
				NameChanged?.Invoke(this, value);
				OnPropertyChanged("DisplayName");
			}
		}
	}

	public string DisplayName
	{
		get
		{
			return Name;
		}
		set
		{
			Name = value;
		}
	}

	public bool HasData => Features.Count > 0;

	public bool HasErrors => false;

	public event IObservableReadOnlyDisplayName.NameChangedEvent NameChanged;

	public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged
	{
		add
		{
		}
		remove
		{
		}
	}

	public ObservableLeveeModTerrainInput(ObservableTerrain tr, LeveeModTerrainInput lyr)
		: base(lyr, (Func<LeveeMod, ObservableLeveeMod>)((LeveeMod levee) => new ObservableLeveeMod(levee)))
	{
		_terrain = tr;
		Features.CollectionChanged += FeaturesCollectionChanged;
	}

	private void FeaturesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
	{
		OnPropertyChanged("HasData");
	}

	public IEnumerable GetErrors(string propertyName)
	{
		throw new NotImplementedException();
	}

	public void ValidateProperty(object localValue, [CallerMemberName] string propertyName = null)
	{
		throw new NotImplementedException();
	}

	public IPolylineCollection AsPolylineCollection()
	{
		if (_plWrapper == null)
		{
			_plWrapper = new ObservablePolylineFtLayerWrapped<LeveeModTerrainInput, LeveeMod, ObservableLeveeMod>(this, (LeveeMod prof) => prof.Line, (Polyline pl) => new LeveeMod
			{
				Name = $"Feature {Features.Count + 1}",
				Line = pl,
				Profile = new StationElevationProfile(new _003C_003Ez__ReadOnlySingleElementList<double>(0.0), new _003C_003Ez__ReadOnlySingleElementList<double>(0.0))
			});
		}
		return _plWrapper;
	}
}
