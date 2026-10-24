using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using Utility.Interfaces;
using Visual.Observables.Interfaces;
using Visual.Observables.Linking;
using Visual.Observables.Notifications;

namespace Visual.Observables.Tree;

public class TreeNodeVM : ObservableObject
{
	public delegate void IsCheckedChangedEvent(TreeNodeVM sender, bool newValue);

	private readonly Linkage _linkage = new Linkage();

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string Name
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<string>.Default.Equals(field, value))
			{
				OnPropertyChanging("Name");
				field = value;
				OnPropertyChanged("Name");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsChecked
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				bool oldValue = field;
				OnIsCheckedChanging(oldValue, value);
				OnPropertyChanging("IsChecked");
				field = value;
				OnIsCheckedChanged(value);
				OnPropertyChanged("IsChecked");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsPinned
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				bool oldValue = field;
				OnPropertyChanging("IsPinned");
				field = value;
				OnIsPinnedChanged(oldValue, value);
				OnPropertyChanged("IsPinned");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsNestNodesAllowed
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("IsNestNodesAllowed");
				field = value;
				OnPropertyChanged("IsNestNodesAllowed");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsPinnable
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("IsPinnable");
				field = value;
				OnPropertyChanged("IsPinnable");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public object Context
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<object>.Default.Equals(field, value))
			{
				object oldValue = field;
				OnPropertyChanging("Context");
				field = value;
				OnContextChanged(oldValue, value);
				OnPropertyChanged("Context");
			}
		}
	}

	public Linkage Linkage => _linkage;

	public bool IsCheckable { get; set; } = true;

	public event IsCheckedChangedEvent IsCheckedChanged;

	public TreeNodeVM()
	{
	}

	public TreeNodeVM(string name, bool isCheckable = true, bool isPinnable = true)
	{
		Name = name;
		IsPinnable = isPinnable;
		if (!isCheckable)
		{
			IsChecked = true;
		}
		IsCheckable = isCheckable;
	}

	public void SetCheckedStateIfAllowed(bool isChecked)
	{
		if (IsCheckable)
		{
			IsChecked = isChecked;
		}
	}

	private void Dn_NameChanged(IObservableReadOnlyDisplayName sender, string newName)
	{
		PushNameFromContextToName();
	}

	private void PushNameFromContextToName()
	{
		if (!(Context is IReadOnlyDisplayName readOnlyDisplayName))
		{
			throw new ArgumentException("Context is not a IObservableReadOnlyDisplayName");
		}
		Name = readOnlyDisplayName.DisplayName;
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnIsCheckedChanging(bool oldValue, bool newValue)
	{
		if (!IsCheckable)
		{
			throw new InvalidOperationException("This node has IsCheckable set to false.");
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnIsCheckedChanged(bool condition)
	{
		ToastNotifiers.TryCatchToast(() =>
		{
			IsCheckedChanged?.Invoke(this, condition);
		}, "Checking node");
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnIsPinnedChanged(bool oldValue, bool newValue)
	{
		if (!IsPinnable)
		{
			throw new InvalidOperationException("This node has IsPinnable set to false.");
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnContextChanged(object oldValue, object newValue)
	{
		if (oldValue is IObservableReadOnlyDisplayName observableReadOnlyDisplayName)
		{
			observableReadOnlyDisplayName.NameChanged -= Dn_NameChanged;
		}
		if (newValue is IObservableReadOnlyDisplayName observableReadOnlyDisplayName2)
		{
			observableReadOnlyDisplayName2.NameChanged += Dn_NameChanged;
			PushNameFromContextToName();
		}
		else if (newValue is IReadOnlyDisplayName)
		{
			PushNameFromContextToName();
		}
	}
}
