using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Utility.Attributes;
using Utility.Interfaces;
using Visual.Observables.Interfaces;

namespace Visual.Observables.Tree;

[Identifier("Folder")]
public class FolderVM : ObservableObject, ISerializable, IObservableDisplayName, IObservableReadOnlyDisplayName, IReadOnlyDisplayName, IDisplayName
{
	public static Type Type => typeof(FolderVM);

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string DisplayName
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
				OnPropertyChanging("DisplayName");
				field = value;
				OnDisplayNameChanged(oldValue, value);
				OnPropertyChanged("DisplayName");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsDefault
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("IsDefault");
				field = value;
				OnPropertyChanged("IsDefault");
			}
		}
	}

	public event IObservableReadOnlyDisplayName.NameChangedEvent NameChanged;

	public FolderVM(string name = "")
	{
		DisplayName = name;
	}

	public void Deserialize(XElement element)
	{
	}

	public void Serialize(XElement serializeTo)
	{
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnDisplayNameChanged(string oldValue, string newValue)
	{
		NameChanged?.Invoke(this, newValue);
	}
}
