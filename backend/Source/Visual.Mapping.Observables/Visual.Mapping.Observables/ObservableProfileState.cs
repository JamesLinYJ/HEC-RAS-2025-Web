using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Rendering;

namespace Visual.Mapping.Observables;

public class ObservableProfileState : ObservableObject
{
	private readonly ProfileState _state;

	public int CurrentProfileIndex
	{
		get
		{
			return _state.CurrentProfileIndex;
		}
		set
		{
			SetProperty(_state.CurrentProfileIndex, value, _state, (ProfileState u, int n) =>
			{
				u.CurrentProfileIndex = n;
			}, "CurrentProfileIndex");
		}
	}

	public ObservableProfileState(ProfileState state)
	{
		_state = state;
	}
}
