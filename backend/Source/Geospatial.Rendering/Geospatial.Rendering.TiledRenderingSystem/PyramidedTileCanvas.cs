using System;
using System.Collections.Generic;
using System.Numerics;
using Geospatial.Rasters;
using Geospatial.Rasters.Convolution;
using Geospatial.Systems.Shapes;
using Geospatial.Vectors;

namespace Geospatial.Rendering.TiledRenderingSystem;

public class PyramidedTileCanvas : ITileCanvas
{
	public const int PyramidLevelCount = 22;

	public const int PyramidLevelStep = 2;

	private TransformationMatrix3x3D _transform;

	private List<PyramidLevel> _pyramidLevels;

	private double _originCellSize;

	private int _shiftFromOrigin;

	public ICanvas Canvas { get; private init; }

	public RasterDefinition CanvasWorldRaster { get; private set; }

	public double CellSize
	{
		get
		{
			if (!CanvasWorldRaster.IsNull())
			{
				return CanvasWorldRaster.CellSize;
			}
			return double.NaN;
		}
	}

	public PyramidLevel PyramidLevel { get; set; }

	public TransformationMatrix3x3D Transform => _transform;

	public IReadOnlyList<PyramidLevel> PyramidLevels => _pyramidLevels;

	public PyramidedTileCanvas(ICanvas canvas)
	{
		Canvas = canvas;
		_transform = new TransformationMatrix3x3D();
		Canvas.Transformation = _transform.AsMatrix3x3();
	}

	public void ShiftPyramidLevels(int shift)
	{
		if (shift == 0)
		{
			return;
		}
		if (shift < 0)
		{
			for (int i = shift; i < 0; i++)
			{
				_pyramidLevels.RemoveAt(0);
				_pyramidLevels.Add(GeneratePyramidLevel(_originCellSize, shift, 22 - _shiftFromOrigin + i - shift + 1));
			}
		}
		else
		{
			for (int j = 1; j <= shift; j++)
			{
				_pyramidLevels.RemoveAt(_pyramidLevels.Count - 1);
				_pyramidLevels.Insert(0, GeneratePyramidLevel(_originCellSize, shift, -(_shiftFromOrigin + j)));
			}
		}
		_shiftFromOrigin += shift;
	}

	public void TryCheckInitializePyramidLevels(Extent extent)
	{
		if (PyramidLevels == null)
		{
			InitializePyramidLevels(extent);
		}
	}

	public void InitializePyramidLevels(Extent defaultExtent)
	{
		if (Canvas.ActualWidth != 0.0)
		{
			double baseCellSize = (_originCellSize = GetTargetBasePyramidCellSize(defaultExtent));
			_shiftFromOrigin = 0;
			_pyramidLevels = new List<PyramidLevel>();
			for (int i = 0; i <= 22; i++)
			{
				_pyramidLevels.Add(GeneratePyramidLevel(baseCellSize, 0, i));
			}
		}
	}

	private PyramidLevel GeneratePyramidLevel(double baseCellSize, int shift, int pyramidLevelIndexOffsetRelativeToBase)
	{
		double baseCellSizeTerm = baseCellSize / Math.Pow(2.0, pyramidLevelIndexOffsetRelativeToBase);
		double cellSize = baseCellSizeTerm * 256.0;
		RasterDefinition tileRaster = new RasterDefinition(new Point(0.0, 0.0), cellSize, 1, 1);
		RasterDefinition worldRaster = new RasterDefinition(new Point(0.0, 0.0), baseCellSizeTerm, 1, 1);
		if (!tileRaster.IsValid() || !worldRaster.IsValid())
		{
			throw new InvalidOperationException($"Pyramid level {pyramidLevelIndexOffsetRelativeToBase + shift + _shiftFromOrigin} is not representable. Base cell size {baseCellSize}, offset {pyramidLevelIndexOffsetRelativeToBase}, shift {shift}, shift from origin {_shiftFromOrigin}, resulting cell size {baseCellSizeTerm}.");
		}
		return new PyramidLevel(pyramidLevelIndexOffsetRelativeToBase + shift + _shiftFromOrigin, worldRaster, tileRaster);
	}

	public double GetTargetBasePyramidCellSize(Extent targetExtent)
	{
		return targetExtent.RatioAroundCenter(4.0).Width / (double)(int)Math.Round(Canvas.ActualWidth);
	}

	public PyramidLevel GetBestPyramidLevel(RasterDefinition raster)
	{
		double cellSize = raster.CellSize;
		if (raster.CellSize >= PyramidLevels[0].CellSize)
		{
			return PyramidLevels[0];
		}
		for (int i = 0; i <= 22; i++)
		{
			double cellSize2 = PyramidLevels[i].CellSize;
			if (cellSize >= cellSize2)
			{
				return PyramidLevels[i];
			}
		}
		IReadOnlyList<PyramidLevel> pyramidLevels = PyramidLevels;
		return pyramidLevels[pyramidLevels.Count - 1];
	}

	public RasterDefinition GetBestScreenRaster(RasterDefinition targetRaster, int pyramidLevel)
	{
		return new RasterDefinition(targetRaster.Extent, PyramidLevels[pyramidLevel].CellSize);
	}

	public bool IsValid()
	{
		if (HasValidPyramidLevels() && PyramidLevel.IsValid())
		{
			return CanvasWorldRaster.IsValid();
		}
		return false;
	}

	public bool HasValidPyramidLevels()
	{
		return PyramidLevels != null;
	}

	public void PartialZoom(Point centerOnScreenPixel, double scale)
	{
		_transform.ExtractScale(out var scale2);
		double centerX = centerOnScreenPixel.X / scale2;
		double centerY = centerOnScreenPixel.Y / scale2;
		_transform.Scale(scale, centerX, centerY);
		Canvas.Transformation = _transform.AsMatrix3x3();
	}

	public void CommitZoom()
	{
	}

	public void PartialTranslate(Point deltaScreenPixels)
	{
		_transform.Translate(0.0 - deltaScreenPixels.X, 0.0 - deltaScreenPixels.Y);
		Canvas.Transformation = _transform.AsMatrix3x3();
		_transform.ExtractScale(out var scale);
		double deltaScreenPixelsCellSizeTerm = deltaScreenPixels.X * (CellSize / scale);
		double deltaScreenPixelsCellSizeTerm2 = (0.0 - deltaScreenPixels.Y) * (CellSize / scale);
		CanvasWorldRaster = CanvasWorldRaster.Translate((float)deltaScreenPixelsCellSizeTerm, (float)deltaScreenPixelsCellSizeTerm2);
	}

	public void CommitPartialTranslation(IList<ActiveTile> activeTiles)
	{
		ResetTranslation();
		_transform.ExtractTranslation(out var translationX, out var translationY);
		_transform.ExtractScale(out var scale);
		for (int i = 0; i < activeTiles.Count; i++)
		{
			SetTilePositioning(activeTiles[i].RenderTile, translationX, translationY, scale);
		}
		Canvas.Invalidate();
	}

	private void ResetTranslation()
	{
		_transform.ResetTranslation();
		Canvas.Transformation = _transform.AsMatrix3x3();
	}

	public void SetWindow(Point upperLeft, double screenRasterCellSize)
	{
		ResetTranslation();
		CanvasWorldRaster = PyramidLevel.WorldRaster.Translate(upperLeft.X, upperLeft.Y);
		double screenRasterCellSizeOverCellSize = screenRasterCellSize / CellSize;
		_transform.SetScale(1.0 / screenRasterCellSizeOverCellSize, 0.0, 0.0);
		Canvas.Transformation = _transform.AsMatrix3x3();
		Canvas.Invalidate();
	}

	public void RefreshAllTilePositioningAndScales(IList<ActiveTile> activeTiles)
	{
		foreach (ActiveTile activeTile in activeTiles)
		{
			SetTilePositioningAndScale(activeTile.RenderTile);
		}
		Canvas.Invalidate();
	}

	public void SetTilePositioningAndScale(RenderTile tile)
	{
		SetTilePositioning(tile);
		SetTileScaling(tile);
	}

	private void SetTilePositioning(RenderTile tile)
	{
		_transform.ExtractTranslation(out var translationX, out var translationY);
		_transform.ExtractScale(out var scale);
		SetTilePositioning(tile, translationX, translationY, scale);
	}

	private void SetTilePositioning(RenderTile tile, double translationX, double translationY, double scale)
	{
		ITileCanvas.SetTilePositioning(CanvasWorldRaster, tile, translationX, translationY, scale, GetTileScale(tile));
	}

	private void SetTileScaling(RenderTile tile)
	{
		double tileScale = GetTileScale(tile);
		tile.Image.Scale = new Vector3((float)tileScale, (float)tileScale, 1f);
		tile.Image.CenterPoint = new Vector3(0f, 0f, 0f);
	}

	private double GetTileScale(RenderTile tile)
	{
		return tile.PyramidLevel.CellSize / PyramidLevel.CellSize;
	}

	public Point ConvertScreenPointToWorldCoordinate(Point screenPixel)
	{
		_transform.ExtractScale(out var scale);
		double screenPixelCellSizeTerm = screenPixel.X * (CellSize / scale);
		double screenPixelCellSizeTerm2 = screenPixel.Y * (CellSize / scale);
		return new Point(CanvasWorldRaster.Left + screenPixelCellSizeTerm, CanvasWorldRaster.Top - screenPixelCellSizeTerm2);
	}
}
