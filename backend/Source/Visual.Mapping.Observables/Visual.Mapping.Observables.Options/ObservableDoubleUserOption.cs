using Geospatial.Rendering.Editing.Options;

namespace Visual.Mapping.Observables.Options;

public sealed class ObservableDoubleUserOption : ObservableUserOption
{
	private readonly OptionKey<double> _typedKey;

	public double Value
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

	public ObservableDoubleUserOption(OptionStore store, OptionKey<double> key)
		: base(store, key)
	{
		_typedKey = key;
	}
}
