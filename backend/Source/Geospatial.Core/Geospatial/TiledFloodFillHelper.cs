using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Geospatial.Rasters;
using Utility.Memory;
using Utility.Progress;

namespace Geospatial;

public class TiledFloodFillHelper<TTile, TBoundary>
{
	public delegate TTile AllocTile();

	public delegate void InitTile(int tileIndex, TTile buffer);

	public delegate void CleanupTile(TTile buffer);

	public delegate bool FromToTest(TTile tile, int fromLocalIndex, int toLocalIndex);

	public delegate bool IncludeTest(TTile tile, int localIndex);

	public delegate TBoundary GetBoundaryContext(TTile tile, int boundaryLocalIndex);

	public delegate bool CrossTileTest(TBoundary sourceContext, TTile targetTile, int targetLocalIndex, bool diagonal);

	private readonly struct DelegateTraversal(FromToTest fromToTest, GetBoundaryContext getBoundaryContext, CrossTileTest crossTileTest) : ITileTraversal<TTile, TBoundary>
	{
		private readonly FromToTest _fromToTest = fromToTest;

		private readonly GetBoundaryContext _getBoundaryContext = getBoundaryContext;

		private readonly CrossTileTest _crossTileTest = crossTileTest;

		public bool CanTraverse(TTile tile, int fromLocalIndex, int toLocalIndex)
		{
			return _fromToTest(tile, fromLocalIndex, toLocalIndex);
		}

		public TBoundary GetBoundaryContext(TTile tile, int boundaryLocalIndex)
		{
			return _getBoundaryContext(tile, boundaryLocalIndex);
		}

		public bool CanCross(TBoundary sourceContext, TTile targetTile, int targetLocalIndex, bool diagonal)
		{
			return _crossTileTest(sourceContext, targetTile, targetLocalIndex, diagonal);
		}
	}

	private struct BoundarySeed(int targetLocalIdx, TBoundary context, bool diagonal)
	{
		public int TargetLocalIdx = targetLocalIdx;

		public TBoundary Context = context;

		public bool Diagonal = diagonal;
	}

	private struct TileWork
	{
		public PoolHandle<List<BoundarySeed>> Handle;

		public readonly List<BoundarySeed> Seeds => Handle.Data;
	}

	private sealed class FillScratch
	{
		public readonly bool[] PreFill;

		public readonly bool[] PostFill;

		public readonly Queue<int> Queue = new Queue<int>();

		public readonly List<int> LocalSeeds = new List<int>();

		public FillScratch(int tileSize)
		{
			PreFill = new bool[tileSize * 4];
			PostFill = new bool[tileSize * 4];
		}
	}

	private sealed class Worker
	{
		public readonly TTile Buffer;

		public readonly FillScratch Scratch;

		public Worker(TTile buffer, FillScratch scratch)
		{
			Buffer = buffer;
			Scratch = scratch;
		}
	}

	private sealed class BoundarySeedListPool
	{
		private readonly Pool<List<BoundarySeed>> _pool = new Pool<List<BoundarySeed>>(() => new List<BoundarySeed>());
		private readonly HashSet<PoolHandle<List<BoundarySeed>>> _outstanding = new HashSet<PoolHandle<List<BoundarySeed>>>();
		private readonly object _sync = new object();

		public int ElementsOnLoan => _pool.ElementsOnLoan;

		public PoolHandle<List<BoundarySeed>> Acquire()
		{
			PoolHandle<List<BoundarySeed>> handle = _pool.Acquire();
			lock (_sync)
			{
				_outstanding.Add(handle);
			}
			return handle;
		}

		public void Return(PoolHandle<List<BoundarySeed>> handle)
		{
			if (handle == null)
			{
				return;
			}
			lock (_sync)
			{
				if (!_outstanding.Remove(handle))
				{
					return;
				}
				handle.Data.Clear();
				_pool.Return(handle);
			}
		}

		public void ReturnAll()
		{
			lock (_sync)
			{
				foreach (PoolHandle<List<BoundarySeed>> handle in _outstanding)
				{
					handle.Data.Clear();
					_pool.Return(handle);
				}
				_outstanding.Clear();
			}
		}
	}

	private readonly int _tileRows;

	private readonly int _tileCols;

	private readonly int _tileSize;

	private readonly bool _use8Connectivity;

	private readonly AllocTile _allocTile;

	private readonly InitTile _initTile;

	private readonly CleanupTile _cleanupTile;

	private readonly FromToTest _fromToTest;

	private readonly GetBoundaryContext _getBoundaryContext;

	private readonly CrossTileTest _crossTileTest;

	private BoundarySeedListPool _seedListPool;

	public TiledFloodFillHelper(int tileRows, int tileCols, int tileSize, RasterCellNeighborSet neighbors, AllocTile allocTile, InitTile initTile, FromToTest fromToTest, GetBoundaryContext getBoundaryContext, CrossTileTest crossTileTest, CleanupTile cleanupTile = null)
		: this(tileRows, tileCols, tileSize, neighbors, allocTile, initTile, cleanupTile)
	{
		_fromToTest = fromToTest;
		_getBoundaryContext = getBoundaryContext;
		_crossTileTest = crossTileTest;
	}

	public TiledFloodFillHelper(int tileRows, int tileCols, int tileSize, RasterCellNeighborSet neighbors, AllocTile allocTile, InitTile initTile, CleanupTile cleanupTile = null)
	{
		_tileRows = tileRows;
		_tileCols = tileCols;
		_tileSize = tileSize;
		_use8Connectivity = neighbors == RasterCellNeighborSet.EightCorners;
		_allocTile = allocTile;
		_initTile = initTile;
		_cleanupTile = cleanupTile;
	}

	public TiledFloodFillHelper(int tileRows, int tileCols, int tileSize, RasterCellNeighborSet neighbors, AllocTile allocTile, InitTile initTile, IncludeTest includeTest, CleanupTile cleanupTile = null)
		: this(tileRows, tileCols, tileSize, neighbors, allocTile, initTile, (FromToTest)((TTile tile, int from, int to) => includeTest(tile, to)), (GetBoundaryContext)((TTile tile, int idx) => default), (CrossTileTest)((TBoundary ctx, TTile tile, int idx, bool diagonal) => includeTest(tile, idx)), cleanupTile)
	{
	}

	public TiledBitArray Fill(Dictionary<int, List<int>> seedsByTile, ProgressReporter progress = null)
	{
		EnsureTraversalDelegates();
		return Fill(seedsByTile, new DelegateTraversal(_fromToTest, _getBoundaryContext, _crossTileTest), progress, CancellationToken.None);
	}

	public TiledBitArray Fill(Dictionary<int, List<int>> seedsByTile, [AllowNull] ProgressReporter progress, CancellationToken cancellationToken)
	{
		EnsureTraversalDelegates();
		return Fill(seedsByTile, new DelegateTraversal(_fromToTest, _getBoundaryContext, _crossTileTest), progress, cancellationToken);
	}

	public TiledBitArray Fill<TTraversal>(Dictionary<int, List<int>> seedsByTile, TTraversal traversal, ProgressReporter progress = null) where TTraversal : struct, ITileTraversal<TTile, TBoundary>
		=> Fill(seedsByTile, traversal, progress, CancellationToken.None);

	public TiledBitArray Fill<TTraversal>(Dictionary<int, List<int>> seedsByTile, TTraversal traversal, [AllowNull] ProgressReporter progress, CancellationToken cancellationToken) where TTraversal : struct, ITileTraversal<TTile, TBoundary>
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (progress == null)
		{
			progress = ProgressReporter.None();
		}
		_seedListPool = new BoundarySeedListPool();
		Dictionary<int, BitArray> bitArrayByInt = new Dictionary<int, BitArray>();
		Dictionary<int, TileWork> currentWork = new Dictionary<int, TileWork>();
		int discovered = seedsByTile.Count;
		int ordinal = 0;
		int lastPct = -1;
		TTile allocTile = _allocTile();
		try
		{
			FillScratch fillScratch = new FillScratch(_tileSize);
			foreach (KeyValuePair<int, List<int>> seedsByTile2 in seedsByTile)
			{
				cancellationToken.ThrowIfCancellationRequested();
				int key = seedsByTile2.Key;
				_initTile(key, allocTile);
				cancellationToken.ThrowIfCancellationRequested();
				BitArray orCreateMask = GetOrCreateMask(bitArrayByInt, key);
				SnapshotPerimeter(orCreateMask, fillScratch.PreFill, cancellationToken);
				MaskAwareFill(traversal, allocTile, orCreateMask, seedsByTile2.Value, fillScratch.Queue, cancellationToken);
				SnapshotPerimeter(orCreateMask, fillScratch.PostFill, cancellationToken);
				EmitBoundaryWork(traversal, allocTile, key, fillScratch.PreFill, fillScratch.PostFill, (int idx, PoolHandle<List<BoundarySeed>> seeds) =>
				{
					if (MergeWork(currentWork, idx, seeds))
					{
						discovered++;
					}
				}, cancellationToken);
				ordinal++;
				ReportTileProgress(progress, ordinal, discovered, ref lastPct);
			}
			while (currentWork.Count > 0)
			{
				Dictionary<int, TileWork> nextWork = new Dictionary<int, TileWork>();
				foreach (KeyValuePair<int, TileWork> currentWork2 in currentWork)
				{
					cancellationToken.ThrowIfCancellationRequested();
					int key2 = currentWork2.Key;
					_initTile(key2, allocTile);
					cancellationToken.ThrowIfCancellationRequested();
					bitArrayByInt.TryGetValue(key2, out var key2BitArrayTerm);
					List<int> validateCrossTileSeeds;
					try { validateCrossTileSeeds = ValidateCrossTileSeeds(traversal, allocTile, key2BitArrayTerm, currentWork2.Value.Seeds, fillScratch.LocalSeeds, cancellationToken); }
					finally { ReturnSeedList(currentWork2.Value.Handle); }
					if (validateCrossTileSeeds.Count == 0)
					{
						ordinal++;
						ReportTileProgress(progress, ordinal, discovered, ref lastPct);
						continue;
					}
					if (key2BitArrayTerm == null)
					{
						key2BitArrayTerm = (bitArrayByInt[key2] = new BitArray(_tileSize * _tileSize));
					}
					SnapshotPerimeter(key2BitArrayTerm, fillScratch.PreFill, cancellationToken);
					MaskAwareFill(traversal, allocTile, key2BitArrayTerm, validateCrossTileSeeds, fillScratch.Queue, cancellationToken);
					SnapshotPerimeter(key2BitArrayTerm, fillScratch.PostFill, cancellationToken);
					EmitBoundaryWork(traversal, allocTile, key2, fillScratch.PreFill, fillScratch.PostFill, (int idx, PoolHandle<List<BoundarySeed>> seeds) =>
					{
						if (MergeWork(nextWork, idx, seeds))
						{
							int discovered2 = discovered;
							discovered = discovered2 + 1;
						}
					}, cancellationToken);
					ordinal++;
					ReportTileProgress(progress, ordinal, discovered, ref lastPct);
				}
				currentWork = nextWork;
			}
		}
		finally
		{
			try { _seedListPool.ReturnAll(); }
			finally { _cleanupTile?.Invoke(allocTile); }
		}
		return new TiledBitArray(_tileRows, _tileCols, _tileSize, bitArrayByInt, cancellationToken);
	}

	public TiledBitArray FillParallel(Dictionary<int, List<int>> seedsByTile, ProgressReporter progress = null)
	{
		EnsureTraversalDelegates();
		return FillParallel(seedsByTile, new DelegateTraversal(_fromToTest, _getBoundaryContext, _crossTileTest), progress, CancellationToken.None);
	}

	public TiledBitArray FillParallel(Dictionary<int, List<int>> seedsByTile, [AllowNull] ProgressReporter progress, CancellationToken cancellationToken)
	{
		EnsureTraversalDelegates();
		return FillParallel(seedsByTile, new DelegateTraversal(_fromToTest, _getBoundaryContext, _crossTileTest), progress, cancellationToken);
	}

	public TiledBitArray FillParallel<TTraversal>(Dictionary<int, List<int>> seedsByTile, TTraversal traversal, ProgressReporter progress = null) where TTraversal : struct, ITileTraversal<TTile, TBoundary>
		=> FillParallel(seedsByTile, traversal, progress, CancellationToken.None);

	public TiledBitArray FillParallel<TTraversal>(Dictionary<int, List<int>> seedsByTile, TTraversal traversal, [AllowNull] ProgressReporter progress, CancellationToken cancellationToken) where TTraversal : struct, ITileTraversal<TTile, TBoundary>
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (progress == null)
		{
			progress = ProgressReporter.None();
		}
		_seedListPool = new BoundarySeedListPool();
		Dictionary<int, BitArray> masks = new Dictionary<int, BitArray>();
		Dictionary<int, TileWork> currentWork = new Dictionary<int, TileWork>();
		int discovered = seedsByTile.Count;
		int processed = 0;
		int lastPct = -1;
		object progressLock = new object();
		TTile allocTile = _allocTile();
		try
		{
			try
			{
				FillScratch fillScratch = new FillScratch(_tileSize);
				foreach (KeyValuePair<int, List<int>> seedsByTile2 in seedsByTile)
				{
					cancellationToken.ThrowIfCancellationRequested();
					int key = seedsByTile2.Key;
					_initTile(key, allocTile);
					cancellationToken.ThrowIfCancellationRequested();
					BitArray orCreateMask = GetOrCreateMask(masks, key);
					SnapshotPerimeter(orCreateMask, fillScratch.PreFill, cancellationToken);
					MaskAwareFill(traversal, allocTile, orCreateMask, seedsByTile2.Value, fillScratch.Queue, cancellationToken);
					SnapshotPerimeter(orCreateMask, fillScratch.PostFill, cancellationToken);
					EmitBoundaryWork(traversal, allocTile, key, fillScratch.PreFill, fillScratch.PostFill, (int idx, PoolHandle<List<BoundarySeed>> seeds) =>
					{
						if (MergeWork(currentWork, idx, seeds))
						{
							discovered++;
						}
					}, cancellationToken);
					processed++;
					ReportTileProgress(progress, processed, discovered, ref lastPct);
				}
			}
			finally
			{
				_cleanupTile?.Invoke(allocTile);
			}

			while (currentWork.Count > 0)
			{
				cancellationToken.ThrowIfCancellationRequested();
				Dictionary<int, TileWork> nextWork = new Dictionary<int, TileWork>();
				Parallel.ForEach(currentWork, new ParallelOptions { CancellationToken = cancellationToken },
					CreateWorker,
					(KeyValuePair<int, TileWork> kvp, ParallelLoopState loopState, Worker worker) =>
					{
						TTile buffer = worker.Buffer;
						FillScratch scratch = worker.Scratch;
						cancellationToken.ThrowIfCancellationRequested();
						int key2 = kvp.Key;
						_initTile(key2, buffer);
						cancellationToken.ThrowIfCancellationRequested();
						BitArray masks2;
						lock (masks)
						{
							masks.TryGetValue(key2, out masks2);
						}
						List<int> validateCrossTileSeeds;
						try { validateCrossTileSeeds = ValidateCrossTileSeeds(traversal, buffer, masks2, kvp.Value.Seeds, scratch.LocalSeeds, cancellationToken); }
						finally { ReturnSeedList(kvp.Value.Handle); }
						if (validateCrossTileSeeds.Count == 0)
						{
							int processed2 = Interlocked.Increment(ref processed);
							lock (progressLock)
							{
								ReportTileProgress(progress, processed2, discovered, ref lastPct);
								return worker;
							}
						}
						lock (masks)
						{
							if (masks2 == null)
							{
								masks2 = new BitArray(_tileSize * _tileSize);
								masks[key2] = masks2;
							}
						}
						SnapshotPerimeter(masks2, scratch.PreFill, cancellationToken);
						MaskAwareFill(traversal, buffer, masks2, validateCrossTileSeeds, scratch.Queue, cancellationToken);
						SnapshotPerimeter(masks2, scratch.PostFill, cancellationToken);
						EmitBoundaryWork(traversal, buffer, key2, scratch.PreFill, scratch.PostFill, (int idx, PoolHandle<List<BoundarySeed>> seeds) =>
						{
							lock (nextWork)
							{
								if (MergeWork(nextWork, idx, seeds))
								{
									int discovered2 = discovered;
									discovered = discovered2 + 1;
								}
							}
						}, cancellationToken);
						int processed3 = Interlocked.Increment(ref processed);
						lock (progressLock)
						{
							ReportTileProgress(progress, processed3, discovered, ref lastPct);
							return worker;
						}
					},
					(Worker worker) => _cleanupTile?.Invoke(worker.Buffer));
				currentWork = nextWork;
			}
			return new TiledBitArray(_tileRows, _tileCols, _tileSize, masks, cancellationToken);
		}
		finally
		{
			_seedListPool.ReturnAll();
		}
	}

	private Worker CreateWorker()
	{
		TTile buffer = _allocTile();
		try
		{
			return new Worker(buffer, new FillScratch(_tileSize));
		}
		catch
		{
			_cleanupTile?.Invoke(buffer);
			throw;
		}
	}

	private static void ReportTileProgress(ProgressReporter progress, int processed, int discovered, ref int lastPct)
	{
		int lastPct2 = ((discovered != 0) ? ((int)(100f * (float)processed / (float)discovered)) : 0);
		if (lastPct2 != lastPct)
		{
			lastPct = lastPct2;
			progress.ReportProgress(lastPct2);
			progress.ReportMessage($"{processed} processed / {discovered} discovered");
		}
	}

	private bool MergeWork(Dictionary<int, TileWork> dict, int tileIdx, PoolHandle<List<BoundarySeed>> handle)
	{
		if (dict.TryGetValue(tileIdx, out var localValue))
		{
			localValue.Seeds.AddRange(handle.Data);
			ReturnSeedList(handle);
			return false;
		}
		dict[tileIdx] = new TileWork
		{
			Handle = handle
		};
		return true;
	}

	private List<int> ValidateCrossTileSeeds<TTraversal>(TTraversal traversal, TTile tileData, BitArray mask, List<BoundarySeed> boundarySeeds, List<int> localSeeds, CancellationToken cancellationToken) where TTraversal : struct, ITileTraversal<TTile, TBoundary>
	{
		localSeeds.Clear();
		int checkedSeeds = 0;
		foreach (BoundarySeed boundarySeed in boundarySeeds)
		{
			if ((checkedSeeds++ & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
			if ((mask == null || !mask[boundarySeed.TargetLocalIdx]) && traversal.CanCross(boundarySeed.Context, tileData, boundarySeed.TargetLocalIdx, boundarySeed.Diagonal))
			{
				localSeeds.Add(boundarySeed.TargetLocalIdx);
			}
		}
		return localSeeds;
	}

	private void ReturnSeedList(PoolHandle<List<BoundarySeed>> handle)
	{
		_seedListPool.Return(handle);
	}

	private void MaskAwareFill<TTraversal>(TTraversal traversal, TTile tileData, BitArray mask, List<int> seeds, Queue<int> intQueue, CancellationToken cancellationToken) where TTraversal : struct, ITileTraversal<TTile, TBoundary>
	{
		int tileSize = _tileSize;
		intQueue.Clear();
		int checkedSeeds = 0;
		foreach (int seed in seeds)
		{
			if ((checkedSeeds++ & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
			if (!mask[seed])
			{
				mask[seed] = true;
				intQueue.Enqueue(seed);
			}
		}
		int result;
		int visited = 0;
		while (intQueue.TryDequeue(out result))
		{
			if ((visited++ & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
			int tileSizeTerm = result / tileSize;
			int tileSizeTerm2 = result % tileSize;
			if (tileSizeTerm > 0)
			{
				TryEnqueue(traversal, tileData, mask, intQueue, result, result - tileSize);
			}
			if (tileSizeTerm < tileSize - 1)
			{
				TryEnqueue(traversal, tileData, mask, intQueue, result, result + tileSize);
			}
			if (tileSizeTerm2 > 0)
			{
				TryEnqueue(traversal, tileData, mask, intQueue, result, result - 1);
			}
			if (tileSizeTerm2 < tileSize - 1)
			{
				TryEnqueue(traversal, tileData, mask, intQueue, result, result + 1);
			}
			if (_use8Connectivity)
			{
				if (tileSizeTerm > 0 && tileSizeTerm2 > 0)
				{
					TryEnqueue(traversal, tileData, mask, intQueue, result, result - tileSize - 1);
				}
				if (tileSizeTerm > 0 && tileSizeTerm2 < tileSize - 1)
				{
					TryEnqueue(traversal, tileData, mask, intQueue, result, result - tileSize + 1);
				}
				if (tileSizeTerm < tileSize - 1 && tileSizeTerm2 > 0)
				{
					TryEnqueue(traversal, tileData, mask, intQueue, result, result + tileSize - 1);
				}
				if (tileSizeTerm < tileSize - 1 && tileSizeTerm2 < tileSize - 1)
				{
					TryEnqueue(traversal, tileData, mask, intQueue, result, result + tileSize + 1);
				}
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void TryEnqueue<TTraversal>(TTraversal traversal, TTile tileData, BitArray mask, Queue<int> intQueue, int from, int to) where TTraversal : struct, ITileTraversal<TTile, TBoundary>
	{
		if (!mask[to] && traversal.CanTraverse(tileData, from, to))
		{
			mask[to] = true;
			intQueue.Enqueue(to);
		}
	}

	private void SnapshotPerimeter(BitArray mask, bool[] perim, CancellationToken cancellationToken)
	{
		int tileSize = _tileSize;
		for (int i = 0; i < tileSize; i++)
		{
			if ((i & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
			perim[i] = mask[i];
		}
		for (int j = 0; j < tileSize; j++)
		{
			if ((j & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
			perim[tileSize + j] = mask[j * tileSize];
		}
		for (int k = 0; k < tileSize; k++)
		{
			if ((k & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
			perim[tileSize * 2 + k] = mask[k * tileSize + tileSize - 1];
		}
		for (int l = 0; l < tileSize; l++)
		{
			if ((l & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
			perim[tileSize * 3 + l] = mask[(tileSize - 1) * tileSize + l];
		}
	}

	private void EmitBoundaryWork<TTraversal>(TTraversal traversal, TTile tileData, int tileIdx, bool[] preFill, bool[] postFill, Action<int, PoolHandle<List<BoundarySeed>>> addWork, CancellationToken cancellationToken) where TTraversal : struct, ITileTraversal<TTile, TBoundary>
	{
		int tileSize = _tileSize;
		int tileIdxOverTileCols = tileIdx / _tileCols;
		int tileIdxWithTileCols = tileIdx % _tileCols;
		bool use8Connectivity = _use8Connectivity;
		if (tileIdxOverTileCols > 0)
		{
			int tileColsTerm = (tileIdxOverTileCols - 1) * _tileCols + tileIdxWithTileCols;
			PoolHandle<List<BoundarySeed>> poolHandle = null;
			List<BoundarySeed> boundarySeeds = null;
			for (int i = 0; i < tileSize; i++)
			{
				if ((i & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
				if (!preFill[i] && postFill[i])
				{
					TBoundary boundaryContext = traversal.GetBoundaryContext(tileData, i);
					if (boundarySeeds == null)
					{
						boundarySeeds = (poolHandle = _seedListPool.Acquire()).Data;
					}
					boundarySeeds.Add(new BoundarySeed((tileSize - 1) * tileSize + i, boundaryContext, diagonal: false));
					if (use8Connectivity && i > 0)
					{
						boundarySeeds.Add(new BoundarySeed((tileSize - 1) * tileSize + (i - 1), boundaryContext, diagonal: true));
					}
					if (use8Connectivity && i < tileSize - 1)
					{
						boundarySeeds.Add(new BoundarySeed((tileSize - 1) * tileSize + (i + 1), boundaryContext, diagonal: true));
					}
				}
			}
			if (poolHandle != null)
			{
				addWork(tileColsTerm, poolHandle);
			}
		}
		if (tileIdxWithTileCols > 0)
		{
			int tileColsTerm2 = tileIdxOverTileCols * _tileCols + (tileIdxWithTileCols - 1);
			PoolHandle<List<BoundarySeed>> poolHandle2 = null;
			List<BoundarySeed> boundarySeedsList = null;
			for (int j = 0; j < tileSize; j++)
			{
				if ((j & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
				int tileSizeTerm = tileSize + j;
				if (!preFill[tileSizeTerm] && postFill[tileSizeTerm])
				{
					TBoundary boundaryContext2 = traversal.GetBoundaryContext(tileData, j * tileSize);
					if (boundarySeedsList == null)
					{
						boundarySeedsList = (poolHandle2 = _seedListPool.Acquire()).Data;
					}
					boundarySeedsList.Add(new BoundarySeed(j * tileSize + tileSize - 1, boundaryContext2, diagonal: false));
					if (use8Connectivity && j > 0)
					{
						boundarySeedsList.Add(new BoundarySeed((j - 1) * tileSize + tileSize - 1, boundaryContext2, diagonal: true));
					}
					if (use8Connectivity && j < tileSize - 1)
					{
						boundarySeedsList.Add(new BoundarySeed((j + 1) * tileSize + tileSize - 1, boundaryContext2, diagonal: true));
					}
				}
			}
			if (poolHandle2 != null)
			{
				addWork(tileColsTerm2, poolHandle2);
			}
		}
		if (tileIdxWithTileCols < _tileCols - 1)
		{
			int tileColsTerm3 = tileIdxOverTileCols * _tileCols + (tileIdxWithTileCols + 1);
			PoolHandle<List<BoundarySeed>> poolHandle3 = null;
			List<BoundarySeed> boundarySeeds2 = null;
			for (int k = 0; k < tileSize; k++)
			{
				if ((k & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
				int tileSizeTerm2 = tileSize * 2 + k;
				if (!preFill[tileSizeTerm2] && postFill[tileSizeTerm2])
				{
					TBoundary boundaryContext3 = traversal.GetBoundaryContext(tileData, k * tileSize + tileSize - 1);
					if (boundarySeeds2 == null)
					{
						boundarySeeds2 = (poolHandle3 = _seedListPool.Acquire()).Data;
					}
					boundarySeeds2.Add(new BoundarySeed(k * tileSize, boundaryContext3, diagonal: false));
					if (use8Connectivity && k > 0)
					{
						boundarySeeds2.Add(new BoundarySeed((k - 1) * tileSize, boundaryContext3, diagonal: true));
					}
					if (use8Connectivity && k < tileSize - 1)
					{
						boundarySeeds2.Add(new BoundarySeed((k + 1) * tileSize, boundaryContext3, diagonal: true));
					}
				}
			}
			if (poolHandle3 != null)
			{
				addWork(tileColsTerm3, poolHandle3);
			}
		}
		if (tileIdxOverTileCols < _tileRows - 1)
		{
			int tileColsTerm4 = (tileIdxOverTileCols + 1) * _tileCols + tileIdxWithTileCols;
			PoolHandle<List<BoundarySeed>> poolHandle4 = null;
			List<BoundarySeed> boundarySeeds3 = null;
			for (int l = 0; l < tileSize; l++)
			{
				if ((l & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
				int tileSizeTerm3 = tileSize * 3 + l;
				if (!preFill[tileSizeTerm3] && postFill[tileSizeTerm3])
				{
					TBoundary boundaryContext4 = traversal.GetBoundaryContext(tileData, (tileSize - 1) * tileSize + l);
					if (boundarySeeds3 == null)
					{
						boundarySeeds3 = (poolHandle4 = _seedListPool.Acquire()).Data;
					}
					boundarySeeds3.Add(new BoundarySeed(l, boundaryContext4, diagonal: false));
					if (use8Connectivity && l > 0)
					{
						boundarySeeds3.Add(new BoundarySeed(l - 1, boundaryContext4, diagonal: true));
					}
					if (use8Connectivity && l < tileSize - 1)
					{
						boundarySeeds3.Add(new BoundarySeed(l + 1, boundaryContext4, diagonal: true));
					}
				}
			}
			if (poolHandle4 != null)
			{
				addWork(tileColsTerm4, poolHandle4);
			}
		}
		if (use8Connectivity)
		{
			cancellationToken.ThrowIfCancellationRequested();
			EmitCorner(traversal, tileData, addWork, preFill, postFill, tileIdxOverTileCols > 0 && tileIdxWithTileCols > 0, 0, (tileIdxOverTileCols - 1) * _tileCols + (tileIdxWithTileCols - 1), 0, (tileSize - 1) * tileSize + (tileSize - 1));
			EmitCorner(traversal, tileData, addWork, preFill, postFill, tileIdxOverTileCols > 0 && tileIdxWithTileCols < _tileCols - 1, tileSize - 1, (tileIdxOverTileCols - 1) * _tileCols + (tileIdxWithTileCols + 1), tileSize - 1, (tileSize - 1) * tileSize);
			EmitCorner(traversal, tileData, addWork, preFill, postFill, tileIdxOverTileCols < _tileRows - 1 && tileIdxWithTileCols > 0, tileSize * 3, (tileIdxOverTileCols + 1) * _tileCols + (tileIdxWithTileCols - 1), (tileSize - 1) * tileSize, tileSize - 1);
			EmitCorner(traversal, tileData, addWork, preFill, postFill, tileIdxOverTileCols < _tileRows - 1 && tileIdxWithTileCols < _tileCols - 1, tileSize * 3 + tileSize - 1, (tileIdxOverTileCols + 1) * _tileCols + (tileIdxWithTileCols + 1), (tileSize - 1) * tileSize + (tileSize - 1), 0);
		}
	}

	private void EmitCorner<TTraversal>(TTraversal traversal, TTile tileData, Action<int, PoolHandle<List<BoundarySeed>>> addWork, bool[] preFill, bool[] postFill, bool inGrid, int perimIdx, int neighborIdx, int sourceIdx, int targetIdx) where TTraversal : struct, ITileTraversal<TTile, TBoundary>
	{
		if (inGrid && !preFill[perimIdx] && postFill[perimIdx])
		{
			PoolHandle<List<BoundarySeed>> poolHandle = _seedListPool.Acquire();
			poolHandle.Data.Add(new BoundarySeed(targetIdx, traversal.GetBoundaryContext(tileData, sourceIdx), diagonal: true));
			addWork(neighborIdx, poolHandle);
		}
	}

	private BitArray GetOrCreateMask(Dictionary<int, BitArray> masks, int tileIdx)
	{
		if (!masks.TryGetValue(tileIdx, out var orCreateMask))
		{
			orCreateMask = (masks[tileIdx] = new BitArray(_tileSize * _tileSize));
		}
		return orCreateMask;
	}

	private void EnsureTraversalDelegates()
	{
		if (_fromToTest == null)
		{
			throw new InvalidOperationException("This helper was constructed without traversal delegates - pass an ITileTraversal struct to the generic Fill/FillParallel overloads instead.");
		}
	}
}
