using System;
using System.Runtime.InteropServices;
using Utility.Logging;
using Utility.Matrices;

namespace Utility.Math.x86;

public class CG : IterativeSparseSolverRCI
{
	public CG(SparseMatrix<double> Amatrix, double[] xArray, double[] bArray)
		: base(Amatrix, xArray, bArray)
	{
	}

	protected override void InitializeSolver(OperationResult res)
	{
		int count = n * 4;
		tmp = new double[count];
		pcgInit(ref n, x, b, ref RCI_request, iparm, dparm, tmp);
		if (RCI_request != 0)
		{
			res.AddFailure(MatricesMessageCodes.GetISSRCIInitError());
		}
	}

	protected override void CheckInputsSolver(OperationResult res)
	{
		pcgCheck(ref n, x, b, ref RCI_request, iparm, dparm, tmp);
		RCIRequestCheckError rCI_request = (RCIRequestCheckError)RCI_request;
		if (RCI_request == -1100)
		{
			res.AddFailure(MatricesMessageCodes.GetISSRCICheckError(IterativeSparseSolverRCI.GetISSRCICheckMessage(rCI_request)));
		}
	}

	protected override void SolveMatrix()
	{
		pcg(ref n, x, b, ref RCI_request, iparm, dparm, tmp);
	}

	protected override double[] GetArrayFromTmp()
	{
		Index start = 0;
		Index end = n;
		return tmp[start..end];
	}

	protected override void PutArrayInTmp(double[] arr)
	{
		int ordinal = n;
		for (int i = 0; i < n; i++)
		{
			tmp[ordinal] = arr[i];
			ordinal++;
		}
	}

	protected override double GetResidual2Norm()
	{
		SparseMatrix<double>.ProductMatrixVector(A, x, workingArray);
		double residual2Norm = 0.0;
		for (int i = 0; i < n; i++)
		{
			residual2Norm += System.Math.Pow(workingArray[i] - rhs[i], 2.0);
		}
		return residual2Norm;
	}

	protected override void SolverGet()
	{
		pcgGet(ref n, x, b, ref RCI_request, iparm, dparm, tmp, ref iterCnt);
	}

	public static void pcgInit(ref int n, double[] x, double[] b, ref int RCI_request, int[] iparm, double[] dparm, double[] tmp)
	{
		GCHandle[] handles = NativeUtilities.PinArrays(x, b, iparm, dparm, tmp);
		try
		{
			PCGInitNative.DCG_INIT(ref n, x, b, ref RCI_request, iparm, dparm, tmp);
		}
		finally
		{
			NativeUtilities.UnpinArrays(handles);
		}
	}

	public static void pcgCheck(ref int n, double[] x, double[] b, ref int RCI_request, int[] iparm, double[] dparm, double[] tmp)
	{
		GCHandle[] handles = NativeUtilities.PinArrays(x, b, iparm, dparm, tmp);
		try
		{
			PCGCheckNative.DCG_CHECK(ref n, x, b, ref RCI_request, iparm, dparm, tmp);
		}
		finally
		{
			NativeUtilities.UnpinArrays(handles);
		}
	}

	public static void pcg(ref int n, double[] x, double[] b, ref int RCI_request, int[] iparm, double[] dparm, double[] tmp)
	{
		GCHandle[] handles = NativeUtilities.PinArrays(x, b, iparm, dparm, tmp);
		try
		{
			PCGNative.DCG(ref n, x, b, ref RCI_request, iparm, dparm, tmp);
		}
		finally
		{
			NativeUtilities.UnpinArrays(handles);
		}
	}

	public static void pcgGet(ref int n, double[] x, double[] b, ref int RCI_request, int[] iparm, double[] dparm, double[] tmp, ref int iterCount)
	{
		GCHandle[] handles = NativeUtilities.PinArrays(x, b, iparm, dparm, tmp);
		try
		{
			PCGGetNative.DCG_GET(ref n, x, b, ref RCI_request, iparm, dparm, tmp, ref iterCount);
		}
		finally
		{
			NativeUtilities.UnpinArrays(handles);
		}
	}
}
