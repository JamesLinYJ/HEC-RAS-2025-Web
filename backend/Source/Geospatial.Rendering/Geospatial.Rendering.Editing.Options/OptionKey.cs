using System;
using System.Diagnostics;

namespace Geospatial.Rendering.Editing.Options;

[DebuggerDisplay("OptionKey: {Id,nq} ({ValueType.Name,nq})")]
public abstract class OptionKey
{
	public string Id { get; }

	public string Label { get; }

	public string Description { get; }

	public Type ValueType { get; }

	public object DefaultValueBoxed { get; }

	private protected OptionKey(string id, string label, string description, Type valueType, object defaultValueBoxed)
	{
		Id = id;
		Label = label;
		Description = description;
		ValueType = valueType;
		DefaultValueBoxed = defaultValueBoxed;
	}
}
public sealed class OptionKey<T> : OptionKey
{
	public T DefaultValue { get; }

	public static OptionKey<T> Define(string id, string label, string description, T defaultValue)
	{
		return new OptionKey<T>(id, label, description, defaultValue);
	}

	private OptionKey(string id, string label, string description, T defaultValue)
		: base(id, label, description, typeof(T), defaultValue)
	{
		DefaultValue = defaultValue;
	}
}
