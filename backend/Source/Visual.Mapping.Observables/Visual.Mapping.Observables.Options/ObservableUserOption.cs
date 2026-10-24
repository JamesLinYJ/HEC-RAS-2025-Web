using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Rendering.Editing.Options;

namespace Visual.Mapping.Observables.Options;

public abstract class ObservableUserOption : ObservableObject
{
	protected readonly OptionStore _store;

	public OptionKey Key { get; }

	public string Label => Key.Label;

	public string Description => Key.Description;

	protected ObservableUserOption(OptionStore store, OptionKey key)
	{
		ArgumentNullException.ThrowIfNull(store, "store");
		ArgumentNullException.ThrowIfNull(key, "key");
		_store = store;
		Key = key;
	}
}
