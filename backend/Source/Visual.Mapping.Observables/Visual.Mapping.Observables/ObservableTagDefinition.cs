using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Visual.Mapping.Observables;

public class ObservableTagDefinition : ObservableObject
{
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public TagType Type
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<TagType>.Default.Equals(field, value))
			{
				OnPropertyChanging("Type");
				field = value;
				OnPropertyChanged("Type");
			}
		}
	}
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string Key
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<string>.Default.Equals(field, value))
			{
				OnPropertyChanging("Key");
				field = value;
				OnPropertyChanged("Key");
			}
		}
	}

	public ObservableCollection<ObservableTopologyFeature> FeaturesWithTag { get; } = new ObservableCollection<ObservableTopologyFeature>();

	public ObservableTagDefinition(TagType type, string key)
	{
		Type = type;
		Key = key;
	}

	public bool IsDefaultValue(object localValue)
	{
		if (localValue == null)
		{
			return true;
		}
		if (Type == TagType.String)
		{
			return string.IsNullOrEmpty((string)localValue);
		}
		if (Type == TagType.Integer)
		{
			return (localValue as int?).GetValueOrDefault() == 0;
		}
		if (Type == TagType.Numeric)
		{
			return (localValue as double?).GetValueOrDefault() == 0.0;
		}
		if (Type == TagType.Boolean)
		{
			return localValue as bool? != true;
		}
		throw new NotSupportedException();
	}

	public void RegisterFeature(ObservableTopologyFeature feature)
	{
		if (feature != null && !FeaturesWithTag.Contains(feature))
		{
			FeaturesWithTag.Add(feature);
		}
	}

	public void UnregisterFeature(ObservableTopologyFeature feature)
	{
		if (feature != null && FeaturesWithTag.Contains(feature))
		{
			FeaturesWithTag.Remove(feature);
		}
	}
}
