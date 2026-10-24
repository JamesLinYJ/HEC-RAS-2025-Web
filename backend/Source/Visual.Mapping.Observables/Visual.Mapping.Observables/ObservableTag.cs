using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Visual.Mapping.Observables;

public class ObservableTag : ObservableObject, IDisposable
{
	private readonly ObservableTopologyFeature _owner;

	private readonly Func<object> _getValue;

	private bool _isRegisteredWithDefinition;
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public ObservableTagDefinition Definition
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<ObservableTagDefinition>.Default.Equals(field, value))
			{
				OnPropertyChanging("Definition");
				field = value;
				OnPropertyChanged("Definition");
			}
		}
	}

	public ObservableTopologyFeature Owner => _owner;

	public object Value => _getValue();

	public static ObservableTag CreateBlankTemplate(ObservableTagDefinition definition, object localValue)
	{
		return new ObservableTag(definition, () => localValue);
	}

	private ObservableTag(ObservableTagDefinition definition, Func<object> valueFactory)
	{
		Definition = definition ?? throw new ArgumentNullException("definition");
		_getValue = valueFactory ?? throw new ArgumentNullException("valueFactory");
		DebugCheckHasValue();
	}

	public ObservableTag(ObservableTopologyFeature owner, ObservableTagDefinition definition, Func<object> valueFactory)
	{
		_owner = owner ?? throw new ArgumentNullException("owner");
		Definition = definition ?? throw new ArgumentNullException("definition");
		_getValue = valueFactory ?? throw new ArgumentNullException("valueFactory");
		_owner.PropertyChanged += OwnerPropertyChanged;
		RefreshValue();
		DebugCheckHasValue();
	}

	private void DebugCheckHasValue()
	{
	}

	private void OwnerPropertyChanged(object sender, PropertyChangedEventArgs e)
	{
		RefreshValue();
	}

	private static bool HasMeaningfulValue(object localValue)
	{
		if (localValue == null)
		{
			return false;
		}
		if (localValue is string textValue)
		{
			return !string.IsNullOrWhiteSpace(textValue);
		}
		if (localValue is float f)
		{
			return !float.IsNaN(f);
		}
		if (localValue is double d)
		{
			return !double.IsNaN(d);
		}
		if (localValue is bool condition)
		{
			return !condition;
		}
		return true;
	}

	private void UpdateDefinitionMembership()
	{
		if (HasMeaningfulValue(_getValue()))
		{
			if (!_isRegisteredWithDefinition)
			{
				Definition?.RegisterFeature(_owner);
				_isRegisteredWithDefinition = true;
			}
		}
		else if (_isRegisteredWithDefinition)
		{
			Definition?.UnregisterFeature(_owner);
			_isRegisteredWithDefinition = false;
		}
	}

	public void RefreshValue()
	{
		UpdateDefinitionMembership();
		OnPropertyChanged("Value");
	}

	public void Dispose()
	{
		_owner.PropertyChanged -= OwnerPropertyChanged;
		if (_isRegisteredWithDefinition)
		{
			Definition?.UnregisterFeature(_owner);
		}
		_isRegisteredWithDefinition = false;
	}
}
