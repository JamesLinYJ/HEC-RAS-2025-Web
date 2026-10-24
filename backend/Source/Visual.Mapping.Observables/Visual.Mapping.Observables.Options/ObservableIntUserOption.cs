using Geospatial.Rendering.Editing.Options;

namespace Visual.Mapping.Observables.Options;

public sealed class ObservableIntUserOption : ObservableUserOption
{
	private readonly OptionKey<int> _typedKey;

	public int Value
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

	public ObservableIntUserOption(OptionStore store, OptionKey<int> key)
		: base(store, key)
	{
		_typedKey = key;
	}
}
