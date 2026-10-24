using System;
using Geospatial.Rendering.Editing.Options;

namespace Visual.Mapping.Observables.Options;

public sealed class ObservableEnumUserOption : ObservableUserOption
{
	private readonly Func<object> _get;

	private readonly Action<object> _set;

	public Type EnumType => Key.ValueType;

	public object Value
	{
		get
		{
			return _get();
		}
		set
		{
			if (!object.Equals(_get(), value))
			{
				_set(value);
				OnPropertyChanged("Value");
			}
		}
	}

	internal ObservableEnumUserOption(OptionStore store, OptionKey key, Func<object> get, Action<object> set)
		: base(store, key)
	{
		_get = get;
		_set = set;
	}
}
