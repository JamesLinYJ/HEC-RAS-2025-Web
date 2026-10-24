using System;
using System.Buffers;
using System.Collections.Generic;
using Geospatial.Rasters;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Systems;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility.DataStructures;
using Utility.Logging;
using Utility.Memory;

namespace Geospatial.Rendering.TiledRenderingSystem;

public class RenderTask : IRenderTask, IDisposable
{
	private static readonly ILogger _logger = LogHelper.GetLogger("RenderTask");

	private CompactList3<FrameGroup> _frameGroups;

	private CommonData _commonData;

	private IList<IRenderLayer> _layers;

	private readonly bool _isTemporary;

	private bool _isDisposed;

	private bool _isInitialized;

	private bool _isPlotting;

	private int _currentOwningTileManagerID = -1;

	public double CellSize { get; private set; }

	public long GlobalRenderState { get; private set; }

	public Pixel TileIndex { get; private set; }

	public PyramidLevel PyramidLevel { get; private set; }

	internal RenderTaskDescriptor RenderTaskDescriptor => new RenderTaskDescriptor(GlobalRenderState, PyramidLevel, TileIndex);

	public RasterDefinition TileRaster { get; private set; }

	public PlotRequest PlotRequest { get; private set; }

	public Memory<int> ColorBufferARGB => PlotRequest.ColorBufferARGB;

	public byte[] ColorBufferBytes => PlotRequest.ColorBufferBytes;

	public bool IsTemporary => _isTemporary;

	public bool IsDisposed => _isDisposed;

	public bool IsCached { get; set; }

	public bool IsActive { get; set; }

	public bool CanDispose
	{
		get
		{
			if (!IsCached)
			{
				return !IsActive;
			}
			return false;
		}
	}

	public bool RenderSuccess { get; set; }

	public bool IsPlotting => _isPlotting;

	public static RenderTask CreateUninitialized()
	{
		return new RenderTask();
	}

	private RenderTask()
	{
		PlotRequest = PlotRequest.CreateUninitialized();
	}

	public RenderTask(int tileManagerID, PyramidLevel pyramidLevel, Pixel tileIdx, int renderState, RasterDefinition tileRasterDef, IList<IRenderLayer> layers)
		: this()
	{
		Reset(tileManagerID, pyramidLevel, tileIdx, renderState, tileRasterDef, layers);
	}

	private void TryCheckCreateFrameGroups()
	{
		if (!_isInitialized)
		{
			CreateFramesAndFrameGroups();
		}
	}

	public void Reset(int tileManagerID, PyramidLevel pyramidLevel, Pixel tileIdx, int renderState, RasterDefinition tileRasterDef, IList<IRenderLayer> layers)
	{
		_currentOwningTileManagerID = tileManagerID;
		IsActive = false;
		RenderSuccess = false;
		PyramidLevel = pyramidLevel;
		TileIndex = tileIdx;
		CellSize = tileRasterDef.CellSize;
		GlobalRenderState = renderState;
		TileRaster = tileRasterDef;
		_layers = layers;
		_isInitialized = false;
	}

	internal RenderTask(Pixel tileIdx, double cellSize, int renderState, RasterDefinition rasterDef)
	{
		TileIndex = tileIdx;
		CellSize = cellSize;
		GlobalRenderState = renderState;
		TileRaster = rasterDef;
		_isTemporary = true;
	}

	private static bool ShouldRenderSeparateFrame(IRenderLayer lyr)
	{
		if (!(lyr is IBandedRenderLayer))
		{
			if (lyr is RenderLayer renderLayer)
			{
				return renderLayer.RequestSeparateRenderFrame;
			}
			return false;
		}
		return true;
	}

	private void CreateFramesAndFrameGroups()
	{
		if (_isPlotting)
		{
			throw new InvalidOperationException();
		}
		_isInitialized = true;
		int cellCount = TileRaster.CellCount;
		_frameGroups.Clear();
		PlotRequest.Reset(TileRaster);
		int ordinal = 0;
		for (int i = 0; i < _layers.Count; i++)
		{
			if (ShouldRenderSeparateFrame(_layers[i]))
			{
				if (i > ordinal)
				{
					AddFrameGroup(ordinal, i, cellCount);
				}
				AddFrameGroup(i, i + 1, cellCount);
				ordinal = i + 1;
			}
		}
		if (_layers.Count > ordinal)
		{
			AddFrameGroup(ordinal, _layers.Count, cellCount);
		}
	}

	private void AddFrameGroup(int start, int end, int frameSize)
	{
		Frame[] frames = new Frame[end - start];
		for (int i = 0; i < frames.Length; i++)
		{
			frames[i] = new Frame(_layers[start + i], frameSize);
		}
		_frameGroups.Add(FrameGroup.Create(frames, TileRaster, PlotRequest.Reporter));
	}

	public void ResetTempState()
	{
		PlotRequest.Reporter.ClearCancellationRequest();
	}

	public bool IsValid(long globalState)
	{
		if (globalState != GlobalRenderState)
		{
			return false;
		}
		return IsFrameLocalStateValid();
	}

	private bool IsFrameLocalStateValid()
	{
		TryCheckCreateFrameGroups();
		for (int i = 0; i < _frameGroups.Count; i++)
		{
			if (!_frameGroups[i].IsValid())
			{
				return false;
			}
		}
		return true;
	}

	public bool Render()
	{
		if (IsDisposed)
		{
			return false;
		}
		TryCheckCreateFrameGroups();
		_isPlotting = true;
		bool condition = false;
		if (PlotRequest.IsDisposed)
		{
			throw new InvalidOperationException();
		}
		if (_commonData == null)
		{
			_commonData = new CommonData();
		}
		_commonData.Clear();
		CommonData commonData = _commonData;
		for (int i = 0; i < _frameGroups.Count; i++)
		{
			FrameGroup frameGroup = _frameGroups[i];
			if (!frameGroup.IsValid())
			{
				try
				{
					frameGroup.Plot(commonData);
				}
				catch (Exception exception)
				{
					_logger.LogError(exception, "Failed to plot frame group");
				}
			}
			if (PlotRequest.Reporter.CancelRequested)
			{
				_isPlotting = false;
				return false;
			}
			if (frameGroup.PlotRequest.SplatCount > 0)
			{
				if (condition)
				{
					PlotRequest.Splat(frameGroup.PlotRequest.ColorBufferARGB);
				}
				else
				{
					PlotRequest.ClearAndSplat(frameGroup.PlotRequest.ColorBufferARGB);
					PlotRequest.CommonData = commonData;
					condition = true;
				}
			}
			lock (frameGroup.PlotRequest.RasterDataLock)
			{
				if (frameGroup.PlotRequest.RasterData == null)
				{
					continue;
				}
				foreach (KeyValuePair<IRenderLayer, IMemoryOwner<float>> rasterDatum in frameGroup.PlotRequest.RasterData)
				{
					Memory<float> floatSpan = rasterDatum.Value.Memory;
					IMemoryOwner<float> data = SharedMemoryPool<float>.RentNoFill(floatSpan.Length, out var mem);
					floatSpan.CopyTo(mem);
					PlotRequest.SetRasterData_TransferOwnership(rasterDatum.Key, data);
				}
			}
		}
		int ordinal = 0;
		for (int j = 0; j < _frameGroups.Count; j++)
		{
			ordinal += _frameGroups[j].PlotRequest.Labels?.Count ?? 0;
		}
		if (ordinal > 0)
		{
			if (PlotRequest.Labels == null)
			{
				PlotRequest.Labels = new List<ILabelDefinition>(ordinal);
			}
			for (int k = 0; k < _frameGroups.Count; k++)
			{
				List<ILabelDefinition> labels = _frameGroups[k].PlotRequest.Labels;
				if (labels == null)
				{
					continue;
				}
				for (int l = 0; l < labels.Count; l++)
				{
					if (!IsLabelOccludedAbove(labels[l], k))
					{
						PlotRequest.Labels.Add(labels[l]);
					}
				}
			}
		}
		Memory<int> colorBufferARGB;
		if (!condition)
		{
			colorBufferARGB = ColorBufferARGB;
			colorBufferARGB.Span.Fill(0);
		}
		colorBufferARGB = ColorBufferARGB;
		Span<int> colorBufferARGBSpan = colorBufferARGB.Span;
		ImageOperations.CopyAndPremultiply(colorBufferARGBSpan, colorBufferARGBSpan);
		_isPlotting = false;
		return true;
	}

	private bool IsLabelOccludedAbove(ILabelDefinition label, int groupIndex)
	{
		if (groupIndex >= _frameGroups.Count - 1)
		{
			return false;
		}
		Memory<Point> polylineLocation = label.PolylineLocation;
		Point point;
		if (polylineLocation.Length <= 0)
		{
			point = label.PointLocation;
		}
		else
		{
			polylineLocation = label.PolylineLocation;
			Span<Point> polylineLocationSpan = polylineLocation.Span;
			polylineLocation = label.PolylineLocation;
			point = polylineLocationSpan[polylineLocation.Length / 2];
		}
		Point point2 = point;
		RasterDefinition rasterQuery = PlotRequest.RasterQuery;
		int row = rasterQuery.Row(in point2.Y);
		int col = rasterQuery.Col(in point2.X);
		if (row < 0 || row >= rasterQuery.Rows || col < 0 || col >= rasterQuery.Cols)
		{
			return false;
		}
		int index = rasterQuery.CellIndex(row, col);
		double unity = 1.0;
		for (int i = groupIndex + 1; i < _frameGroups.Count; i++)
		{
			PlotRequest plotRequest = _frameGroups[i].PlotRequest;
			if (plotRequest.SplatCount != 0)
			{
				int colorBufferARGBPlotRequestTerm = (plotRequest.ColorBufferARGB.Span[index] >> 24) & 0xFF;
				unity *= 1.0 - (double)colorBufferARGBPlotRequestTerm / 255.0;
				if (1.0 - unity > 0.25)
				{
					return true;
				}
			}
		}
		return false;
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(GlobalRenderState, TileIndex);
	}

	public override bool Equals(object obj)
	{
		if (obj == null)
		{
			return false;
		}
		if (obj is RenderTask renderTask)
		{
			if (renderTask.GlobalRenderState == GlobalRenderState)
			{
				return renderTask.TileIndex == TileIndex;
			}
			return false;
		}
		return false;
	}

	private void DisposeFramegroups()
	{
		if (_isPlotting)
		{
			throw new InvalidOperationException();
		}
		for (int i = 0; i < _frameGroups.Count; i++)
		{
			_frameGroups[i]?.Dispose();
		}
		_frameGroups.Clear();
	}

	private void DisposeInternalResources()
	{
		DisposeFramegroups();
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_isDisposed)
		{
			if (disposing && !_isTemporary)
			{
				DisposeInternalResources();
				PlotRequest?.Dispose();
			}
			_isDisposed = true;
		}
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
