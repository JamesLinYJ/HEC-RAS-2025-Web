using CommunityToolkit.Mvvm.ComponentModel;
using Ras.Layers;

namespace Ras.Observables;

public class ObservableStableChannelDesign : ObservableObject
{
	private StableChannelDesign _stableChannel;

	public StableChannelDesign StableChannel => _stableChannel;

	public ObservableTractiveForceMethod TractiveForce { get; }

	public ObservableStableChannelDesign(StableChannelDesign stableChannel)
	{
		_stableChannel = stableChannel;
		TractiveForce = new ObservableTractiveForceMethod(_stableChannel.TractiveForce);
	}
}
