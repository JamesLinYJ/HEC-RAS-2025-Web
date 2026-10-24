using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Rasters.Modifiers;
using Geospatial.Vectors;
using Visual.Mapping.Observables;

namespace Ras.Observables.Layers;

public class ObservableLeveeMod : ObservableValidator, IObservableFeature<LeveeMod>
{
	private LeveeMod _leveeMod;

	public LeveeMod BaseFeature => _leveeMod;

	public ObservableStationElevationProfile Profile { get; private set; }

	public Polyline Line => _leveeMod.Line;

	public bool HasProperties
	{
		get
		{
			LeveeModProperties properties;
			return BaseFeature.TryEvaluateAssociations(out properties);
		}
	}

	public string Name
	{
		get
		{
			return _leveeMod.Name;
		}
		set
		{
			SetProperty(_leveeMod.Name, value, _leveeMod, (LeveeMod mod, string textValue) =>
			{
				mod.Name = textValue;
			}, "Name");
		}
	}

	public string LeveePropertyName
	{
		get
		{
			return _leveeMod.LeveePropertyName;
		}
		set
		{
			SetProperty(_leveeMod.LeveePropertyName, value, _leveeMod, (LeveeMod mod, string leveePropertyName) =>
			{
				mod.LeveePropertyName = leveePropertyName;
			}, "LeveePropertyName");
			OnPropertyChanged("HasProperties");
		}
	}

	public ObservableLeveeMod(LeveeMod leveeMod)
	{
		_leveeMod = leveeMod;
		Profile = new ObservableStationElevationProfile(leveeMod.Profile);
	}
}
