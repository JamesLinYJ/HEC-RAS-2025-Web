using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Geospatial.Rasters;
using Geospatial.Vectors;

namespace Geospatial.Core.Rasters;

public static class FlatAreaDetector
{
	private readonly struct SlopeTraversal(double cellSize, double maxSlopeRatio, float? noData, int tileSize) : ITileTraversal<float[], float>
	{
		private readonly double _cellSize = cellSize;

		private readonly double _diagonalCellSize = cellSize * Math.Sqrt(2.0);

		private readonly double _maxSlopeRatio = maxSlopeRatio;

		private readonly float _noData = noData.GetValueOrDefault();

		private readonly bool _hasNoData = noData.HasValue;

		private readonly int _tileSize = tileSize;

		public bool CanTraverse(float[] tile, int from, int to)
		{
			bool diagonal = from / _tileSize != to / _tileSize && from % _tileSize != to % _tileSize;
			return SlopeUnderThreshold(tile[from], tile[to], diagonal);
		}

		public float GetBoundaryContext(float[] tile, int idx)
		{
			return tile[idx];
		}

		public bool CanCross(float sourceElev, float[] targetTile, int targetIdx, bool diagonal)
		{
			return SlopeUnderThreshold(sourceElev, targetTile[targetIdx], diagonal);
		}

		private bool SlopeUnderThreshold(float fromElev, float toElev, bool diagonal)
		{
			if (_hasNoData && toElev == _noData)
			{
				return false;
			}
			if (float.IsNaN(toElev) || float.IsInfinity(toElev))
			{
				return false;
			}
			double diagonalCellSize = (diagonal ? _diagonalCellSize : _cellSize);
			return (double)Math.Abs(toElev - fromElev) / diagonalCellSize <= _maxSlopeRatio;
		}
	}

	public const long MaxDenseMaskCells = 250000000L;

	public const int DefaultContourTileCells = 1024;

	public static ComplexPolygonCollection Detect(IResample<float> elevation, IReadOnlyList<Point> seedPoints, FlatAreaDetectorOptions options = null, FlatAreaDetectorDiagnostics diagnostics = null)
		=> Detect(elevation, seedPoints, options, diagnostics, CancellationToken.None);

	public static ComplexPolygonCollection Detect(IResample<float> elevation, IReadOnlyList<Point> seedPoints,
		[AllowNull] FlatAreaDetectorOptions options, [AllowNull] FlatAreaDetectorDiagnostics diagnostics, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (options == null)
		{
			options = new FlatAreaDetectorOptions();
		}
		if (elevation == null)
		{
			throw new ArgumentException("Elevation raster cannot be null.");
		}
		if (seedPoints == null || seedPoints.Count == 0)
		{
			throw new ArgumentException("Seed points cannot be null or empty.");
		}
		if (options.CellSize <= 0.0)
		{
			throw new ArgumentException("Cell size must be greater than 0.");
		}
		IResample<float> resample = elevation;
		if (!options.ClipExtent.IsNull())
		{
			Extent ext = resample.ValidExtent.Intersection(options.ClipExtent);
			if (!ext.IsValidAndFinite())
			{
				throw new ArgumentException("Clip extent does not overlap the elevation raster.");
			}
			resample = resample.ClipTo(ext);
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		SamplingParams samplingParams = new SamplingParams(options.SampleInterpolation, initBufferToNDV: true);
		ITileProvider<float> provider = resample.AsTileProvider(options.CellSize, 256, samplingParams);
		RasterDefinition rasterDefinition = provider.RasterDefinition;
		RasterDefinition tiledRasterDefinition = provider.TiledRasterDefinition;
		int rows = rasterDefinition.Rows;
		int cols = rasterDefinition.Cols;
		int ts = provider.TileSize;
		int tileCols = tiledRasterDefinition.Cols;
		int rows2 = tiledRasterDefinition.Rows;
		double cellSize = options.CellSize;
		float? noDataValue = resample.NoDataValue;
		double maxSlope = options.MaxSlope;
		Dictionary<int, List<int>> intListByInt = new Dictionary<int, List<int>>();
		int seedIndex = 0;
		foreach (Point seedPoint in seedPoints)
		{
			if ((seedIndex++ & 31) == 0) cancellationToken.ThrowIfCancellationRequested();
			if (!rasterDefinition.Extent.Contains(seedPoint))
			{
				continue;
			}
			Pixel px = rasterDefinition.CellIndex2D(seedPoint);
			if (!rasterDefinition.InBounds(px))
			{
				continue;
			}
			float samplePoint = resample.SamplePoint(seedPoint, samplingParams);
			cancellationToken.ThrowIfCancellationRequested();
			if ((!noDataValue.HasValue || samplePoint != noDataValue.Value) && !float.IsNaN(samplePoint) && !float.IsInfinity(samplePoint))
			{
				int key = px.Row / ts * tileCols + px.Column / ts;
				int rowPxTerm = px.Row % ts * ts + px.Column % ts;
				if (!intListByInt.TryGetValue(key, out var localValue))
				{
					localValue = (intListByInt[key] = new List<int>());
				}
				localValue.Add(rowPxTerm);
			}
		}
		cancellationToken.ThrowIfCancellationRequested();
		if (diagnostics != null)
		{
			diagnostics.SeedTime = stopwatch.Elapsed;
		}
		if (intListByInt.Count == 0)
		{
			if (diagnostics != null)
			{
				TimeSpan timeSpan = (diagnostics.ContourTime = TimeSpan.Zero);
				TimeSpan fillTime = (diagnostics.MaskTime = timeSpan);
				diagnostics.FillTime = fillTime;
			}
			return new ComplexPolygonCollection();
		}
		TiledFloodFillHelper<float[], float>.AllocTile allocTile = () => new float[ts * ts];
		float boundaryFill = noDataValue ?? float.NaN;
		TiledFloodFillHelper<float[], float>.InitTile initTile = (int tileIndex, float[] tile) =>
		{
			cancellationToken.ThrowIfCancellationRequested();
			provider.ReadTile(tileIndex, tile);
			cancellationToken.ThrowIfCancellationRequested();
			int tileIndexTileColsTerm = tileIndex / tileCols * ts;
			int tileIndexTileColsTerm2 = tileIndex % tileCols * ts;
			if (tileIndexTileColsTerm + ts > rows || tileIndexTileColsTerm2 + ts > cols)
			{
				for (int i = 0; i < ts; i++)
				{
					if ((i & 31) == 0) cancellationToken.ThrowIfCancellationRequested();
					for (int j = 0; j < ts; j++)
					{
						if (tileIndexTileColsTerm + i >= rows || tileIndexTileColsTerm2 + j >= cols)
						{
							tile[i * ts + j] = boundaryFill;
						}
					}
				}
			}
		};
		SlopeTraversal traversal = new SlopeTraversal(cellSize, maxSlope, noDataValue, ts);
		TiledFloodFillHelper<float[], float> tiledFloodFillHelper = new TiledFloodFillHelper<float[], float>(rows2, tileCols, ts, options.Connectivity, allocTile, initTile);
		long totalAllocatedBytes3 = ((diagnostics != null) ? GC.GetTotalAllocatedBytes(precise: true) : 0);
		stopwatch.Restart();
		TiledBitArray tiledBitArray = options.ParallelFill
			? tiledFloodFillHelper.FillParallel(intListByInt, traversal, null, cancellationToken)
			: tiledFloodFillHelper.Fill(intListByInt, traversal, null, cancellationToken);
		if (diagnostics != null)
		{
			diagnostics.FillTime = stopwatch.Elapsed;
			long totalAllocatedBytes = GC.GetTotalAllocatedBytes(precise: true);
			diagnostics.FillAllocatedBytes = totalAllocatedBytes - totalAllocatedBytes3;
			totalAllocatedBytes3 = totalAllocatedBytes;
		}
		stopwatch.Restart();
		cancellationToken.ThrowIfCancellationRequested();
		int maxValueTsTerm = 0;
		int rowsOffset = rows - 1;
		int maxValue2TsTerm = 0;
		int colsOffset = cols - 1;
		if (options.ContourFilledBoundsOnly)
		{
			int maxValue = int.MaxValue;
			int ordinal = -1;
			int maxValue2 = int.MaxValue;
			int ordinal2 = -1;
			int filledTileOrdinal = 0;
			foreach (int filledTileIndex in tiledBitArray.FilledTileIndices)
			{
				if ((filledTileOrdinal++ & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
				int filledTileIndexOverTileCols = filledTileIndex / tileCols;
				int filledTileIndexWithTileCols = filledTileIndex % tileCols;
				maxValue = Math.Min(maxValue, filledTileIndexOverTileCols);
				ordinal = Math.Max(ordinal, filledTileIndexOverTileCols);
				maxValue2 = Math.Min(maxValue2, filledTileIndexWithTileCols);
				ordinal2 = Math.Max(ordinal2, filledTileIndexWithTileCols);
			}
			maxValueTsTerm = Math.Max(0, maxValue * ts - 1);
			rowsOffset = Math.Min(rows - 1, (ordinal + 1) * ts);
			maxValue2TsTerm = Math.Max(0, maxValue2 * ts - 1);
			colsOffset = Math.Min(cols - 1, (ordinal2 + 1) * ts);
		}
		int ordinal3 = rowsOffset - maxValueTsTerm + 1;
		int ordinal4 = colsOffset - maxValue2TsTerm + 1;
		bool contourModeOptionsTerm = options.ContourMode switch
		{
			FlatAreaContourMode.Dense => false, 
			FlatAreaContourMode.Tiled => true, 
			_ => (long)ordinal3 * (long)ordinal4 > 250000000, 
		};
		double minimumRingArea = options.MinimumRingArea;
		if (contourModeOptionsTerm)
		{
			if (diagnostics != null)
			{
				diagnostics.MaskTime = TimeSpan.Zero;
				diagnostics.MaskAllocatedBytes = 0L;
			}
			int contourTileCells = ((options.ContourTileCells > 0) ? options.ContourTileCells : 1024);
			int blockTiles = Math.Max(1, (contourTileCells + ts - 1) / ts);
			ContourShapeOperations.GetContourShapes(tiledBitArray, rasterDefinition, out var polygons, out var _, options.SimplifyDistance, blockTiles, minimumRingArea, cancellationToken);
			if (diagnostics != null)
			{
				diagnostics.ContourTime = stopwatch.Elapsed;
				diagnostics.ContourAllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - totalAllocatedBytes3;
			}
			cancellationToken.ThrowIfCancellationRequested();
			return polygons;
		}
		Point upperLeftCorner = new Point(rasterDefinition.Extent.MinX + (double)maxValue2TsTerm * rasterDefinition.CellSize, rasterDefinition.Extent.MaxY - (double)maxValueTsTerm * rasterDefinition.CellSize);
		RasterDefinition rasterDef = new RasterDefinition(upperLeftCorner, rasterDefinition.CellSize, ordinal3, ordinal4);
		long count = (long)ordinal3 * (long)ordinal4;
		if (count > Array.MaxLength)
		{
			throw new InvalidOperationException($"Dense contour mask is {count:N0} cells, past the {Array.MaxLength:N0} array-length limit. Use {"FlatAreaContourMode"}.{"Auto"} or {"Tiled"} for a raster this large.");
		}
		float[] floatValues = new float[(int)count];
		int maskTileOrdinal = 0;
		foreach (int filledTileIndex2 in tiledBitArray.FilledTileIndices)
		{
			if ((maskTileOrdinal++ & 31) == 0) cancellationToken.ThrowIfCancellationRequested();
			BitArray tileMask = tiledBitArray.GetTileMask(filledTileIndex2);
			int filledTileIndex2TileColsTerm = filledTileIndex2 / tileCols * ts;
			int filledTileIndex2TileColsTerm2 = filledTileIndex2 % tileCols * ts;
			for (int intervalIndex = 0; intervalIndex < ts; intervalIndex++)
			{
				if ((intervalIndex & 31) == 0) cancellationToken.ThrowIfCancellationRequested();
				int ordinal5 = filledTileIndex2TileColsTerm + intervalIndex;
				if (ordinal5 > rowsOffset)
				{
					break;
				}
				for (int intervalIndex2 = 0; intervalIndex2 < ts; intervalIndex2++)
				{
					if ((intervalIndex2 & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
					int ordinal6 = filledTileIndex2TileColsTerm2 + intervalIndex2;
					if (ordinal6 > colsOffset)
					{
						break;
					}
					if (tileMask[intervalIndex * ts + intervalIndex2])
					{
						floatValues[(ordinal5 - maxValueTsTerm) * ordinal4 + (ordinal6 - maxValue2TsTerm)] = 1f;
					}
				}
			}
		}
		if (diagnostics != null)
		{
			diagnostics.MaskTime = stopwatch.Elapsed;
			long totalAllocatedBytes2 = GC.GetTotalAllocatedBytes(precise: true);
			diagnostics.MaskAllocatedBytes = totalAllocatedBytes2 - totalAllocatedBytes3;
			totalAllocatedBytes3 = totalAllocatedBytes2;
		}
		stopwatch.Restart();
		ContourShapeOperations.GetContourShapes(floatValues, rasterDef, 0.5f, out var polygons2, out var _, options.SimplifyDistance, minimumRingArea, cancellationToken);
		if (diagnostics != null)
		{
			diagnostics.ContourTime = stopwatch.Elapsed;
			diagnostics.ContourAllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - totalAllocatedBytes3;
		}
		cancellationToken.ThrowIfCancellationRequested();
		return polygons2;
	}
}
