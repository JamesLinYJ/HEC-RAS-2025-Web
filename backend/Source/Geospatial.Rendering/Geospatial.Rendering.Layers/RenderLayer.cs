using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading;
using Geospatial.Rasters;
using Geospatial.Rendering.Systems;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility.ContextActions;
using Utility.Interfaces;
using Utility.Logging;

namespace Geospatial.Rendering.Layers;

[DebuggerDisplay("{DisplayName ?? GetType().ToString()}")]
public abstract class RenderLayer : IRenderLayer, IDisplayName, IReadOnlyDisplayName
{
	private static readonly ILogger _logger = LogHelper.GetLogger("RenderLayer");

	private long _renderState = 1L;

	private readonly List<IContextBase> _layerContextElements;

	private readonly List<Func<HitTestArgs, IContextBase>> _hitTestActions;

	protected readonly List<Func<PlotRequest, HitTestArgs, MapHover>> _hoverActions;

	private readonly List<IRenderLayer> _preRenderStage = new List<IRenderLayer>();

	private readonly List<IRenderLayer> _postRenderStage = new List<IRenderLayer>();

	private object _renderSerialLock;

	public abstract Extent ValidExtent { get; }

	public bool IsBackground { get; set; }

	public virtual bool RequestSeparateRenderFrame { get; set; }

	public string DisplayName { get; set; }

	public bool SerializeRenderCalls
	{
		get
		{
			return _renderSerialLock != null;
		}
		set
		{
			_renderSerialLock = (value ? new object() : null);
		}
	}

	public RenderLayer()
	{
		_layerContextElements = new List<IContextBase>();
		_hitTestActions = new List<Func<HitTestArgs, IContextBase>>();
		_hoverActions = new List<Func<PlotRequest, HitTestArgs, MapHover>>();
	}

	public virtual bool Render(PlotRequest request, ref object cache)
	{
		if (_renderSerialLock != null)
		{
			Monitor.Enter(_renderSerialLock);
		}
		try
		{
			return PreRender(request) | RenderInternal(request, ref cache) | PostRender(request);
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Rendering render layer");
			return false;
		}
		finally
		{
			if (_renderSerialLock != null)
			{
				Monitor.Exit(_renderSerialLock);
			}
		}
	}

	protected virtual bool PreRender(PlotRequest request)
	{
		bool condition = false;
		for (int i = 0; i < _preRenderStage.Count; i++)
		{
			object cache = null;
			condition |= _preRenderStage[i].Render(request, ref cache);
		}
		return condition;
	}

	protected abstract bool RenderInternal(PlotRequest request, ref object cache);

	protected virtual bool PostRender(PlotRequest request)
	{
		bool condition = false;
		for (int i = 0; i < _postRenderStage.Count; i++)
		{
			object cache = null;
			condition |= _postRenderStage[i].Render(request, ref cache);
		}
		return condition;
	}

	public void AddPreRenderStage(IRenderLayer lyr)
	{
		_preRenderStage.Add(lyr);
	}

	public void AddPostRenderStage(IRenderLayer lyr)
	{
		_postRenderStage.Add(lyr);
	}

	public virtual List<MapHover> MapHover(HitTestArgs args, ref object hitTestCache)
	{
		return null;
	}

	public void AddMapHitTest(Func<HitTestArgs, IContextBase> hitTestAction)
	{
		_hitTestActions.Add(hitTestAction);
	}

	public void AddMapHover(Func<PlotRequest, HitTestArgs, MapHover> mapHover)
	{
		_hoverActions.Add(mapHover);
	}

	protected void IncrementRenderState()
	{
		Interlocked.Increment(ref _renderState);
	}

	public void DebugIncrementRenderState()
	{
		IncrementRenderState();
	}

	public long GetRenderState()
	{
		return _renderState + GetInternalRenderState();
	}

	protected virtual long GetInternalRenderState()
	{
		return 1L;
	}

	public virtual Dictionary<string, List<ILabelDefinition>> GetEnabledScreenAnnotations(RasterDefinition raster)
	{
		return null;
	}

	[SupportedOSPlatform("windows")]
	public void DebugViewImage(int nPx = 1048576)
	{
		if (!ValidExtent.IsFiniteAndValid())
		{
			throw new Exception("Cannot render image with infinite extent - reduce it.");
		}
		using PlotRequest plotRequest = new PlotRequest(RasterDefinition.ComputeBoundingRaster(ValidExtent, nPx));
		object cache = null;
		Render(plotRequest, ref cache);
		plotRequest.DebugView();
	}
}
