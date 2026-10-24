using CommunityToolkit.Mvvm.ComponentModel;
using Ras.Layers;
using Utility.DataStructures;

namespace Ras.Observables;

public class ObservableHydraulicDesignData : ObservableObject
{
	private readonly HydraulicDesignData _data;

	private AdvancedCollection<ObservableStableChannelDesign> _stableChannelDesigns;

	public IReadOnlyAdvancedCollection<ObservableStableChannelDesign> StableChannelDesigns => _stableChannelDesigns;

	public ObservableHydraulicDesignData(HydraulicDesignData data)
	{
		_data = data;
		_stableChannelDesigns = new AdvancedCollection<ObservableStableChannelDesign>();
		foreach (StableChannelDesign stableChannelDesign in _data.StableChannelDesigns)
		{
			_stableChannelDesigns.Add(new ObservableStableChannelDesign(stableChannelDesign));
		}
	}

	public void AddStableChannelDesign(ObservableStableChannelDesign scd)
	{
		_data.AddStableChannelDesign(scd.StableChannel);
		_stableChannelDesigns.Add(scd);
	}
}
