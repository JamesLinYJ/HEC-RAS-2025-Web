using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Visual.Mapping.Observables;

public class ObservableClassificationMapRow : ObservableObject
{
	public delegate void ValueChangedEvent(ObservableClassificationMapRow sender, string oldValue, string newValue);

	public int Key { get; }
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string Value
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<string>.Default.Equals(field, value))
			{
				string oldValue = field;
				OnPropertyChanging("Value");
				field = value;
				OnValueChanged(oldValue, value);
				OnPropertyChanged("Value");
			}
		}
	}

	public event ValueChangedEvent ValueChanged;

	public ObservableClassificationMapRow(int key, string stringValue)
	{
		Key = key;
		Value = stringValue;
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnValueChanged(string oldValue, string newValue)
	{
		ValueChanged?.Invoke(this, oldValue, newValue);
	}
}
