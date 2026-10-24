using System;
using System.Reflection;
using Geospatial.Rendering.Editing.Options;

namespace Visual.Mapping.Observables.Options;

public static class ObservableUserOptionFactory
{
	public static ObservableUserOption Create(OptionStore store, OptionKey key)
	{
		ArgumentNullException.ThrowIfNull(store, "store");
		ArgumentNullException.ThrowIfNull(key, "key");
		if (key is OptionKey<bool> key2)
		{
			return new ObservableBoolUserOption(store, key2);
		}
		if (key is OptionKey<double> key3)
		{
			return new ObservableDoubleUserOption(store, key3);
		}
		if (key is OptionKey<int> key4)
		{
			return new ObservableIntUserOption(store, key4);
		}
		if (key.ValueType.IsEnum)
		{
			return CreateEnumOption(store, key);
		}
		throw new NotSupportedException("OptionKey value type '" + key.ValueType.Name + "' is not supported by the options flyout. Add a wrapper subclass and a NumericPropertyItem-style widget rendering for it.");
	}

	private static ObservableEnumUserOption CreateEnumOption(OptionStore store, OptionKey key)
	{
		Type valueType = key.ValueType;
		MethodInfo getMethod = typeof(OptionStore).GetMethod("Get").MakeGenericMethod(valueType);
		MethodInfo setMethod = typeof(OptionStore).GetMethod("Set").MakeGenericMethod(valueType);
		Func<object> get = () => getMethod.Invoke(store, new object[1] { key });
		Action<object> set = (object v) =>
		{
			setMethod.Invoke(store, new object[2] { key, v });
		};
		return new ObservableEnumUserOption(store, key, get, set);
	}
}
