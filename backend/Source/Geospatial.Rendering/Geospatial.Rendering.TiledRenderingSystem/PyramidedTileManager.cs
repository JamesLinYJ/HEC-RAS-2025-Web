using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using Geospatial.Rasters;
using Geospatial.Rasters.Convolution;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Systems.Shapes;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.DataStructures;
using Utility.Memory;
using Utility.VisualElements;

namespace Geospatial.Rendering.TiledRenderingSystem;

public class PyramidedTileManager : PyramidedTileEmployee, IDisposable
{
	public delegate void OnActivePointUpdatedEvent(Geospatial.Vectors.Point pt);

	public delegate void OnScreenRasterUpdatedEvent(RasterDefinition rd);

	public delegate void OnPanZoomCompletedEvent();

	private enum WindowSource
	{
		None,
		InitialExtent,
		Layers,
		DefaultExtent
	}

	public enum LoopType
	{
		ScanLine,
		InToOut,
		OutToIn
	}

	public readonly int InstanceID = Guid.NewGuid().GetHashCode();

	public const float ScreenTileBuffer = 1f;

	public const int MaximumOnScreenTiles = 5000;

	private const int MinimumFramableCanvasPixels = 8;

	private Extent _animationTargetExtent = Extent.NullExtent;

	private bool _animationRunning;

	private readonly Stopwatch _animationStopwatch = new Stopwatch();

	private TimeSpan _animationLastFrameTime = TimeSpan.Zero;

	private double _animationDurationSeconds;

	private RasterDefinition _liveScreenRaster;

	private bool _showingDefaultExtent;

	private Extent _initialExtent;

	private readonly List<ActiveTile> _activeTiles = new List<ActiveTile>();

	private Dictionary<TileKey, ActiveTile> _activeTileLookup = new Dictionary<TileKey, ActiveTile>();

	private List<TileKey>[] _activeTilePyramidLookup;

	private Dictionary<RenderTaskDescriptor, RenderTask> _inFlightTiles = new Dictionary<RenderTaskDescriptor, RenderTask>();

	private Dictionary<TileKey, int> _inFlightTilesLookup = new Dictionary<TileKey, int>();

	private TaskCompletionSource _renderCompleteSignal;

	private IRenderLayer _renderContext;

	private int _currentRenderState;

	private PyramidLevel _bestPyramidLevel;

	private readonly CacheManager<CachedTileIndex, RenderTask> _tileCache;

	private LabelHandler _labelHandler;

	private bool _isDisposed;

	private Extent _lastClearedExtent = Extent.NullExtent;

	private double _lastClearedCellSize = -1.0;

	private bool _clearQueued;

	private PixelRange[] _pixelRangeCache;

	private VectorID _tileBackgroundVec;

	private Pixel _debugForceTileRender = Pixel.NullPixel;

	private PyramidLevel _debugForceTileRenderPyramidLevel;

	private Pixel _debugTagTile = Pixel.NullPixel;

	private PyramidLevel _debugTagTilePyramidLevel;

	private int[] _expectedTilesCountRelativeScratchSpace;

	private PyramidLevel _lastResultPyramidLevel = PyramidLevel.Dynamic;

	public int BufferOverride { get; set; } = 1;

	public bool DebugForceSerialRendering { get; set; }

	public bool DebugDisableCaching
	{
		get => field;
		set
		{
			field = value;
			if (value) _tileCache?.ForceEvictAll();
		}
	} = true;

	public Func<Extent, Extent> VisibleAreaAdjuster { get; set; }

	public RasterDefinition LiveScreenRaster
	{
		get
		{
			return _liveScreenRaster;
		}
		private set
		{
			_ = Math.Abs(_liveScreenRaster.Left - value.Left) / _liveScreenRaster.CellSize;
			_ = 500.0;
			_liveScreenRaster = value;
			if (!_liveScreenRaster.IsValid())
			{
				PyramidedTileEmployee._logger.LogWarning("Screen raster set to a bad value.");
			}
			ScreenRasterChanged?.Invoke(_liveScreenRaster);
			LiveScreenRasterUpdated?.Invoke(_liveScreenRaster);
		}
	}

	public Geospatial.Vectors.Point ActivePoint
	{
		get
		{
			return field;
		}
		set
		{
			field = value;
			ActivePointUpdated?.Invoke(value);
		}
	}

	public IVectorManager UnderlayVectorManager { get; private init; }

	public Extent InitialExtent
	{
		get
		{
			return _initialExtent;
		}
		set
		{
			_initialExtent = value;
		}
	}

	public Extent DefaultExtent { get; private set; } = Extent.NullExtent;

	public int ActiveTileCount => _activeTiles.Count;

	public int InFlightTilesCount => _inFlightTiles.Count;

	public IRenderLayer RenderContext => _renderContext;

	public ITileRenderSystem<RenderTask> TileRenderSystem { get; }

	public int GlobalRenderState => _currentRenderState;

	public int CurrentZoomPanVersion { get; private set; }

	public PyramidedTileCanvas TileCanvas { get; }

	public ICanvas Canvas => TileCanvas?.Canvas ?? null;

	public ICanvas AnnotationCanvas { get; }

	public PyramidLevel BestPyramidLevel => _bestPyramidLevel;

	public IImageProvider ImageProvider { get; }

	public IBitmapProvider BitmapProvider { get; }

	public ILabelProvider LabelProvider { get; }

	public int TileCacheSize => _tileCache?.ActualCacheSize() ?? 0;

	public int TilesInCache => _tileCache?.ItemsInCache ?? 0;

	public IVectorManager VectorManager { get; private set; }

	public LabelHandler LabelHandler => _labelHandler;

	public bool IsActive { get; set; }

	public event OnScreenRasterUpdatedEvent ScreenRasterChanged;

	public event OnPanZoomCompletedEvent PanZoomCompleted;

	public event OnActivePointUpdatedEvent ActivePointUpdated;

	public event OnScreenRasterUpdatedEvent LiveScreenRasterUpdated;

	public Extent FrameToVisibleArea(Extent ext)
	{
		return FrameToVisibleArea(ext, frame: true);
	}

	private Extent FrameToVisibleArea(Extent ext, bool frame)
	{
		if (!frame || VisibleAreaAdjuster == null || !ext.IsValid())
		{
			return ext;
		}
		Extent result = VisibleAreaAdjuster(ext);
		if (!result.IsValid())
		{
			return ext;
		}
		return result;
	}

	public void SetDefaultExtent(Extent ext)
	{
		DefaultExtent = ext;
		if (TryInitializeIsValid())
		{
			QueueRenderTiles();
		}
	}

	private bool HasFramableExtent()
	{
		return GetFramableExtent().IsFiniteAndValid();
	}

	private Extent GetFramableExtent()
	{
		if (_renderContext == null)
		{
			return Extent.NullExtent;
		}
		return Extent.Union(from l in GetLayers()
			where !(l is RenderLayer renderLayer) || !renderLayer.IsBackground
			select l?.ValidExtent ?? Extent.NullExtent into e
			where e.IsFiniteAndValid()
			select e);
	}

	public PyramidedTileManager(ICanvas canvas, ICanvas annotationCanvas, IVectorManager underlayVectorManager, IImageProvider imageProvider, IBitmapProvider bitmapProvider, ILabelProvider labelProvider, ISynchronizationContext uiThreadSynchronizationContext, OnScreenRasterUpdatedEvent screenRasterChangedCallback = null)
		: base(uiThreadSynchronizationContext)
	{
		if (PyramidedTileBoss.Instance == null)
		{
			PyramidedTileBoss.Instance = new PyramidedTileBoss(UIThreadSynchronizationContext);
		}
		PyramidedTileBoss.Instance.Register(this);
		if (screenRasterChangedCallback != null)
		{
			ScreenRasterChanged += screenRasterChangedCallback;
		}
		_tileCache = new CacheManager<CachedTileIndex, RenderTask>(task => checked(task.ColorBufferBytes.Length));
		_tileCache.OnItemEvicted += (_, task) =>
		{
			task.IsCached = false;
			if (task.CanDispose && !task.IsPlotting) task.Dispose();
		};
		UnderlayVectorManager = underlayVectorManager;
		TileCanvas = new PyramidedTileCanvas(canvas);
		TileRenderSystem = new RenderThreadTileRenderSystem<RenderTask>("PyramidedTileManager", UIThreadSynchronizationContext, NotifyRenderTaskComplete, (RenderTask task) => !task.IsDisposed && IsTaskStillValid(task));
		ImageProvider = imageProvider;
		BitmapProvider = bitmapProvider;
		LabelProvider = labelProvider;
		AnnotationCanvas = annotationCanvas;
		TileRenderSystem.InitializeRenderingSystem();
	}

	~PyramidedTileManager()
	{
		if (!_isDisposed)
		{
			Dispose(disposing: false);
		}
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	public void Dispose(bool disposing)
	{
		if (_isDisposed)
		{
			return;
		}
		foreach (KeyValuePair<RenderTaskDescriptor, RenderTask> inFlightTile in _inFlightTiles)
		{
			RenderTask renderTask = inFlightTile.Value;
			if (!renderTask.IsPlotting)
			{
				renderTask.Dispose();
			}
		}
		foreach (ActiveTile activeTile in _activeTiles)
		{
			activeTile.RenderTile.Dispose();
			activeTile.RenderTask.Dispose();
		}
		_tileCache.ForceEvictAll();
		TileRenderSystem.ShutdownRenderingSystem();
		PyramidedTileBoss.Instance.Unregister(this);
		VectorManager?.Dispose(disposing);
		UnderlayVectorManager?.Dispose(disposing);
		AnnotationCanvas?.Dispose(disposing);
		_isDisposed = true;
	}

	private IList<IRenderLayer> GetLayers()
	{
		if (_renderContext is RenderLayerCollection renderLayerCollection)
		{
			return FlattenLayers(renderLayerCollection.Layers);
		}
		return new CompactList<IRenderLayer>(_renderContext);
	}

	private static IList<IRenderLayer> FlattenLayers(IList<IRenderLayer> layers)
	{
		bool condition = false;
		for (int i = 0; i < layers.Count; i++)
		{
			if (layers[i] is RenderLayerCollection)
			{
				condition = true;
				break;
			}
		}
		if (!condition)
		{
			return layers;
		}
		List<IRenderLayer> iRenderLayers = new List<IRenderLayer>();
		for (int j = 0; j < layers.Count; j++)
		{
			if (layers[j] is RenderLayerCollection renderLayerCollection)
			{
				iRenderLayers.AddRange(FlattenLayers(renderLayerCollection.Layers));
			}
			else
			{
				iRenderLayers.Add(layers[j]);
			}
		}
		return iRenderLayers;
	}

	public void Reset()
	{
		ForceClearAllTiles();
		TileRenderSystem.ForceCancelAllWork();
		LiveScreenRaster = RasterDefinition.NullRaster;
		InitialExtent = Extent.NullExtent;
	}

	public void InitializeVectorRenderer(IVectorManager vectorManager)
	{
		VectorManager = vectorManager;
		_labelHandler = new LabelHandler(vectorManager);
	}

	public void SetRenderContext(IRenderLayer renderContext)
	{
		AssertOwnerThread();
		QueueUpdate(() =>
		{
			_renderContext = renderContext;
			TryInitializeExtent();
			NotifyNewRenderState();
		});
	}

	private void TryInitializeExtent()
	{
		if (Canvas.ActualWidth < 8.0 || Canvas.ActualHeight < 8.0)
		{
			return;
		}
		Extent extent = GetDesiredExtent(out var source);
		if (!extent.IsNull())
		{
			if (source == WindowSource.Layers || source == WindowSource.DefaultExtent)
			{
				extent = FrameToVisibleArea(extent, frame: true);
			}
			if (!extent.Equals(LiveScreenRaster.Extent) || TileCanvas.PyramidLevel == null)
			{
				TryUpdatePyramidLevels(extent);
			}
			SetWindow(extent);
			_showingDefaultExtent = source == WindowSource.DefaultExtent;
		}
	}

	private Extent GetDesiredExtent(out WindowSource source)
	{
		source = WindowSource.None;
		if (InitialExtent.IsFiniteAndValid())
		{
			Extent initialExtent = InitialExtent;
			InitialExtent = Extent.NullExtent;
			source = WindowSource.InitialExtent;
			return initialExtent;
		}
		if (IsValid() && (!_showingDefaultExtent || !HasFramableExtent()))
		{
			return Extent.NullExtent;
		}
		Extent framableExtent = GetFramableExtent();
		if (framableExtent.IsFiniteAndValid())
		{
			source = WindowSource.Layers;
			return framableExtent;
		}
		if (DefaultExtent.IsFiniteAndValid())
		{
			source = WindowSource.DefaultExtent;
			return DefaultExtent;
		}
		return Extent.NullExtent;
	}

	private void TryUpdatePyramidLevels(Extent extent, bool updateIfLowerQuality = true, bool updateIfHigherQuality = true)
	{
		if (TileCanvas.PyramidLevels == null)
		{
			TileCanvas.InitializePyramidLevels(extent);
			return;
		}
		double targetBasePyramidCellSize = TileCanvas.GetTargetBasePyramidCellSize(extent);
		if ((!updateIfLowerQuality || !(targetBasePyramidCellSize >= TileCanvas.PyramidLevels[0].CellSize * 2.0)) && (!updateIfHigherQuality || !(targetBasePyramidCellSize <= TileCanvas.PyramidLevels[0].CellSize / 2.0)))
		{
			return;
		}
		while (_activeTiles.Count > 0)
		{
			if (!TryRemoveAndRecover(_activeTiles[0]))
			{
				ForceRemoveAndRecoverTile(_activeTiles[0]);
			}
		}
		TileRenderSystem.ForceCancelAllWork();
		TileCanvas.InitializePyramidLevels(extent);
		CheckPyramidLevelUpdate();
	}

	private void TryNormalizePyramidLevels(Extent extent)
	{
		if (extent.IsNull())
		{
			return;
		}
		EnsureActiveTilePyramidLookup();
		if (TileCanvas.PyramidLevels == null)
		{
			TileCanvas.InitializePyramidLevels(extent);
			return;
		}
		double targetBasePyramidCellSize = TileCanvas.GetTargetBasePyramidCellSize(extent);
		double cellSize = TileCanvas.PyramidLevels[0].CellSize;
		IReadOnlyList<PyramidLevel> pyramidLevels = TileCanvas.PyramidLevels;
		double cellSize2 = pyramidLevels[pyramidLevels.Count - 2].CellSize;
		bool targetBasePyramidCellSizeCellSizeTerm = targetBasePyramidCellSize >= cellSize;
		bool targetBasePyramidCellSizeCellSize2Term = targetBasePyramidCellSize <= cellSize2;
		if (!targetBasePyramidCellSizeCellSizeTerm && !targetBasePyramidCellSizeCellSize2Term)
		{
			return;
		}
		int targetBasePyramidCellSizeCellSize2Term2 = ((!targetBasePyramidCellSizeCellSizeTerm) ? Math.Min((int)Math.Round(Math.Log2(targetBasePyramidCellSize / cellSize2)), -3) : Math.Max((int)Math.Round(Math.Log2(targetBasePyramidCellSize / cellSize)), 3));
		PyramidedTileEmployee._logger.LogInformation("Normalizing (shifting) pyramid levels by {shiftAmount}", targetBasePyramidCellSizeCellSize2Term2);
		for (int i = 0; i < TileCanvas.PyramidLevels.Count; i++)
		{
			TileCanvas.PyramidLevels[i].Shift(targetBasePyramidCellSizeCellSize2Term2);
		}
		Dictionary<TileKey, ActiveTile> activeTileLookup = new Dictionary<TileKey, ActiveTile>();
		foreach (KeyValuePair<TileKey, ActiveTile> activeTileLookup2 in _activeTileLookup)
		{
			ActiveTile activeTile2 = activeTileLookup2.Value;
			activeTileLookup[new TileKey(activeTile2.RenderTask)] = activeTile2;
		}
		_activeTileLookup = activeTileLookup;
		List<TileKey>[] activeTilePyramidLookup = new List<TileKey>[23];
		for (int j = 0; j <= 22; j++)
		{
			int ordinal = j - targetBasePyramidCellSizeCellSize2Term2;
			List<TileKey> activeTilePyramidLookupList = ((ordinal <= 22 && ordinal >= 0) ? _activeTilePyramidLookup[ordinal] : new List<TileKey>());
			for (int k = 0; k < activeTilePyramidLookupList.Count; k++)
			{
				activeTilePyramidLookupList[k] = new TileKey(activeTilePyramidLookupList[k].Level + targetBasePyramidCellSizeCellSize2Term2, activeTilePyramidLookupList[k].TileIndex);
			}
			activeTilePyramidLookup[j] = activeTilePyramidLookupList;
		}
		_activeTilePyramidLookup = activeTilePyramidLookup;
		Dictionary<RenderTaskDescriptor, RenderTask> inFlightTiles = new Dictionary<RenderTaskDescriptor, RenderTask>();
		foreach (KeyValuePair<RenderTaskDescriptor, RenderTask> inFlightTile in _inFlightTiles)
		{
			RenderTaskDescriptor key = inFlightTile.Key;
			inFlightTiles[new RenderTaskDescriptor(key.GlobalRenderState, key.PyramidLevel, key.TileIndex)] = inFlightTile.Value;
		}
		_inFlightTiles = inFlightTiles;
		Dictionary<TileKey, int> inFlightTilesLookup = new Dictionary<TileKey, int>();
		foreach (KeyValuePair<TileKey, int> inFlightTilesLookup2 in _inFlightTilesLookup)
		{
			inFlightTilesLookup[new TileKey(inFlightTilesLookup2.Key.Level + targetBasePyramidCellSizeCellSize2Term2, inFlightTilesLookup2.Key.TileIndex)] = inFlightTilesLookup2.Value;
		}
		_inFlightTilesLookup = inFlightTilesLookup;
		TileCanvas.ShiftPyramidLevels(targetBasePyramidCellSizeCellSize2Term2);
		for (int l = 0; l < _activeTiles.Count; l++)
		{
			ActiveTile activeTile = _activeTiles[l];
			if (activeTile.PyramidLevel.IsInvalidated)
			{
				if (TryRemoveAndRecover(activeTile))
				{
					l--;
				}
				else
				{
					ForceRemoveAndRecoverTile(activeTile);
				}
			}
		}
	}

	public bool IsValid()
	{
		if (LiveScreenRaster.IsNull() || !LiveScreenRaster.Extent.IsFiniteAndValid())
		{
			return false;
		}
		return true;
	}

	public bool TryInitializeIsValid()
	{
		if (!LiveScreenRaster.IsNull() && LiveScreenRaster.Extent.IsFiniteAndValid() && (!_showingDefaultExtent || !HasFramableExtent()))
		{
			return true;
		}
		if (RenderContext == null && !DefaultExtent.IsFiniteAndValid())
		{
			return false;
		}
		TryInitializeExtent();
		return IsValid();
	}

	private void CheckPyramidLevelUpdate()
	{
		PyramidLevel bestPyramidLevel = TileCanvas.GetBestPyramidLevel(LiveScreenRaster);
		if (!bestPyramidLevel.Equals(_bestPyramidLevel))
		{
			_bestPyramidLevel = bestPyramidLevel;
		}
	}

	public PlotRequest TryGetRenderedData(Geospatial.Vectors.Point pt)
	{
		foreach (ActiveTile activeTile in _activeTiles)
		{
			if (activeTile.RenderTask.TileRaster.Contains(pt))
			{
				return activeTile.RenderTask.PlotRequest;
			}
		}
		return null;
	}

	public void PartialZoom_Old(Geospatial.Vectors.Point centerOnScreenPixel, double scale)
	{
		CurrentZoomPanVersion++;
		TryNormalizePyramidLevels(LiveScreenRaster.Extent);
		CheckPyramidLevelUpdate();
		TileCanvas.PyramidLevel = _bestPyramidLevel;
		Geospatial.Vectors.Point origin = ConvertScreenPointToWorldCoordinate(centerOnScreenPixel);
		Extent extent = LiveScreenRaster.Extent.Ratio(1.0 / scale, origin);
		double cellSize = extent.Width / (double)(float)Canvas.ActualWidth;
		RasterDefinition screenRaster = GetScreenRaster(extent.UpperLeft, cellSize);
		if (!(scale > 1.0) || !PixelRange.IsNull(GetBufferedOnscreenTiles(screenRaster, _bestPyramidLevel, 0)))
		{
			LiveScreenRaster = screenRaster;
			TileCanvas.SetWindow(extent.UpperLeft, LiveScreenRaster.CellSize);
			TileCanvas.RefreshAllTilePositioningAndScales(_activeTiles);
			QueueRenderTiles();
			VectorManager.TransformationFromBase = LiveScreenRaster.AsTransformationMatrix3x3D();
			VectorManager.Canvas.Invalidate();
			QueueClearOffscreenTiles();
		}
	}

	public void PartialZoom(Geospatial.Vectors.Point centerOnScreenPixel, double scale)
	{
		if (IsValid())
		{
			CurrentZoomPanVersion++;
			Geospatial.Vectors.Point origin = ConvertScreenPointToWorldCoordinate(centerOnScreenPixel);
			Extent ext = LiveScreenRaster.Extent.Ratio(1.0 / scale, origin);
			if (_animationRunning)
			{
				ext = _animationTargetExtent.Ratio(1.0 / scale, origin);
			}
			SetWindow_Animated(ext, render: true, 0.25, frameToVisibleArea: false);
		}
	}

	public void CompleteZoom()
	{
		VectorManager.ResetZoom();
		PanZoomCompleted?.Invoke();
	}

	public void PartialPan(double totalXPixels, double totalYPixels)
	{
		if (!_animationRunning)
		{
			TileCanvas.Transform.ExtractTranslation(out var translationX, out var translationY);
			double xComponent = translationX - totalXPixels;
			double yComponent = translationY - totalYPixels;
			TileCanvas.PartialTranslate(new Geospatial.Vectors.Point(xComponent, yComponent));
			LiveScreenRaster = LiveScreenRaster.Translate(xComponent * LiveScreenRaster.CellSize, (0.0 - yComponent) * LiveScreenRaster.CellSize);
			VectorManager.TranslateAllVectorsDelta(0.0 - xComponent, 0.0 - yComponent);
			if (AnnotationCanvas != null)
			{
				AnnotationCanvas.Transformation = Matrix3x3D.Translation((float)totalXPixels, (float)totalYPixels);
			}
			QueueClearOffscreenTiles();
			TileCanvas.Canvas.Invalidate();
			VectorManager.Canvas.Invalidate();
		}
	}

	public void FinishPan()
	{
		if (AnnotationCanvas != null)
		{
			AnnotationCanvas.Transformation = Matrix3x3D.Identity;
		}
		if (!_animationRunning)
		{
			CurrentZoomPanVersion++;
			TileCanvas.Transform.ExtractTranslation(out var translationX, out var translationY);
			if (translationX != 0.0 || translationY != 0.0)
			{
				QueueClearOffscreenTiles();
				TileCanvas.CommitPartialTranslation(_activeTiles);
				VectorManager.Canvas.Invalidate();
				PanZoomCompleted?.Invoke();
			}
		}
	}

	public Extent TryGetFullExtentOfCurrentLayers()
	{
		Extent framableExtent = GetFramableExtent();
		if (framableExtent.IsFiniteAndValid())
		{
			return framableExtent;
		}
		return Extent.NullExtent;
	}

	public async Task<bool> TryZoomToFull()
	{
		Extent ext = TryGetFullExtentOfCurrentLayers();
		if (ext.IsValid())
		{
			await SetWindowAsync(ext);
			return true;
		}
		return false;
	}

	public async Task TryZoomToAsync(Extent ext)
	{
		await SetWindowAsync(ext);
	}

	public Task SetWindowAsync(Extent ext, bool render = true, bool frameToVisibleArea = true)
	{
		SetWindow(ext, render, frameToVisibleArea);
		return Task.CompletedTask;
	}

	public void SetWindow(Extent ext, bool render = true, bool frameToVisibleArea = true)
	{
		_showingDefaultExtent = false;
		SetWindow(FrameToVisibleArea(ext, frameToVisibleArea));
		if (render)
		{
			QueueRenderTiles();
		}
	}

	private void SetWindow(Extent ext)
	{
		if (!ext.IsValid())
		{
			throw new InvalidOperationException();
		}
		if (Canvas.ActualWidth != 0.0 && Canvas.ActualHeight != 0.0)
		{
			CurrentZoomPanVersion++;
			TileCanvas.TryCheckInitializePyramidLevels(ext);
			TryNormalizePyramidLevels(ext);
			ext = AsScreenAspectRatio(ext);
			double widthOverActualWidth = ext.Width / Canvas.ActualWidth;
			RasterDefinition screenRaster = GetScreenRaster(ext.UpperLeft, widthOverActualWidth);
			TileCanvas.PyramidLevel = TileCanvas.GetBestPyramidLevel(screenRaster);
			TileCanvas.SetWindow(screenRaster.Extent.UpperLeft, widthOverActualWidth);
			TileCanvas.RefreshAllTilePositioningAndScales(_activeTiles);
			LiveScreenRaster = screenRaster;
			_bestPyramidLevel = TileCanvas.PyramidLevel;
			QueueClearOffscreenTiles();
			VectorManager.TransformationFromBase = LiveScreenRaster.AsTransformationMatrix3x3D();
			VectorManager.Canvas.Invalidate();
		}
	}

	private Extent AsScreenAspectRatio(Extent ext)
	{
		int actualWidthCanvasTerm = (int)Math.Round(Canvas.ActualWidth);
		int actualHeightCanvasTerm = (int)Math.Round(Canvas.ActualHeight);
		ext = ext.EnforceAspectRatio(actualWidthCanvasTerm, actualHeightCanvasTerm);
		return ext;
	}

	private void SetWindow(Geospatial.Vectors.Point ulCorner, double cellSize)
	{
		int actualWidthCanvasTerm = (int)Math.Round(Canvas.ActualWidth);
		int actualHeightCanvasTerm = (int)Math.Round(Canvas.ActualHeight);
		if (actualWidthCanvasTerm > 0 && actualHeightCanvasTerm > 0)
		{
			Extent window = new Extent(ulCorner, cellSize * (double)actualWidthCanvasTerm, cellSize * (double)actualHeightCanvasTerm);
			SetWindow(window);
		}
	}

	private void NotifyNewRenderState()
	{
		_currentRenderState++;
		if (_inFlightTiles.Count == 0)
		{
			return;
		}
		PyramidedTileEmployee._logger?.LogInformation("NotifyNewRenderState: {Count} tile cancellations requested.", _inFlightTiles.Count);
		foreach (KeyValuePair<RenderTaskDescriptor, RenderTask> inFlightTile in _inFlightTiles)
		{
			inFlightTile.Value.PlotRequest.Reporter.RequestCancel();
		}
	}

	public void ClearOldTiles()
	{
		for (int i = 0; i < _activeTiles.Count; i++)
		{
			if (_activeTiles[i].RenderTask.GlobalRenderState != GlobalRenderState && TryRemoveAndRecover(_activeTiles[i]))
			{
				i--;
			}
		}
	}

	public void ForceClearAllTiles()
	{
		while (_activeTiles.Count != 0)
		{
			if (!TryRemoveAndRecover(_activeTiles[0]))
			{
				ForceRemoveAndRecoverTile(_activeTiles[0]);
			}
		}
		Canvas.Invalidate();
	}

	private void QueueClearOffscreenTiles()
	{
		if (!_clearQueued)
		{
			_clearQueued = true;
			UIThreadSynchronizationContext.Sync(() =>
			{
				_clearQueued = false;
				ClearOffscreenTiles();
			});
		}
	}

	private void ClearOffscreenTiles(bool force = false)
	{
		Geospatial.Vectors.Point center = _lastClearedExtent.Center;
		Geospatial.Vectors.Point center2 = LiveScreenRaster.Center;
		double scaledCellSize = LiveScreenRaster.CellSize * 256.0;
		if (!force && !_lastClearedExtent.IsNull() && _lastClearedCellSize == LiveScreenRaster.CellSize && Math.Abs(center.X - center2.X) < scaledCellSize && Math.Abs(center.Y - center2.Y) < scaledCellSize)
		{
			return;
		}
		ValidatePixelRangeCache();
		_lastClearedExtent = LiveScreenRaster.Extent;
		_lastClearedCellSize = LiveScreenRaster.CellSize;
		for (int i = 0; i < _activeTiles.Count; i++)
		{
			if (TryClearOffscreenTile(_bestPyramidLevel, _activeTiles[i], useCachingForBatchTileClearing: true))
			{
				i--;
			}
		}
	}

	private void ValidatePixelRangeCache()
	{
		if (_pixelRangeCache == null)
		{
			_pixelRangeCache = new PixelRange[23];
		}
		for (int i = 0; i <= 22; i++)
		{
			_pixelRangeCache[i] = PixelRange.NullRange;
		}
	}

	private bool TryClearOffscreenTile(PyramidLevel renderPyramidLevel, ActiveTile tile, bool useCachingForBatchTileClearing = false)
	{
		bool notIsTileOnScreen = !IsTileOnScreen(renderPyramidLevel, tile.RenderTask, useCachingForBatchTileClearing);
		_inFlightTilesLookup.ContainsKey(new TileKey(tile.RenderTask));
		if (!notIsTileOnScreen)
		{
			return false;
		}
		return TryRemoveAndRecover(tile);
	}

	private bool IsTileBestPyramidLevel(PyramidLevel renderPyramidLevel, PyramidLevel tilePyramidLevel)
	{
		return renderPyramidLevel.Equals(tilePyramidLevel);
	}

	private bool IsTileOnScreen(PyramidLevel renderPyramidLevel, RenderTask tile, bool useCachingForBatchTileClearing = false)
	{
		int pyramidLevel = tile.PyramidLevel;
		if (pyramidLevel < 0 || pyramidLevel > 22)
		{
			return false;
		}
		if (useCachingForBatchTileClearing && _pixelRangeCache[pyramidLevel].IsNull())
		{
			_pixelRangeCache[pyramidLevel] = GetBufferedOnscreenTiles(tile.PyramidLevel);
		}
		return (useCachingForBatchTileClearing ? _pixelRangeCache[pyramidLevel] : GetBufferedOnscreenTiles(tile.PyramidLevel)).InRange(tile.TileIndex);
	}

	private bool IsTaskStillValid(RenderTask task)
	{
		if (task.GlobalRenderState == GlobalRenderState && IsTileBestPyramidLevel(_bestPyramidLevel, task.PyramidLevel) && IsTileOnScreen(_bestPyramidLevel, task))
		{
			return !task.PyramidLevel.IsInvalidated;
		}
		return false;
	}

	private void AddInFlightTile(RenderTask task)
	{
		_inFlightTiles[task.RenderTaskDescriptor] = task;
		TileKey key = new TileKey(task);
		bool tryGetValue = _inFlightTilesLookup.TryGetValue(key, out var localValue);
		_inFlightTilesLookup[key] = (tryGetValue ? localValue : 0) + 1;
	}

	private bool IsTileStillPlotting(RenderTask task)
	{
		if (_inFlightTiles.TryGetValue(task.RenderTaskDescriptor, out var localValue))
		{
			return localValue == task;
		}
		return false;
	}

	public async ValueTask RenderAllTilesAsync()
	{
		AssertOwnerThread();
		TryProcessQueuedUpdates();
		await RenderAllTilesInternalAsync(LoopType.InToOut);
		while (TryProcessQueuedUpdates())
		{
			await RenderAllTilesInternalAsync(LoopType.InToOut);
		}
	}

	private async ValueTask RenderAllTilesInternalAsync(LoopType loopType)
	{
		if (!TryInitializeIsValid())
		{
			return;
		}
		CheckPyramidLevelUpdate();
		if (GetLayers().Count == 0)
		{
			ClearOldTiles();
			return;
		}
		IList<RenderTask> renderWorkload = GetRenderWorkload(_bestPyramidLevel, loopType);
		if (renderWorkload != null && renderWorkload.Count != 0)
		{
			await TileRenderSystem.RenderTasksAsync(renderWorkload, DebugForceSerialRendering);
			ClearOldTiles();
		}
	}

	public override void QueueRenderTiles()
	{
		AssertOwnerThread();
		TryProcessQueuedUpdates();
		QueueRenderTilesInternal(LoopType.InToOut);
	}

	private void QueueRenderTilesInternal(LoopType loopType)
	{
		if (!TryInitializeIsValid())
		{
			return;
		}
		CheckPyramidLevelUpdate();
		if (GetLayers().Count == 0)
		{
			ClearOldTiles();
			Canvas.Invalidate();
			return;
		}
		IList<RenderTask> renderWorkload = GetRenderWorkload(_bestPyramidLevel, loopType);
		if (renderWorkload != null && renderWorkload.Count != 0)
		{
			TileRenderSystem.QueueRenderTasks(renderWorkload);
		}
	}

	public async ValueTask RefreshAllTilesAsync()
	{
		if (TryInitializeIsValid())
		{
			NotifyNewRenderState();
			await RenderAllTilesAsync();
		}
	}

	public override bool IsAnythingRenderingRightNow()
	{
		return _inFlightTiles.Count > 0;
	}

	public Task WaitForRenderCompleteAsync()
	{
		AssertOwnerThread();
		if (_inFlightTiles.Count == 0)
		{
			return Task.CompletedTask;
		}
		if (_renderCompleteSignal == null || _renderCompleteSignal.Task.IsCompleted)
		{
			_renderCompleteSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		}
		return _renderCompleteSignal.Task;
	}

	public async Task ResizedAsync(bool render = true)
	{
		if (TryInitializeIsValid())
		{
			if (!render)
			{
				SetWindow(LiveScreenRaster.Extent.UpperLeft, LiveScreenRaster.CellSize);
				return;
			}
			bool wasDefault = _showingDefaultExtent;
			await SetWindowAsync(LiveScreenRaster.Extent, render: true, frameToVisibleArea: false);
			_showingDefaultExtent = wasDefault;
		}
	}

	private PixelRange GetBufferedOnscreenTiles(PyramidLevel pyramidLevel, int buffer = 1)
	{
		return GetBufferedOnscreenTiles(LiveScreenRaster, pyramidLevel, buffer);
	}

	private PixelRange GetBufferedOnscreenTiles(RasterDefinition raster, PyramidLevel pyramidLevel, int buffer = 1)
	{
		ref readonly RasterDefinition worldTileRaster = ref pyramidLevel.WorldTileRaster;
		Extent extent = raster.Extent;
		int row = worldTileRaster.Row(in extent.MaxY);
		ref readonly RasterDefinition worldTileRaster2 = ref pyramidLevel.WorldTileRaster;
		extent = raster.Extent;
		int row2 = worldTileRaster2.Row(in extent.MinY);
		ref readonly RasterDefinition worldTileRaster3 = ref pyramidLevel.WorldTileRaster;
		extent = raster.Extent;
		int col = worldTileRaster3.Col(in extent.MinX);
		ref readonly RasterDefinition worldTileRaster4 = ref pyramidLevel.WorldTileRaster;
		extent = raster.Extent;
		int col2 = worldTileRaster4.Col(in extent.MaxX);
		if (row == int.MinValue || row == int.MaxValue || row2 == int.MinValue || row2 == int.MaxValue || col == int.MinValue || col == int.MaxValue || col2 == int.MinValue || col2 == int.MaxValue)
		{
			return PixelRange.NullRange;
		}
		buffer = BufferOverride;
		return new PixelRange(row - buffer, row2 + buffer, col - buffer, col2 + buffer);
	}

	private RasterDefinition GetScreenRaster(Geospatial.Vectors.Point upperLeft, double cellSize)
	{
		return new RasterDefinition(upperLeft, cellSize, (int)Math.Round(Canvas.ActualHeight), (int)Math.Round(Canvas.ActualWidth));
	}

	public Geospatial.Vectors.Point ConvertScreenPointToWorldCoordinate(Geospatial.Vectors.Point screenPoint)
	{
		if (!IsValid())
		{
			return Geospatial.Vectors.Point.NullPoint;
		}
		Pixel px = new Pixel((int)Math.Floor(screenPoint.Y), (int)Math.Floor(screenPoint.X));
		return LiveScreenRaster.CellCenter(px);
	}

	public Pixel ConvertWorldCoordinateToScreenPixel(Geospatial.Vectors.Point worldCoord)
	{
		if (!IsValid())
		{
			return Pixel.NullPixel;
		}
		return LiveScreenRaster.CellIndex2D(worldCoord);
	}

	public Geospatial.Vectors.Point ConvertWorldCoordinateToScreenPoint(Geospatial.Vectors.Point worldCoord)
	{
		Pixel pixel = ConvertWorldCoordinateToScreenPixel(worldCoord);
		return new Geospatial.Vectors.Point(pixel.X, pixel.Y);
	}

	public HitTestArgs GetHitTestArgs(Geospatial.Vectors.Point screenPoint)
	{
		return new HitTestArgs(ConvertScreenPointToWorldCoordinate(screenPoint), LiveScreenRaster);
	}

	private void EnsureActiveTilePyramidLookup()
	{
		if (_activeTilePyramidLookup == null)
		{
			_activeTilePyramidLookup = new List<TileKey>[23];
			for (int i = 0; i < _activeTilePyramidLookup.Length; i++)
			{
				_activeTilePyramidLookup[i] = new List<TileKey>();
			}
		}
	}

	private void RemoveActiveTile(ActiveTile tile)
	{
		_activeTiles.Remove(tile);
		if (!_activeTileLookup.ContainsKey(new TileKey(tile.RenderTask)))
		{
			throw new InvalidOperationException("Removed active tile that wasn't registered in _activeTileLookup");
		}
		_activeTileLookup.Remove(new TileKey(tile.RenderTask));
		if (!tile.PyramidLevel.IsInvalidated)
		{
			if ((int)tile.PyramidLevel < 0 || (int)tile.PyramidLevel >= _activeTilePyramidLookup.Length)
			{
				throw new InvalidOperationException("Invalid pyramid level index on a non-invalidated pyramid level.");
			}
			_activeTilePyramidLookup[(int)tile.PyramidLevel].Remove(new TileKey(tile.RenderTask));
		}
		tile.RenderTask.IsActive = false;
	}

	private bool TryRemoveAndRecover(ActiveTile key)
	{
		return TryRemoveAndRecover(new TileKey(key.RenderTask));
	}

	private bool TryRemoveAndRecover(TileKey key)
	{
		if (_inFlightTilesLookup.ContainsKey(key))
		{
			return false;
		}
		if (!_activeTileLookup.TryGetValue(key, out var localValue))
		{
			bool activeTilePyramidLookupLevelTerm = _activeTilePyramidLookup[key.Level].Contains(key);
			bool activeTilesActiveTileTerm = _activeTiles.Select((ActiveTile selectTile) => new TileKey(selectTile.RenderTask).Equals(key)).Count() == 1;
			PyramidedTileEmployee._logger.LogError("TryRemoveAndRecoverTile tried removing a tile that wasn't in _activeTileLookup. containedInPyramidLookup: {containedInPyramidLookup}, containedInActiveTiles: {containedInActiveTiles}", activeTilePyramidLookupLevelTerm, activeTilesActiveTileTerm);
			return false;
		}
		ForceRemoveAndRecoverTile(localValue);
		return true;
	}

	private void ForceRemoveAndRecoverTile(ActiveTile tile)
	{
		RemoveActiveTile(tile);
		tile.RenderTile.Detach();
		tile.RenderTile.Dispose();
		if (!IsTileStillPlotting(tile.RenderTask))
		{
			CacheOrDispose(tile.RenderTask);
		}
	}

	private void CacheOrDispose(RenderTask task)
	{
		if (!DebugDisableCaching && !task.IsDisposed && !task.IsTemporary && task.RenderSuccess
			&& !task.IsActive && !task.IsPlotting && !task.PyramidLevel.IsInvalidated
			&& task.GlobalRenderState == _currentRenderState)
		{
			CachedTileIndex key = new CachedTileIndex(task.PyramidLevel, task.TileIndex, _currentRenderState);
			_tileCache.ForceEvict(key);
			task.IsCached = true;
			_tileCache.Make(key, task);
		}
		else if (task.CanDispose && !task.IsPlotting)
		{
			task.Dispose();
		}
	}

	public void DebugToggleTileOutlines()
	{
		if (_tileBackgroundVec.IsNull())
		{
			_tileBackgroundVec = VectorManager.AddLineVector(new Geospatial.Vectors.Point(0.0, 0.0), new VectorSymbology(Color.FromArgb(255, 255, 115, 0), 2.0, default));
			VectorManager.SetVectorDynamicDataSource(_tileBackgroundVec, DebugTileOutlinesInternal);
		}
		else
		{
			VectorManager.DeleteVector(_tileBackgroundVec);
			_tileBackgroundVec = 0L;
		}
	}

	private List<Polygon> DebugTileOutlinesInternal()
	{
		PixelRange unboundedPixelRange = _bestPyramidLevel.WorldTileRaster.GetUnboundedPixelRange(LiveScreenRaster.Extent.RatioAroundCenter(2.0));
		return _bestPyramidLevel.WorldTileRaster.GetSubRaster(unboundedPixelRange, clipToBounds: false).GetCellPolygons();
	}

	public async ValueTask DebugForceTileRenderAsync(Geospatial.Vectors.Point queryPt)
	{
		RasterDefinition worldTileRaster = BestPyramidLevel.WorldTileRaster;
		Pixel debugForceTileRender = worldTileRaster.CellIndex2D(queryPt);
		_debugForceTileRender = debugForceTileRender;
		_debugForceTileRenderPyramidLevel = _bestPyramidLevel;
		await RenderAllTilesAsync();
	}

	public async ValueTask DebugTagTileForDebuggingRenderAsync(Geospatial.Vectors.Point queryPt)
	{
		RasterDefinition worldTileRaster = BestPyramidLevel.WorldTileRaster;
		Pixel debugTagTile = worldTileRaster.CellIndex2D(queryPt);
		_debugTagTile = debugTagTile;
		_debugTagTilePyramidLevel = _bestPyramidLevel;
		await RenderAllTilesAsync();
	}

	public static void FreezeAllLayers(IList<IRenderLayer> layers)
	{
		for (int i = 0; i < layers.Count; i++)
		{
			if (layers[i] is IVectorRenderLayer vectorRenderLayer)
			{
				vectorRenderLayer.GetSpatialVersionContext()?.FreezeOne();
			}
		}
	}

	public static void ThawAllLayers(IList<IRenderLayer> layers)
	{
		for (int i = 0; i < layers.Count; i++)
		{
			if (layers[i] is IVectorRenderLayer vectorRenderLayer)
			{
				vectorRenderLayer.GetSpatialVersionContext()?.ThawOne();
			}
		}
	}

	private void SetWindowPreservingStandIn(Extent ext)
	{
		bool showingDefaultExtent = _showingDefaultExtent;
		SetWindow(ext, render: true, frameToVisibleArea: false);
		_showingDefaultExtent = showingDefaultExtent;
	}

	public void NotifyRendering()
	{
		if (!_animationRunning)
		{
			return;
		}
		if (_animationTargetExtent.IsNull() || LiveScreenRaster.IsNull())
		{
			_animationRunning = false;
			return;
		}
		if (AreExtentsClose(LiveScreenRaster.Extent, _animationTargetExtent))
		{
			SetWindowPreservingStandIn(_animationTargetExtent);
			_animationRunning = false;
			return;
		}
		TimeSpan elapsed = _animationStopwatch.Elapsed;
		TimeSpan timeSpan = elapsed - _animationLastFrameTime;
		_animationLastFrameTime = elapsed;
		if (!(timeSpan.TotalSeconds <= 0.0))
		{
			double totalSecondsOverAnimationDurationSeconds = ((_animationDurationSeconds > 0.0) ? (timeSpan.TotalSeconds / _animationDurationSeconds) : 1.0);
			if (totalSecondsOverAnimationDurationSeconds > 1.0)
			{
				totalSecondsOverAnimationDurationSeconds = 1.0;
			}
			Extent ext = LerpExtent(LiveScreenRaster.Extent, _animationTargetExtent, totalSecondsOverAnimationDurationSeconds);
			ext = AsScreenAspectRatio(ext);
			Extent extent = LiveScreenRaster.Extent;
			SetWindowPreservingStandIn(ext);
			Extent extent2 = LiveScreenRaster.Extent;
			if (AreExtentsClose(extent, extent2))
			{
				_animationRunning = false;
			}
		}
	}

	public void SetWindow_Animated(Extent ext, bool render = true, double durationSeconds = 0.3, bool frameToVisibleArea = true)
	{
		ext = FrameToVisibleArea(ext, frameToVisibleArea);
		if (!ext.IsValid() || Canvas.ActualWidth == 0.0 || Canvas.ActualHeight == 0.0)
		{
			SetWindow(ext, render, frameToVisibleArea: false);
			return;
		}
		_animationTargetExtent = AsScreenAspectRatio(ext);
		if (!_animationRunning)
		{
			_animationDurationSeconds = durationSeconds / 4.0;
			_animationLastFrameTime = TimeSpan.Zero;
			_animationStopwatch.Restart();
			_animationRunning = true;
		}
	}

	private bool AreExtentsClose(Extent a, Extent b)
	{
		double scaledCellSize = LiveScreenRaster.CellSize * 0.25;
		if (Math.Abs(a.MinX - b.MinX) < scaledCellSize && Math.Abs(a.MaxX - b.MaxX) < scaledCellSize && Math.Abs(a.MinY - b.MinY) < scaledCellSize)
		{
			return Math.Abs(a.MaxY - b.MaxY) < scaledCellSize;
		}
		return false;
	}

	private static Extent LerpExtent(Extent a, Extent b, double t)
	{
		double minX = Lerp(a.MinX, b.MinX, t);
		double maxX = Lerp(a.MaxX, b.MaxX, t);
		double minY = Lerp(a.MinY, b.MinY, t);
		double maxY = Lerp(a.MaxY, b.MaxY, t);
		return new Extent(minX, maxX, minY, maxY);
	}

	private static double Lerp(double from, double to, double t)
	{
		return from + (to - from) * t;
	}

	private ValueTask NotifyRenderTaskComplete(RenderTask task)
	{
		try
		{
			if (_isDisposed)
			{
				return ValueTask.CompletedTask;
			}
			TileKey tileKey = new TileKey(task);
			if (!_inFlightTilesLookup.ContainsKey(tileKey))
			{
				bool containsKey = _inFlightTiles.ContainsKey(task.RenderTaskDescriptor);
				bool containsKey2 = _activeTileLookup.ContainsKey(tileKey);
				bool isActive = task.IsActive;
				bool isPlotting = task.IsPlotting;
				if (containsKey | containsKey2 | isActive | isPlotting)
				{
					throw new InvalidOperationException("Something has gone poorly.");
				}
				return ValueTask.CompletedTask;
			}
			_inFlightTiles.Remove(task.RenderTaskDescriptor);
			_inFlightTilesLookup[tileKey]--;
			if (_inFlightTilesLookup[tileKey] == 0)
			{
				_inFlightTilesLookup.Remove(tileKey);
			}
			if (_activeTiles.Count > 5000)
			{
				throw new OverflowException("Too many active tiles. The current limit is 5000");
			}
			if (!task.RenderSuccess)
			{
				task.Dispose();
				return ValueTask.CompletedTask;
			}
			if (!IsTaskStillValid(task))
			{
				if (!task.IsDisposed)
				{
					task.Dispose();
				}
				return ValueTask.CompletedTask;
			}
			ActiveTile activeTile = TryFindActiveTile(task.PyramidLevel, task.TileIndex);
			if (activeTile != null && !activeTile.IsNull)
			{
				RenderTile renderTile = activeTile.RenderTile;
				renderTile.RenderColorBufferToBitmap(task.ColorBufferBytes);
				renderTile.SetLabels(task.PlotRequest.Labels);
				renderTile.SetSourceHasData(task.PlotRequest.SplatCount != 0);
				RemoveActiveTile(activeTile);
				if (task != activeTile.RenderTask && !IsTileStillPlotting(activeTile.RenderTask))
				{
					activeTile.RenderTask.Dispose();
				}
				SetActiveTile(task, renderTile);
			}
			else
			{
				RenderTile renderTile = GetNewTile(task.PyramidLevel, task.TileIndex);
				renderTile.RenderColorBufferToBitmap(task.ColorBufferBytes);
				renderTile.SetLabels(task.PlotRequest.Labels);
				renderTile.Attach();
				renderTile.SetSourceHasData(task.PlotRequest.SplatCount != 0);
				TileCanvas.SetTilePositioningAndScale(renderTile);
				SetActiveTile(task, renderTile);
			}
			TryClearHigherQualityTiles(tileKey);
			TryClearLowerQualityTiles(tileKey);
			Geospatial.Vectors.Point upperLeft = task.TileRaster.Extent.UpperLeft;
			Geospatial.Vectors.Point lowerRight = task.TileRaster.Extent.LowerRight;
			Geospatial.Vectors.Point point = ConvertWorldCoordinateToScreenPoint(upperLeft);
			Geospatial.Vectors.Point point2 = ConvertWorldCoordinateToScreenPoint(lowerRight);
			Canvas.Invalidate(point.X, point.Y, point2.X, point2.Y);
			if (TryProcessQueuedUpdates())
			{
				QueueRenderTiles();
			}
		}
		catch (Exception exception)
		{
			PyramidedTileEmployee._logger.LogError(exception, "Failed to complete render task");
			throw;
		}
		finally
		{
			PyramidedTileBoss.Instance.NotifyRenderingComplete(this);
			if (_inFlightTiles.Count == 0)
			{
				_renderCompleteSignal?.TrySetResult();
			}
		}
		return ValueTask.CompletedTask;
	}

	private RenderTile GetNewTile(PyramidLevel pyramidLevel, Pixel tileIndex)
	{
		RenderTile renderTile = RenderTile.CreateUninitialized();
		renderTile.TryInitialize(TileCanvas, ImageProvider, BitmapProvider, _labelHandler);
		renderTile.Reset(tileIndex.Row, tileIndex.Column, pyramidLevel);
		return renderTile;
	}

	private RenderTask GetNewRenderTask(PyramidLevel pyramidLevel, Pixel tileIndex)
	{
		CachedTileIndex cacheKey = new CachedTileIndex(pyramidLevel, tileIndex, _currentRenderState);
		if (!DebugDisableCaching && _tileCache.TryTake(cacheKey, out RenderTask cached))
		{
			cached.IsCached = false;
			if (!cached.IsDisposed && cached.IsValid(_currentRenderState))
			{
				cached.ResetTempState();
				return cached;
			}
			cached.Dispose();
		}
		RenderTask renderTask = RenderTask.CreateUninitialized();
		if (renderTask.IsActive)
		{
			throw new InvalidOperationException("Acquired already active task... it should have been set inactive, or not been returned.");
		}
		renderTask.Reset(tileRasterDef: GetTileRaster(pyramidLevel, tileIndex), tileManagerID: InstanceID, pyramidLevel: pyramidLevel, tileIdx: tileIndex, renderState: _currentRenderState, layers: GetLayers());
		return renderTask;
	}

	private RasterDefinition GetTileRaster(PyramidLevel pyramidLevel, Pixel tileIndex)
	{
		return new RasterDefinition(pyramidLevel.WorldTileRaster.CellExtent(tileIndex), pyramidLevel.CellSize);
	}

	private void SetActiveTile(RenderTask rtask, RenderTile rt)
	{
		if (rtask.IsDisposed)
		{
			throw new Exception("Re-using disposed instance - error.");
		}
		TileKey tileKey = new TileKey(rtask);
		ActiveTile activeTile = new ActiveTile(rtask, rt);
		_activeTiles.Add(activeTile);
		_activeTileLookup[tileKey] = activeTile;
		EnsureActiveTilePyramidLookup();
		_activeTilePyramidLookup[(int)rtask.PyramidLevel].Add(tileKey);
		rtask.IsActive = true;
	}

	private IList<RenderTask> GetRenderWorkload(PyramidLevel renderPyramidLevel, LoopType loopType = LoopType.ScanLine)
	{
		if (!_debugForceTileRender.IsNull())
		{
			RenderTask newRenderTask = GetNewRenderTask(_debugForceTileRenderPyramidLevel, _debugForceTileRender);
			List<RenderTask> result = new List<RenderTask> { newRenderTask };
			AddInFlightTile(newRenderTask);
			_debugForceTileRender = Pixel.NullPixel;
			return result;
		}
		IList<IRenderLayer> layers = GetLayers();
		FreezeAllLayers(layers);
		try
		{
			PixelRange bufferedOnscreenTiles = GetBufferedOnscreenTiles(renderPyramidLevel);
			List<RenderTask> renderWorkloadForPyramidLevelAndRange = GetRenderWorkloadForPyramidLevelAndRange(loopType, renderPyramidLevel, bufferedOnscreenTiles);
			if (renderWorkloadForPyramidLevelAndRange != null && renderWorkloadForPyramidLevelAndRange.Count > 5000)
			{
				OnMaximumScreenTilesExceeded(renderWorkloadForPyramidLevelAndRange);
				return default(CompactList<RenderTask>);
			}
			return renderWorkloadForPyramidLevelAndRange;
		}
		finally
		{
			ThawAllLayers(layers);
		}
	}

	private void OnMaximumScreenTilesExceeded(List<RenderTask> tasksBounced)
	{
		PyramidedTileEmployee._logger.LogError("Queueing {ct} tiles to be rendered, which is more than the cap {cap}", tasksBounced.Count, 5000);
		foreach (RenderTask tasksBounced2 in tasksBounced)
		{
			RenderTaskDescriptor renderTaskDescriptor = tasksBounced2.RenderTaskDescriptor;
			if (!_inFlightTiles.TryGetValue(renderTaskDescriptor, out var localValue) || localValue != tasksBounced2)
			{
				continue;
			}
			_inFlightTiles.Remove(renderTaskDescriptor);
			TileKey key = new TileKey(tasksBounced2);
			if (_inFlightTilesLookup.TryGetValue(key, out var localValue2))
			{
				if (localValue2 > 1)
				{
					_inFlightTilesLookup[key] = localValue2 - 1;
				}
				else
				{
					_inFlightTilesLookup.Remove(key);
				}
			}
			if (tasksBounced2.CanDispose)
			{
				tasksBounced2.Dispose();
			}
		}
	}

	private List<RenderTask> GetRenderWorkloadForPyramidLevelAndRange(LoopType loopType, PyramidLevel pl, PixelRange screenRange)
	{
		List<RenderTask> retn = null;
		switch (loopType)
		{
		case LoopType.ScanLine:
			GetRenderWorkloadHelper_ScanLine(screenRange, pl, ref retn);
			break;
		case LoopType.InToOut:
			GetRenderWorkloadHelper_InToOut(screenRange, pl, ref retn);
			break;
		case LoopType.OutToIn:
			GetRenderWorkloadHelper_OutToIn(screenRange, pl, ref retn);
			break;
		default:
			throw new NotImplementedException();
		}
		return retn;
	}

	private void GetRenderWorkloadHelper_ScanLine(PixelRange screenRange, PyramidLevel renderPyramidLevel, ref List<RenderTask> retn)
	{
		for (int i = screenRange.MinRow; i <= screenRange.MaxRow; i++)
		{
			for (int j = screenRange.MinCol; j <= screenRange.MaxCol; j++)
			{
				if (retn != null && retn.Count > 5000)
				{
					return;
				}
				Pixel tileIndex = new Pixel(i, j);
				TryGetRenderWorkloadForTile(tileIndex, renderPyramidLevel, ref retn);
			}
		}
	}

	private void GetRenderWorkloadHelper_OutToIn(PixelRange screenRange, PyramidLevel renderPyramidLevel, ref List<RenderTask> retn)
	{
		int maxColScreenRangeTerm = screenRange.MaxCol - screenRange.MinCol + 1;
		int maxRowScreenRangeTerm = screenRange.MaxRow - screenRange.MinRow + 1;
		int count = Math.Min(maxColScreenRangeTerm, maxRowScreenRangeTerm) / 2;
		for (int i = 0; i < count; i++)
		{
			if (retn != null && retn.Count > 5000)
			{
				return;
			}
			int minRowScreenRangeTerm = screenRange.MinRow + i;
			int count2 = screenRange.MaxRow - i;
			int minColScreenRangeTerm = screenRange.MinCol + i;
			int count3 = screenRange.MaxCol - i;
			for (int count4 = count3; count4 >= minColScreenRangeTerm; count4--)
			{
				int r = minRowScreenRangeTerm;
				Pixel tileIndex = new Pixel(r, count4);
				TryGetRenderWorkloadForTile(tileIndex, renderPyramidLevel, ref retn);
			}
			for (int j = minRowScreenRangeTerm + 1; j <= count2; j++)
			{
				int c = minColScreenRangeTerm;
				Pixel tileIndex2 = new Pixel(j, c);
				TryGetRenderWorkloadForTile(tileIndex2, renderPyramidLevel, ref retn);
			}
			for (int k = minColScreenRangeTerm + 1; k <= count3; k++)
			{
				int r2 = count2;
				Pixel tileIndex3 = new Pixel(r2, k);
				TryGetRenderWorkloadForTile(tileIndex3, renderPyramidLevel, ref retn);
			}
			for (int count2Offset = count2 - 1; count2Offset > minRowScreenRangeTerm; count2Offset--)
			{
				int c2 = count3;
				Pixel tileIndex4 = new Pixel(count2Offset, c2);
				TryGetRenderWorkloadForTile(tileIndex4, renderPyramidLevel, ref retn);
			}
		}
		if (maxColScreenRangeTerm % 2 != 0 && maxRowScreenRangeTerm % 2 != 0)
		{
			int r3 = (screenRange.MaxRow + screenRange.MinRow) / 2;
			int c3 = (screenRange.MaxCol + screenRange.MinCol) / 2;
			Pixel tileIndex5 = new Pixel(r3, c3);
			TryGetRenderWorkloadForTile(tileIndex5, renderPyramidLevel, ref retn);
		}
	}

	private void GetRenderWorkloadHelper_InToOut(PixelRange screenRange, PyramidLevel renderPyramidLevel, ref List<RenderTask> retn)
	{
		int maxColScreenRangeTerm = screenRange.MaxCol - screenRange.MinCol + 1;
		int maxRowScreenRangeTerm = screenRange.MaxRow - screenRange.MinRow + 1;
		int maxRowScreenRangeTerm2 = (screenRange.MaxRow - screenRange.MinRow + 1) / 2;
		int maxColScreenRangeTerm2 = (screenRange.MaxCol - screenRange.MinCol + 1) / 2;
		int ordinal = Math.Max(maxRowScreenRangeTerm2, maxColScreenRangeTerm2);
		if (maxColScreenRangeTerm % 2 != 0 && maxRowScreenRangeTerm % 2 != 0)
		{
			int r = (screenRange.MaxRow + screenRange.MinRow) / 2;
			int c = (screenRange.MaxCol + screenRange.MinCol) / 2;
			Pixel tileIndex = new Pixel(r, c);
			TryGetRenderWorkloadForTile(tileIndex, renderPyramidLevel, ref retn);
		}
		int ordinal2 = ordinal - 1;
		while (ordinal2 >= 0 && (retn == null || retn.Count <= 5000))
		{
			int minRowScreenRangeTerm = screenRange.MinRow + ordinal2;
			int count = screenRange.MaxRow - ordinal2;
			int minColScreenRangeTerm = screenRange.MinCol + ordinal2;
			int count2 = screenRange.MaxCol - ordinal2;
			for (int count3 = count; count3 >= minRowScreenRangeTerm; count3--)
			{
				int c2 = count2;
				Pixel tileIndex2 = new Pixel(count3, c2);
				TryGetRenderWorkloadForTile(tileIndex2, renderPyramidLevel, ref retn);
			}
			for (int count2Offset = count2 - 1; count2Offset >= minColScreenRangeTerm; count2Offset--)
			{
				int r2 = minRowScreenRangeTerm;
				Pixel tileIndex3 = new Pixel(r2, count2Offset);
				TryGetRenderWorkloadForTile(tileIndex3, renderPyramidLevel, ref retn);
			}
			for (int i = minRowScreenRangeTerm + 1; i <= count; i++)
			{
				int c3 = minColScreenRangeTerm;
				Pixel tileIndex4 = new Pixel(i, c3);
				TryGetRenderWorkloadForTile(tileIndex4, renderPyramidLevel, ref retn);
			}
			for (int j = minColScreenRangeTerm + 1; j < count2; j++)
			{
				int r3 = count;
				Pixel tileIndex5 = new Pixel(r3, j);
				TryGetRenderWorkloadForTile(tileIndex5, renderPyramidLevel, ref retn);
			}
			ordinal2--;
		}
	}

	private void TryGetRenderWorkloadForTile(Pixel tileIndex, PyramidLevel renderPyramidLevel, ref List<RenderTask> retn)
	{
		if (tileIndex == _debugTagTile && renderPyramidLevel == _debugTagTilePyramidLevel)
		{
			_debugTagTile = Pixel.NullPixel;
			Debugger.Break();
		}
		ActiveTile activeTile = TryFindActiveTile(renderPyramidLevel, tileIndex);
		RenderTask renderTask = null;
		if (activeTile != null && !activeTile.IsNull)
		{
			renderTask = activeTile.RenderTask;
			if (renderTask.IsValid(GlobalRenderState))
			{
				return;
			}
		}
		RenderTaskDescriptor key = new RenderTaskDescriptor(_currentRenderState, renderPyramidLevel, tileIndex);
		TileKey key2 = new TileKey(renderPyramidLevel, tileIndex);
		if (_inFlightTiles.TryGetValue(key, out var _))
		{
			return;
		}
		if ((!_inFlightTilesLookup.TryGetValue(key2, out var localValue) || localValue <= 0) && renderTask != null && !renderTask.IsTemporary && _currentRenderState == renderTask.GlobalRenderState)
		{
			renderTask.ResetTempState();
			if (retn == null)
			{
				retn = new List<RenderTask>();
			}
			retn.Add(renderTask);
			AddInFlightTile(renderTask);
		}
		else
		{
			RenderTask newRenderTask = GetNewRenderTask(renderPyramidLevel, tileIndex);
			if (retn == null)
			{
				retn = new List<RenderTask>();
			}
			retn.Add(newRenderTask);
			AddInFlightTile(newRenderTask);
		}
	}

	private void GetAllParentTiles(Pixel tileIndex, int pl, ref List<ActiveTile> at)
	{
		if (pl >= 0)
		{
			if (_activeTileLookup.TryGetValue(new TileKey(pl, tileIndex), out var localValue))
			{
				at.Add(localValue);
			}
			Pixel tileIndex2 = new Pixel((int)Math.Floor((float)tileIndex.Row / 2f), (int)Math.Floor((float)tileIndex.Column / 2f));
			GetAllParentTiles(tileIndex2, pl - 1, ref at);
		}
	}

	private void TryClearHigherQualityTiles(TileKey baseTile)
	{
		TileSystemHelpers.TryClearHigherQualityTiles(this, baseTile, _activeTileLookup.Keys, GetTilesAtPyramidLevel, TryRemoveAndRecover, 22);
	}

	private static List<TileKey> GetTilesAtPyramidLevel<Model>(Model model, int pyramidLevel) where Model : PyramidedTileManager
	{
		return model._activeTilePyramidLookup[pyramidLevel];
	}

	private static bool TryRemoveAndRecover<Model>(Model model, TileKey tileKey) where Model : PyramidedTileManager
	{
		return model.TryRemoveAndRecover(tileKey);
	}

	private static PixelRange GetOnScreenTilesForPyramidLevel<Model>(Model model, int pyramidLevel) where Model : PyramidedTileManager
	{
		return model.GetBufferedOnscreenTiles(model.TileCanvas.PyramidLevels[pyramidLevel]);
	}

	private void TryClearLowerQualityTiles(TileKey baseTile)
	{
		TileSystemHelpers.TryClearLowerQualityTilesInternal(this, baseTile, _activeTileLookup.Keys, GetTilesAtPyramidLevel, TryRemoveAndRecover, GetOnScreenTilesForPyramidLevel, 22);
	}

	private int[] CalculateExpectedTilesCountRelative(PyramidLevel level)
	{
		if (level.Equals(_lastResultPyramidLevel))
		{
			return _expectedTilesCountRelativeScratchSpace;
		}
		if (_expectedTilesCountRelativeScratchSpace == null)
		{
			_expectedTilesCountRelativeScratchSpace = new int[23];
		}
		Array.Clear(_expectedTilesCountRelativeScratchSpace);
		double x = 4.0;
		for (int i = 0; i < 22; i++)
		{
			if (i >= (int)level)
			{
				_expectedTilesCountRelativeScratchSpace[i] = (int)Math.Pow(x, i - (int)level);
			}
		}
		_lastResultPyramidLevel = level;
		return _expectedTilesCountRelativeScratchSpace;
	}

	private ActiveTile TryFindActiveTile(PyramidLevel tileIndexPyramidLevel, Pixel tileIndex)
	{
		if (_activeTileLookup.TryGetValue(new TileKey(tileIndexPyramidLevel, tileIndex), out var localValue))
		{
			return localValue;
		}
		return null;
	}
}
