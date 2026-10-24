using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Rasters;
using Geospatial.Rendering;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.TiledRenderingSystem;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.Communication;
using Utility.Interfaces;
using Utility.Logging;
using Visual.Mapping.Observables.Symbology;
using Visual.Observables.Interfaces;
using Visual.Observables.Tree;

namespace Visual.Mapping.Observables.Map;

public abstract class MapBase : ObservableObject, IDisposable, IObservableReadOnlyDisplayName, IReadOnlyDisplayName
{
	public delegate void InitializedEvent(MapBase sender);

	public delegate void ActiveContextChangedEvent(object activeContext);

	private static readonly ILogger _logger = LogHelper.GetLogger("MapBase");

	private object _activeContext;

	private TreeNode _activeNode;

	protected List<IRenderLayer> _staticRenderLayers = new List<IRenderLayer>();

	private PyramidedTileManager _tileManager;

	private string _displayName;

	protected bool _initialized;

	private XElement _queuedXML;

	protected bool _isDisposed;

	public static Type Type => typeof(MapBase);

	public abstract string MapTypeName { get; }
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public RenderLayerCollection Layers
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<RenderLayerCollection>.Default.Equals(field, value))
			{
				RenderLayerCollection oldValue = field;
				OnPropertyChanging("Layers");
				field = value;
				OnLayersChanged(oldValue, value);
				OnPropertyChanged("Layers");
			}
		}
	}
	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public RenderLayerCollection TreeEnabledLayers
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<RenderLayerCollection>.Default.Equals(field, value))
			{
				OnPropertyChanging("TreeEnabledLayers");
				field = value;
				OnPropertyChanged("TreeEnabledLayers");
			}
		}
	}

	public VisualContext ActiveVisualContext => ActiveContext as VisualContext;

	public IReadOnlyList<object> ActiveSecondaryContexts => ActiveVisualContext?.GetActiveVisualizationContexts() ?? null;

	public object ActiveContext
	{
		get
		{
			return _activeContext;
		}
		set
		{
			if (ActiveContext != value)
			{
				VisualContext activeVisualContext = ActiveVisualContext;
				SetProperty(ref _activeContext, value, "ActiveContext");
				activeVisualContext?.Deactivate();
				ActiveVisualContext?.Activate();
				ActiveContextChanged?.Invoke(ActiveContext);
				OnPropertyChanged("ActiveSecondaryContexts");
				OnPropertyChanged("ActiveVisualContext");
			}
		}
	}

	public TreeNode ActiveNode
	{
		get
		{
			return _activeNode;
		}
		set
		{
			if (ActiveNode != value)
			{
				if (ActiveNode != null)
				{
					ActiveNode.IsSelected = false;
				}
				SetProperty(ref _activeNode, value, "ActiveNode");
				if (ActiveNode != null && ActiveNode.Context != null)
				{
					ActiveContext = ActiveNode.Context;
				}
				else
				{
					ActiveContext = null;
				}
			}
		}
	}

	public VectorSymbologyOption LabelSymbologyOption { get; private set; }

	public ObservableLabelHandler LabelHandler { get; private set; }

	public PyramidedTileManager TileManager
	{
		get
		{
			return _tileManager;
		}
		set
		{
			if (_tileManager != null)
			{
				if (_tileManager != value)
				{
					throw new InvalidOperationException("Can not change TileManager once it's already initialized.");
				}
			}
			else
			{
				SetProperty(ref _tileManager, value, "TileManager");
				PostLoadInitializeInternal();
				value.LiveScreenRasterUpdated += LiveScreenRasterUpdated;
				value.PanZoomCompleted += InvalidateActiveContextAnnotations;
				OnPropertyChanged("LiveScreenRaster");
			}
		}
	}

	public RasterDefinition LiveScreenRaster => TileManager.LiveScreenRaster;

	public Extent LiveScreenExtent => TileManager?.LiveScreenRaster.Extent ?? Extent.NullExtent;

	public ISynchronizationContext UIThreadSyncContext => TileManager.UIThreadSynchronizationContext;

	public ISynchronizationContext MapSyncContext => TileManager.MapSyncContext;

	public string DisplayName
	{
		get
		{
			return _displayName;
		}
		set
		{
			SetProperty(ref _displayName, value, "DisplayName");
			NameChanged?.Invoke(this, value);
		}
	}

	public bool IsInitialized => _initialized;

	public event InitializedEvent Initialized;

	public event ActiveContextChangedEvent ActiveContextChanged;

	public event IObservableReadOnlyDisplayName.NameChangedEvent NameChanged;

	private void LiveScreenRasterUpdated(RasterDefinition rd)
	{
		OnPropertyChanged("LiveScreenRaster");
		OnPropertyChanged("LiveScreenExtent");
		InvalidateActiveContextAnnotations();
	}

	public void InvalidateActiveContextAnnotations()
	{
		ActiveVisualContext?.NotLazyLoadedEditLayer?.InvalidateAnnotations();
	}

	public MapBase(string name)
	{
		DisplayName = name;
	}

	public MapBase(PyramidedTileManager tileManager, string name)
		: this(name)
	{
		TileManager = tileManager;
	}

	public abstract K GetOrMakeContext<T, K>(T context, Func<T, MapBase, K> make) where T : class where K : VisualContext;

	public abstract bool TryGetVisualContextFromRenderLayer(IRenderLayer rl, out VisualContext vc);

	public abstract bool TryGetVisualContextContainingObject(object localValue, out VisualContext vc);

	public abstract void SaveXMLInternal(XElement mapNode);

	public abstract void LoadXMLInternal(XElement mapNode);

	public abstract void RebuildRenderLayers();

	public virtual void PostLoadInitialize()
	{
	}

	private void PostLoadInitializeInternal()
	{
		if (_initialized)
		{
			_logger.LogError("Called PostLoadInit on an already-loaded map.");
			return;
		}
		PostLoadInitialize();
		InitializeLabelSymbology();
		if (_queuedXML != null)
		{
			try
			{
				LoadXML(_queuedXML);
			}
			catch (Exception exception)
			{
				_logger.LogError(exception, "Failed to parse map XML.");
			}
		}
		RebuildRenderLayers();
		_initialized = true;
		Initialized?.Invoke(this);
	}

	private void InitializeLabelSymbology()
	{
		LabelHandler = new ObservableLabelHandler(TileManager.LabelHandler, TileManager.MapSyncContext);
		LabelSymbologyOption = new VectorSymbologyOption(LabelHandler.LabelSymbologyDefinition, LabelHandler.LabelSymbology);
	}

	public void RegisterXML(XElement ele)
	{
		string displayName = ele.Attribute("Name")?.Value;
		if (!string.IsNullOrEmpty(displayName))
		{
			DisplayName = displayName;
		}
		if (_initialized)
		{
			LoadXML(ele);
		}
		else
		{
			_queuedXML = ele;
		}
	}

	public void LoadXML(XElement mapNode)
	{
		string displayName = mapNode.Attribute("Name")?.Value;
		if (!string.IsNullOrEmpty(displayName))
		{
			DisplayName = displayName;
		}
		XElement xElement = mapNode.Element("Extent");
		if (xElement != null)
		{
			double minX = GetDouble(xElement, "Left");
			double maxX = GetDouble(xElement, "Right");
			double maxY = GetDouble(xElement, "Top");
			double minY = GetDouble(xElement, "Bottom");
			Extent extent = new Extent(minX, maxX, minY, maxY);
			if (extent.IsValid() && TileManager != null)
			{
				TileManager.InitialExtent = extent;
				if (TileManager.LiveScreenRaster.IsValid())
				{
					TileManager.SetWindow(extent, render: true, frameToVisibleArea: false);
				}
			}
		}
		LoadXMLInternal(mapNode);
	}

	public XElement SaveXML()
	{
		if (!_initialized && _queuedXML != null)
		{
			return new XElement(_queuedXML);
		}
		XElement xElement = new XElement("Map");
		xElement.SetAttributeValue("Name", DisplayName);
		xElement.SetAttributeValue("Type", MapTypeName);
		XElement xElement2 = new XElement("Extent");
		xElement2.SetAttributeValue("Left", TileManager.LiveScreenRaster.Extent.Left);
		xElement2.SetAttributeValue("Top", TileManager.LiveScreenRaster.Extent.Top);
		xElement2.SetAttributeValue("Right", TileManager.LiveScreenRaster.Extent.Right);
		xElement2.SetAttributeValue("Bottom", TileManager.LiveScreenRaster.Extent.Bottom);
		xElement.Add(xElement2);
		SaveXMLInternal(xElement);
		return xElement;
	}

	private double GetDouble(XElement parent, string name)
	{
		XAttribute xAttribute = parent.Attribute(name);
		if (xAttribute == null)
		{
			return double.NaN;
		}
		if (!double.TryParse(xAttribute.Value, out var result))
		{
			return double.NaN;
		}
		return result;
	}

	public void AddStaticRenderLayer(IRenderLayer rl, bool top = false)
	{
		if (!_staticRenderLayers.Contains(rl))
		{
			_staticRenderLayers.Add(rl);
			RebuildRenderLayers();
		}
	}

	public void RemoveStaticRenderLayer(IRenderLayer rl)
	{
		if (_staticRenderLayers.Contains(rl))
		{
			_staticRenderLayers.Remove(rl);
			RebuildRenderLayers();
		}
	}

	public Task SetProfileTimeStep(IProfileState state, int intValue)
	{
		if (MapSyncContext == null)
		{
			_logger.LogError("Trying set profile time step before TileManager initialized on map.");
			return Task.CompletedTask;
		}
		PyramidedTileBoss.Instance.MapSyncContext.Sync(() =>
		{
			state.CurrentProfileIndex = intValue;
		});
		return Task.CompletedTask;
	}

	public Task<S3Client> TryGetS3DevAuth()
	{
		throw new NotImplementedException();
	}

	protected virtual void Dispose(bool disposing)
	{
	}

	~MapBase()
	{
		Dispose(disposing: false);
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnLayersChanged(RenderLayerCollection oldValue, RenderLayerCollection newValue)
	{
		InvalidateActiveContextAnnotations();
	}
}
