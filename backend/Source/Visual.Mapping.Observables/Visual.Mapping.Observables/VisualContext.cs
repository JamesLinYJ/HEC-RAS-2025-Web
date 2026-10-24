using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Rendering.Editing;
using Geospatial.Rendering.Layers;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.DataStructures;
using Utility.Interfaces;
using Utility.Logging;
using Utility.Serialization.XML;
using Visual.Mapping.Observables.Map;
using Visual.Mapping.Observables.Selection;
using Visual.Observables;
using Visual.Observables.Interfaces;
using Visual.Observables.Linking;

namespace Visual.Mapping.Observables;

public abstract class VisualContext : ObservableValidator, ISerializable, ILinkProvider, IObservableReadOnlyDisplayName, IReadOnlyDisplayName
{
	public delegate void RenderLayerInitializedEvent(VisualContext sender, IRenderLayer builtLayer);

	public delegate void EmittedRenderLayersChangedEvent(VisualContext sender);

	private static readonly ILogger _logger = LogHelper.GetLogger("VisualContext");

	protected IRenderLayer _renderLayer;

	private bool _attemptedToAcquireRenderLayerAlready;

	protected EditingSession _editLayer;

	private readonly List<IFeatureSelection> _redrawSelections = new List<IFeatureSelection>();

	private bool _queuedInvalidateToolOrchestrator;

	private bool _isActive;

	private object _context;

	private MapBase _map;

	private IReadOnlyList<ToolNode> _cachedContributedTools;

	public IRenderLayer NotLazyLoadedRenderLayer => _renderLayer;

	public virtual IRenderLayer RenderLayer
	{
		get
		{
			try
			{
				if (_renderLayer == null)
				{
					if (_attemptedToAcquireRenderLayerAlready)
					{
						return null;
					}
					_attemptedToAcquireRenderLayerAlready = true;
					_renderLayer = CreateRenderLayer();
					if (_renderLayer != null)
					{
						RenderLayerInitialized?.Invoke(this, _renderLayer);
					}
					TryPushNameToRenderLayer();
				}
				return _renderLayer;
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "Could not initialize renderer for {Name}", DisplayName);
				return null;
			}
		}
	}
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
				string oldValue = field;
				OnPropertyChanging("Name");
				OnPropertyChanging("DisplayName");
				field = value;
				OnNameChanged(oldValue, value);
				OnPropertyChanged("Name");
				OnPropertyChanged("DisplayName");
			}
		}
	}

	public string DisplayName => Name;

	public abstract string DefaultName { get; }

	public EditingSession NotLazyLoadedEditLayer => _editLayer;

	public virtual EditingSession EditLayer
	{
		get
		{
			EnsureEditorLayer();
			return _editLayer;
		}
	}
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public List<Message> LayerErrors
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<List<Message>>.Default.Equals(field, value))
			{
				OnPropertyChanging("LayerErrors");
				field = value;
				OnPropertyChanged("LayerErrors");
			}
		}
	} = new List<Message>();

	public virtual IEnumerable<IFeatureSelection> FeatureSelections => Array.Empty<IFeatureSelection>();

	public virtual object Context
	{
		get
		{
			return _context;
		}
		protected set
		{
			RemoveNameChangeListeners(_context as IObservableReadOnlyDisplayName);
			_context = value;
			RegisterNameChangeListeners(value as IObservableReadOnlyDisplayName);
			TryPushNameToRenderLayer();
		}
	}

	public Linkage Linkage { get; private init; }

	public RenderOrderCategory RenderCategory { get; set; }

	public int RenderZIndex { get; set; }

	public abstract string IconResource { get; }

	public MapBase Map
	{
		get
		{
			return _map;
		}
		set
		{
			if (_map != null)
			{
				throw new InvalidOperationException("Map has already been assigned. Can not change the map instance for a VisualContext.");
			}
			SetProperty(ref _map, value, "Map");
		}
	}

	public virtual string LayerUnits => string.Empty;

	protected XElement ToDeserializeElement { get; set; }

	public event RenderLayerInitializedEvent RenderLayerInitialized;

	public event EmittedRenderLayersChangedEvent EmittedRenderLayersChanged;

	public event IObservableReadOnlyDisplayName.NameChangedEvent NameChanged;

	public void EnsureEditorLayer()
	{
		try
		{
			if (_editLayer == null)
			{
				_editLayer = CreateEditLayer();
			}
		}
		catch (Exception)
		{
			_logger.LogError("Trying to initialize ErrorLayer for {context} VisualContext threw an exception. Most likely it is not implemented.", GetType().ToString());
		}
	}

	private void DeactivateEditLayer()
	{
		if (_editLayer == null)
		{
			return;
		}
		try
		{
			_editLayer.DeactivateAnnotations();
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Error deactivating EditLayer for {context}.", GetType().Name);
		}
	}

	private void ActivateEditLayer()
	{
		EditLayer?.ActivateAnnotations();
	}

	public void Activate()
	{
		_isActive = true;
		ActivateEditLayer();
		AttachSelectionRedraw();
	}

	public void Deactivate()
	{
		_isActive = false;
		DeactivateEditLayer();
		DetachSelectionRedraw();
	}

	protected void RefreshSelectionRedraw()
	{
		if (_isActive)
		{
			AttachSelectionRedraw();
		}
	}

	private void AttachSelectionRedraw()
	{
		DetachSelectionRedraw();
		foreach (IFeatureSelection featureSelection in FeatureSelections)
		{
			if (featureSelection != null)
			{
				featureSelection.SelectionChanged += SelectionRedrawRequested;
				_redrawSelections.Add(featureSelection);
			}
		}
	}

	private void DetachSelectionRedraw()
	{
		foreach (IFeatureSelection redrawSelection in _redrawSelections)
		{
			redrawSelection.SelectionChanged -= SelectionRedrawRequested;
		}
		_redrawSelections.Clear();
	}

	private void SelectionRedrawRequested(object sender, NotifyCollectionChangedEventArgs e)
	{
		InvalidateToolOrchestrator();
	}

	public void InvalidateToolOrchestrator()
	{
		if (_queuedInvalidateToolOrchestrator)
		{
			return;
		}
		ISynchronizationContext synchronizationContext = Map?.TileManager?.UIThreadSynchronizationContext;
		if (synchronizationContext == null)
		{
			return;
		}
		_queuedInvalidateToolOrchestrator = true;
		synchronizationContext.Sync(() =>
		{
			_queuedInvalidateToolOrchestrator = false;
			try
			{
				ToolOrchestrator editLayer = EditLayer as ToolOrchestrator;
				editLayer?.Invalidate();
				editLayer?.PreviewRender(null, default(EditModifiers));
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "Error redrawing {context} after a selection change.", GetType().Name);
			}
		});
	}

	public VisualContext(MapBase parentMap)
	{
		Name = DefaultName;
		Linkage = new Linkage
		{
			ParentContext = this
		};
		Map = parentMap;
	}

	protected void OnEmittedRenderLayersChanged()
	{
		RefreshSelectionRedraw();
		EmittedRenderLayersChanged?.Invoke(this);
	}

	protected void SyncToUIThread(Action action)
	{
		ISynchronizationContext synchronizationContext = Map?.TileManager?.UIThreadSynchronizationContext;
		if (synchronizationContext == null)
		{
			action();
		}
		else
		{
			synchronizationContext.Sync(action);
		}
	}

	protected virtual IRenderLayer CreateRenderLayer()
	{
		return null;
	}

	protected virtual EditingSession CreateEditLayer()
	{
		ToolOrchestrator toolOrchestrator = InstantiateToolOrchestrator();
		EditingSession result = CreateEditLayerCore(toolOrchestrator);
		toolOrchestrator?.ActivateAnnotations();
		return result;
	}

	protected virtual ToolOrchestrator InstantiateToolOrchestrator()
	{
		if (Map?.TileManager == null)
		{
			return null;
		}
		return new ToolOrchestrator(Map.TileManager.VectorManager, Map.MapSyncContext, Map.UIThreadSyncContext);
	}

	protected virtual EditingSession CreateEditLayerCore(ToolOrchestrator editLayer)
	{
		return null;
	}

	protected virtual IReadOnlyList<ToolNode> BuildSelectionTools()
	{
		if (RenderLayer is IVectorRenderLayer)
		{
			return new _003C_003Ez__ReadOnlySingleElementList<ToolNode>(StandardToolFactories.Selection(() => NotLazyLoadedRenderLayer as IVectorRenderLayer));
		}
		return Array.Empty<ToolNode>();
	}

	public IReadOnlyList<ToolNode> BuildContributedTools()
	{
		if (_cachedContributedTools != null)
		{
			return _cachedContributedTools;
		}
		IReadOnlyList<ToolNode> readOnlyList = BuildContributedToolsInternal();
		if (readOnlyList == null || readOnlyList.Count == 0)
		{
			return Array.Empty<ToolNode>();
		}
		_cachedContributedTools = readOnlyList;
		return _cachedContributedTools;
	}

	protected virtual IReadOnlyList<ToolNode> BuildContributedToolsInternal()
	{
		IReadOnlyList<ToolNode> readOnlyList = BuildSelectionTools();
		if (!(EditLayer is ToolOrchestrator toolOrchestrator))
		{
			return readOnlyList;
		}
		ToolDescriptor tool = StandardToolFactories.FocusedEditDescriptor(GetType().Name + ".Edit", "Edit", "Pencil", "Edit features.", toolOrchestrator.Router, () => EditLayer as ToolOrchestrator);
		IReadOnlyList<ToolNode> readOnlyList2 = readOnlyList;
		int ordinal = 0;
		ToolNode[] toolNodes = new ToolNode[1 + readOnlyList2.Count];
		foreach (ToolNode readOnlyList22 in readOnlyList2)
		{
			toolNodes[ordinal] = readOnlyList22;
			ordinal++;
		}
		toolNodes[ordinal] = new ToolLeaf(tool)
		{
			ZIndex = 200
		};
		return new _003C_003Ez__ReadOnlyArray<ToolNode>(toolNodes);
	}

	public virtual IReadOnlyList<IRenderLayer> GetActiveRenderLayers()
	{
		return RenderLayer?.WrapInCompactList() ?? ((CompactList<IRenderLayer>)null);
	}

	public virtual IReadOnlyList<object> GetActiveVisualizationContexts()
	{
		return this.WrapInCompactList();
	}

	public string FormatLayerValue(object localValue)
	{
		string layerValueNoUnits = FormatLayerValueNoUnits(localValue);
		string layerUnits = LayerUnits;
		if (!string.IsNullOrWhiteSpace(layerUnits))
		{
			return layerValueNoUnits + " " + layerUnits;
		}
		return layerValueNoUnits;
	}

	public virtual string FormatLayerValueNoUnits(object localValue)
	{
		return localValue?.ToString() ?? string.Empty;
	}

	protected void RegisterNameChangeListeners(IObservableReadOnlyDisplayName on)
	{
		if (on != null)
		{
			on.NameChanged += ContextNameChanged;
		}
	}

	protected void RemoveNameChangeListeners(IObservableReadOnlyDisplayName on)
	{
		if (on != null)
		{
			on.NameChanged -= ContextNameChanged;
		}
	}

	private void ContextNameChanged(IObservableReadOnlyDisplayName sender, string newName)
	{
		TryPushNameToRenderLayer();
	}

	protected void TryPushNameToRenderLayer()
	{
		if (Context is IReadOnlyDisplayName readOnlyDisplayName)
		{
			Name = readOnlyDisplayName.DisplayName;
			IDisplayName notLazyLoadedRenderLayer = NotLazyLoadedRenderLayer;
			if (notLazyLoadedRenderLayer != null)
			{
				notLazyLoadedRenderLayer.DisplayName = readOnlyDisplayName.DisplayName;
			}
		}
	}

	public virtual bool TryMatchXmlNode(XElement element, XElement[] parents)
	{
		return true;
	}

	public virtual void Serialize(XElement serializeTo)
	{
		XMLParser.Serialize(this, serializeTo);
	}

	public virtual void Deserialize(XElement element)
	{
		XMLParser.Parse(this, element);
		ToDeserializeElement = element;
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnNameChanged(string oldValue, string newValue)
	{
		NameChanged?.Invoke(this, newValue);
		if (Context is IObservableReadOnlyDisplayName observableReadOnlyDisplayName && !newValue.Equals(observableReadOnlyDisplayName.DisplayName))
		{
			if (!(observableReadOnlyDisplayName is IObservableDisplayName observableDisplayName))
			{
				throw new InvalidOperationException("This layer can not have its name changed.");
			}
			observableDisplayName.DisplayName = newValue;
		}
	}
}
