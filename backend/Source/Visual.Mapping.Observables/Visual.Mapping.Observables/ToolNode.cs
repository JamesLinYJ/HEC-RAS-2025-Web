using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using Utility.DataStructures;

namespace Visual.Mapping.Observables;

[DebuggerDisplay("Node: {FaceDescriptor?.Id,nq} (Active={IsActive}, Available={IsAvailable})")]
public abstract class ToolNode : ObservableObject, IDisposable
{
	public abstract ToolDescriptor FaceDescriptor { get; }

	public Func<bool> AvailableWhen { get; init; }

	public int ZIndex { get; init; }

	public bool IsExperimental { get; init; }

	public virtual IReadOnlyList<ToolNode> Children => AdvancedCollection<ToolNode>.Empty;

	public virtual IReadOnlyList<ToolNode> Descendants { get; } = Array.Empty<ToolNode>();
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowLabel
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("ShowLabel");
				field = value;
				OnPropertyChanged("ShowLabel");
			}
		}
	}
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public ToolLabelOrientation LabelOrientation
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<ToolLabelOrientation>.Default.Equals(field, value))
			{
				OnPropertyChanging("LabelOrientation");
				field = value;
				OnPropertyChanged("LabelOrientation");
			}
		}
	}
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public ToolBarOrientation BarOrientation
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<ToolBarOrientation>.Default.Equals(field, value))
			{
				OnPropertyChanging("BarOrientation");
				field = value;
				OnPropertyChanged("BarOrientation");
			}
		}
	}
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsActive
	{
		get
		{
			return field;
		}
		internal set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("IsActive");
				field = value;
				OnPropertyChanged("IsActive");
			}
		}
	}
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsAvailable
	{
		get
		{
			return field;
		}
		internal set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("IsAvailable");
				field = value;
				OnPropertyChanged("IsAvailable");
			}
		}
	}

	public event Action<ToolNode> ActivateRequested;

	public void Activate()
	{
		ActivateRequested?.Invoke(this);
	}

	public ToolNode()
	{
	}

	public abstract bool ContainsDescriptor(ToolDescriptor descriptor);

	public virtual void Dispose()
	{
		foreach (ToolNode child in Children)
		{
			child.Dispose();
		}
	}
}
