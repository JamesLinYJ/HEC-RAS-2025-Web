using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Utility.Logging;

namespace Utility.Matrices;

public class BlockMatrixSolver : IMatrixSolver<double>
{
	private sealed class InnerSlot
	{
		public IMatrixSolver<double> Solver;

		public SolverKind Kind;

		public SparseMatrix<double> BlockMatrix;

		public int[] PositionMap;

		public double[] xBlock;

		public double[] bBlock;

		public int CurrentSize;

		public IPreconditioner<double> Precond;

		public bool UpgradedToPardiso;

		public int[] PrevPositionMap;

		public int PrevPositionMapLength;

		public bool HasPreparedOnce;
	}

	public int TinyMaxSize = 50;

	public int MediumMaxSize = 1000;

	private bool _parallel;

	private int _numCores = 1;

	private readonly ParallelOptions _parallelOptions = new ParallelOptions
	{
		MaxDegreeOfParallelism = 1
	};

	private readonly SolverOptionsBundle _options;

	private readonly bool _allowPardiso;

	private PartialSolve _partSolve;

	private readonly Dictionary<int, InnerSlot> _solverByRegionId = new Dictionary<int, InnerSlot>();

	private readonly HashSet<int> _activeRegionIds = new HashSet<int>();

	private readonly List<int> _prepareBlockOrder = new List<int>();

	private int[] _apportionSizes = Array.Empty<int>();

	private int[] _apportionResult = Array.Empty<int>();

	private InnerSlot[] _apportionSlots = Array.Empty<InnerSlot>();

	private int _globalMaxRows;

	private int _globalMaxNonZero;

	public static readonly List<BlockMatrixSolver> _liveInstances = new List<BlockMatrixSolver>();

	public static bool EnableProfiling = true;

	private long _ticksPrepareTotal;

	private long _ticksExtractBlock;

	private long _ticksInnerPrepare;

	private long _ticksSolveTotal;

	private long _ticksMapBlockData;

	private long _ticksGatherBlock;

	private long _ticksInnerSolve;

	private long _ticksScatterBlock;

	private readonly long[] _innerSolveTicksByKind = new long[8];

	private readonly long[] _innerSolveCountByKind = new long[8];

	private readonly long[] _innerPrepareTicksByKind = new long[8];

	private readonly long[] _innerPrepareCountByKind = new long[8];

	private readonly long[] _innerPrepareSkippedByKind = new long[8];

	private long _ticksInnerPrepareSkipped;

	private long _prepareCalls;

	private long _blocksSeen;

	private long _minBlockSize = long.MaxValue;

	private long _maxBlockSize;

	private long _sumBlockSize;

	private long _pcgUpgradesToPardiso;

	private long _solveCalls;

	private long _solveFallbackCalls;

	public string SolverName => "BlockSolver";

	public bool IsDirectSolver => false;

	public bool Parallel
	{
		get
		{
			return _parallel;
		}
		set
		{
			_parallel = value;
			if (_partSolve != null)
			{
				ApplyThreadBudget(_partSolve, _partSolve.BlockCount);
			}
		}
	}

	public int NumCores
	{
		get
		{
			return _numCores;
		}
		set
		{
			_numCores = value;
			_parallelOptions.MaxDegreeOfParallelism = ResolveThreadBudget(value);
			if (_partSolve != null)
			{
				ApplyThreadBudget(_partSolve, _partSolve.BlockCount);
			}
		}
	}

	public int InnerSolverCount => _solverByRegionId.Count;

	public BlockMatrixSolver(SolverRequest request)
	{
		_options = request?.Options ?? new SolverOptionsBundle();
		_allowPardiso = (object)request != null && request.Policy?.Primary == SolverKind.Pardiso;
		if (EnableProfiling)
		{
			lock (_liveInstances)
			{
				_liveInstances.Add(this);
			}
		}
	}

	public static void DumpAndResetAllProfiles()
	{
		lock (_liveInstances)
		{
			foreach (BlockMatrixSolver liveInstance in _liveInstances)
			{
				liveInstance.DumpProfileAndReset();
			}
		}
	}

	public static void ClearLiveInstances()
	{
		lock (_liveInstances)
		{
			_liveInstances.Clear();
		}
	}

	public OperationResult Configure(object options)
	{
		return OperationResult.Success();
	}

	public void SetThreadBudget(int numThreads)
	{
		NumCores = numThreads;
	}

	public OperationResult Prepare(SparseMatrix<double> A, IPreconditioner<double> precond = null, PartialSolve partSolve = null)
	{
		if (partSolve == null || partSolve.BlockCount <= 0)
		{
			return OperationResult.Fail(MatrixSolverMessageCodes.GetSolverNotPrepared(SolverName));
		}
		long timestamp2 = (EnableProfiling ? Stopwatch.GetTimestamp() : 0);
		_partSolve = partSolve;
		_activeRegionIds.Clear();
		_prepareBlockOrder.Clear();
		_globalMaxRows = A.MaxRows;
		_globalMaxNonZero = A.MaxNonZero;
		int blockCount = partSolve.BlockCount;
		if (EnableProfiling)
		{
			Interlocked.Increment(ref _prepareCalls);
			for (int i = 0; i < blockCount; i++)
			{
				int blockSize = partSolve.BlockSize(i);
				Interlocked.Increment(ref _blocksSeen);
				Interlocked.Add(ref _sumBlockSize, blockSize);
				if (blockSize < _minBlockSize)
				{
					_minBlockSize = blockSize;
				}
				if (blockSize > _maxBlockSize)
				{
					_maxBlockSize = blockSize;
				}
			}
		}
		for (int j = 0; j < blockCount; j++)
		{
			int regionIdIndex = partSolve.RegionIds[j];
			int currentSize = partSolve.BlockSize(j);
			SolverKind solverKind = ChooseKind(currentSize);
			_activeRegionIds.Add(regionIdIndex);
			_solverByRegionId.TryGetValue(regionIdIndex, out var localValue);
			if (localValue != null && localValue.UpgradedToPardiso && _allowPardiso)
			{
				solverKind = SolverKind.Pardiso;
			}
			InnerSlot innerSlot = localValue;
			if (innerSlot == null || innerSlot.Kind != solverKind)
			{
				bool upgradedToPardiso = innerSlot?.UpgradedToPardiso ?? false;
				if (innerSlot != null)
				{
					DisposeSlot(innerSlot);
				}
				innerSlot = CreateSlot(solverKind);
				innerSlot.UpgradedToPardiso = upgradedToPardiso;
				_solverByRegionId[regionIdIndex] = innerSlot;
			}
			EnsureSlotCapacity(innerSlot, currentSize);
			innerSlot.CurrentSize = currentSize;
			_prepareBlockOrder.Add(j);
		}
		PruneStaleSlots();
		ApplyThreadBudget(partSolve, blockCount);
		OperationResult firstFailure = OperationResult.Success();
		object failLock = new object();
		if (ShouldRunBlocksInParallel(blockCount))
		{
			System.Threading.Tasks.Parallel.For(0, blockCount, _parallelOptions, PrepareOne);
		}
		else
		{
			for (int k = 0; k < blockCount; k++)
			{
				PrepareOne(k);
			}
		}
		if (EnableProfiling)
		{
			Interlocked.Add(ref _ticksPrepareTotal, Stopwatch.GetTimestamp() - timestamp2);
		}
		return firstFailure;
		void PrepareOne(int r)
		{
			int key = partSolve.RegionIds[r];
			if (_solverByRegionId.TryGetValue(key, out var localValue2))
			{
				long timestamp3 = (EnableProfiling ? Stopwatch.GetTimestamp() : 0);
				partSolve.ExtractBlock(A, r, ref localValue2.BlockMatrix, ref localValue2.PositionMap);
				long timestamp4 = (EnableProfiling ? Stopwatch.GetTimestamp() : 0);
				int nonZeroCount = localValue2.BlockMatrix.NonZeroCount;
				if (localValue2.HasPreparedOnce && localValue2.PrevPositionMap != null && localValue2.PrevPositionMapLength == nonZeroCount && ((ReadOnlySpan<int>)localValue2.PositionMap.AsSpan(0, nonZeroCount)).SequenceEqual((ReadOnlySpan<int>)localValue2.PrevPositionMap.AsSpan(0, nonZeroCount)))
				{
					partSolve.MapBlockData(A, localValue2.BlockMatrix, localValue2.PositionMap);
					if (EnableProfiling)
					{
						long timestamp = Stopwatch.GetTimestamp();
						Interlocked.Add(ref _ticksExtractBlock, timestamp4 - timestamp3);
						int kind = (int)localValue2.Kind;
						if ((uint)kind < (uint)_innerPrepareSkippedByKind.Length)
						{
							Interlocked.Increment(ref _innerPrepareSkippedByKind[kind]);
						}
						Interlocked.Add(ref _ticksInnerPrepareSkipped, timestamp - timestamp4);
					}
				}
				else
				{
					OperationResult operationResult = localValue2.Solver.Prepare(localValue2.BlockMatrix, localValue2.Precond);
					long timestamp5 = (EnableProfiling ? Stopwatch.GetTimestamp() : 0);
					if (EnableProfiling)
					{
						Interlocked.Add(ref _ticksExtractBlock, timestamp4 - timestamp3);
						Interlocked.Add(ref _ticksInnerPrepare, timestamp5 - timestamp4);
						int kind2 = (int)localValue2.Kind;
						if ((uint)kind2 < (uint)_innerPrepareTicksByKind.Length)
						{
							Interlocked.Add(ref _innerPrepareTicksByKind[kind2], timestamp5 - timestamp4);
							Interlocked.Increment(ref _innerPrepareCountByKind[kind2]);
						}
					}
					if (!operationResult.Result)
					{
						lock (failLock)
						{
							if (firstFailure.Result)
							{
								firstFailure = operationResult;
							}
							return;
						}
					}
					BufferCapacity.EnsureArrayCapacity(ref localValue2.PrevPositionMap, nonZeroCount, _globalMaxNonZero);
					Array.Copy(localValue2.PositionMap, localValue2.PrevPositionMap, nonZeroCount);
					localValue2.PrevPositionMapLength = nonZeroCount;
					localValue2.HasPreparedOnce = true;
				}
			}
		}
	}

	public OperationResult PrepareAndSolve(SparseMatrix<double> A, double[] x, double[] b, IPreconditioner<double> precond = null, PartialSolve partSolve = null)
	{
		OperationResult operationResult = Prepare(A, precond, partSolve);
		if (!operationResult.Result)
		{
			return operationResult;
		}
		return Solve(A, x, b);
	}

	public OperationResult Solve(SparseMatrix<double> A, double[] x, double[] b)
	{
		if (_partSolve == null || _partSolve.BlockCount <= 0)
		{
			return OperationResult.Fail(MatrixSolverMessageCodes.GetSolverNotPrepared(SolverName));
		}
		long timestamp = (EnableProfiling ? Stopwatch.GetTimestamp() : 0);
		if (EnableProfiling)
		{
			Interlocked.Increment(ref _solveCalls);
		}
		int blockCount = _partSolve.BlockCount;
		OperationResult firstFailure = OperationResult.Success();
		object failLock = new object();
		bool[] needsPardisoFallback = null;
		if (ShouldRunBlocksInParallel(blockCount))
		{
			System.Threading.Tasks.Parallel.For(0, blockCount, _parallelOptions, SolveOne);
		}
		else
		{
			for (int i = 0; i < blockCount; i++)
			{
				SolveOne(i);
			}
		}
		if (needsPardisoFallback != null)
		{
			if (EnableProfiling)
			{
				Interlocked.Increment(ref _solveFallbackCalls);
			}
			for (int j = 0; j < blockCount; j++)
			{
				if (!needsPardisoFallback[j])
				{
					continue;
				}
				int key = _partSolve.RegionIds[j];
				if (!_solverByRegionId.TryGetValue(key, out var solverByRegionId))
				{
					continue;
				}
				int currentSize = solverByRegionId.CurrentSize;
				DisposeSlot(solverByRegionId);
				solverByRegionId = CreateSlot(SolverKind.Pardiso);
				solverByRegionId.UpgradedToPardiso = true;
				EnsureSlotCapacity(solverByRegionId, currentSize);
				solverByRegionId.CurrentSize = currentSize;
				solverByRegionId.Solver.SetThreadBudget(ResolveThreadBudget(NumCores));
				_solverByRegionId[key] = solverByRegionId;
				if (EnableProfiling)
				{
					Interlocked.Increment(ref _pcgUpgradesToPardiso);
				}
				_partSolve.ExtractBlock(A, j, ref solverByRegionId.BlockMatrix, ref solverByRegionId.PositionMap);
				OperationResult operationResult = solverByRegionId.Solver.Prepare(solverByRegionId.BlockMatrix, solverByRegionId.Precond);
				if (!operationResult.Result)
				{
					if (firstFailure.Result)
					{
						firstFailure = operationResult;
					}
					continue;
				}
				_partSolve.GatherBlock(j, x, b, solverByRegionId.xBlock, solverByRegionId.bBlock);
				OperationResult operationResult2 = solverByRegionId.Solver.Solve(solverByRegionId.BlockMatrix, solverByRegionId.xBlock, solverByRegionId.bBlock);
				if (!operationResult2.Result)
				{
					if (firstFailure.Result)
					{
						firstFailure = operationResult2;
					}
				}
				else
				{
					_partSolve.ScatterBlock(j, solverByRegionId.xBlock, x);
				}
			}
		}
		if (EnableProfiling)
		{
			Interlocked.Add(ref _ticksSolveTotal, Stopwatch.GetTimestamp() - timestamp);
		}
		return firstFailure;
		void SolveOne(int r)
		{
			int key2 = _partSolve.RegionIds[r];
			if (!_solverByRegionId.TryGetValue(key2, out var localValue))
			{
				lock (failLock)
				{
					if (firstFailure.Result)
					{
						firstFailure = OperationResult.Fail(MatrixSolverMessageCodes.GetSolverNotPrepared(SolverName));
					}
					return;
				}
			}
			long timestamp2 = (EnableProfiling ? Stopwatch.GetTimestamp() : 0);
			_partSolve.MapBlockData(A, localValue.BlockMatrix, localValue.PositionMap);
			long timestamp3 = (EnableProfiling ? Stopwatch.GetTimestamp() : 0);
			_partSolve.GatherBlock(r, x, b, localValue.xBlock, localValue.bBlock);
			long timestamp4 = (EnableProfiling ? Stopwatch.GetTimestamp() : 0);
			OperationResult operationResult3 = localValue.Solver.Solve(localValue.BlockMatrix, localValue.xBlock, localValue.bBlock);
			long timestamp5 = (EnableProfiling ? Stopwatch.GetTimestamp() : 0);
			if (EnableProfiling)
			{
				Interlocked.Add(ref _ticksMapBlockData, timestamp3 - timestamp2);
				Interlocked.Add(ref _ticksGatherBlock, timestamp4 - timestamp3);
				Interlocked.Add(ref _ticksInnerSolve, timestamp5 - timestamp4);
				int kind = (int)localValue.Kind;
				if ((uint)kind < (uint)_innerSolveTicksByKind.Length)
				{
					Interlocked.Add(ref _innerSolveTicksByKind[kind], timestamp5 - timestamp4);
					Interlocked.Increment(ref _innerSolveCountByKind[kind]);
				}
			}
			if (!operationResult3.Result)
			{
				if (localValue.Kind == SolverKind.PcgManaged && _allowPardiso)
				{
					lock (failLock)
					{
						if (needsPardisoFallback == null)
						{
							needsPardisoFallback = new bool[blockCount];
						}
						needsPardisoFallback[r] = true;
						return;
					}
				}
				lock (failLock)
				{
					if (firstFailure.Result)
					{
						firstFailure = operationResult3;
					}
					return;
				}
			}
			_partSolve.ScatterBlock(r, localValue.xBlock, x);
			if (EnableProfiling)
			{
				Interlocked.Add(ref _ticksScatterBlock, Stopwatch.GetTimestamp() - timestamp5);
			}
		}
	}

	public OperationResult Update(SparseMatrix<double> A)
	{
		if (_partSolve == null)
		{
			return OperationResult.Success();
		}
		int blockCount = _partSolve.BlockCount;
		if (ShouldRunBlocksInParallel(blockCount))
		{
			System.Threading.Tasks.Parallel.For(0, blockCount, _parallelOptions, UpdateOne);
		}
		else
		{
			for (int i = 0; i < blockCount; i++)
			{
				UpdateOne(i);
			}
		}
		return OperationResult.Success();
		void UpdateOne(int r)
		{
			int key = _partSolve.RegionIds[r];
			if (_solverByRegionId.TryGetValue(key, out var localValue))
			{
				_partSolve.MapBlockData(A, localValue.BlockMatrix, localValue.PositionMap);
				localValue.Solver.Update(localValue.BlockMatrix);
			}
		}
	}

	public OperationResult Solve(double[] x, double[] b)
	{
		if (_partSolve == null || _partSolve.BlockCount <= 0)
		{
			return OperationResult.Fail(MatrixSolverMessageCodes.GetSolverNotPrepared(SolverName));
		}
		int blockCount = _partSolve.BlockCount;
		OperationResult firstFailure = OperationResult.Success();
		object failLock = new object();
		if (ShouldRunBlocksInParallel(blockCount))
		{
			System.Threading.Tasks.Parallel.For(0, blockCount, _parallelOptions, SolveOne);
		}
		else
		{
			for (int i = 0; i < blockCount; i++)
			{
				SolveOne(i);
			}
		}
		return firstFailure;
		void SolveOne(int r)
		{
			int key = _partSolve.RegionIds[r];
			if (!_solverByRegionId.TryGetValue(key, out var localValue))
			{
				lock (failLock)
				{
					if (firstFailure.Result)
					{
						firstFailure = OperationResult.Fail(MatrixSolverMessageCodes.GetSolverNotPrepared(SolverName));
					}
					return;
				}
			}
			_partSolve.GatherBlock(r, x, b, localValue.xBlock, localValue.bBlock);
			OperationResult operationResult = localValue.Solver.Solve(localValue.xBlock, localValue.bBlock);
			if (!operationResult.Result)
			{
				lock (failLock)
				{
					if (firstFailure.Result)
					{
						firstFailure = operationResult;
					}
					return;
				}
			}
			_partSolve.ScatterBlock(r, localValue.xBlock, x);
		}
	}

	public OperationResult Destroy(SparseMatrix<double> A)
	{
		if (EnableProfiling)
		{
			DumpProfile();
		}
		foreach (InnerSlot innerSlot in _solverByRegionId.Values)
		{
			DisposeSlot(innerSlot);
		}
		_solverByRegionId.Clear();
		_activeRegionIds.Clear();
		_partSolve = null;
		return OperationResult.Success();
	}

	public void DumpProfileAndReset()
	{
		DumpProfile();
		_ticksPrepareTotal = (_ticksExtractBlock = (_ticksInnerPrepare = 0L));
		_ticksSolveTotal = (_ticksMapBlockData = (_ticksGatherBlock = (_ticksInnerSolve = (_ticksScatterBlock = 0L))));
		_ticksInnerPrepareSkipped = 0L;
		Array.Clear(_innerSolveTicksByKind);
		Array.Clear(_innerSolveCountByKind);
		Array.Clear(_innerPrepareTicksByKind);
		Array.Clear(_innerPrepareCountByKind);
		Array.Clear(_innerPrepareSkippedByKind);
		_prepareCalls = (_blocksSeen = (_sumBlockSize = 0L));
		_maxBlockSize = 0L;
		_minBlockSize = long.MaxValue;
		_pcgUpgradesToPardiso = 0L;
		_solveCalls = (_solveFallbackCalls = 0L);
	}

	private void DumpProfile()
	{
		if (_prepareCalls == 0L && _solveCalls == 0L)
		{
			return;
		}
		long minBlockSize = ((_minBlockSize == long.MaxValue) ? 0 : _minBlockSize);
		double sumBlockSizeOverBlocksSeen = ((_blocksSeen > 0) ? ((double)_sumBlockSize / (double)_blocksSeen) : 0.0);
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("===== BlockMatrixSolver profile =====");
		long innerPrepareSkippedByKindSum = 0L;
		for (int i = 0; i < _innerPrepareSkippedByKind.Length; i++)
		{
			innerPrepareSkippedByKindSum += _innerPrepareSkippedByKind[i];
		}
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(31, 2, stringBuilder2);
		handler.AppendLiteral("  Prepare calls : ");
		handler.AppendFormatted(_prepareCalls, 10);
		handler.AppendLiteral("    total ");
		handler.AppendFormatted(ToMs(_ticksPrepareTotal), 10, "F1");
		handler.AppendLiteral(" ms");
		stringBuilder3.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder4 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(28, 1, stringBuilder2);
		handler.AppendLiteral("    ExtractBlock         ");
		handler.AppendFormatted(ToMs(_ticksExtractBlock), 10, "F1");
		handler.AppendLiteral(" ms");
		stringBuilder4.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder5 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(28, 1, stringBuilder2);
		handler.AppendLiteral("    inner.Prepare        ");
		handler.AppendFormatted(ToMs(_ticksInnerPrepare), 10, "F1");
		handler.AppendLiteral(" ms");
		stringBuilder5.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder6 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(72, 2, stringBuilder2);
		handler.AppendLiteral("    inner.Prepare SKIPPED via PositionMap cache: ");
		handler.AppendFormatted(innerPrepareSkippedByKindSum, 6);
		handler.AppendLiteral("  (");
		handler.AppendFormatted(ToMs(_ticksInnerPrepareSkipped), 8, "F1");
		handler.AppendLiteral(" ms in MapBlockData)");
		stringBuilder6.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder7 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(54, 3, stringBuilder2);
		handler.AppendLiteral("  Solve calls   : ");
		handler.AppendFormatted(_solveCalls, 10);
		handler.AppendLiteral("    total ");
		handler.AppendFormatted(ToMs(_ticksSolveTotal), 10, "F1");
		handler.AppendLiteral(" ms    (fallback passes: ");
		handler.AppendFormatted(_solveFallbackCalls);
		handler.AppendLiteral(")");
		stringBuilder7.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder8 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(28, 1, stringBuilder2);
		handler.AppendLiteral("    MapBlockData         ");
		handler.AppendFormatted(ToMs(_ticksMapBlockData), 10, "F1");
		handler.AppendLiteral(" ms");
		stringBuilder8.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder9 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(28, 1, stringBuilder2);
		handler.AppendLiteral("    GatherBlock          ");
		handler.AppendFormatted(ToMs(_ticksGatherBlock), 10, "F1");
		handler.AppendLiteral(" ms");
		stringBuilder9.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder10 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(28, 1, stringBuilder2);
		handler.AppendLiteral("    inner.Solve          ");
		handler.AppendFormatted(ToMs(_ticksInnerSolve), 10, "F1");
		handler.AppendLiteral(" ms");
		stringBuilder10.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder11 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(28, 1, stringBuilder2);
		handler.AppendLiteral("    ScatterBlock         ");
		handler.AppendFormatted(ToMs(_ticksScatterBlock), 10, "F1");
		handler.AppendLiteral(" ms");
		stringBuilder11.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder12 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(40, 4, stringBuilder2);
		handler.AppendLiteral("  Blocks seen   : ");
		handler.AppendFormatted(_blocksSeen, 10);
		handler.AppendLiteral("    min ");
		handler.AppendFormatted(minBlockSize);
		handler.AppendLiteral("   avg ");
		handler.AppendFormatted(sumBlockSizeOverBlocksSeen, "F1");
		handler.AppendLiteral("   max ");
		handler.AppendFormatted(_maxBlockSize);
		stringBuilder12.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder13 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(28, 1, stringBuilder2);
		handler.AppendLiteral("  PCG -> Pardiso upgrades : ");
		handler.AppendFormatted(_pcgUpgradesToPardiso);
		stringBuilder13.AppendLine(ref handler);
		stringBuilder.AppendLine("  Per-kind inner timings:");
		string[] names = Enum.GetNames(typeof(SolverKind));
		int[] getValues = (int[])Enum.GetValues(typeof(SolverKind));
		for (int j = 0; j < getValues.Length; j++)
		{
			int getValue = getValues[j];
			if (getValue >= 0 && getValue < _innerSolveTicksByKind.Length)
			{
				long innerPrepareCountByKind = _innerPrepareCountByKind[getValue];
				long innerSolveCountByKind = _innerSolveCountByKind[getValue];
				long innerPrepareSkippedByKind = _innerPrepareSkippedByKind[getValue];
				if (innerPrepareCountByKind != 0L || innerSolveCountByKind != 0L || innerPrepareSkippedByKind != 0L)
				{
					stringBuilder2 = stringBuilder;
					StringBuilder stringBuilder14 = stringBuilder2;
					handler = new StringBuilder.AppendInterpolatedStringHandler(55, 6, stringBuilder2);
					handler.AppendLiteral("    ");
					handler.AppendFormatted<string>(names[j], -12);
					handler.AppendLiteral("  prep ");
					handler.AppendFormatted(innerPrepareCountByKind, 8);
					handler.AppendLiteral(" calls / ");
					handler.AppendFormatted(ToMs(_innerPrepareTicksByKind[getValue]), 9, "F1");
					handler.AppendLiteral(" ms   skipped ");
					handler.AppendFormatted(innerPrepareSkippedByKind, 8);
					handler.AppendLiteral("   solve ");
					handler.AppendFormatted(innerSolveCountByKind, 8);
					handler.AppendLiteral(" calls / ");
					handler.AppendFormatted(ToMs(_innerSolveTicksByKind[getValue]), 9, "F1");
					handler.AppendLiteral(" ms");
					stringBuilder14.AppendLine(ref handler);
				}
			}
		}
		Console.WriteLine(stringBuilder.ToString());
		static double ToMs(long ticks)
		{
			return (double)ticks * 1000.0 / (double)Stopwatch.Frequency;
		}
	}

	private void ApplyThreadBudget(PartialSolve partSolve, int blockCount)
	{
		int resolveThreadBudget = ResolveThreadBudget(NumCores);
		if (!Parallel)
		{
			foreach (InnerSlot innerSlot in _solverByRegionId.Values)
			{
				innerSlot.Solver.SetThreadBudget(resolveThreadBudget);
			}
			return;
		}
		EnsureApportionScratch(blockCount);
		int count = 0;
		for (int i = 0; i < blockCount; i++)
		{
			int key = partSolve.RegionIds[i];
			if (_solverByRegionId.TryGetValue(key, out var apportionSlots))
			{
				_apportionSizes[count] = Math.Max(apportionSlots.CurrentSize, 1);
				_apportionSlots[count] = apportionSlots;
				count++;
			}
		}
		if (count != 0)
		{
			ApportionThreads(_apportionSizes.AsSpan(0, count), resolveThreadBudget, _apportionResult.AsSpan(0, count));
			for (int j = 0; j < count; j++)
			{
				_apportionSlots[j].Solver.SetThreadBudget(_apportionResult[j]);
			}
		}
	}

	private void EnsureApportionScratch(int count)
	{
		if (_apportionSizes.Length < count)
		{
			_apportionSizes = new int[count];
			_apportionResult = new int[count];
			_apportionSlots = new InnerSlot[count];
		}
	}

	private static int ResolveThreadBudget(int numThreads)
	{
		if (numThreads <= 0)
		{
			return Environment.ProcessorCount;
		}
		return numThreads;
	}

	private bool ShouldRunBlocksInParallel(int blockCount)
	{
		if (Parallel && blockCount > 1)
		{
			return _parallelOptions.MaxDegreeOfParallelism > 1;
		}
		return false;
	}

	public static void ApportionThreads(ReadOnlySpan<int> sizes, int budget, Span<int> result)
	{
		int length = sizes.Length;
		if (length == 0)
		{
			return;
		}
		if (budget < length)
		{
			for (int i = 0; i < length; i++)
			{
				result[i] = 1;
			}
			return;
		}
		long sizeSum = 0L;
		for (int j = 0; j < length; j++)
		{
			sizeSum += ((sizes[j] <= 0) ? 1 : sizes[j]);
		}
		int budgetAboveLength = budget - length;
		int ordinal = 0;
		for (int k = 0; k < length; k++)
		{
			int sizesSizeSumTerm = (int)((long)budgetAboveLength * (long)((sizes[k] <= 0) ? 1 : sizes[k]) / sizeSum);
			result[k] = 1 + sizesSizeSumTerm;
			ordinal += sizesSizeSumTerm;
		}
		int count = budgetAboveLength - ordinal;
		if (count <= 0)
		{
			return;
		}
		for (int l = 0; l < count; l++)
		{
			int ordinal2 = -1;
			long ordinal3 = -1L;
			for (int m = 0; m < length; m++)
			{
				if (result[m] >= 0)
				{
					long sizesSizeSumTerm2 = (long)budgetAboveLength * (long)((sizes[m] <= 0) ? 1 : sizes[m]) % sizeSum;
					if (sizesSizeSumTerm2 > ordinal3)
					{
						ordinal3 = sizesSizeSumTerm2;
						ordinal2 = m;
					}
				}
			}
			if (ordinal2 < 0)
			{
				break;
			}
			result[ordinal2] = -result[ordinal2];
		}
		for (int n = 0; n < length; n++)
		{
			if (result[n] < 0)
			{
				result[n] = 1 - result[n];
			}
		}
	}

	private SolverKind ChooseKind(int blockSize)
	{
		if (blockSize <= TinyMaxSize)
		{
			return SolverKind.Gauss;
		}
		if (blockSize <= MediumMaxSize || !_allowPardiso)
		{
			return SolverKind.PcgManaged;
		}
		return SolverKind.Pardiso;
	}

	private InnerSlot CreateSlot(SolverKind kind)
	{
		SolverPolicy policy = new SolverPolicy(kind, (kind != SolverKind.Pardiso) ? null : new SolverKind[1] { SolverKind.PcgManaged });
		SolverOptionsBundle options = _options;
		if (kind == SolverKind.PcgManaged)
		{
			options = WithPerSlotPcgConvergence(_options);
		}
		IMatrixSolver<double> matrixSolver = MatrixSolverFactory.Create(new SolverRequest(policy, new LinearSystemInfo(IsSparse: true, IsSymmetric: true, IsPositiveDefinite: true, 1), options));
		IPreconditioner<double> precond = (matrixSolver.IsDirectSolver ? null : new IC<double>());
		return new InnerSlot
		{
			Solver = matrixSolver,
			Kind = kind,
			BlockMatrix = null,
			PositionMap = null,
			xBlock = null,
			bBlock = null,
			CurrentSize = 0,
			Precond = precond
		};
	}

	private void EnsureSlotCapacity(InnerSlot slot, int blockSize)
	{
		BufferCapacity.EnsureArrayCapacity(ref slot.xBlock, blockSize, _globalMaxRows);
		BufferCapacity.EnsureArrayCapacity(ref slot.bBlock, blockSize, _globalMaxRows);
		if (slot.BlockMatrix == null)
		{
			slot.BlockMatrix = new SparseMatrix<double>();
		}
		slot.BlockMatrix.EnsureCapacity(blockSize, 0, _globalMaxRows, _globalMaxNonZero);
	}

	private static SolverOptionsBundle WithPerSlotPcgConvergence(SolverOptionsBundle src)
	{
		PcgManagedOptions pcgManagedOpts = src.PcgManagedOpts;
		MatrixConvergence<double> matrixConvergence = pcgManagedOpts?.MatrixConv;
		MatrixConvergence<double> matrixConvergence2 = new MatrixConvergence<double>();
		if (matrixConvergence != null)
		{
			matrixConvergence2.ToleranceMaxMAE = matrixConvergence.ToleranceMaxMAE;
			matrixConvergence2.ToleranceMAE = matrixConvergence.ToleranceMAE;
			matrixConvergence2.ToleranceRMSE = matrixConvergence.ToleranceRMSE;
			matrixConvergence2.MinIterations = matrixConvergence.MinIterations;
			matrixConvergence2.MaxIterations = matrixConvergence.MaxIterations;
			matrixConvergence2.CheckConvergence = matrixConvergence.CheckConvergence;
			matrixConvergence2.PrecondIterations = matrixConvergence.PrecondIterations;
		}
		PcgManagedOptions pcgManagedOpts2 = ((pcgManagedOpts != null) ? pcgManagedOpts with
		{
			MatrixConv = matrixConvergence2
		} : new PcgManagedOptions(matrixConvergence2));
		return new SolverOptionsBundle
		{
			PardisoOpts = src.PardisoOpts,
			FgmresOpts = src.FgmresOpts,
			PcgNativeOpts = src.PcgNativeOpts,
			PcgManagedOpts = pcgManagedOpts2,
			SsorOpts = src.SsorOpts,
			PgmresOpts = src.PgmresOpts,
			GaussOpts = src.GaussOpts
		};
	}

	private static void DisposeSlot(InnerSlot slot)
	{
		if (slot?.Solver == null)
		{
			return;
		}
		try
		{
			slot.Solver.Destroy(slot.BlockMatrix);
		}
		catch
		{
		}
	}

	private void PruneStaleSlots()
	{
		List<int> intList = null;
		foreach (int key in _solverByRegionId.Keys)
		{
			if (!_activeRegionIds.Contains(key))
			{
				(intList ?? (intList = new List<int>())).Add(key);
			}
		}
		if (intList == null)
		{
			return;
		}
		foreach (int ordinal in intList)
		{
			if (_solverByRegionId.TryGetValue(ordinal, out var localValue))
			{
				DisposeSlot(localValue);
			}
			_solverByRegionId.Remove(ordinal);
		}
	}

	public bool TryGetInnerSolverKind(int regionId, out SolverKind kind)
	{
		if (_solverByRegionId.TryGetValue(regionId, out var localValue))
		{
			kind = localValue.Kind;
			return true;
		}
		kind = SolverKind.Pardiso;
		return false;
	}
}
