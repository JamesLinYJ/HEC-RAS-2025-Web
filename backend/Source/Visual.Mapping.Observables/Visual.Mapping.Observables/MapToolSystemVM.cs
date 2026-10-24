using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Rendering.Systems.Tools;
using Geospatial.Rendering.TiledRenderingSystem;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Systems;

namespace Visual.Mapping.Observables;

public sealed class MapToolSystemVM : ObservableObject, IDisposable
{
	private static readonly ILogger _logger = LogHelper.GetLogger("MapToolSystemVM");

	private readonly Dictionary<string, MapTool> _toolCache = new Dictionary<string, MapTool>(StringComparer.Ordinal);

	public ToolSet ToolSet { get; }

	public PyramidedTileManager TileManager { get; }

	public Func<KeyCode, bool> KeyState { get; }
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public MapTool PrimaryTool
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<MapTool>.Default.Equals(field, value))
			{
				OnPropertyChanging("PrimaryTool");
				field = value;
				OnPropertyChanged("PrimaryTool");
			}
		}
	}
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public MapTool SecondaryTool
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<MapTool>.Default.Equals(field, value))
			{
				OnPropertyChanging("SecondaryTool");
				field = value;
				OnPropertyChanged("SecondaryTool");
			}
		}
	}

	public ToolGroup ActiveRootNode
	{
		get
		{
			if (ToolSet.ActiveDescriptor != null)
			{
				return ToolSet.GetRootNodeFor(ToolSet.ActiveDescriptor) as ToolGroup;
			}
			return null;
		}
	}

	public Func<ToolDescriptor, ToolDescriptor> PickSecondaryDescriptor
	{
		get;
		set
		{
			field = value;
			RefreshSecondaryTool();
		}
	}

	public MapToolSystemVM(ToolSet toolSet, PyramidedTileManager tileManager, Func<KeyCode, bool> keyState)
	{
		ArgumentNullException.ThrowIfNull(toolSet, "toolSet");
		ArgumentNullException.ThrowIfNull(tileManager, "tileManager");
		ArgumentNullException.ThrowIfNull(keyState, "keyState");
		ToolSet = toolSet;
		TileManager = tileManager;
		KeyState = keyState;
		ToolSet.ActiveDescriptorChanged += ToolSet_ActiveDescriptorChanged;
		ToolSet.RootNodes.CollectionChanged += ToolSet_RootNodesCollectionChanged;
		TryAutoActivateFirstTool();
	}

	private void ToolSet_RootNodesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.OldItems != null)
		{
			foreach (object oldItem in e.OldItems)
			{
				if (oldItem is ToolNode node)
				{
					EvictCacheForSubtree(node);
				}
			}
		}
		TryAutoActivateFirstTool();
		RefreshSecondaryTool();
		OnPropertyChanged("ActiveRootNode");
	}

	private void EvictCacheForSubtree(ToolNode node)
	{
		if (node is ToolLeaf { Tool: var tool } toolLeaf && tool?.Id != null)
		{
			_toolCache.Remove(toolLeaf.Tool.Id);
		}
		foreach (ToolNode descendant in node.Descendants)
		{
			if (descendant is ToolLeaf { Tool: var tool2 } toolLeaf2 && tool2?.Id != null)
			{
				_toolCache.Remove(toolLeaf2.Tool.Id);
			}
		}
	}

	private void ToolSet_ActiveDescriptorChanged(ToolSet sender, ToolDescriptor oldVal, ToolDescriptor newVal)
	{
		MapTool orBuild = GetOrBuild(newVal);
		if (orBuild != null)
		{
			PrimaryTool = orBuild;
			RefreshSecondaryTool();
			OnPropertyChanged("ActiveRootNode");
		}
	}

	private void TryAutoActivateFirstTool()
	{
		if (ToolSet.ActiveDescriptor != null)
		{
			return;
		}
		foreach (ToolNode rootNode in ToolSet.RootNodes)
		{
			if (ToolSet.TryActivate(rootNode.FaceDescriptor))
			{
				break;
			}
		}
	}

	private void RefreshSecondaryTool()
	{
		ToolDescriptor activeDescriptor = ToolSet.ActiveDescriptor;
		if (activeDescriptor == null)
		{
			SecondaryTool = null;
			return;
		}
		ToolDescriptor toolDescriptor = ((PickSecondaryDescriptor != null) ? PickSecondaryDescriptor(activeDescriptor) : PickDefaultSecondary(activeDescriptor));
		SecondaryTool = ((toolDescriptor == null || toolDescriptor == activeDescriptor) ? null : GetOrBuild(toolDescriptor));
	}

	private ToolDescriptor PickDefaultSecondary(ToolDescriptor primary)
	{
		if (ToolSet.RootNodes.Count < 2)
		{
			return null;
		}
		ToolNode toolNode = ToolSet.RootNodes[1];
		if (!toolNode.ContainsDescriptor(primary))
		{
			return toolNode.FaceDescriptor;
		}
		return ToolSet.RootNodes[0].FaceDescriptor;
	}

	public void Activate(ToolDescriptor descriptor)
	{
		if (descriptor == null)
		{
			throw new InvalidOperationException("A tool can not be null.");
		}
		if (!ToolSet.TryActivate(descriptor))
		{
			throw new InvalidOperationException("Could not find a tool matching descriptor '" + descriptor.Id + "'.");
		}
	}

	public void NotifyAvailabilityChanged()
	{
		ToolSet.ReevaluateAllAvailability();
	}

	public bool TryGetBuiltTool(ToolDescriptor descriptor, out MapTool tool)
	{
		if (descriptor == null)
		{
			tool = null;
			return false;
		}
		return _toolCache.TryGetValue(descriptor.Id, out tool);
	}

	public MapTool GetOrBuild(ToolDescriptor descriptor)
	{
		if (descriptor == null)
		{
			return null;
		}
		if (_toolCache.TryGetValue(descriptor.Id, out var localValue))
		{
			return localValue;
		}
		try
		{
			MapTool mapTool = descriptor.Build(TileManager, KeyState);
			if (mapTool == null)
			{
				_logger.LogError("Tool descriptor {Id} returned null from Build.", descriptor.Id);
				return null;
			}
			_toolCache[descriptor.Id] = mapTool;
			return mapTool;
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Failed to build tool {Id}.", descriptor.Id);
			return null;
		}
	}

	public void Dispose()
	{
		ToolSet.ActiveDescriptorChanged -= ToolSet_ActiveDescriptorChanged;
		ToolSet.RootNodes.CollectionChanged -= ToolSet_RootNodesCollectionChanged;
		ToolSet.Dispose();
		_toolCache.Clear();
	}
}
