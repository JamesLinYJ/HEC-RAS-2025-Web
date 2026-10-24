using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using Utility.Attributes;
using Utility.Interfaces;
using Visual.Observables.Interfaces;

namespace Visual.Observables.Tree;

[Identifier("NotFound")]
public class LayerNotFoundVM : ObservableObject, IObservableDisplayName, IObservableReadOnlyDisplayName, IReadOnlyDisplayName, IDisplayName
{
	public static Type Type => typeof(LayerNotFoundVM);

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
	public ITree ParentTree
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<ITree>.Default.Equals(field, value))
			{
				OnPropertyChanging("ParentTree");
				field = value;
				OnPropertyChanged("ParentTree");
			}
		}
	}

	public event IObservableReadOnlyDisplayName.NameChangedEvent NameChanged;

	public LayerNotFoundVM(ITree parentTree, string name = "")
	{
		ParentTree = parentTree;
		DisplayName = name;
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnDisplayNameChanged(string oldValue, string newValue)
	{
		NameChanged?.Invoke(this, newValue);
	}
}
