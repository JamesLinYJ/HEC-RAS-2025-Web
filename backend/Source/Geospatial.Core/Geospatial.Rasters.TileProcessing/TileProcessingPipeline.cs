using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;
using Geospatial.Vectors;
using Utility;
using Utility.Memory;
using Utility.Progress;

namespace Geospatial.Rasters.TileProcessing;

public static class TileProcessingPipeline
{
	public static TResult Run<TPixel, TState, TResult>(ITileProvider<TPixel> source, ITileProcessor<TPixel, TState, TResult> processor, TileProcessingOptions options = null, ProgressReporter progress = null) where TPixel : unmanaged, IEquatable<TPixel>
	{
		if (source == null)
		{
			throw new ArgumentNullException("source");
		}
		if (processor == null)
		{
			throw new ArgumentNullException("processor");
		}
		if (options == null)
		{
			options = new TileProcessingOptions();
		}
		if (progress == null)
		{
			progress = ProgressReporter.None();
		}
		Extent? region = options.Region;
		if (!region.HasValue)
		{
			_ = source.ValidExtent;
		}
		else
		{
			region.GetValueOrDefault();
		}
		RasterDefinition tiledRasterDefinition = source.TiledRasterDefinition;
		using TileStateStore<TState> tileStateStore = new TileStateStore<TState>((long tileIndex) => processor.CreateState(tileIndex, source.GetTileRasterDefinition(tileIndex)), processor.Serializer, options.BackingStore, Math.Max(1, options.MaxResidentTiles));
		Accumulate(source, processor, options, tileStateStore);
		RunResolvePasses(source, processor, options, tileStateStore, tiledRasterDefinition, progress);
		return processor.Reduce(tileStateStore, source);
	}

	private static void Accumulate<TPixel, TState, TResult>(ITileProvider<TPixel> source, ITileProcessor<TPixel, TState, TResult> processor, TileProcessingOptions options, TileStateStore<TState> store) where TPixel : unmanaged, IEquatable<TPixel>
	{
		Extent region = options.Region ?? source.ValidExtent;
		bool runParallel = options.AccumulateIntent == ParallelizationIntent.Parallel && options.BackingStore == null;
		if (options.NeighborhoodRadius <= 0)
		{
			AccumulateLocal(source, processor, store, region, runParallel);
		}
		else
		{
			AccumulateWithNeighbors(source, processor, options, store, region, runParallel);
		}
	}

	private static void AccumulateLocal<TPixel, TState, TResult>(ITileProvider<TPixel> source, ITileProcessor<TPixel, TState, TResult> processor, TileStateStore<TState> store, Extent region, bool runParallel) where TPixel : unmanaged, IEquatable<TPixel>
	{
		TPixel? noData = source.NoDataValue;
		ParallelizationIntent parallelizationIntent = (runParallel ? ParallelizationIntent.Parallel : ParallelizationIntent.Serial);
		RasterOperations.ProcessTiles(source, region, clipTilesToDesiredExtent: false, (long tIdx, RasterDefinition tileDef, Memory<TPixel> buf) =>
		{
			using (store.BeginScope())
			{
				TState forUpdate = store.GetForUpdate(tIdx);
				TileWithNeighbors<TPixel> tiles = new TileWithNeighbors<TPixel>
				{
					Center = new RasterData<TPixel>(tileDef, buf)
					{
						NoDataValue = noData
					}
				};
				processor.Accumulate(new TileAccumulateContext<TPixel, TState>(tIdx, tileDef, noData, forUpdate, tiles));
			}
		}, parallelizationIntent);
	}

	private static void AccumulateWithNeighbors<TPixel, TState, TResult>(ITileProvider<TPixel> source, ITileProcessor<TPixel, TState, TResult> processor, TileProcessingOptions options, TileStateStore<TState> store, Extent region, bool runParallel) where TPixel : unmanaged, IEquatable<TPixel>
	{
		if (runParallel)
		{
			AccumulateWithNeighborsParallel(source, processor, store, region);
		}
		else
		{
			AccumulateWithNeighborsSerial(source, processor, options, store, region);
		}
	}

	private static void AccumulateWithNeighborsSerial<TPixel, TState, TResult>(ITileProvider<TPixel> source, ITileProcessor<TPixel, TState, TResult> processor, TileProcessingOptions options, TileStateStore<TState> store, Extent region) where TPixel : unmanaged, IEquatable<TPixel>
	{
		TPixel? noData = source.NoDataValue;
		double cellSize = source.TiledRasterDefinition.CellSize;
		RasterOperations.ProcessTilesWithNeighbors(source, region, cellSize, options.MaxPixelTilesInMemory, (int tIdx, TileWithNeighbors<TPixel> tiles) =>
		{
			using (store.BeginScope())
			{
				TState forUpdate = store.GetForUpdate(tIdx);
				RasterDefinition rasterDefinition = tiles.Center.RasterDefinition;
				processor.Accumulate(new TileAccumulateContext<TPixel, TState>(tIdx, rasterDefinition, noData, forUpdate, tiles));
			}
		});
	}

	private static void AccumulateWithNeighborsParallel<TPixel, TState, TResult>(ITileProvider<TPixel> source, ITileProcessor<TPixel, TState, TResult> processor, TileStateStore<TState> store, Extent region) where TPixel : unmanaged, IEquatable<TPixel>
	{
		TPixel? noData = source.NoDataValue;
		RasterDefinition rbDef = source.GetRenderBlockTileDefinition();
		double cellSize = source.TiledRasterDefinition.CellSize;
		PixelRange executeRange = rbDef.GetPixelRange(region);
		PixelRange readRange = rbDef.GetPixelRange(region.Extend(cellSize));
		if (executeRange.IsNull() || readRange.IsNull())
		{
			return;
		}
		int tilePx = source.TileSize * source.TileSize;
		int colCount = executeRange.ColCount;
		System.Threading.Tasks.Parallel.For(0, executeRange.CellCount, Utility.Parallel.GetReasonableParallelOptions(), (int i) =>
		{
			Pixel px = new Pixel(executeRange.MinRow + i / colCount, executeRange.MinCol + i % colCount);
			List<IMemoryOwner<TPixel>> iMemoryOwners = new List<IMemoryOwner<TPixel>>(9);
			try
			{
				TileWithNeighbors<TPixel> tileWithNeighbors = new TileWithNeighbors<TPixel>
				{
					UpperLeft = ReadTile(px.UpperLeft(), iMemoryOwners),
					Top = ReadTile(px.Top(), iMemoryOwners),
					UpperRight = ReadTile(px.UpperRight(), iMemoryOwners),
					Left = ReadTile(px.Left(), iMemoryOwners),
					Center = ReadTile(px, iMemoryOwners),
					Right = ReadTile(px.Right(), iMemoryOwners),
					LowerLeft = ReadTile(px.LowerLeft(), iMemoryOwners),
					Bottom = ReadTile(px.Bottom(), iMemoryOwners),
					LowerRight = ReadTile(px.LowerRight(), iMemoryOwners)
				};
				if (tileWithNeighbors.Center == null)
				{
					return;
				}
				long tileIndex = rbDef.CellIndexLong(px.Row, px.Column);
				RasterDefinition rasterDefinition = tileWithNeighbors.Center.RasterDefinition;
				using (store.BeginScope())
				{
					TState forUpdate = store.GetForUpdate(tileIndex);
					processor.Accumulate(new TileAccumulateContext<TPixel, TState>(tileIndex, rasterDefinition, noData, forUpdate, tileWithNeighbors));
				}
			}
			finally
			{
				foreach (IMemoryOwner<TPixel> iMemoryOwner in iMemoryOwners)
				{
					iMemoryOwner.Dispose();
				}
			}
		});
		RasterData<TPixel> ReadTile(Pixel px, List<IMemoryOwner<TPixel>> owners)
		{
			if (!rbDef.InBounds(px) || !readRange.InRange(px))
			{
				return null;
			}
			IMemoryOwner<TPixel> memoryOwner = SharedMemoryPool<TPixel>.RentNoFill(tilePx, out var mem);
			long blockIndex = rbDef.CellIndexLong(px.Row, px.Column);
			if (!source.ReadBlock(blockIndex, mem))
			{
				memoryOwner.Dispose();
				throw new InvalidOperationException($"Failed to read tile {px} during parallel accumulate.");
			}
			owners.Add(memoryOwner);
			return new RasterData<TPixel>(source.GetTileRasterDefinition(blockIndex), mem)
			{
				NoDataValue = noData
			};
		}
	}

	private static void RunResolvePasses<TPixel, TState, TResult>(ITileProvider<TPixel> source, ITileProcessor<TPixel, TState, TResult> processor, TileProcessingOptions options, TileStateStore<TState> store, RasterDefinition tiledDef, ProgressReporter progress) where TPixel : unmanaged, IEquatable<TPixel>
	{
		for (int i = 0; i < options.MaxResolvePasses; i++)
		{
			if (progress.CancelRequested)
			{
				break;
			}
			bool condition = false;
			foreach (long tileIndex in store.TileIndices)
			{
				if (progress.CancelRequested)
				{
					break;
				}
				using (store.BeginScope())
				{
					if (store.TryGetForUpdate(tileIndex, out var state))
					{
						Pixel tileXY = tiledDef.CellIndex2D(tileIndex);
						condition |= processor.Resolve(new TileResolveContext<TState>(tileIndex, tileXY, source.GetTileRasterDefinition(tileIndex), state, source, store));
					}
				}
			}
			if (!condition)
			{
				break;
			}
		}
	}
}
