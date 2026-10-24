using Geospatial.Rendering.Editing.Options;

namespace Visual.Mapping.Observables.Options;

public sealed class ObservableBoolUserOption : ObservableUserOption
{
	private readonly OptionKey<bool> _typedKey;

	public bool Value
	{
		get
		{
			return _store.Get(_typedKey);
		}
		set
		{
			if (_store.Get(_typedKey) != value)
			{
				_store.Set(_typedKey, value);
				OnPropertyChanged("Value");
			}
		}
	}

	public ObservableBoolUserOption(OptionStore store, OptionKey<bool> key)
		: base(store, key)
	{
		_typedKey = key;
	}
}
