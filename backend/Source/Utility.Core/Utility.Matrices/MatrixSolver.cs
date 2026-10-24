#define TRACE
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Utility.Logging;

namespace Utility.Matrices;

public class MatrixSolver<T> where T : unmanaged, INumber<T>, IRootFunctions<T>, IFloatingPointIeee754<T>
{
	public enum ScalingType
	{
		None,
		Rows,
		Symmetric
	}

	public enum Ticker
	{
		PREPARE,
		PREPAREERRORARRAY,
		PREPAREPARTIALSOLVE,
		PREPAREREORDER,
		PREPAREUPPERTRIANGULAR,
		PREPAREPARTIALUPPER,
		PREPAREPOSITIONMAP,
		PREPARESCALING,
		PREPAREPRECONDITIONER,
		UPDATE,
		UPDATESCALING,
		PRESOLVE,
		PRESOLVEPARTIALSOLVE,
		PRESOLVEREORDER,
		PRESOLVESCALING,
		COMPUTECONVERGENCE
	}

	private const int DefaultLoopSize = 6000;

	private int _loopSize = 6000;

	public IPreconditioner<T> Preconditioner;

	public SparseMatrix<T> Matrix;

	public T[] Solution;

	public T[] RightHandSide;

	public T[] Error;

	public PartialSolve PartSolve;

	private int[] _partialPositionMap;

	private SparseMatrix<T> _partialMatrix;

	private T[] _partialSolution;

	private T[] _partialRightHandSide;

	public bool IsReorder;

	public RCM rcm;

	private SparseMatrix<T> _reordMatrix;

	private T[] _reordSolution;

	private T[] _reordRightHandSide;

	private int[] _reordPositionMap;

	public bool IsExtractUpperTriangular;

	private SparseMatrix<T> _upperMatrix;

	private int[] _upperPositionMap;

	private int[] _positionMap;

	public ScalingType Scaling;

	private SparseMatrix<T> _scaledMatrix;

	private T[] _scaledSolution;

	private T[] _scaledRightHandSide;

	private T[] _inverseDiagonal;

	private T[] _diagonal;

	public long[] ticks;

	public MatrixConvergence<T> Convergence;

	protected ParallelOptions SolverParallelOptions { get; } = new ParallelOptions();

	public bool HasPreconditioner => Preconditioner != null;

	public bool IsPartialSolve => PartSolve != null;

	private bool HasPositionMap => _positionMap != null;

	public bool IsScaled => Scaling != ScalingType.None;

	public MatrixSolver()
	{
		Convergence = new MatrixConvergence<T>();
		ticks = new long[Enum.GetValues<Ticker>().Length];
	}

	public MatrixSolver(MatrixConvergence<T> convergence)
	{
		Convergence = convergence;
		ticks = new long[Enum.GetValues<Ticker>().Length];
	}

	public virtual void SetThreadBudget(int numThreads)
	{
		SolverParallelOptions.MaxDegreeOfParallelism = ((numThreads > 0) ? numThreads : (-1));
	}

	private static int GetLoopSize(int rowCount)
	{
		return 6000;
	}

	private void EnsurePartialSolveCapacity(int requiredRows, int requiredNnz, int rowCap, int nnzCap)
	{
		if (_partialMatrix == null)
		{
			_partialMatrix = new SparseMatrix<T>();
		}
		_partialMatrix.EnsureCapacity(requiredRows, requiredNnz, rowCap, nnzCap);
		BufferCapacity.EnsureArrayCapacity(ref _partialRightHandSide, requiredRows, rowCap);
		BufferCapacity.EnsureArrayCapacity(ref _partialSolution, requiredRows, rowCap);
		BufferCapacity.EnsureArrayCapacity(ref _partialPositionMap, requiredNnz, nnzCap);
	}

	private void ComputePartialSolve(SparseMatrix<T> A)
	{
		T zero = T.Zero;
		bool[] keep = new bool[A.RowCount];
		SparseMatrix<T>.FindActive(A, keep, ignoreDiag: true, zero);
		PartSolve.Compute(keep);
	}

	private void EnsureReorderCapacity(int requiredRows, int requiredNnz, int rowCap, int nnzCap)
	{
		if (_reordMatrix == null)
		{
			_reordMatrix = new SparseMatrix<T>();
		}
		_reordMatrix.EnsureCapacity(requiredRows, requiredNnz, rowCap, nnzCap);
		BufferCapacity.EnsureArrayCapacity(ref _reordRightHandSide, requiredRows, rowCap);
		BufferCapacity.EnsureArrayCapacity(ref _reordSolution, requiredRows, rowCap);
		BufferCapacity.EnsureArrayCapacity(ref _reordPositionMap, requiredNnz, nnzCap);
		if (rcm == null)
		{
			rcm = new RCM();
		}
	}

	private void EnsureUpperCapacity(int requiredRows, int requiredNnz, int rowCap, int nnzCap)
	{
		int requiredRowsTerm = (requiredNnz - requiredRows) / 2 + requiredRows;
		if (requiredRowsTerm < requiredRows)
		{
			requiredRowsTerm = requiredRows;
		}
		if (_upperMatrix == null)
		{
			_upperMatrix = new SparseMatrix<T>();
		}
		_upperMatrix.EnsureCapacity(requiredRows, requiredRowsTerm, rowCap, nnzCap);
		BufferCapacity.EnsureArrayCapacity(ref _upperPositionMap, requiredNnz, nnzCap);
	}

	public virtual OperationResult Prepare(SparseMatrix<T> A, IPreconditioner<T> precond = null, PartialSolve partSolve = null)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		Matrix = A;
		Convergence.Prepare();
		PrepareErrorArray(A.MaxRows);
		PartSolve = partSolve;
		if (IsPartialSolve)
		{
			PreparePartialSolve(A);
		}
		if (IsReorder)
		{
			PrepareReorder(A);
		}
		if (IsExtractUpperTriangular)
		{
			PrepareUpperTriangular(A);
		}
		PreparePostionMap();
		Matrix.ComputeDiagonalPointers();
		if (IsScaled)
		{
			PrepareScaling(A);
		}
		PreparePreconditioner(precond);
		int rowCount = Matrix.RowCount;
		_loopSize = GetLoopSize(rowCount);
		ticks[0] += stopwatch.ElapsedTicks;
		return OperationResult.Success();
	}

	private void PrepareErrorArray(int maxRows)
	{
		BufferCapacity.EnsureArrayCapacity(ref Error, maxRows, maxRows);
	}

	private void PreparePartialSolve(SparseMatrix<T> A)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		EnsurePartialSolveCapacity(A.MaxRows, A.MaxNonZero, A.MaxRows, A.MaxNonZero);
		if (PartSolve.NumFull == 0 || PartSolve.FullToPart == null)
		{
			ComputePartialSolve(A);
		}
		A.ExtractSubmatrix(PartSolve.PartToFull, PartSolve.FullToPart, _partialMatrix, _partialPositionMap);
		Matrix = _partialMatrix;
		ticks[2] += stopwatch.ElapsedTicks;
	}

	private void PrepareReorder(SparseMatrix<T> A)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		EnsureReorderCapacity(A.MaxRows, A.MaxNonZero, A.MaxRows, A.MaxNonZero);
		rcm.Compute(Matrix.RowCount, Matrix.RowPointers, Matrix.ColumnIndices);
		Matrix.PermuteSymmetric(_reordMatrix, rcm.PermInverse, _reordPositionMap);
		Matrix = _reordMatrix;
		ticks[3] += stopwatch.ElapsedTicks;
	}

	private void PrepareUpperTriangular(SparseMatrix<T> A)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		EnsureUpperCapacity(A.MaxRows, A.MaxNonZero, A.MaxRows, A.MaxNonZero);
		Matrix.ExtractUpperTriangular(_upperMatrix, _upperPositionMap);
		Matrix = _upperMatrix;
		ticks[4] += stopwatch.ElapsedTicks;
	}

	private void PreparePostionMap()
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		switch ((IsPartialSolve ? 1 : 0) + (IsReorder ? 1 : 0) + (IsExtractUpperTriangular ? 1 : 0))
		{
		case 0:
			_positionMap = null;
			return;
		case 1:
			if (IsPartialSolve)
			{
				_positionMap = _partialPositionMap;
			}
			else if (IsReorder)
			{
				_positionMap = _reordPositionMap;
			}
			else
			{
				_positionMap = _upperPositionMap;
			}
			return;
		}
		int nonZeroCount = Matrix.NonZeroCount;
		int maxNonZero = Matrix.MaxNonZero;
		BufferCapacity.EnsureArrayCapacity(ref _positionMap, maxNonZero, maxNonZero);
		switch ((int)((uint)((IsPartialSolve ? 4 : 0) | (IsReorder ? 2 : 0)) | (IsExtractUpperTriangular ? 1u : 0u)))
		{
		case 3:
			PreparePostionMapReorderUpper(nonZeroCount);
			break;
		case 5:
			PreparePostionMapPartialUpper(nonZeroCount);
			break;
		case 6:
			PreparePostionMapPartialReorder(nonZeroCount);
			break;
		case 7:
			PreparePostionMapPartialReorderUpper(nonZeroCount);
			break;
		}
		ticks[6] += stopwatch.ElapsedTicks;
	}

	private void PreparePostionMapReorderUpper(int nonZeroCount)
	{
		int loopSize = _loopSize;
		int[] posMap = _positionMap;
		int[] reordMap = _reordPositionMap;
		int[] upperMap = _upperPositionMap;
		Parallel.SmartFor(nonZeroCount, (int start, int stop) =>
		{
			for (int i = start; i <= stop; i++)
			{
				posMap[i] = reordMap[upperMap[i]];
			}
		}, SolverParallelOptions, loopSize);
	}

	private void PreparePostionMapPartialUpper(int nonZeroCount)
	{
		int loopSize = _loopSize;
		int[] posMap = _positionMap;
		int[] partialMap = _partialPositionMap;
		int[] upperMap = _upperPositionMap;
		Parallel.SmartFor(nonZeroCount, (int start, int stop) =>
		{
			for (int i = start; i <= stop; i++)
			{
				posMap[i] = partialMap[upperMap[i]];
			}
		}, SolverParallelOptions, loopSize);
	}

	private void PreparePostionMapPartialReorder(int nonZeroCount)
	{
		int loopSize = _loopSize;
		int[] posMap = _positionMap;
		int[] partialMap = _partialPositionMap;
		int[] reordMap = _reordPositionMap;
		Parallel.SmartFor(nonZeroCount, (int start, int stop) =>
		{
			for (int i = start; i <= stop; i++)
			{
				posMap[i] = partialMap[reordMap[i]];
			}
		}, SolverParallelOptions, loopSize);
	}

	private void PreparePostionMapPartialReorderUpper(int nonZeroCount)
	{
		int loopSize = _loopSize;
		int[] posMap = _positionMap;
		int[] partialMap = _partialPositionMap;
		int[] reordMap = _reordPositionMap;
		int[] upperMap = _upperPositionMap;
		Parallel.SmartFor(nonZeroCount, (int start, int stop) =>
		{
			for (int i = start; i <= stop; i++)
			{
				posMap[i] = partialMap[reordMap[upperMap[i]]];
			}
		}, SolverParallelOptions, loopSize);
	}

	private void PrepareScaling(SparseMatrix<T> A)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		if (_scaledMatrix == null)
		{
			_scaledMatrix = new SparseMatrix<T>();
		}
		_scaledMatrix.CopyStructureFromSparse(Matrix);
		_scaledMatrix.EnsureValuesCapacity(Matrix.MaxNonZero, A.MaxNonZero);
		BufferCapacity.EnsureArrayCapacity(ref _scaledRightHandSide, Matrix.MaxRows, A.MaxRows);
		BufferCapacity.EnsureArrayCapacity(ref _inverseDiagonal, Matrix.MaxRows, A.MaxRows);
		if (Scaling == ScalingType.Symmetric)
		{
			BufferCapacity.EnsureArrayCapacity(ref _scaledSolution, Matrix.MaxRows, A.MaxRows);
			BufferCapacity.EnsureArrayCapacity(ref _diagonal, Matrix.MaxRows, A.MaxRows);
		}
		ticks[7] += stopwatch.ElapsedTicks;
	}

	private void PreparePreconditioner(IPreconditioner<T> precond)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		Preconditioner = precond;
		if (!HasPreconditioner)
		{
			Preconditioner = new NoPreconditioner<T>();
		}
		ticks[8] += stopwatch.ElapsedTicks;
	}

	public virtual OperationResult Update(SparseMatrix<T> A)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		if (HasPositionMap)
		{
			Matrix.MapData(A, _positionMap);
		}
		else
		{
			Matrix = A;
		}
		if (IsScaled)
		{
			UpdateScaling();
		}
		if (HasPreconditioner)
		{
			OperationResult operationResult = Preconditioner.Prepare(Matrix);
			if (!operationResult)
			{
				return operationResult;
			}
		}
		ticks[9] += stopwatch.ElapsedTicks;
		return OperationResult.Success();
	}

	private void UpdateScaling()
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		switch (Scaling)
		{
		case ScalingType.Rows:
			UpdateInverseDiagonal();
			UpdateScalingRows();
			break;
		case ScalingType.Symmetric:
			UpdateDiagonalSymmetric();
			UpdateScalingSymmetric();
			break;
		}
		ticks[10] += stopwatch.ElapsedTicks;
	}

	private void UpdateInverseDiagonal()
	{
		int rowCount = Matrix.RowCount;
		int loopSize = _loopSize;
		SparseMatrix<T> matrix = Matrix;
		if (typeof(T) == typeof(double))
		{
			UpdateInverseDiagonalDouble(matrix, rowCount, loopSize);
		}
		else if (typeof(T) == typeof(float))
		{
			UpdateInverseDiagonalFloat(matrix, rowCount, loopSize);
		}
		else
		{
			UpdateInverseDiagonalGeneric(matrix, rowCount, loopSize);
		}
	}

	private void UpdateInverseDiagonalDouble(SparseMatrix<T> matrix, int rowCount, int loopSize)
	{
		Parallel.SmartFor(rowCount, (int start, int stop) =>
		{
			Span<T> inverseDiagonal = _inverseDiagonal.AsSpan();
			for (int i = start; i <= stop; i++)
			{
				double bitCast = System.Runtime.CompilerServices.Unsafe.BitCast<T, double>(matrix.GetDataDiagonalRow(i));
				bitCast = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(bitCast) & 0x7FF0000000000000L);
				inverseDiagonal[i] = System.Runtime.CompilerServices.Unsafe.BitCast<double, T>(1.0 / bitCast);
			}
		}, SolverParallelOptions, loopSize);
	}

	private void UpdateInverseDiagonalFloat(SparseMatrix<T> matrix, int rowCount, int loopSize)
	{
		Parallel.SmartFor(rowCount, (int start, int stop) =>
		{
			Span<T> inverseDiagonal = _inverseDiagonal.AsSpan();
			for (int i = start; i <= stop; i++)
			{
				float bitCast = System.Runtime.CompilerServices.Unsafe.BitCast<T, float>(matrix.GetDataDiagonalRow(i));
				bitCast = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(bitCast) & 0x7F800000);
				inverseDiagonal[i] = System.Runtime.CompilerServices.Unsafe.BitCast<float, T>(1f / bitCast);
			}
		}, SolverParallelOptions, loopSize);
	}

	private void UpdateInverseDiagonalGeneric(SparseMatrix<T> matrix, int rowCount, int loopSize)
	{
		Parallel.SmartFor(rowCount, (int start, int stop) =>
		{
			Span<T> inverseDiagonal = _inverseDiagonal.AsSpan();
			for (int i = start; i <= stop; i++)
			{
				T oneILogBTerm = T.ScaleB(T.One, T.ILogB(matrix.GetDataDiagonalRow(i)));
				inverseDiagonal[i] = T.One / oneILogBTerm;
			}
		}, SolverParallelOptions, loopSize);
	}

	private void UpdateDiagonalSymmetric()
	{
		int rowCount = Matrix.RowCount;
		int loopSize = _loopSize;
		SparseMatrix<T> matrix = Matrix;
		if (typeof(T) == typeof(double))
		{
			UpdateDiagonalSymmetricDouble(matrix, rowCount, loopSize);
		}
		else if (typeof(T) == typeof(float))
		{
			UpdateDiagonalSymmetricFloat(matrix, rowCount, loopSize);
		}
		else
		{
			UpdateDiagonalSymmetricGeneric(matrix, rowCount, loopSize);
		}
	}

	private void UpdateDiagonalSymmetricDouble(SparseMatrix<T> matrix, int rowCount, int loopSize)
	{
		Parallel.SmartFor(rowCount, (int start, int stop) =>
		{
			Span<T> diagonal = _diagonal.AsSpan();
			Span<T> inverseDiagonal = _inverseDiagonal.AsSpan();
			for (int i = start; i <= stop; i++)
			{
				double bitCast = System.Runtime.CompilerServices.Unsafe.BitCast<T, double>(T.Sqrt(matrix.GetDataDiagonalRow(i)));
				bitCast = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(bitCast) & 0x7FF0000000000000L);
				diagonal[i] = System.Runtime.CompilerServices.Unsafe.BitCast<double, T>(bitCast);
				inverseDiagonal[i] = System.Runtime.CompilerServices.Unsafe.BitCast<double, T>(1.0 / bitCast);
			}
		}, SolverParallelOptions, loopSize);
	}

	private void UpdateDiagonalSymmetricFloat(SparseMatrix<T> matrix, int rowCount, int loopSize)
	{
		Parallel.SmartFor(rowCount, (int start, int stop) =>
		{
			Span<T> diagonal = _diagonal.AsSpan();
			Span<T> inverseDiagonal = _inverseDiagonal.AsSpan();
			for (int i = start; i <= stop; i++)
			{
				float bitCast = System.Runtime.CompilerServices.Unsafe.BitCast<T, float>(T.Sqrt(matrix.GetDataDiagonalRow(i)));
				bitCast = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(bitCast) & 0x7F800000);
				diagonal[i] = System.Runtime.CompilerServices.Unsafe.BitCast<float, T>(bitCast);
				inverseDiagonal[i] = System.Runtime.CompilerServices.Unsafe.BitCast<float, T>(1f / bitCast);
			}
		}, SolverParallelOptions, loopSize);
	}

	private void UpdateDiagonalSymmetricGeneric(SparseMatrix<T> matrix, int rowCount, int loopSize)
	{
		Parallel.SmartFor(rowCount, (int start, int stop) =>
		{
			Span<T> diagonal = _diagonal.AsSpan();
			Span<T> inverseDiagonal = _inverseDiagonal.AsSpan();
			for (int i = start; i <= stop; i++)
			{
				T diagonal2 = T.ScaleB(T.One, T.ILogB(T.Sqrt(matrix.GetDataDiagonalRow(i))));
				diagonal[i] = diagonal2;
				inverseDiagonal[i] = T.One / diagonal2;
			}
		}, SolverParallelOptions, loopSize);
	}

	private void UpdateScalingRows()
	{
		SparseMatrix<T>.ScaleRows(_scaledMatrix, Matrix, _inverseDiagonal, SolverParallelOptions);
		Matrix = _scaledMatrix;
	}

	private void UpdateScalingSymmetric()
	{
		SparseMatrix<T>.ScaleSymmetric(_scaledMatrix, Matrix, _inverseDiagonal, SolverParallelOptions);
		Matrix = _scaledMatrix;
	}

	protected OperationResult PreSolve(T[] x, T[] b)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		Solution = x;
		RightHandSide = b;
		if (IsPartialSolve)
		{
			PreSolvePartialSolve(x, b);
		}
		if (IsReorder)
		{
			PreSolveReorder();
		}
		if (IsScaled)
		{
			PreSolveScaling();
		}
		ticks[11] += stopwatch.ElapsedTicks;
		return OperationResult.Success();
	}

	private void PreSolvePartialSolve(T[] x, T[] b)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		int numPart = PartSolve.NumPart;
		int loopSize = _loopSize;
		List<int> partToFull = PartSolve.PartToFull;
		T[] partialSol = _partialSolution;
		T[] partialRhs = _partialRightHandSide;
		Parallel.SmartFor(numPart, (int start, int stop) =>
		{
			for (int i = start; i <= stop; i++)
			{
				int partToFull2 = partToFull[i];
				partialSol[i] = x[partToFull2];
				partialRhs[i] = b[partToFull2];
			}
		}, SolverParallelOptions, loopSize);
		Solution = _partialSolution;
		RightHandSide = _partialRightHandSide;
		ticks[12] += stopwatch.ElapsedTicks;
	}

	private void PreSolveReorder()
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		int rowCount = Matrix.RowCount;
		int loopSize = _loopSize;
		int[] perm = rcm.Perm;
		T[] sol = Solution;
		T[] rhs = RightHandSide;
		T[] reordSol = _reordSolution;
		T[] reordRhs = _reordRightHandSide;
		Parallel.SmartFor(rowCount, (int start, int stop) =>
		{
			for (int i = start; i <= stop; i++)
			{
				int perm2 = perm[i];
				reordSol[i] = sol[perm2];
				reordRhs[i] = rhs[perm2];
			}
		}, SolverParallelOptions, loopSize);
		Solution = _reordSolution;
		RightHandSide = _reordRightHandSide;
		ticks[13] += stopwatch.ElapsedTicks;
	}

	private void PreSolveScaling()
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		switch (Scaling)
		{
		case ScalingType.Rows:
			PreSolveScalingRows();
			break;
		case ScalingType.Symmetric:
			PreSolveScalingSymmetric();
			break;
		}
		ticks[14] += stopwatch.ElapsedTicks;
	}

	private void PreSolveScalingRows()
	{
		int rowCount = Matrix.RowCount;
		int loopSize = _loopSize;
		T[] scaledRhs = _scaledRightHandSide;
		T[] rhs = RightHandSide;
		T[] inverseDiag = _inverseDiagonal;
		Parallel.SmartFor(rowCount, (int start, int stop) =>
		{
			for (int i = start; i <= stop; i++)
			{
				scaledRhs[i] = rhs[i] * inverseDiag[i];
			}
		}, SolverParallelOptions, loopSize);
		RightHandSide = _scaledRightHandSide;
	}

	private void PreSolveScalingSymmetric()
	{
		int rowCount = Matrix.RowCount;
		int loopSize = _loopSize;
		T[] scaledSol = _scaledSolution;
		T[] scaledRhs = _scaledRightHandSide;
		T[] sol = Solution;
		T[] rhs = RightHandSide;
		T[] diag = _diagonal;
		T[] inverseDiag = _inverseDiagonal;
		Parallel.SmartFor(rowCount, (int start, int stop) =>
		{
			for (int i = start; i <= stop; i++)
			{
				scaledSol[i] = sol[i] * diag[i];
				scaledRhs[i] = rhs[i] * inverseDiag[i];
			}
		}, SolverParallelOptions, loopSize);
		Solution = _scaledSolution;
		RightHandSide = _scaledRightHandSide;
	}

	protected OperationResult PostSolve(T[] x)
	{
		int rowCount = Matrix.RowCount;
		bool scalingSymmetricTerm = Scaling == ScalingType.Symmetric;
		if (!IsPartialSolve && !IsReorder && !scalingSymmetricTerm)
		{
			return OperationResult.Success();
		}
		switch ((int)((uint)((IsPartialSolve ? 4 : 0) | (IsReorder ? 2 : 0)) | (scalingSymmetricTerm ? 1u : 0u)))
		{
		case 1:
			PostSolveScalingOnly(x, rowCount);
			break;
		case 2:
			PostSolveReorderOnly(x, rowCount);
			break;
		case 3:
			PostSolveReorderScaling(x, rowCount);
			break;
		case 4:
			PostSolvePartialOnly(x, rowCount);
			break;
		case 5:
			PostSolvePartialScaling(x, rowCount);
			break;
		case 6:
			PostSolvePartialReorder(x, rowCount);
			break;
		case 7:
			PostSolvePartialReorderScaling(x, rowCount);
			break;
		}
		return OperationResult.Success();
	}

	private void PostSolveScalingOnly(T[] x, int rowCount)
	{
		int loopSize = _loopSize;
		T[] xLocal = Solution;
		T[] inverseDiag = _inverseDiagonal;
		Parallel.SmartFor(rowCount, (int start, int stop) =>
		{
			for (int i = start; i <= stop; i++)
			{
				x[i] = inverseDiag[i] * xLocal[i];
			}
		}, SolverParallelOptions, loopSize);
	}

	private void PostSolveReorderOnly(T[] x, int rowCount)
	{
		int loopSize = _loopSize;
		int[] permInverse = rcm.PermInverse;
		T[] xLocal = Solution;
		Parallel.SmartFor(rowCount, (int start, int stop) =>
		{
			for (int i = start; i <= stop; i++)
			{
				x[i] = xLocal[permInverse[i]];
			}
		}, SolverParallelOptions, loopSize);
	}

	private void PostSolveReorderScaling(T[] x, int rowCount)
	{
		int loopSize = _loopSize;
		int[] permInverse = rcm.PermInverse;
		T[] xLocal = Solution;
		T[] inverseDiag = _inverseDiagonal;
		Parallel.SmartFor(rowCount, (int start, int stop) =>
		{
			for (int i = start; i <= stop; i++)
			{
				int permInverse2 = permInverse[i];
				x[i] = inverseDiag[permInverse2] * xLocal[permInverse2];
			}
		}, SolverParallelOptions, loopSize);
	}

	private void PostSolvePartialOnly(T[] x, int rowCount)
	{
		int loopSize = _loopSize;
		List<int> partToFull = PartSolve.PartToFull;
		T[] xLocal = Solution;
		Parallel.SmartFor(rowCount, (int start, int stop) =>
		{
			for (int i = start; i <= stop; i++)
			{
				x[partToFull[i]] = xLocal[i];
			}
		}, SolverParallelOptions, loopSize);
	}

	private void PostSolvePartialScaling(T[] x, int rowCount)
	{
		int loopSize = _loopSize;
		List<int> partToFull = PartSolve.PartToFull;
		T[] xLocal = Solution;
		T[] inverseDiag = _inverseDiagonal;
		Parallel.SmartFor(rowCount, (int start, int stop) =>
		{
			for (int i = start; i <= stop; i++)
			{
				x[partToFull[i]] = inverseDiag[i] * xLocal[i];
			}
		}, SolverParallelOptions, loopSize);
	}

	private void PostSolvePartialReorder(T[] x, int rowCount)
	{
		int loopSize = _loopSize;
		int[] permInverse = rcm.PermInverse;
		List<int> partToFull = PartSolve.PartToFull;
		T[] xLocal = Solution;
		T[] partialSol = _partialSolution;
		Parallel.SmartFor(rowCount, (int start, int stop) =>
		{
			for (int i = start; i <= stop; i++)
			{
				T xLocal2 = xLocal[permInverse[i]];
				partialSol[i] = xLocal2;
				x[partToFull[i]] = xLocal2;
			}
		}, SolverParallelOptions, loopSize);
	}

	private void PostSolvePartialReorderScaling(T[] x, int rowCount)
	{
		int loopSize = _loopSize;
		int[] permInverse = rcm.PermInverse;
		List<int> partToFull = PartSolve.PartToFull;
		T[] xLocal = Solution;
		T[] inverseDiag = _inverseDiagonal;
		T[] partialSol = _partialSolution;
		Parallel.SmartFor(rowCount, (int start, int stop) =>
		{
			for (int i = start; i <= stop; i++)
			{
				int permInverse2 = permInverse[i];
				T partialSol2 = inverseDiag[permInverse2] * xLocal[permInverse2];
				partialSol[i] = partialSol2;
				x[partToFull[i]] = partialSol2;
			}
		}, SolverParallelOptions, loopSize);
	}

	public bool ComputeConvergence(T[] res)
	{
		if (!Convergence.CheckConvergence)
		{
			return false;
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		ComputeError(res);
		bool result = Convergence.Compute(Error, Matrix.RowCount);
		ticks[15] += stopwatch.ElapsedTicks;
		return result;
	}

	protected static OperationResult ValidateMatrix(SparseMatrix<T> A, string solverName)
	{
		if (A == null)
		{
			return OperationResult.Fail(MatrixSolverMessageCodes.GetNullMatrix(solverName));
		}
		if (A.RowCount <= 0 || A.NonZeroCount < A.RowCount)
		{
			return OperationResult.Fail(MatrixSolverMessageCodes.GetInvalidMatrixDimension(solverName, A.RowCount, A.NonZeroCount));
		}
		return OperationResult.Success();
	}

	protected static OperationResult ValidateArray(T[] values, string arrayName, int requiredLength, string solverName)
	{
		if (values == null)
		{
			return OperationResult.Fail(MatrixSolverMessageCodes.GetNullArray(solverName, arrayName));
		}
		if (values.Length < requiredLength)
		{
			return OperationResult.Fail(MatrixSolverMessageCodes.GetArrayLengthMismatch(solverName, arrayName, values.Length, requiredLength));
		}
		return OperationResult.Success();
	}

	protected OperationResult ValidatePrepared(string solverName)
	{
		if (Matrix == null)
		{
			return OperationResult.Fail(MatrixSolverMessageCodes.GetSolverNotPrepared(solverName));
		}
		return OperationResult.Success();
	}

	public void ComputeError(T[] res)
	{
		if (Scaling == ScalingType.Symmetric)
		{
			int rowCount = Matrix.RowCount;
			int loopSize = _loopSize;
			T[] error = Error;
			T[] inverseDiag = _inverseDiagonal;
			Parallel.SmartFor(rowCount, (int start, int stop) =>
			{
				for (int i = start; i <= stop; i++)
				{
					error[i] = inverseDiag[i] * res[i];
				}
			}, SolverParallelOptions, loopSize);
		}
		else
		{
			Error = res;
		}
	}

	public void WriteTicker()
	{
		Ticker[] values = Enum.GetValues<Ticker>();
		foreach (Ticker ticker in values)
		{
			int ticker2 = (int)ticker;
			if (ticker2 < ticks.Length)
			{
				long ticksTicker2Term = ticks[ticker2] / 10000;
				if (ticksTicker2Term > 0)
				{
					Trace.WriteLine($"{ticker}   " + ticksTicker2Term);
				}
				continue;
			}
			break;
		}
	}

	public static StringBuilder SerializeToCode(SparseMatrix<T> A, T[] x, T[] b)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendFormat("SparseMatrix<{0}> A;\n", typeof(T));
		stringBuilder.Append("{\n  var rowPtr = ");
		SerializeToCodeExpression(stringBuilder, A.RowPointers, "  ");
		stringBuilder.Append(";\n");
		stringBuilder.Append("  var colInd = ");
		SerializeToCodeExpression(stringBuilder, A.ColumnIndices, "  ");
		stringBuilder.Append(";\n");
		stringBuilder.Append("  var values = ");
		SerializeToCodeExpression(stringBuilder, A.Values, "  ");
		stringBuilder.Append(";\n");
		stringBuilder.AppendFormat("  A = new SparseMatrix<{0}>( {1}, rowPtr, colInd, values);", typeof(T), A.RowCount);
		stringBuilder.Append("\n}\n");
		stringBuilder.Append("var x = ");
		SerializeToCodeExpression(stringBuilder, x);
		stringBuilder.Append(";\n");
		stringBuilder.Append("var b = ");
		SerializeToCodeExpression(stringBuilder, b);
		stringBuilder.Append(";\n");
		return stringBuilder;
	}

	public static void SerializeToDebug(SparseMatrix<T> A, T[] x, T[] b)
	{
		SerializeToCode(A, x, b);
	}

	public static void SerializeToCodeExpression<T2>(StringBuilder sb, T2[] vec, string indent = "")
	{
		sb.AppendFormat("new {0}[]{{", typeof(T2));
		int ordinal = 20;
		for (int i = 0; i < vec.Length; i++)
		{
			if (i % ordinal == 0)
			{
				sb.Append("\n" + indent + "  ");
			}
			sb.Append(vec[i]);
			sb.Append(",");
		}
		sb.Append("\n" + indent + "}");
	}
}
