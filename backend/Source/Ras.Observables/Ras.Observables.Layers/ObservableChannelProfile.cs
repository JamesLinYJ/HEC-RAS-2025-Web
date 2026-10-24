using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Ras.Layers.Terrains;
using Visual.Mapping.Observables;

namespace Ras.Observables.Layers;

public class ObservableChannelProfile : ObservableObject, IObservableFeature<ChannelProfile>
{
	public event Action<ObservableChannelProfile> DataChanged;
	private ChannelProfile _channelProfile;

	public ChannelProfile BaseFeature => _channelProfile;

	public ObservableStationElevationProfile Profile { get; private set; }

	public string Name
	{
		get
		{
			return _channelProfile.Name;
		}
		set
		{
			if (SetProperty(_channelProfile.Name, value, _channelProfile, (ChannelProfile mod, string textValue) =>
			{
				mod.Name = textValue;
			}, "Name")) DataChanged?.Invoke(this);
		}
	}

	public ObservableChannelProfile(ChannelProfile channelProfile)
	{
		_channelProfile = channelProfile;
		Profile = new ObservableStationElevationProfile(_channelProfile.Profile);
		Profile.DataChanged += () => DataChanged?.Invoke(this);
	}
}
