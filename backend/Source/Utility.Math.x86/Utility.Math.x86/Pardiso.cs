using System.Runtime.InteropServices;
using Utility.Logging;
using Utility.Matrices;

namespace Utility.Math.x86;

public class Pardiso : MatrixSolver<double>, IMatrixSolver<double>
{
	public enum PardisoError
	{
		None = 0,
		InputInconsistent = -1,
		NotEnoughMemory = -2,
		ReorderingProblem = -3,
		ZeroPivot = -4,
		UnclassifiedInternalError = -5,
		ReorderingFail = -6,
		SingularDiagonalMatrix = -7,
		Overflow = -8,
		NotEnoughMemoryOOC = -9,
		FileErrorOOC = -10,
		ReadWriteErrorOOC = -11,
		LibraryMismatch = -12,
		Interrupt = -13,
		IparmInternalError = -15
	}

	public enum MatrixType
	{
		RealStructurallySymmetric = 1,
		RealSymmetricPositiveDefinite = 2,
		RealSymmetricIndefinite = -2,
		ComplexStructurallySymmetric = 3,
		ComplexHermitianPositiveDefinite = 4,
		ComplexHermitianIndefinite = -4,
		ComplexSymmetric = 6,
		RealUnsymmetric = 11,
		ComplexUnsymmetric = 13
	}

	public enum PhaseType
	{
		Analysis = 11,
		AnalysisFactorization = 12,
		AnalysisFactorizationSolve = 13,
		Factorization = 22,
		FactorizationSolve = 23,
		SolveIterativeRefinement = 33,
		SolveOnlyForwardSubstitution = 331,
		SolveOnlyDiagonalSubstitution = 332,
		SolveOnlyBackwardSubstitution = 333,
		ReleaseLUInternalMemory = 0,
		ReleaseAllMemory = -1
	}

	public enum OrderingType
	{
		MinimumDegrees = 0,
		NestedDissection = 2,
		ParallelNestedDissection = 3
	}

	public int maxfct = 1;

	public int mnum = 1;

	public int msglvl;

	public int error;

	public int[] perm;

	public MatrixType matrixType = MatrixType.RealSymmetricPositiveDefinite;

	public int nrhs = 1;

	public int[] iparm = new int[64];

	private long[] handle = new long[64];

	private int mtype = 2;

	private int _numThreads;

	private int phase;

	private double[] ddum = new double[1];

	private int[] idum = new int[1];

	public string SolverName => "Pardiso";

	public bool IsDirectSolver => true;

	public OrderingType orderingType
	{
		get
		{
			return (OrderingType)iparm[1];
		}
		set
		{
			iparm[1] = (int)value;
		}
	}

	public int IterativeRefinementStepsPerformed => iparm[6];

	public int IterativeRefinementSteps
	{
		get
		{
			return iparm[7];
		}
		set
		{
			iparm[7] = value;
		}
	}

	public int PivotingPerturbationThreshold => iparm[9];

	public bool MatrixScaling
	{
		get
		{
			return iparm[10] == 1;
		}
		set
		{
			iparm[10] = (value ? 1 : 0);
		}
	}

	public int TransposeOption => iparm[11];

	public bool WeightedMatching
	{
		get
		{
			return iparm[12] > 0;
		}
		set
		{
			iparm[12] = (value ? 1 : 0);
		}
	}

	public int NonzeroFactors => iparm[17];

	public int NumberFactorization => iparm[18];

	public int NumberCGIterations => iparm[19];

	public bool CheckMatrix
	{
		get
		{
			return iparm[26] > 0;
		}
		set
		{
			iparm[26] = (value ? 1 : 0);
		}
	}

	public bool SinglePrecision
	{
		get
		{
			return iparm[27] > 0;
		}
		set
		{
			iparm[27] = (value ? 1 : 0);
		}
	}

	public bool ZeroBasedIndexing
	{
		get
		{
			return iparm[34] > 0;
		}
		set
		{
			iparm[34] = (value ? 1 : 0);
		}
	}

	public bool ParallelFactorization
	{
		get
		{
			return iparm[23] > 0;
		}
		set
		{
			iparm[23] = (value ? 1 : 0);
		}
	}

	public static string GetErrorMessage(PardisoError code)
	{
		return code switch
		{
			PardisoError.None => "No error", 
			PardisoError.InputInconsistent => "Inconsistent inputs", 
			PardisoError.NotEnoughMemory => "Not enough memory", 
			PardisoError.ReorderingProblem => "Problem reordering matrix", 
			PardisoError.ZeroPivot => "Zero pivot encountered in factorization", 
			PardisoError.UnclassifiedInternalError => "Unclassified internal error", 
			PardisoError.ReorderingFail => "Matrix reordering failure", 
			PardisoError.SingularDiagonalMatrix => "Diagonal matrix is singular", 
			PardisoError.Overflow => "32-bit integer overflow problem", 
			PardisoError.NotEnoughMemoryOOC => "Not enough out-of-core memory available", 
			PardisoError.FileErrorOOC => "Error opening out-of-core memory files", 
			PardisoError.ReadWriteErrorOOC => "Read/write error with out-of-core memory files", 
			PardisoError.LibraryMismatch => "Pardiso 64 called from 32-bit library", 
			PardisoError.Interrupt => "Computations interrupted by MKL progress function", 
			PardisoError.IparmInternalError => "Internal error with maximum weighted matching algorithm", 
			_ => "Unknown error", 
		};
	}

	public OperationResult Configure(object options)
	{
		if (options == null || !(options is PardisoOptions pardisoOptions))
		{
			return OperationResult.Fail(MatrixSolverMessageCodes.GetSolverOptionsError(SolverName));
		}
		mtype = pardisoOptions.MatrixIntCode;
		IsExtractUpperTriangular = pardisoOptions.IsExtractUpperTriangular;
		Initialize();
		return OperationResult.Success();
	}

	public override void SetThreadBudget(int numThreads)
	{
		base.SetThreadBudget(numThreads);
		_numThreads = numThreads;
	}

	private void Initialize()
	{
		for (int i = 0; i < handle.Length; i++)
		{
			handle[i] = 0L;
		}
		pardisoinit(handle, ref mtype, iparm);
		ZeroBasedIndexing = true;
		if (mtype != -2)
		{
			IterativeRefinementSteps = 0;
		}
	}

	public OperationResult Analysis(SparseMatrix<double> A)
	{
		return Analysis(A.RowCount, A.RowPointers, A.ColumnIndices, A.Values);
	}

	public OperationResult Analysis(int n, int[] ia, int[] ja, double[] a)
	{
		OperationResult operationResult = new OperationResult();
		phase = 11;
		pardiso(handle, ref maxfct, ref mnum, ref mtype, ref phase, ref n, a, ia, ja, perm, ref nrhs, iparm, ref msglvl, ddum, ddum, ref error);
		PardisoError pardisoError = (PardisoError)error;
		if (pardisoError != PardisoError.None)
		{
			operationResult.AddFailure(MatricesMessageCodes.GetPardisoError(GetErrorMessage(pardisoError)));
		}
		return operationResult;
	}

	public OperationResult Factorization(SparseMatrix<double> A)
	{
		return Factorization(A.RowCount, A.RowPointers, A.ColumnIndices, A.Values);
	}

	public OperationResult Factorization(int n, int[] ia, int[] ja, double[] a)
	{
		OperationResult operationResult = new OperationResult();
		phase = 22;
		pardiso(handle, ref maxfct, ref mnum, ref mtype, ref phase, ref n, a, ia, ja, perm, ref nrhs, iparm, ref msglvl, ddum, ddum, ref error);
		PardisoError pardisoError = (PardisoError)error;
		if (pardisoError != PardisoError.None)
		{
			operationResult.AddFailure(MatricesMessageCodes.GetPardisoError(GetErrorMessage(pardisoError)));
		}
		return operationResult;
	}

	public OperationResult AnalysisFactorization(SparseMatrix<double> A)
	{
		return AnalysisFactorization(A.RowCount, A.RowPointers, A.ColumnIndices, A.Values);
	}

	public OperationResult AnalysisFactorization(int n, int[] ia, int[] ja, double[] a)
	{
		OperationResult operationResult = new OperationResult();
		phase = 12;
		pardiso(handle, ref maxfct, ref mnum, ref mtype, ref phase, ref n, a, ia, ja, perm, ref nrhs, iparm, ref msglvl, ddum, ddum, ref error);
		PardisoError pardisoError = (PardisoError)error;
		if (pardisoError != PardisoError.None)
		{
			operationResult.AddFailure(MatricesMessageCodes.GetPardisoError(GetErrorMessage(pardisoError)));
		}
		return operationResult;
	}

	public override OperationResult Prepare(SparseMatrix<double> A, IPreconditioner<double> precond, PartialSolve partSolve = null)
	{
		OperationResult operationResult = base.Prepare(A, precond, partSolve);
		if (!operationResult)
		{
			return operationResult;
		}
		operationResult.MergeIn(Analysis(Matrix, Solution, RightHandSide));
		return operationResult;
	}

	public OperationResult Analysis(SparseMatrix<double> A, double[] x, double[] b)
	{
		return Analysis(A.RowCount, A.RowPointers, A.ColumnIndices, A.Values, x, b);
	}

	public OperationResult Analysis(int n, int[] ia, int[] ja, double[] a, double[] x, double[] b)
	{
		OperationResult operationResult = new OperationResult();
		phase = 11;
		pardiso(handle, ref maxfct, ref mnum, ref mtype, ref phase, ref n, a, ia, ja, idum, ref nrhs, iparm, ref msglvl, b, x, ref error);
		PardisoError pardisoError = (PardisoError)error;
		if (pardisoError != PardisoError.None)
		{
			operationResult.AddFailure(MatricesMessageCodes.GetPardisoError(GetErrorMessage(pardisoError)));
		}
		return operationResult;
	}

	public OperationResult PrepareAndSolve(SparseMatrix<double> A, double[] x, double[] b, IPreconditioner<double> precond = null, PartialSolve partSolve = null)
	{
		OperationResult operationResult = Prepare(A, precond, partSolve);
		if (!operationResult.Result)
		{
			return operationResult;
		}
		operationResult.MergeIn(PreSolve(x, b));
		if (!operationResult.Result)
		{
			return operationResult;
		}
		operationResult.MergeIn(FactorizationSolve(Matrix, Solution, RightHandSide));
		if (operationResult.Result)
		{
			operationResult.MergeIn(PostSolve(x));
		}
		return operationResult;
	}

	public OperationResult Solve(SparseMatrix<double> A, double[] x, double[] b)
	{
		OperationResult operationResult = new OperationResult();
		operationResult.MergeIn(Update(A));
		operationResult.MergeIn(Solve(x, b));
		return operationResult;
	}

	public OperationResult Solve(double[] x, double[] b)
	{
		PreSolve(x, b);
		OperationResult result = FactorizationSolve(Matrix, Solution, RightHandSide);
		PostSolve(x);
		return result;
	}

	public OperationResult FactorizationSolve(SparseMatrix<double> A, double[] x, double[] b)
	{
		return FactorizationSolve(A.RowCount, A.RowPointers, A.ColumnIndices, A.Values, x, b);
	}

	public OperationResult FactorizationSolve(int n, int[] ia, int[] ja, double[] a, double[] x, double[] b)
	{
		OperationResult operationResult = new OperationResult();
		phase = 23;
		pardiso(handle, ref maxfct, ref mnum, ref mtype, ref phase, ref n, a, ia, ja, idum, ref nrhs, iparm, ref msglvl, b, x, ref error);
		PardisoError pardisoError = (PardisoError)error;
		if (pardisoError != PardisoError.None)
		{
			operationResult.AddFailure(MatricesMessageCodes.GetPardisoError(GetErrorMessage(pardisoError)));
		}
		return operationResult;
	}

	public OperationResult SolveIterativeRefinement(SparseMatrix<double> A, double[] x, double[] b)
	{
		return SolveIterativeRefinement(A.RowCount, A.RowPointers, A.ColumnIndices, A.Values, x, b);
	}

	public OperationResult SolveIterativeRefinement(int n, int[] ia, int[] ja, double[] a, double[] x, double[] b)
	{
		OperationResult operationResult = new OperationResult();
		phase = 33;
		pardiso(handle, ref maxfct, ref mnum, ref mtype, ref phase, ref n, a, ia, ja, idum, ref nrhs, iparm, ref msglvl, b, x, ref error);
		PardisoError pardisoError = (PardisoError)error;
		if (pardisoError != PardisoError.None)
		{
			operationResult.AddFailure(MatricesMessageCodes.GetPardisoError(GetErrorMessage(pardisoError)));
		}
		return operationResult;
	}

	public OperationResult AnalysisFactorizationSolve(SparseMatrix<double> A, double[] x, double[] b)
	{
		return AnalysisFactorizationSolve(A.RowCount, A.RowPointers, A.ColumnIndices, A.Values, x, b);
	}

	public OperationResult AnalysisFactorizationSolve(int n, int[] ia, int[] ja, double[] a, double[] x, double[] b)
	{
		OperationResult operationResult = new OperationResult();
		phase = 13;
		pardiso(handle, ref maxfct, ref mnum, ref mtype, ref phase, ref n, a, ia, ja, idum, ref nrhs, iparm, ref msglvl, b, x, ref error);
		PardisoError pardisoError = (PardisoError)error;
		if (pardisoError != PardisoError.None)
		{
			operationResult.AddFailure(MatricesMessageCodes.GetPardisoError(GetErrorMessage(pardisoError)));
		}
		return operationResult;
	}

	public OperationResult Destroy(SparseMatrix<double> A)
	{
		return ReleaseAllMemory(A.RowCount, A.RowPointers, A.ColumnIndices);
	}

	public OperationResult ReleaseAllMemory(SparseMatrix<double> A)
	{
		return ReleaseAllMemory(A.RowCount, A.RowPointers, A.ColumnIndices);
	}

	public OperationResult ReleaseAllMemory(int n, int[] ia, int[] ja)
	{
		OperationResult operationResult = new OperationResult();
		phase = -1;
		pardiso(handle, ref maxfct, ref mnum, ref mtype, ref phase, ref n, ddum, ia, ja, idum, ref nrhs, iparm, ref msglvl, ddum, ddum, ref error);
		PardisoError pardisoError = (PardisoError)error;
		if (pardisoError != PardisoError.None)
		{
			operationResult.AddFailure(MatricesMessageCodes.GetPardisoError(GetErrorMessage(pardisoError)));
		}
		return operationResult;
	}

	public void pardiso(long[] handle, ref int maxfct, ref int mnum, ref int mtype, ref int phase, ref int n, double[] a, int[] ia, int[] ja, int[] perm, ref int nrhs, int[] iparm, ref int msglvl, double[] b, double[] x, ref int error)
	{
		GCHandle[] handles = NativeUtilities.PinArrays(handle, a, ia, ja, perm, iparm, b, x);
		int local = ((_numThreads > 0) ? MklThreading.SetLocal(_numThreads) : 0);
		try
		{
			PardisoNative.PARDISO(handle, ref maxfct, ref mnum, ref mtype, ref phase, ref n, a, ia, ja, perm, ref nrhs, iparm, ref msglvl, b, x, ref error);
		}
		finally
		{
			if (_numThreads > 0)
			{
				MklThreading.SetLocal(local);
			}
			NativeUtilities.UnpinArrays(handles);
		}
	}

	public static void pardisoinit(long[] handle, ref int mtype, int[] iparm)
	{
		GCHandle[] handles = NativeUtilities.PinArrays(handle, iparm);
		try
		{
			PardisoInitNative.PARDISOINIT(handle, ref mtype, iparm);
		}
		finally
		{
			NativeUtilities.UnpinArrays(handles);
		}
	}
}
