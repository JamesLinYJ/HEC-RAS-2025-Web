using System;
using System.Collections.Generic;
using Geospatial.Rasters;
using Geospatial.Rendering.Editing.Options;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public sealed class SmartAppendLineFeaturePointHandler<T, K> : IEditHandler where T : IPointCollection, IReadOnlyList<Point>, ISpatiallyIndexedPointCollection where K : GenericEditableLine<T>
{
	public static class Options
	{
		public static readonly OptionKey<SmartDigitizeAlgorithm> Algorithm = OptionKey<SmartDigitizeAlgorithm>.Define("Polyline.SmartAdd.Algorithm", "Algorithm", "Path-finder used for smart append. HessianRidgeness is polarity-agnostic; HighGround biases toward ridges only.", SmartDigitizeAlgorithm.HessianRidgeness);

		public static readonly OptionKey<double> DpTolerancePixels = OptionKey<double>.Define("Polyline.SmartAdd.DpTolerancePixels", "Simplify Tolerance (px)", "Douglas–Peucker tolerance applied to the walker output, in screen pixels.", 1.5);

		public static readonly OptionKey<double> MaxSegmentPixels = OptionKey<double>.Define("Polyline.SmartAdd.MaxSegmentPixels", "Max Segment Length (px)", "Maximum straight-line A→B distance in screen pixels before the handler bails to vanilla append.", 1500.0);

		public static readonly OptionKey<double> PreviewThrottlePixels = OptionKey<double>.Define("Polyline.SmartAdd.PreviewThrottlePixels", "Preview Throttle (px)", "Skip A* recompute when the cursor has moved less than this many pixels since the last frame.", 1.5);

		public static readonly OptionKey<double> HighGroundAlpha = OptionKey<double>.Define("Polyline.SmartAdd.HighGround.Alpha", "HighGround α", "Ridge preference weight. 0 = straight-line; higher values track ridges harder.", 3.0);

		public static readonly OptionKey<double> HighGroundBailRatio = OptionKey<double>.Define("Polyline.SmartAdd.HighGround.BailRatio", "HighGround Bail Ratio", "Multiplier on straight-line distance used to gate runaway A* search.", 5.0);

		public static readonly OptionKey<int> HighGroundSnapRadiusCells = OptionKey<int>.Define("Polyline.SmartAdd.HighGround.SnapRadiusCells", "HighGround Snap Radius (cells)", "Radius in grid cells used to snap the cursor target to the highest nearby cell.", 15);

		public static readonly OptionKey<double> HessianFrangiBeta = OptionKey<double>.Define("Polyline.SmartAdd.Hessian.FrangiBeta", "Hessian Frangi β", "Frangi anisotropy weight. Standard value is 0.5; lower values penalize plate-like responses harder.", 0.5);

		public static readonly OptionKey<double> HessianFeaturePenaltyKappa = OptionKey<double>.Define("Polyline.SmartAdd.Hessian.FeaturePenaltyKappa", "Hessian Feature Penalty κ", "A* edge-cost multiplier for off-feature cells (cost = step·(1 + κ·(1 − V\u0302))). Default 3.0.", 3.0);

		public static readonly OptionKey<double> HessianOffFeatureBailMultiplier = OptionKey<double>.Define("Polyline.SmartAdd.Hessian.OffFeatureBailMultiplier", "Hessian Off-Feature Bail", "Multiplier on Euclidean distance used to gate off-feature meander before A* gives up. Default 100.0.", 100.0);

		public static readonly OptionKey<int> HessianMinExpansionsBudget = OptionKey<int>.Define("Polyline.SmartAdd.Hessian.MinExpansionsBudget", "Hessian Min Expansions Budget", "Floor on the A* expansion budget regardless of Euclidean distance. Default 250 000.", 250000);

		public static readonly OptionKey<int> HessianExpansionsPerEuclidPx = OptionKey<int>.Define("Polyline.SmartAdd.Hessian.ExpansionsPerEuclidPx", "Hessian Expansions / px", "Per-pixel scale on the A* expansion budget above the floor. Default 500.", 500);

		public static readonly OptionKey<int> HessianMaxEuclidCells = OptionKey<int>.Define("Polyline.SmartAdd.Hessian.MaxEuclidCells", "Hessian Max Distance (cells)", "Hard cap on Euclidean distance (in cells) before the Hessian walker bails without building its feature surface.", 1500);
	}

	private readonly K _editable;

	private readonly Func<RasterDefinition, Memory<float>, bool> _batchSample;

	private int[] _parentBuf;

	private double[] _gScoreBuf;

	private double[] _lengthBuf;

	private float[] _zGridBuf;

	private PriorityQueue<int, double> _openPq;

	private List<(int row, int col)> _pixelPathBuf;

	private Polyline _polyForDp;

	private bool _gridCacheValid;

	private RasterDefinition _gridCacheWindow;

	private double _gridCacheZMin;

	private double _gridCacheZMax;

	private HessianRidgenessWalker _hessianWalker;

	private bool _hasCachedPreview;

	private Point _cachedA;

	private Point _cachedB;

	private double _cachedCellSize;

	private SmartDigitizeAlgorithm _cachedAlgorithm;

	private List<Point> _cachedSimplified;

	public SmartAppendLineFeaturePointHandler(K editable, Func<RasterDefinition, Memory<float>, bool> batchSample)
	{
		_editable = editable;
		_batchSample = batchSample;
	}

	public bool CanHandle(CommandType type, EditContext ctx)
	{
		if (_batchSample == null)
		{
			return false;
		}
		if (type != CommandType.Click)
		{
			return type == CommandType.BeginDragging;
		}
		return true;
	}

	public HandlerResult Handle(EditCommand cmd, EditContext ctx)
	{
		if (cmd.Command != CommandType.Click && cmd.Command != CommandType.BeginDragging)
		{
			return HandlerResult.NotHandled;
		}
		if (_editable.Vector.Count < 1)
		{
			return HandlerResult.WasHandled;
		}
		HitTestArgs location = cmd.Location;
		if (location == null)
		{
			return HandlerResult.WasHandled;
		}
		Point other = _editable.Vector[_editable.Vector.Count - 1];
		Point queryPoint = location.QueryPoint;
		double cellSize = (location.Window.IsNull() ? 0.0 : location.Window.CellSize);
		SmartDigitizeAlgorithm smartDigitizeAlgorithm = ctx.Options.Get(Options.Algorithm);
		if (_hasCachedPreview && _cachedSimplified != null && _cachedSimplified.Count > 0 && cellSize > 0.0 && _cachedCellSize == cellSize && _cachedAlgorithm == smartDigitizeAlgorithm && _cachedA.EqualXY(in other))
		{
			_editable.TryAppendPoints(_cachedSimplified);
			InvalidateCache();
			return HandlerResult.WasHandled;
		}
		if (!TryComputeSmartPath(other, queryPoint, location.Window, smartDigitizeAlgorithm, ctx, out var simplified))
		{
			InvalidateCache();
			return HandlerResult.WasHandled;
		}
		_editable.TryAppendPoints(simplified);
		InvalidateCache();
		return HandlerResult.WasHandled;
	}

	public bool Preview(HitTestArgs hit, IEditModifiers mods, EditContext ctx)
	{
		if (_batchSample == null)
		{
			return false;
		}
		if (hit == null)
		{
			return false;
		}
		if (_editable.Vector.Count < 1)
		{
			return false;
		}
		Point other = _editable.Vector[_editable.Vector.Count - 1];
		Point queryPoint = hit.QueryPoint;
		double cellSize = (hit.Window.IsNull() ? 0.0 : hit.Window.CellSize);
		SmartDigitizeAlgorithm smartDigitizeAlgorithm = ctx.Options.Get(Options.Algorithm);
		double optionsCtxTerm = ctx.Options.Get(Options.PreviewThrottlePixels);
		if (_hasCachedPreview && cellSize > 0.0 && _cachedCellSize == cellSize && _cachedAlgorithm == smartDigitizeAlgorithm && _cachedA.EqualXY(in other))
		{
			double queryPointCachedBTerm = (queryPoint.X - _cachedB.X) / cellSize;
			double queryPointCachedBTerm2 = (queryPoint.Y - _cachedB.Y) / cellSize;
			if (queryPointCachedBTerm * queryPointCachedBTerm + queryPointCachedBTerm2 * queryPointCachedBTerm2 <= optionsCtxTerm * optionsCtxTerm)
			{
				_editable.PreviewSmartPath(other, _cachedSimplified);
				return true;
			}
		}
		if (!TryComputeSmartPath(other, queryPoint, hit.Window, smartDigitizeAlgorithm, ctx, out var simplified))
		{
			InvalidateCache();
			_editable.ClearSmartPathPreview();
			return true;
		}
		_hasCachedPreview = true;
		_cachedA = other;
		_cachedB = queryPoint;
		_cachedCellSize = cellSize;
		_cachedAlgorithm = smartDigitizeAlgorithm;
		_cachedSimplified = simplified;
		_editable.PreviewSmartPath(other, simplified);
		return true;
	}

	private void InvalidateCache()
	{
		_hasCachedPreview = false;
		_cachedSimplified = null;
	}

	private bool TryComputeSmartPath(Point a, Point b, RasterDefinition window, SmartDigitizeAlgorithm algorithm, EditContext ctx, out List<Point> simplified)
	{
		simplified = null;
		if (window.IsNull())
		{
			return false;
		}
		double cellSize = window.CellSize;
		if (!(cellSize > 0.0))
		{
			return false;
		}
		double optionsCtxTerm = ctx.Options.Get(Options.MaxSegmentPixels);
		double optionsCtxTerm2 = ctx.Options.Get(Options.DpTolerancePixels);
		double cellSizeTerm = (b.X - a.X) / cellSize;
		double cellSizeTerm2 = (b.Y - a.Y) / cellSize;
		double computed = Math.Sqrt(cellSizeTerm * cellSizeTerm + cellSizeTerm2 * cellSizeTerm2);
		if (computed > optionsCtxTerm)
		{
			return false;
		}
		if (computed < 1E-06)
		{
			return false;
		}
		if (!TryUseGridCache(window) && !RebuildGridCache(window))
		{
			return false;
		}
		Point point = new Point(_gridCacheWindow.MinX, _gridCacheWindow.MaxY);
		int rows = _gridCacheWindow.Rows;
		int cols = _gridCacheWindow.Cols;
		int total = rows * cols;
		double gridCacheZMin = _gridCacheZMin;
		double gridCacheZMax = _gridCacheZMax;
		int rowsTerm = (int)Math.Floor((point.Y - a.Y) / cellSize);
		int colsTerm = (int)Math.Floor((a.X - point.X) / cellSize);
		int rowsTerm2 = (int)Math.Floor((point.Y - b.Y) / cellSize);
		int colsTerm2 = (int)Math.Floor((b.X - point.X) / cellSize);
		rowsTerm = Math.Clamp(rowsTerm, 0, rows - 1);
		colsTerm = Math.Clamp(colsTerm, 0, cols - 1);
		rowsTerm2 = Math.Clamp(rowsTerm2, 0, rows - 1);
		colsTerm2 = Math.Clamp(colsTerm2, 0, cols - 1);
		if (algorithm == SmartDigitizeAlgorithm.HighGround)
		{
			int snapRadiusCells = ctx.Options.Get(Options.HighGroundSnapRadiusCells);
			int ordinal = rowsTerm2;
			int ordinal2 = colsTerm2;
			SnapToHighestCell(rows, cols, snapRadiusCells, ref rowsTerm2, ref colsTerm2);
			if (rowsTerm2 != ordinal || colsTerm2 != ordinal2)
			{
				b = new Point(point.X + ((double)colsTerm2 + 0.5) * cellSize, point.Y - ((double)rowsTerm2 + 0.5) * cellSize);
			}
		}
		if (!TryDispatchWalker(algorithm, ctx, rows, cols, total, cellSize, gridCacheZMin, gridCacheZMax, computed, rowsTerm, colsTerm, rowsTerm2, colsTerm2, out List<(int, int)> pixelPath))
		{
			return false;
		}
		if (pixelPath == null || pixelPath.Count < 2)
		{
			return false;
		}
		if (_polyForDp == null)
		{
			_polyForDp = new Polyline();
		}
		_polyForDp.Clear();
		_polyForDp.Add(a);
		int count = pixelPath.Count - 1;
		for (int i = 1; i < count; i++)
		{
			(int, int) tuple = pixelPath[i];
			int tupleTerm = tuple.Item1;
			int tupleTerm2 = tuple.Item2;
			double x = point.X + ((double)tupleTerm2 + 0.5) * cellSize;
			double y = point.Y - ((double)tupleTerm + 0.5) * cellSize;
			_polyForDp.Add(new Point(x, y));
		}
		_polyForDp.Add(b);
		_polyForDp = _polyForDp.CloneWithMovingAverageSmoothing(3, 2);
		double tolerance = optionsCtxTerm2 * cellSize;
		List<Point> simplifiedList = _polyForDp.DouglasPeuckerReduction(tolerance);
		if (simplifiedList.Count >= 1 && simplifiedList[0].EqualXY(in a))
		{
			simplifiedList.RemoveAt(0);
		}
		if (simplifiedList.Count == 0)
		{
			return false;
		}
		simplified = simplifiedList;
		return true;
	}

	private bool TryDispatchWalker(SmartDigitizeAlgorithm algorithm, EditContext ctx, int rows, int cols, int total, double cellSize, double zMin, double zMax, double euclidPx, int aRow, int aCol, int bRow, int bCol, out List<(int row, int col)> pixelPath)
	{
		double minX = _gridCacheWindow.MinX;
		double maxY = _gridCacheWindow.MaxY;
		switch (algorithm)
		{
		case SmartDigitizeAlgorithm.HessianRidgeness:
			if (_hessianWalker == null)
			{
				_hessianWalker = new HessianRidgenessWalker();
			}
			return _hessianWalker.TryFindPath(_zGridBuf, rows, cols, cellSize, minX, maxY, zMin, zMax, aRow, aCol, bRow, bCol, ctx.Options.Get(Options.HessianFrangiBeta), ctx.Options.Get(Options.HessianFeaturePenaltyKappa), ctx.Options.Get(Options.HessianOffFeatureBailMultiplier), ctx.Options.Get(Options.HessianMinExpansionsBudget), ctx.Options.Get(Options.HessianExpansionsPerEuclidPx), ctx.Options.Get(Options.HessianMaxEuclidCells), out pixelPath);
		default:
		{
			_parentBuf = EnsureCapacity(_parentBuf, total);
			_gScoreBuf = EnsureCapacity(_gScoreBuf, total);
			_lengthBuf = EnsureCapacity(_lengthBuf, total);
			if (_openPq == null)
			{
				_openPq = new PriorityQueue<int, double>();
			}
			if (_pixelPathBuf == null)
			{
				_pixelPathBuf = new List<(int, int)>(256);
			}
			double alpha = ctx.Options.Get(Options.HighGroundAlpha);
			double optionsCtxTerm = ctx.Options.Get(Options.HighGroundBailRatio);
			double bailPixelLength = optionsCtxTerm * euclidPx;
			int maxExpansions = Math.Max(50000, (int)(128.0 * euclidPx * optionsCtxTerm));
			return AStarRidgeWalker.TryFindPath(_zGridBuf, rows, cols, aRow, aCol, bRow, bCol, alpha, zMin, zMax, maxExpansions, bailPixelLength, _parentBuf, _gScoreBuf, _lengthBuf, _openPq, _pixelPathBuf, out pixelPath);
		}
		}
	}

	private bool TryUseGridCache(RasterDefinition window)
	{
		if (!_gridCacheValid)
		{
			return false;
		}
		if (_gridCacheWindow.Rows != window.Rows || _gridCacheWindow.Cols != window.Cols)
		{
			return false;
		}
		if (_gridCacheWindow.CellSize != window.CellSize)
		{
			return false;
		}
		if (_gridCacheWindow.MinX != window.MinX || _gridCacheWindow.MaxY != window.MaxY)
		{
			return false;
		}
		return true;
	}

	private bool RebuildGridCache(RasterDefinition window)
	{
		int rows = window.Rows;
		int cols = window.Cols;
		if (rows < 2 || cols < 2)
		{
			_gridCacheValid = false;
			return false;
		}
		if ((long)rows * (long)cols > 8000000)
		{
			_gridCacheValid = false;
			return false;
		}
		int count = rows * cols;
		_zGridBuf = EnsureCapacity(_zGridBuf, count);
		if (!_batchSample(window, _zGridBuf.AsMemory(0, count)))
		{
			_gridCacheValid = false;
			return false;
		}
		double positiveInfinity = double.PositiveInfinity;
		double negativeInfinity = double.NegativeInfinity;
		for (int i = 0; i < count; i++)
		{
			float zGridBuf = _zGridBuf[i];
			if (zGridBuf < -1E+30f)
			{
				_zGridBuf[i] = float.NaN;
			}
			else if (!float.IsNaN(zGridBuf))
			{
				if ((double)zGridBuf < positiveInfinity)
				{
					positiveInfinity = zGridBuf;
				}
				if ((double)zGridBuf > negativeInfinity)
				{
					negativeInfinity = zGridBuf;
				}
			}
		}
		if (double.IsInfinity(positiveInfinity) || double.IsInfinity(negativeInfinity))
		{
			_gridCacheValid = false;
			return false;
		}
		_gridCacheValid = true;
		_gridCacheWindow = window;
		_gridCacheZMin = positiveInfinity;
		_gridCacheZMax = negativeInfinity;
		return true;
	}

	private void SnapToHighestCell(int rows, int cols, int snapRadiusCells, ref int bRow, ref int bCol)
	{
		int bRowAboveSnapRadiusCells = Math.Max(0, bRow - snapRadiusCells);
		int count = Math.Min(rows - 1, bRow + snapRadiusCells);
		int bColAboveSnapRadiusCells = Math.Max(0, bCol - snapRadiusCells);
		int count2 = Math.Min(cols - 1, bCol + snapRadiusCells);
		float negativeInfinity = float.NegativeInfinity;
		int bRow2 = bRow;
		int bCol2 = bCol;
		double positiveInfinity = double.PositiveInfinity;
		double snapRadiusCellsPolynomial = (double)snapRadiusCells * (double)snapRadiusCells;
		int bRow3 = bRow;
		int bCol3 = bCol;
		for (int i = bRowAboveSnapRadiusCells; i <= count; i++)
		{
			int bRow3Term = i - bRow3;
			int colsTerm = i * cols;
			for (int j = bColAboveSnapRadiusCells; j <= count2; j++)
			{
				int bCol3Term = j - bCol3;
				double positiveInfinity2 = bRow3Term * bRow3Term + bCol3Term * bCol3Term;
				if (!(positiveInfinity2 > snapRadiusCellsPolynomial))
				{
					float zGridBuf = _zGridBuf[colsTerm + j];
					if (!float.IsNaN(zGridBuf) && (zGridBuf > negativeInfinity || (zGridBuf == negativeInfinity && positiveInfinity2 < positiveInfinity)))
					{
						negativeInfinity = zGridBuf;
						bRow2 = i;
						bCol2 = j;
						positiveInfinity = positiveInfinity2;
					}
				}
			}
		}
		bRow = bRow2;
		bCol = bCol2;
	}

	private static T2[] EnsureCapacity<T2>(T2[] buf, int needed)
	{
		if (buf != null && buf.Length >= needed)
		{
			return buf;
		}
		return new T2[needed];
	}
}
