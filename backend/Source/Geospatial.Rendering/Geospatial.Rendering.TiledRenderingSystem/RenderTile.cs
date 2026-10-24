using System;
using System.Collections.Generic;
using System.Diagnostics;
using Geospatial.Rendering.Systems;
using Geospatial.Systems.Shapes;
using Utility.VisualElements;

namespace Geospatial.Rendering.TiledRenderingSystem;

[DebuggerDisplay("{TileRow},{TileColumn}")]
public class RenderTile : IDisposable
{
	public const int TileSize = 256;

	public const int OnScreenTileSize = 256;

	public static readonly int GlobalLabelShiftUp = 18;

	public static readonly int GlobalLabelShiftRight = 0;

	public static readonly int AssumedPixelsPerCharHz = 10;

	public static readonly int AssumedPixelsPerCharVert = 10;

	private ITileCanvas _tileCanvas;

	private IImageProvider _imgProvider;

	private IBitmapProvider _bmpProvider;

	public IImage Image;

	public IBitmap Bitmap;

	public List<ILabelDefinition> LayerLabels;

	public PyramidLevel PyramidLevel;

	public int TileRow;

	public int TileColumn;

	private bool _isDisposed;

	private bool _isInitialized;

	private ILabelHandler _labelHandler;

	private ICanvas _cnv => _tileCanvas.Canvas;

	public static RenderTile CreateUninitialized()
	{
		return new RenderTile();
	}

	private RenderTile()
	{
	}

	public void TryInitialize(ITileCanvas cnv, IImageProvider imgProvider, IBitmapProvider bmpProvider, ILabelHandler labelHandler)
	{
		if (!_isInitialized)
		{
			_tileCanvas = cnv;
			_imgProvider = imgProvider;
			_bmpProvider = bmpProvider;
			_labelHandler = labelHandler;
			_isInitialized = true;
		}
	}

	public void Reset(int row, int col, PyramidLevel pyramidLevel)
	{
		_isDisposed = false;
		Bitmap = _bmpProvider.Acquire(256, 256);
		Image = _imgProvider.Acquire(256.0, 256.0);
		Image.Source = Bitmap;
		TileRow = row;
		TileColumn = col;
		PyramidLevel = pyramidLevel;
		_tileCanvas.SetTilePositioningAndScale(this);
	}

	public void Attach()
	{
		_cnv.SetZIndex(Image, 10 + (int)PyramidLevel);
		_cnv.AddChild(Image);
	}

	public void SetSourceHasData(bool hasData)
	{
		Image.Source.HasData = hasData;
	}

	public void Detach()
	{
		_cnv.RemoveChild(Image);
		if (LayerLabels != null)
		{
			_labelHandler.RemoveLabels(LayerLabels);
			LayerLabels.Clear();
		}
	}

	public void RenderColorBufferToBitmap(Memory<int> colorBuffer)
	{
		Bitmap.RenderColorBufferToBitmap(colorBuffer);
	}

	public void RenderColorBufferToBitmap(byte[] colorBuffer)
	{
		Bitmap.RenderColorBufferToBitmap(colorBuffer);
	}

	public void SetLabels(List<ILabelDefinition> hovers)
	{
		if (LayerLabels != null)
		{
			_labelHandler.RemoveLabels(LayerLabels);
			LayerLabels.Clear();
		}
		if (hovers != null && hovers.Count <= 1000 && hovers.Count != 0)
		{
			if (LayerLabels == null)
			{
				LayerLabels = new List<ILabelDefinition>();
			}
			for (int i = 0; i < hovers.Count; i++)
			{
				ILabelDefinition hover = hovers[i];
				LayerLabels.Add(hover);
			}
			_labelHandler.AddLabels(LayerLabels);
		}
	}

	protected virtual void Dispose(bool disposing)
	{
		if (_isDisposed)
		{
			return;
		}
		try
		{
			_imgProvider?.Return(Image);
			_bmpProvider?.Return(Bitmap);
			_isDisposed = true;
			if (disposing)
			{
				List<ILabelDefinition> layerLabels = LayerLabels;
				if (layerLabels != null && layerLabels.Count > 0)
				{
					LayerLabels.Clear();
					throw new Exception();
				}
			}
		}
		catch
		{
		}
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	~RenderTile()
	{
		if (!_isDisposed)
		{
			Dispose(disposing: false);
		}
	}
}
