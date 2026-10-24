using System;
using System.Runtime.InteropServices;
using Utility.Logging;
using Utility.Matrices;

namespace Utility.Math.x86;

public abstract class IterativeSparseSolverRCI
{
	public enum RCIRequestType
	{
		MaxIterReached = -1,
		DivideByZero = -10,
		InfiniteCycle = -11,
		ParamErrors = -12,
		Success = 0,
		MultiplyMatrix = 1,
		TestUserDefinedStop = 2,
		ApplyPreconditioner = 3,
		CheckZeroNorm = 4
	}

	public enum RCIRequestInitError
	{
		Failure = -10000,
		Success = 0
	}

	public enum RCIRequestCheckError
	{
		Interrupted = -1100,
		Warnings = -1001,
		ParamChanged = -1010,
		WarningsAndParamChanged = -1011,
		Success = 0
	}

	public enum PreCondError
	{
		None = 0,
		RoutineInterrupted = -101,
		ZeroDiagonalElement = -102,
		SmallDiagonalElement = -103,
		InsufficientMemory = -104,
		MatrixSizeZero = -105,
		JaNotAscendingOrder = -106
	}

	protected int RCI_request;

	public int[] iparm = new int[128];

	public double[] dparm = new double[128];

	protected double[] tmp;

	public int n;

	public int iterCnt;

	public SparseMatrix<double> A;

	public double[] x;

	public double[] b;

	public double[] rhs;

	public double UserDefMax2Norm;

	public SparseMatrix<double> PreCond;

	protected double[] workingArray;

	public bool PrintMessagesToScreen
	{
		get
		{
			return iparm[1] == 6;
		}
		set
		{
			iparm[1] = (value ? 6 : 0);
		}
	}

	public int MaxIterations
	{
		get
		{
			return iparm[4];
		}
		set
		{
			iparm[4] = value;
		}
	}

	public bool LogErrorMessages
	{
		get
		{
			return iparm[5] == 1;
		}
		set
		{
			iparm[5] = (value ? 1 : 0);
		}
	}

	public bool LogWarningMessages
	{
		get
		{
			return iparm[6] == 1;
		}
		set
		{
			iparm[6] = (value ? 1 : 0);
		}
	}

	public bool UseMaxIterStoppingTest
	{
		get
		{
			return iparm[7] == 1;
		}
		set
		{
			iparm[7] = (value ? 1 : 0);
		}
	}

	public bool UseResidStoppingTest
	{
		get
		{
			return iparm[8] == 1;
		}
		set
		{
			iparm[8] = (value ? 1 : 0);
		}
	}

	public bool UseUserDefStoppingTest
	{
		get
		{
			return iparm[9] == 1;
		}
		set
		{
			iparm[9] = (value ? 1 : 0);
		}
	}

	public bool UsePreConditioner
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

	public bool UseAutoTestForZeroNorm
	{
		get
		{
			return iparm[11] == 1;
		}
		set
		{
			iparm[11] = (value ? 1 : 0);
		}
	}

	public bool WriteSolnToBVec
	{
		get
		{
			return iparm[12] == 1;
		}
		set
		{
			iparm[12] = (value ? 1 : 0);
		}
	}

	public int MaxNonRestartIterations
	{
		get
		{
			return iparm[14];
		}
		set
		{
			iparm[14] = value;
		}
	}

	public bool PreCondReplaceZeroDiag
	{
		get
		{
			return iparm[30] == 1;
		}
		set
		{
			iparm[30] = (value ? 1 : 0);
		}
	}

	public double RelativeTolerance
	{
		get
		{
			return dparm[0];
		}
		set
		{
			dparm[0] = value;
		}
	}

	public double AbsoluteTolerance
	{
		get
		{
			return dparm[1];
		}
		set
		{
			dparm[1] = value;
		}
	}

	public double ZeroNormTolerance
	{
		get
		{
			return dparm[7];
		}
		set
		{
			dparm[7] = value;
		}
	}

	public double PreCondZeroDiagTol
	{
		get
		{
			return dparm[30];
		}
		set
		{
			dparm[30] = value;
		}
	}

	public double PreCondZeroDiagVal
	{
		get
		{
			return dparm[31];
		}
		set
		{
			dparm[31] = value;
		}
	}

	public IterativeSparseSolverRCI(SparseMatrix<double> Amatrix, double[] xArray, double[] bArray)
	{
		A = Amatrix;
		x = xArray;
		b = bArray;
	}

	public static string GetPreCondErrorMessage(PreCondError code)
	{
		return code switch
		{
			PreCondError.None => "No error", 
			PreCondError.RoutineInterrupted => "Routine was interrupted", 
			PreCondError.ZeroDiagonalElement => "Matrix contains zero valued diagonal element", 
			PreCondError.SmallDiagonalElement => "Matrix contains a diagonal element small enough to cause overflow", 
			PreCondError.InsufficientMemory => "Insufficient memory for the internal work array", 
			PreCondError.MatrixSizeZero => "Matrix size zero", 
			PreCondError.JaNotAscendingOrder => "Column indices JA are not in ascending order", 
			_ => "Unknown error", 
		};
	}

	public static string GetISSRCICheckMessage(RCIRequestCheckError code)
	{
		return code switch
		{
			RCIRequestCheckError.Success => "No error", 
			RCIRequestCheckError.Interrupted => "Routine was interrupted", 
			RCIRequestCheckError.Warnings => "There are warning messages", 
			RCIRequestCheckError.ParamChanged => "Some parameters were changed in the routine", 
			RCIRequestCheckError.WarningsAndParamChanged => "There are warning messages and some parameters were changed", 
			_ => "Unknown error", 
		};
	}

	public static string GetISSRCIMessage(RCIRequestType code)
	{
		return code switch
		{
			RCIRequestType.Success => "No error", 
			RCIRequestType.MaxIterReached => "Maximum number of iterations reached", 
			RCIRequestType.DivideByZero => "Attempt to divide by zero. Check for degenerate matrix.", 
			RCIRequestType.ParamErrors => "Errors in iparm or dparm", 
			RCIRequestType.InfiniteCycle => "Solution entered infinite cycle", 
			_ => "Unknown error", 
		};
	}

	public OperationResult Initialize()
	{
		OperationResult operationResult = new OperationResult();
		n = A.RowCount;
		workingArray = new double[n];
		InitializeSolver(operationResult);
		PrintMessagesToScreen = false;
		LogErrorMessages = true;
		LogWarningMessages = true;
		UseResidStoppingTest = true;
		UseAutoTestForZeroNorm = true;
		return operationResult;
	}

	protected abstract void InitializeSolver(OperationResult res);

	public OperationResult CreatePreconditioner(bool isILU0 = true, int maxFill = 1, double fillInTol = 1E-06)
	{
		OperationResult operationResult = new OperationResult();
		int error = 0;
		if (isILU0)
		{
			PreCond = new SparseMatrix<double>();
			PreCond.Clone(A);
			AdjustMatrixIndices01(PreCond);
			dcsrilu0(ref n, A.Values, PreCond.RowPointers, PreCond.ColumnIndices, PreCond.Values, iparm, dparm, ref error);
			AdjustMatrixIndices10(PreCond);
		}
		else
		{
			int maxNonZero = (2 * maxFill + 1) * n - maxFill * (maxFill + 1) + 1;
			PreCond = new SparseMatrix<double>(n, maxNonZero);
			AdjustMatrixIndices01(A);
			dcsrilut(ref n, A.Values, A.RowPointers, A.ColumnIndices, PreCond.Values, PreCond.RowPointers, PreCond.ColumnIndices, ref fillInTol, ref maxFill, iparm, dparm, ref error);
			AdjustMatrixIndices10(A);
			AdjustMatrixIndices10(PreCond);
		}
		PreCondError preCondError = (PreCondError)error;
		if (preCondError != PreCondError.None)
		{
			operationResult.AddFailure(MatricesMessageCodes.GetILUPreCondError(GetPreCondErrorMessage(preCondError)));
			return operationResult;
		}
		UsePreConditioner = true;
		return operationResult;
	}

	private static void AdjustMatrixIndices01(SparseMatrix<double> matrix)
	{
		for (int i = 0; i < matrix.RowPointers.Length; i++)
		{
			matrix.RowPointers[i]++;
		}
		for (int j = 0; j < matrix.ColumnIndices.Length; j++)
		{
			matrix.ColumnIndices[j]++;
		}
	}

	private static void AdjustMatrixIndices10(SparseMatrix<double> matrix)
	{
		for (int i = 0; i < matrix.RowPointers.Length; i++)
		{
			matrix.RowPointers[i]--;
		}
		for (int j = 0; j < matrix.ColumnIndices.Length; j++)
		{
			matrix.ColumnIndices[j]--;
		}
	}

	public OperationResult CheckInputs()
	{
		OperationResult operationResult = new OperationResult();
		CheckInputsSolver(operationResult);
		return operationResult;
	}

	protected abstract void CheckInputsSolver(OperationResult res);

	public OperationResult Solve(SparseMatrix<double> A, double[] x, double[] b)
	{
		OperationResult operationResult = new OperationResult();
		if (UseUserDefStoppingTest)
		{
			rhs = new double[n];
			Array.Copy(b, rhs, n);
		}
		while (true)
		{
			SolveMatrix();
			RCIRequestType rCI_request = (RCIRequestType)RCI_request;
			switch (rCI_request)
			{
			case RCIRequestType.MultiplyMatrix:
			{
				double[] arrayFromTmp2 = GetArrayFromTmp();
				SparseMatrix<double>.ProductMatrixVector(A, arrayFromTmp2, workingArray);
				PutArrayInTmp(workingArray);
				continue;
			}
			case RCIRequestType.TestUserDefinedStop:
				if (!(GetResidual2Norm() < 0.001))
				{
					continue;
				}
				break;
			case RCIRequestType.ApplyPreconditioner:
			{
				double[] arrayFromTmp = GetArrayFromTmp();
				SparseMatrix<double>.SolveUnitLower(PreCond, workingArray, arrayFromTmp);
				SparseMatrix<double>.SolveUpper(PreCond, arrayFromTmp, workingArray);
				PutArrayInTmp(arrayFromTmp);
				continue;
			}
			case RCIRequestType.CheckZeroNorm:
				if (!(dparm[6] < ZeroNormTolerance))
				{
					continue;
				}
				break;
			default:
				operationResult.AddFailure(MatricesMessageCodes.GetISSRCISolveError(GetISSRCIMessage(rCI_request)));
				MklFreeBuffers();
				return operationResult;
			case RCIRequestType.Success:
				break;
			}
			break;
		}
		WriteSolnToBVec = false;
		SolverGet();
		MklFreeBuffers();
		return operationResult;
	}

	protected abstract void SolveMatrix();

	protected abstract double[] GetArrayFromTmp();

	protected abstract void PutArrayInTmp(double[] arr);

	protected abstract double GetResidual2Norm();

	protected abstract void SolverGet();

	public static void MklFreeBuffers()
	{
		MKLFreeBuffersNative.mkl_free_buffers();
	}

	public static void dcsrilu0(ref int n, double[] a, int[] ia, int[] ja, double[] bilu0, int[] iparm, double[] dparm, ref int error)
	{
		GCHandle[] handles = NativeUtilities.PinArrays(a, ia, ja, bilu0, iparm, dparm);
		try
		{
			DCSRILU0Native.DCSRILU0(ref n, a, ia, ja, bilu0, iparm, dparm, ref error);
		}
		finally
		{
			NativeUtilities.UnpinArrays(handles);
		}
	}

	public static void dcsrilut(ref int n, double[] a, int[] ia, int[] ja, double[] bilut, int[] ibilut, int[] jbilut, ref double fillInTol, ref int maxFill, int[] iparm, double[] dparm, ref int error)
	{
		GCHandle[] handles = NativeUtilities.PinArrays(a, ia, ja, bilut, ibilut, jbilut, iparm, dparm);
		try
		{
			DCSRILUTNative.DCSRILUT(ref n, a, ia, ja, bilut, ibilut, jbilut, ref fillInTol, ref maxFill, iparm, dparm, ref error);
		}
		finally
		{
			NativeUtilities.UnpinArrays(handles);
		}
	}
}
