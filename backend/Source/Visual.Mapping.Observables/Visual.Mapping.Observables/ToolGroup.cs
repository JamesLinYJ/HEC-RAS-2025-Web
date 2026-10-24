using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Visual.Mapping.Observables;

[DebuggerDisplay("Group: {Id,nq} [{_children.Count} children] Face={FaceDescriptor?.Id,nq}")]
public class ToolGroup : ToolNode
{
	private ObservableCollection<ToolNode> _children = new ObservableCollection<ToolNode>();

	private List<ToolNode> _descendants = new List<ToolNode>();

	public string Id { get; init; }

	public ToolDescriptor Default { get; init; }

	public override IReadOnlyList<ToolNode> Children => _children;

	public override IReadOnlyList<ToolNode> Descendants => _descendants;
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public ToolDescriptor DisplayDescriptor
	{
		get
		{
			return field;
		}
		internal set
		{
			if (!EqualityComparer<ToolDescriptor>.Default.Equals(field, value))
			{
				OnPropertyChanging("DisplayDescriptor");
				OnPropertyChanging("FaceDescriptor");
				field = value;
				OnPropertyChanged("DisplayDescriptor");
				OnPropertyChanged("FaceDescriptor");
			}
		}
	}

	public override ToolDescriptor FaceDescriptor => DisplayDescriptor ?? Default;

	public ToolGroup(string id, ToolDescriptor defaultDescriptor, params ToolNode[] children)
	{
		Id = id;
		Default = defaultDescriptor;
		_children.CollectionChanged += ChildCollectionChanged;
		if (children != null)
		{
			foreach (ToolNode children2 in children)
			{
				_children.Add(children2);
			}
		}
	}

	private void ChildCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.NewItems != null)
		{
			foreach (ToolNode newItem in e.NewItems)
			{
				_descendants.Add(newItem);
				if (newItem is ToolGroup toolGroup)
				{
					_descendants.AddRange(toolGroup.Descendants);
				}
			}
		}
		if (e.OldItems != null)
		{
			foreach (ToolNode oldItem in e.OldItems)
			{
				_descendants.Remove(oldItem);
				if (!(oldItem is ToolGroup toolGroup2))
				{
					continue;
				}
				foreach (ToolNode descendant in toolGroup2.Descendants)
				{
					_descendants.Remove(descendant);
				}
			}
		}
		if (e.Action == NotifyCollectionChangedAction.Reset)
		{
			_descendants.Clear();
		}
	}

	public void AddChild(ToolNode child)
	{
		ArgumentNullException.ThrowIfNull(child, "child");
		_children.Add(child);
	}

	public override bool ContainsDescriptor(ToolDescriptor descriptor)
	{
		if (descriptor == null)
		{
			return false;
		}
		foreach (ToolNode child in Children)
		{
			if (child.ContainsDescriptor(descriptor))
			{
				return true;
			}
		}
		return false;
	}
}
