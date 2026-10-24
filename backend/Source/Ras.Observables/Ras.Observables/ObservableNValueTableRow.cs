using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Ras.Observables;

public class ObservableNValueTableRow : ObservableValidator
{
	public delegate void KeyChangedEvent(ObservableNValueTableRow sender, string oldValue, string newValue);

	public delegate void ValueChangedEvent(ObservableNValueTableRow sender, float oldValue, float newValue);
	private string _key;
	private float _value;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string Key
	{
		get
		{
			return _key;
		}
		[MemberNotNull("_key")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_key, value))
			{
				string key = _key;
				OnPropertyChanging("Key");
				_key = value;
				OnKeyChanged(key, value);
				OnPropertyChanged("Key");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public float Value
	{
		get
		{
			return _value;
		}
		set
		{
			if (!EqualityComparer<float>.Default.Equals(_value, value))
			{
				float computed = _value;
				OnPropertyChanging("Value");
				_value = value;
				OnValueChanged(computed, value);
				OnPropertyChanged("Value");
			}
		}
	}

	public event KeyChangedEvent KeyChanged;

	public event ValueChangedEvent ValueChanged;

	public ObservableNValueTableRow(string key, float floatValue)
	{
		Key = key;
		Value = floatValue;
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnKeyChanged(string? oldValue, string newValue)
	{
		KeyChanged?.Invoke(this, oldValue, newValue);
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnValueChanged(float oldValue, float newValue)
	{
		ValueChanged?.Invoke(this, oldValue, newValue);
	}
}
