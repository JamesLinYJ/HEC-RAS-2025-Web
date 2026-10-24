using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Geospatial.Clipper2Lib;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.Logging;

namespace Geospatial.Core.Rasters;

public static class ContourShapeOperations
{
	private struct ContourSegment
	{
		public Point PositionA;

		public Point PositionB;

		public Pixel KeyA;

		public Pixel KeyB;

		public ContourSegment(Point cellOrigin, int indexA, float fractionA, int indexB, float fractionB, bool clockwise, RasterDefinition rasterDef)
		{
			PositionA = cellOrigin + _marchingEdges[indexA].PointAtFraction(fractionA);
			PositionA.X /= rasterDef.Cols;
			PositionA.X *= rasterDef.Width;
			PositionA.Y /= rasterDef.Rows;
			PositionA.Y *= rasterDef.Height;
			PositionA.Y = 0.0 - PositionA.Y;
			PositionA += rasterDef.Extent.UpperLeft;
			PositionA += new Point(rasterDef.CellSize * 0.5, (0.0 - rasterDef.CellSize) * 0.5);
			PositionB = cellOrigin + _marchingEdges[indexB].PointAtFraction(fractionB);
			PositionB.X /= rasterDef.Cols;
			PositionB.X *= rasterDef.Width;
			PositionB.Y /= rasterDef.Rows;
			PositionB.Y *= rasterDef.Height;
			PositionB.Y = 0.0 - PositionB.Y;
			PositionB += rasterDef.Extent.UpperLeft;
			PositionB += new Point(rasterDef.CellSize * 0.5, (0.0 - rasterDef.CellSize) * 0.5);
			Point point = cellOrigin + _marchingEdges[indexA].PointAtFraction(0.5);
			Point point2 = cellOrigin + _marchingEdges[indexB].PointAtFraction(0.5);
			KeyA = new Pixel((int)(point.Y * 4.0), (int)(point.X * 4.0));
			KeyB = new Pixel((int)(point2.Y * 4.0), (int)(point2.X * 4.0));
			if (!clockwise)
			{
				Point positionB = PositionB;
				Point positionA = PositionA;
				PositionA = positionB;
				PositionB = positionA;
				Pixel keyB = KeyB;
				Pixel keyA = KeyA;
				KeyA = keyB;
				KeyB = keyA;
			}
		}
	}

	private struct ContourPoint(Pixel key, Point position)
	{
		public const int NoLink = -1;

		public Point Position = position;

		public Pixel Key = key;

		public int Prev = -1;

		public int Next = -1;
	}

	private class ContourPointCollection
	{
		private List<ContourPoint> _points;

		private Dictionary<Pixel, int> _keysToPoints;

		private bool _finalizedShapes;

		public ContourPointCollection()
		{
			_points = new List<ContourPoint>();
			_keysToPoints = new Dictionary<Pixel, int>();
			_finalizedShapes = false;
		}

		public int GetOrCreatePoint(Pixel key, Point position)
		{
			if (_keysToPoints.TryGetValue(key, out var keysToPoints))
			{
				return keysToPoints;
			}
			keysToPoints = _points.Count;
			_points.Add(new ContourPoint(key, position));
			_keysToPoints[key] = keysToPoints;
			return keysToPoints;
		}

		public void Link(int a, int b)
		{
			Span<ContourPoint> points = CollectionsMarshal.AsSpan(_points);
			points[a].Next = b;
			points[b].Prev = a;
		}

		private IReadOnlyList<Polyline> IterateShapes(bool lines, double simplifyDistance, List<ContourChain> chains, CancellationToken cancellationToken)
		{
			List<Polyline> polylines = new List<Polyline>();
			Span<ContourPoint> points = CollectionsMarshal.AsSpan(_points);
			for (int i = 0; i < points.Length; i++)
			{
				if ((i & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
				if ((points[i].Prev == -1 && points[i].Next == -1) || (lines && points[i].Prev != -1 && points[i].Next != -1))
				{
					continue;
				}
				PathD pathD = new PathD();
				int nextTerm = i;
				int traversedPoints = 0;
				do
				{
					if ((traversedPoints++ & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
					pathD.Add(points[nextTerm].Position);
					int next = points[nextTerm].Next;
					if (next == -1)
					{
						break;
					}
					points[nextTerm].Next = -1;
					points[next].Prev = -1;
					nextTerm = next;
				}
				while (nextTerm != i);
				bool condition = nextTerm == i;
				if (simplifyDistance > 0.0)
				{
					pathD = Clipper.SimplifyPath(pathD, simplifyDistance, condition);
				}
				Polyline polyline = Polyline.CreateWithBuffer(pathD);
				polylines.Add(polyline);
				if (lines && chains != null && !condition)
				{
					chains.Add(new ContourChain(polyline, points[i].Key, points[nextTerm].Key));
				}
			}
			return polylines;
		}

		public void FinalizeShapes(out IReadOnlyList<Polygon> polygons, out IReadOnlyList<Polyline> polylines, double simplifyDistance, List<ContourChain> chains, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (_finalizedShapes)
			{
				throw new Exception("Tried to finalize shapes on a ContourPointCollection that has already done so.");
			}
			polylines = IterateShapes(lines: true, simplifyDistance, chains, cancellationToken);
			List<Polygon> polygonsList = new List<Polygon>();
			foreach (Polyline iterateShape in IterateShapes(lines: false, simplifyDistance, null, cancellationToken))
			{
				cancellationToken.ThrowIfCancellationRequested();
				polygonsList.Add(Polygon.CreateWithBuffer(iterateShape));
			}
			polygons = polygonsList;
		}
	}

	private readonly struct ContourChain(Polyline line, Pixel startKey, Pixel endKey)
	{
		public readonly Polyline Line = line;

		public readonly Pixel StartKey = startKey;

		public readonly Pixel EndKey = endKey;
	}

	private static readonly ILogger _logger = LogHelper.GetLogger("ContourShapeOperations");

	private static Segment[] _marchingEdges = new Segment[8]
	{
		new Segment(0.0, 0.0, 1.0, 0.0),
		new Segment(0.5, 0.5, 1.0, 0.0),
		new Segment(1.0, 0.0, 1.0, 1.0),
		new Segment(0.5, 0.5, 1.0, 1.0),
		new Segment(1.0, 1.0, 0.0, 1.0),
		new Segment(0.5, 0.5, 0.0, 1.0),
		new Segment(0.0, 1.0, 0.0, 0.0),
		new Segment(0.5, 0.5, 0.0, 0.0)
	};

	private const int MaxCellSegments = 12;

	private static float GetContourFraction(float target, float computed, float computed2)
	{
		computed -= target;
		computed2 -= target;
		if (computed == 0f)
		{
			computed -= 0.001f;
		}
		if (computed2 == 0f)
		{
			computed2 -= 0.001f;
		}
		if (computed * computed2 > 0f)
		{
			return -1f;
		}
		return (0f - computed) / (computed2 - computed);
	}

	private static float GetValue(ReadOnlySpan<float> data, RasterDefinition rasterDef, int x, int y, float boundaryVal)
	{
		if (x < 0 || y < 0)
		{
			return boundaryVal;
		}
		if (x >= rasterDef.Cols || y >= rasterDef.Rows)
		{
			return boundaryVal;
		}
		return data[y * rasterDef.Cols + x];
	}

	private static void GetShapesFromRaster(ReadOnlyMemory<float> rasterData, RasterDefinition rasterDef, float target, out IReadOnlyList<Polygon> polygons, out IReadOnlyList<Polyline> polylines, double simplifyDistance = 0.0, bool borders = true)
	{
		int xStart = (borders ? (-1) : 0);
		int xEnd = (borders ? rasterDef.Cols : (rasterDef.Cols - 1));
		int yStart = (borders ? (-1) : 0);
		int yEnd = (borders ? rasterDef.Rows : (rasterDef.Rows - 1));
		GetShapesFromRaster(rasterData, rasterDef, target, xStart, xEnd, yStart, yEnd, out polygons, out polylines, simplifyDistance, null);
	}

	private static void GetShapesFromRaster(ReadOnlyMemory<float> rasterData, RasterDefinition rasterDef, float target, int xStart, int xEnd, int yStart, int yEnd, out IReadOnlyList<Polygon> polygons, out IReadOnlyList<Polyline> polylines, double simplifyDistance, List<ContourChain> chains)
		=> GetShapesFromRaster(rasterData, rasterDef, target, xStart, xEnd, yStart, yEnd, out polygons, out polylines, simplifyDistance, chains, CancellationToken.None);

	private static void GetShapesFromRaster(ReadOnlyMemory<float> rasterData, RasterDefinition rasterDef, float target, int xStart, int xEnd, int yStart, int yEnd, out IReadOnlyList<Polygon> polygons, out IReadOnlyList<Polyline> polylines, double simplifyDistance, List<ContourChain> chains, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		long yEndYStartTerm = (long)(yEnd - yStart) * (long)(xEnd - xStart);
		int processorCount = ((yEndYStartTerm < 4194304) ? 1 : Environment.ProcessorCount);
		int rowsPerBand = (yEnd - yStart + processorCount - 1) / processorCount;
		Stopwatch stopwatch = Stopwatch.StartNew();
		ContourPointCollection contourPointCollection = new ContourPointCollection();
		if (processorCount == 1)
		{
			ReadOnlySpan<float> rasterDataSpan = rasterData.Span;
			Span<float> edgeFractions = stackalloc float[8];
			Span<ContourSegment> outSegs = stackalloc ContourSegment[12];
			int marchedCells = 0;
			for (int i = yStart; i < yEnd; i++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				for (int j = xStart; j < xEnd; j++)
				{
					if ((marchedCells++ & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
					int cellSegments = ComputeCellSegments(rasterDataSpan, rasterDef, target, j, i, edgeFractions, outSegs);
					for (int k = 0; k < cellSegments; k++)
					{
						ContourSegment contourSegment = outSegs[k];
						int orCreatePoint = contourPointCollection.GetOrCreatePoint(contourSegment.KeyA, contourSegment.PositionA);
						int orCreatePoint2 = contourPointCollection.GetOrCreatePoint(contourSegment.KeyB, contourSegment.PositionB);
						contourPointCollection.Link(orCreatePoint, orCreatePoint2);
					}
				}
			}
			contourPointCollection.FinalizeShapes(out polygons, out polylines, simplifyDistance, chains, cancellationToken);
			return;
		}
		List<ContourSegment>[] bandSegments = new List<ContourSegment>[processorCount];
		System.Threading.Tasks.Parallel.For(0, processorCount, new ParallelOptions { CancellationToken = cancellationToken }, (int b) =>
		{
			int yStartRowsPerBandTerm = yStart + b * rowsPerBand;
			int yEndRowsPerBandTerm = Math.Min(yEnd, yStartRowsPerBandTerm + rowsPerBand);
			bandSegments[b] = ((yStartRowsPerBandTerm < yEndRowsPerBandTerm) ? MarchBand(yStartRowsPerBandTerm, yEndRowsPerBandTerm) : new List<ContourSegment>());
		});
		TimeSpan elapsed = stopwatch.Elapsed;
		stopwatch.Restart();
		List<ContourSegment>[] bandSegmentsList = bandSegments;
		for (int intervalIndex = 0; intervalIndex < bandSegmentsList.Length; intervalIndex++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			int linkedSegments = 0;
			foreach (ContourSegment bandSegment in bandSegmentsList[intervalIndex])
			{
				if ((linkedSegments++ & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
				int orCreatePoint3 = contourPointCollection.GetOrCreatePoint(bandSegment.KeyA, bandSegment.PositionA);
				int orCreatePoint4 = contourPointCollection.GetOrCreatePoint(bandSegment.KeyB, bandSegment.PositionB);
				contourPointCollection.Link(orCreatePoint3, orCreatePoint4);
			}
		}
		TimeSpan elapsed2 = stopwatch.Elapsed;
		stopwatch.Restart();
		contourPointCollection.FinalizeShapes(out polygons, out polylines, simplifyDistance, chains, cancellationToken);
		_logger.LogDebug("Contour march: {Cells} cells across {Bands} bands in {MarchMs} ms; linked {Segments} segments in {LinkMs} ms; finalized in {FinalizeMs} ms", yEndYStartTerm, processorCount, (long)elapsed.TotalMilliseconds, bandSegments.Sum((List<ContourSegment> s) => s.Count), (long)elapsed2.TotalMilliseconds, (long)stopwatch.Elapsed.TotalMilliseconds);
		List<ContourSegment> MarchBand(int y0, int y1)
		{
			List<ContourSegment> contourSegments = new List<ContourSegment>();
			ReadOnlySpan<float> rasterData2 = rasterData.Span;
			Span<float> edgeFractions2 = stackalloc float[8];
			Span<ContourSegment> outSegs2 = stackalloc ContourSegment[12];
			int marchedCells = 0;
			for (int l = y0; l < y1; l++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				for (int m = xStart; m < xEnd; m++)
				{
					if ((marchedCells++ & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
					int cellSegments2 = ComputeCellSegments(rasterData2, rasterDef, target, m, l, edgeFractions2, outSegs2);
					for (int n = 0; n < cellSegments2; n++)
					{
						contourSegments.Add(outSegs2[n]);
					}
				}
			}
			return contourSegments;
		}
	}

	private static int ComputeCellSegments(ReadOnlySpan<float> data, RasterDefinition rasterDef, float target, int x, int y, Span<float> edgeFractions, Span<ContourSegment> outSegs)
	{
		float getValue = GetValue(data, rasterDef, x, y, target);
		float getValue2 = GetValue(data, rasterDef, x + 1, y, target);
		float getValue3 = GetValue(data, rasterDef, x, y + 1, target);
		float getValue4 = GetValue(data, rasterDef, x + 1, y + 1, target);
		float getValueGetValue2Term = (getValue + getValue2 + getValue3 + getValue4) / 4f;
		edgeFractions[0] = GetContourFraction(target, getValue, getValue2);
		edgeFractions[1] = GetContourFraction(target, getValueGetValue2Term, getValue2);
		edgeFractions[2] = GetContourFraction(target, getValue2, getValue4);
		edgeFractions[3] = GetContourFraction(target, getValueGetValue2Term, getValue4);
		edgeFractions[4] = GetContourFraction(target, getValue4, getValue3);
		edgeFractions[5] = GetContourFraction(target, getValueGetValue2Term, getValue3);
		edgeFractions[6] = GetContourFraction(target, getValue3, getValue);
		edgeFractions[7] = GetContourFraction(target, getValueGetValue2Term, getValue);
		Point cellOrigin = new Point(x, y);
		int result = 0;
		for (int i = 0; i < 8; i++)
		{
			int ordinal = (i + 1) % 8;
			if (edgeFractions[i] > 0f && edgeFractions[ordinal] > 0f)
			{
				outSegs[result++] = new ContourSegment(cellOrigin, i, edgeFractions[i], ordinal, edgeFractions[ordinal], getValueGetValue2Term > target, rasterDef);
			}
		}
		for (int j = 1; j < 8; j += 2)
		{
			int ordinal2 = (j + 2) % 8;
			if (edgeFractions[j] > 0f && edgeFractions[ordinal2] > 0f)
			{
				outSegs[result++] = new ContourSegment(cellOrigin, j, edgeFractions[j], ordinal2, edgeFractions[ordinal2], getValueGetValue2Term > target, rasterDef);
			}
		}
		return result;
	}

	public static void GetContourShapes(IRaster<float> raster, int tileSize, float target, out ComplexPolygonCollection polygons, out PolylineCollection polylines, double simplifyDistance = 0.0)
	{
		RasterDefinition tileDef = raster.RasterDefinition.TiledRaster(tileSize);
		int processorCount = Environment.ProcessorCount;
		new ParallelOptions().MaxDegreeOfParallelism = processorCount;
		float[][] coresTileData = new float[processorCount][];
		for (int i = 0; i < processorCount; i++)
		{
			coresTileData[i] = new float[tileSize * tileSize];
		}
		int tilesPerCore = (int)Math.Ceiling((double)tileDef.CellCount / (double)processorCount);
		ConcurrentQueue<Polygon> initialPolygons = new ConcurrentQueue<Polygon>();
		ConcurrentQueue<Polyline> initialPolylines = new ConcurrentQueue<Polyline>();
		Stopwatch stopwatch = Stopwatch.StartNew();
		int tilesIterated = 0;
		int tilesMarched = 0;
		int tilesFull = 0;
		System.Threading.Tasks.Parallel.For(0, processorCount, (int coreIdx) =>
		{
			for (int j = 0; j < tilesPerCore; j++)
			{
				int incrementInterlockedTerm = Interlocked.Increment(ref tilesIterated) - 1;
				if (incrementInterlockedTerm < tileDef.CellCount)
				{
					float[] coresTileDataArray = coresTileData[coreIdx];
					RasterDefinition cellRasterDefinition = tileDef.GetCellRasterDefinition(incrementInterlockedTerm, tileSize);
					ResampleArgs args = new ResampleArgs(cellRasterDefinition, InterpolationMode.Linear);
					raster.Resample(args, coresTileDataArray);
					Buffers.MaxMin(coresTileDataArray, -9999f, out var max, out var min);
					bool coresTileDataArrayTileSizeTerm = coresTileDataArray[0] == -9999f || coresTileDataArray[tileSize - 1] == -9999f || coresTileDataArray[tileSize * (tileSize - 1)] == -9999f || coresTileDataArray[tileSize * tileSize - 1] == -9999f;
					if (!(max < target))
					{
						if (min > target && !coresTileDataArrayTileSizeTerm)
						{
							Polygon polygon = new Polygon
							{
								cellRasterDefinition.Extent.UpperLeft + new Point(0.0 - cellRasterDefinition.CellSize, cellRasterDefinition.CellSize),
								cellRasterDefinition.Extent.UpperRight + new Point(cellRasterDefinition.CellSize, cellRasterDefinition.CellSize),
								cellRasterDefinition.Extent.LowerRight + new Point(cellRasterDefinition.CellSize, 0.0 - cellRasterDefinition.CellSize),
								cellRasterDefinition.Extent.LowerLeft + new Point(0.0 - cellRasterDefinition.CellSize, 0.0 - cellRasterDefinition.CellSize)
							};
							initialPolygons.Enqueue(polygon);
							Interlocked.Increment(ref tilesFull);
						}
						else
						{
							GetShapesFromRaster(coresTileDataArray, cellRasterDefinition, target, out var polygons2, out var polylines2, simplifyDistance);
							Interlocked.Increment(ref tilesMarched);
							foreach (Polygon polygons22 in polygons2)
							{
								initialPolygons.Enqueue(polygons22);
							}
							foreach (Polyline polylines22 in polylines2)
							{
								initialPolylines.Enqueue(polylines22);
							}
						}
					}
				}
			}
		});
		TimeSpan elapsed = stopwatch.Elapsed;
		stopwatch.Restart();
		polylines = new PolylineCollection(initialPolylines);
		polygons = ClipperUnionPolygonsParallel(initialPolygons.ToArray());
		_logger.LogDebug("Tiled contour: {Tiles} tiles ({Full} full, {Marched} marched) in {MarchMs} ms; union of {Pieces} pieces in {UnionMs} ms -> {Polygons} polygons", tileDef.CellCount, tilesFull, tilesMarched, (long)elapsed.TotalMilliseconds, initialPolygons.Count, (long)stopwatch.Elapsed.TotalMilliseconds, polygons.Count);
	}

	public static void GetContourShapes(TiledBitArray mask, RasterDefinition rasterDef, out ComplexPolygonCollection polygons, out PolylineCollection polylines, double simplifyDistance = 0.0, int blockTiles = 1, double minRingArea = 0.0)
		=> GetContourShapes(mask, rasterDef, out polygons, out polylines, simplifyDistance, blockTiles, minRingArea, CancellationToken.None);

	public static void GetContourShapes(TiledBitArray mask, RasterDefinition rasterDef, out ComplexPolygonCollection polygons, out PolylineCollection polylines, double simplifyDistance, int blockTiles, double minRingArea, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		int ts = mask.TileSize;
		int tileCols = mask.TileCols;
		int tileRows = mask.TileRows;
		int rows = rasterDef.Rows;
		int cols = rasterDef.Cols;
		double cellSize = rasterDef.CellSize;
		double originX = rasterDef.Extent.MinX;
		double originY = rasterDef.Extent.MaxY;
		int k = Math.Max(1, blockTiles);
		int blockCols = (tileCols + k - 1) / k;
		int blockCells = k * ts;
		int bs = blockCells + 2;
		HashSet<int> intSet = new HashSet<int>();
		int filledTileCount = 0;
		foreach (int filledTileIndex in mask.FilledTileIndices)
		{
			if ((filledTileCount++ & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
			int filledTileIndexOverTileCols = filledTileIndex / tileCols;
			int filledTileIndexWithTileCols = filledTileIndex % tileCols;
			int ordinal = filledTileIndexOverTileCols / k;
			int ordinal2 = filledTileIndexWithTileCols / k;
			intSet.Add(ordinal * blockCols + ordinal2);
			bool condition = filledTileIndexOverTileCols % k == 0 && ordinal > 0;
			bool condition2 = filledTileIndexWithTileCols % k == 0 && ordinal2 > 0;
			if (condition2)
			{
				intSet.Add(ordinal * blockCols + ordinal2 - 1);
			}
			if (condition)
			{
				intSet.Add((ordinal - 1) * blockCols + ordinal2);
			}
			if (condition & condition2)
			{
				intSet.Add((ordinal - 1) * blockCols + ordinal2 - 1);
			}
		}
		List<int> blocks = intSet.ToList();
		blocks.Sort();
		List<Polygon>[] perBlockRings = new List<Polygon>[blocks.Count];
		List<ContourChain>[] perBlockChains = new List<ContourChain>[blocks.Count];
		int blocksSkipped = 0;
		int blocksMarched = 0;
		Stopwatch stopwatch = Stopwatch.StartNew();
		System.Threading.Tasks.Parallel.For<float[]>(0, blocks.Count, new ParallelOptions { CancellationToken = cancellationToken }, () => new float[bs * bs], (int i, ParallelLoopState loopState, float[] buffer) =>
		{
			cancellationToken.ThrowIfCancellationRequested();
			int block = blocks[i];
			int blockBlockColsTerm = block / blockCols * k;
			int blockBlockColsTerm2 = block % blockCols * k;
			int count = Math.Min(blockBlockColsTerm + k, tileRows) - 1;
			int count2 = Math.Min(blockBlockColsTerm2 + k, tileCols) - 1;
			int baseRow = blockBlockColsTerm * ts;
			int baseCol = blockBlockColsTerm2 * ts;
			if (count == blockBlockColsTerm + k - 1 && count2 == blockBlockColsTerm2 + k - 1 && mask.AllFilledInTileRange(blockBlockColsTerm, count, blockBlockColsTerm2, count2) && RingFilled())
			{
				Interlocked.Increment(ref blocksSkipped);
				return buffer;
			}
			Array.Clear(buffer);
			for (int j = blockBlockColsTerm; j <= count; j++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				for (int l = blockBlockColsTerm2; l <= count2; l++)
				{
					cancellationToken.ThrowIfCancellationRequested();
					BitArray tileMask = mask.GetTileMask(j * tileCols + l);
					if (tileMask != null)
					{
						int tsTerm = (j - blockBlockColsTerm) * ts + 1;
						int tsTerm2 = (l - blockBlockColsTerm2) * ts + 1;
						for (int m = 0; m < ts; m++)
						{
							if ((m & 15) == 0) cancellationToken.ThrowIfCancellationRequested();
							int bsTerm = (tsTerm + m) * bs + tsTerm2;
							int tsTerm3 = m * ts;
							for (int n = 0; n < ts; n++)
							{
								if (tileMask[tsTerm3 + n])
								{
									buffer[bsTerm + n] = 1f;
								}
							}
						}
					}
				}
			}
			for (int unity = -1; unity <= blockCells; unity++)
			{
				if ((unity & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
				buffer[unity + 1] = NeighborValue(baseRow - 1, baseCol + unity);
				buffer[(bs - 1) * bs + unity + 1] = NeighborValue(baseRow + blockCells, baseCol + unity);
				buffer[(unity + 1) * bs] = NeighborValue(baseRow + unity, baseCol - 1);
				buffer[(unity + 1) * bs + bs - 1] = NeighborValue(baseRow + unity, baseCol + blockCells);
			}
			RasterDefinition rasterDef2 = new RasterDefinition(new Point(originX + (double)(baseCol - 1) * cellSize, originY - (double)(baseRow - 1) * cellSize), cellSize, bs, bs);
			int xStart = ((baseCol != 0) ? 1 : 0);
			int xEnd = Math.Min(blockCells, cols - baseCol) + 1;
			int yStart = ((baseRow != 0) ? 1 : 0);
			int yEnd = Math.Min(blockCells, rows - baseRow) + 1;
			List<ContourChain> contourChains = new List<ContourChain>();
			GetShapesFromRaster(buffer, rasterDef2, 0.5f, xStart, xEnd, yStart, yEnd, out var polygons2, out var _, 0.0, contourChains, cancellationToken);
			Interlocked.Increment(ref blocksMarched);
			List<Polygon> perBlockRingsList = null;
			foreach (Polygon polygons22 in polygons2)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!(minRingArea > 0.0) || !(Math.Abs(polygons22.Area) < minRingArea))
				{
					(perBlockRingsList ?? (perBlockRingsList = new List<Polygon>())).Add(polygons22);
				}
			}
			perBlockRings[i] = perBlockRingsList;
			Pixel pixel = new Pixel(4 * (baseRow - 1), 4 * (baseCol - 1));
			List<ContourChain> perBlockChainsList = null;
			int convertedChains = 0;
			foreach (ContourChain contourChain3 in contourChains)
			{
				if ((convertedChains++ & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
				(perBlockChainsList ?? (perBlockChainsList = new List<ContourChain>())).Add(new ContourChain(contourChain3.Line, contourChain3.StartKey + pixel, contourChain3.EndKey + pixel));
			}
			perBlockChains[i] = perBlockChainsList;
			return buffer;
			bool RingFilled()
			{
				for (int unity2 = -1; unity2 <= blockCells; unity2++)
				{
					if ((unity2 & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
					if (NeighborValue(baseRow - 1, baseCol + unity2) == 0f || NeighborValue(baseRow + blockCells, baseCol + unity2) == 0f || NeighborValue(baseRow + unity2, baseCol - 1) == 0f || NeighborValue(baseRow + unity2, baseCol + blockCells) == 0f)
					{
						return false;
					}
				}
				return true;
			}
		}, (float[] _) =>
		{
		});
		TimeSpan elapsed = stopwatch.Elapsed;
		stopwatch.Restart();
		cancellationToken.ThrowIfCancellationRequested();
		List<ContourChain> contourChainsList = new List<ContourChain>();
		List<ContourChain>[] perBlockChains2 = perBlockChains;
		foreach (List<ContourChain> perBlockChain in perBlockChains2)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (perBlockChain != null)
			{
				contourChainsList.AddRange(perBlockChain);
			}
		}
		Dictionary<Pixel, int> intByPixel = new Dictionary<Pixel, int>(contourChainsList.Count);
		for (int intervalIndex = 0; intervalIndex < contourChainsList.Count; intervalIndex++)
		{
			if ((intervalIndex & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
			intByPixel[contourChainsList[intervalIndex].StartKey] = intervalIndex;
		}
		List<Polygon> polygonsList = new List<Polygon>();
		List<Polygon>[] perBlockRings2 = perBlockRings;
		foreach (List<Polygon> perBlockRing in perBlockRings2)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (perBlockRing != null)
			{
				polygonsList.AddRange(perBlockRing);
			}
		}
		bool[] boolValues = new bool[contourChainsList.Count];
		for (int intervalIndex2 = 0; intervalIndex2 < contourChainsList.Count; intervalIndex2++)
		{
			if ((intervalIndex2 & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
			if (boolValues[intervalIndex2])
			{
				continue;
			}
			boolValues[intervalIndex2] = true;
			ContourChain contourChain = contourChainsList[intervalIndex2];
			List<Point> line = new List<Point>(contourChain.Line);
			Pixel endKey = contourChain.EndKey;
			bool condition3 = true;
			int stitchedChains = 0;
			while (endKey != contourChain.StartKey)
			{
				if ((stitchedChains++ & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
				if (!intByPixel.TryGetValue(endKey, out var localValue) || boolValues[localValue])
				{
					condition3 = false;
					break;
				}
				boolValues[localValue] = true;
				ContourChain contourChain2 = contourChainsList[localValue];
				for (int unity3 = 1; unity3 < contourChain2.Line.Count; unity3++)
				{
					if ((unity3 & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
					line.Add(contourChain2.Line[unity3]);
				}
				endKey = contourChain2.EndKey;
			}
			if (!condition3)
			{
				_logger.LogWarning("Tiled contour stitch: dropped an unclosed cycle ({Points} points)", line.Count);
				continue;
			}
			line.RemoveAt(line.Count - 1);
			if (line.Count >= 3)
			{
				Polygon polygon = Polygon.CreateWithBuffer(line);
				if (!(minRingArea > 0.0) || !(Math.Abs(polygon.Area) < minRingArea))
				{
					polygonsList.Add(polygon);
				}
			}
		}
		TimeSpan elapsed2 = stopwatch.Elapsed;
		stopwatch.Restart();
		if (simplifyDistance > 0.0)
		{
			List<Polygon> polygons3 = new List<Polygon>(polygonsList.Count);
			foreach (Polygon polygon3 in polygonsList)
			{
				cancellationToken.ThrowIfCancellationRequested();
				PathD pathD = Clipper.SimplifyPath(new PathD(polygon3), simplifyDistance);
				if (pathD.Count >= 3)
				{
					polygons3.Add(Polygon.CreateWithBuffer(pathD));
				}
			}
			polygons = ClipperUnionPolygonsParallel(polygons3);
		}
		else
		{
			polygons = ComplexPolygonCollection.FromDisjointRings(polygonsList);
		}
		polylines = new PolylineCollection();
		foreach (ComplexPolygon polygon2 in polygons)
		{
			cancellationToken.ThrowIfCancellationRequested();
			polylines.AddRange(polygon2.Rings);
		}
		_logger.LogDebug("Tiled mask contour ({BlockCells} cells/block): {Blocks} blocks ({Skipped} interior-full skipped, {Marched} marched) in {MarchMs} ms; stitched {Chains} chains in {StitchMs} ms; nested {Rings} rings in {NestMs} ms -> {Polygons} polygons", blockCells, blocks.Count, blocksSkipped, blocksMarched, (long)elapsed.TotalMilliseconds, contourChainsList.Count, (long)elapsed2.TotalMilliseconds, polygonsList.Count, (long)stopwatch.Elapsed.TotalMilliseconds, polygons.Count);
		float NeighborValue(int gr, int gc)
		{
			if (gr < 0 || gc < 0 || gr >= tileRows * ts || gc >= tileCols * ts)
			{
				return 0f;
			}
			if (!mask.Get(gr / ts * tileCols + gc / ts, gr % ts * ts + gc % ts))
			{
				return 0f;
			}
			return 1f;
		}
	}

	public static void GetContourShapes(ReadOnlyMemory<float> data, RasterDefinition rasterDef, float target, out ComplexPolygonCollection polygons, out PolylineCollection polylines, double simplifyDistance = 0.0, double minRingArea = 0.0)
		=> GetContourShapes(data, rasterDef, target, out polygons, out polylines, simplifyDistance, minRingArea, CancellationToken.None);

	public static void GetContourShapes(ReadOnlyMemory<float> data, RasterDefinition rasterDef, float target, out ComplexPolygonCollection polygons, out PolylineCollection polylines, double simplifyDistance, double minRingArea, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		polylines = new PolylineCollection();
		GetShapesFromRaster(data, rasterDef, target, -1, rasterDef.Cols, -1, rasterDef.Rows, out var polygons2, out var _, simplifyDistance, null, cancellationToken);
		if (minRingArea > 0.0)
		{
			List<Polygon> filteredPolygons = new List<Polygon>(polygons2.Count);
			foreach (Polygon polygon in polygons2)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (Math.Abs(polygon.Area) >= minRingArea)
					filteredPolygons.Add(polygon);
			}
			polygons2 = filteredPolygons;
		}
		foreach (Polygon polygon in polygons2)
		{
			cancellationToken.ThrowIfCancellationRequested();
			polylines.Add(polygon);
		}
		cancellationToken.ThrowIfCancellationRequested();
		polygons = ((simplifyDistance > 0.0) ? ClipperUnionPolygons(polygons2) : ComplexPolygonCollection.FromDisjointRings(polygons2));
		cancellationToken.ThrowIfCancellationRequested();
	}

	public static void GetContourShapes(IRaster<float> raster, float target, out ComplexPolygonCollection polygons, out PolylineCollection polylines)
	{
		float[] floatValues = new float[raster.RasterDefinition.CellCount];
		ResampleArgs args = new ResampleArgs(raster.RasterDefinition, InterpolationMode.Linear);
		raster.Resample(args, floatValues);
		GetContourShapes(floatValues, raster.RasterDefinition, target, out polygons, out polylines);
	}

	public static ComplexPolygonCollection ClipperUnionPolygons(IReadOnlyList<Polygon> inPolygons)
	{
		PathsD pathsD = new PathsD();
		foreach (Polygon inPolygon in inPolygons)
		{
			inPolygon.Reverse();
			pathsD.Add(new PathD(inPolygon));
		}
		return UnionToComplexPolygons(pathsD);
	}

	public static ComplexPolygonCollection ClipperUnionPolygonsParallel(IReadOnlyList<Polygon> inPolygons)
	{
		if (inPolygons.Count <= 4096)
		{
			return ClipperUnionPolygons(inPolygons);
		}
		double maxValue = double.MaxValue;
		double minValue = double.MinValue;
		double maxValue2 = double.MaxValue;
		double minValue2 = double.MinValue;
		foreach (Polygon inPolygon in inPolygons)
		{
			Point point = inPolygon[0];
			maxValue = Math.Min(maxValue, point.X);
			minValue = Math.Max(minValue, point.X);
			maxValue2 = Math.Min(maxValue2, point.Y);
			minValue2 = Math.Max(minValue2, point.Y);
		}
		int inPolygonsTerm = (int)Math.Ceiling(Math.Sqrt((double)inPolygons.Count / 512.0));
		double minValueMaxValueTerm = Math.Max((minValue - maxValue) / (double)inPolygonsTerm, double.Epsilon);
		double minValue2MaxValue2Term = Math.Max((minValue2 - maxValue2) / (double)inPolygonsTerm, double.Epsilon);
		List<Polygon>[] buckets = new List<Polygon>[inPolygonsTerm * inPolygonsTerm];
		foreach (Polygon inPolygon2 in inPolygons)
		{
			Point point2 = inPolygon2[0];
			int point2MaxValueTerm = Math.Min(inPolygonsTerm - 1, (int)((point2.X - maxValue) / minValueMaxValueTerm));
			int point2MaxValue2Term = Math.Min(inPolygonsTerm - 1, (int)((point2.Y - maxValue2) / minValue2MaxValue2Term));
			List<Polygon>[] bucketsList = buckets;
			int ordinal = point2MaxValue2Term * inPolygonsTerm + point2MaxValueTerm;
			(bucketsList[ordinal] ?? (bucketsList[ordinal] = new List<Polygon>())).Add(inPolygon2);
		}
		PathsD[] partials = new PathsD[buckets.Length];
		System.Threading.Tasks.Parallel.For(0, buckets.Length, (int i) =>
		{
			List<Polygon> bucket = buckets[i];
			if (bucket != null)
			{
				PathsD pathsD3 = new PathsD();
				foreach (Polygon bucket2 in bucket)
				{
					bucket2.Reverse();
					pathsD3.Add(new PathD(bucket2));
				}
				PathsD pathsD4 = new PathsD();
				ClipperD clipperD = new ClipperD();
				clipperD.AddSubject(pathsD3);
				clipperD.Execute(ClipType.Union, FillRule.NonZero, pathsD4);
				partials[i] = pathsD4;
			}
		});
		PathsD pathsD = new PathsD();
		PathsD[] partialsArray = partials;
		foreach (PathsD pathsD2 in partialsArray)
		{
			if (pathsD2 != null)
			{
				pathsD.AddRange(pathsD2);
			}
		}
		return UnionToComplexPolygons(pathsD);
	}

	private static ComplexPolygonCollection UnionToComplexPolygons(PathsD unionPolygons)
	{
		ComplexPolygonCollection complexPolygonCollection = new ComplexPolygonCollection();
		Stopwatch stopwatch = Stopwatch.StartNew();
		PolyTreeD polyTreeD = new PolyTreeD();
		ClipperD clipperD = new ClipperD();
		clipperD.AddSubject(unionPolygons);
		clipperD.Execute(ClipType.Union, FillRule.NonZero, polyTreeD);
		if (stopwatch.ElapsedMilliseconds > 1000)
		{
			_logger.LogDebug("Clipper union: {Paths} paths / {Vertices} vertices in {ExecuteMs} ms", unionPolygons.Count, unionPolygons.Sum((PathD p) => p.Count), stopwatch.ElapsedMilliseconds);
		}
		for (int intervalIndex = 0; intervalIndex < polyTreeD.Count; intervalIndex++)
		{
			ComplexPolygon polygon = null;
			RecursePolyPath(polyTreeD[intervalIndex], outer: true);
			complexPolygonCollection.Add(polygon);
			void RecursePolyPath(PolyPathD node, bool outer = false)
			{
				node.Polygon.Reverse();
				Polygon pgon = Polygon.CreateWithBuffer(node.Polygon);
				if (outer)
				{
					polygon = ComplexPolygon.FromOuterRing(pgon);
				}
				else
				{
					polygon.AddWithoutValidation(pgon);
				}
				for (int i = 0; i < node.Count; i++)
				{
					RecursePolyPath(node[i]);
				}
			}
		}
		return complexPolygonCollection;
	}

	public static void GetUnclippedShapes(IRaster<float> raster, int tileSize, float target, out PolygonCollection polygons, out PolylineCollection polylines, double simplifyDistance = 0.0)
	{
		polygons = new PolygonCollection();
		polylines = new PolylineCollection();
		RasterDefinition rasterDefinition = raster.RasterDefinition.TiledRaster(tileSize);
		float[] floatValues = new float[tileSize * tileSize];
		for (int i = 0; i < rasterDefinition.CellCount; i++)
		{
			RasterDefinition cellRasterDefinition = rasterDefinition.GetCellRasterDefinition(i, tileSize);
			ResampleArgs args = new ResampleArgs(cellRasterDefinition, InterpolationMode.Linear);
			raster.Resample(args, floatValues);
			GetShapesFromRaster(floatValues, cellRasterDefinition, target, out var polygons2, out var polylines2, simplifyDistance, borders: false);
			polygons.AddRange(polygons2);
			polylines.AddRange(polylines2);
		}
	}

	public static void GetUnclippedShapes(IRaster<float> raster, float target, out PolygonCollection polygons, out PolylineCollection polylines, double simplifyDistance = 0.0)
	{
		polygons = new PolygonCollection();
		polylines = new PolylineCollection();
		float[] floatValues = new float[raster.RasterDefinition.CellCount];
		ResampleArgs args = new ResampleArgs(raster.RasterDefinition, InterpolationMode.Linear);
		raster.Resample(args, floatValues);
		GetShapesFromRaster(floatValues, raster.RasterDefinition, target, out var polygons2, out var polylines2, simplifyDistance, borders: false);
		polygons.AddRange(polygons2);
		polylines.AddRange(polylines2);
	}
}
