using System;
using System.Runtime.InteropServices;
using Utility.Logging;
using Utility.Matrices;

namespace Utility.Math.x86;

public class FGMRES : IterativeSparseSolverRCI
{
	public FGMRES(SparseMatrix<double> Amatrix, double[] xArray, double[] bArray)
		: base(Amatrix, xArray, bArray)
	{
	}

	protected override void InitializeSolver(OperationResult res)
	{
		int sizeTmp = ComputeSizeTmp(System.Math.Min(150, n), n);
		tmp = new double[sizeTmp];
		fgmresInit(ref n, x, b, ref RCI_request, iparm, dparm, tmp);
		if (RCI_request != 0)
		{
			res.AddFailure(MatricesMessageCodes.GetISSRCIInitError());
		}
	}

	private static int ComputeSizeTmp(int iter, int nn)
	{
		return (2 * iter + 1) * nn + iter * (iter + 9) / 2 + 1;
	}

	protected override void CheckInputsSolver(OperationResult res)
	{
		fgmresCheck(ref n, x, b, ref RCI_request, iparm, dparm, tmp);
		RCIRequestCheckError rCI_request = (RCIRequestCheckError)RCI_request;
		if (RCI_request == -1100)
		{
			res.AddFailure(MatricesMessageCodes.GetISSRCICheckError(IterativeSparseSolverRCI.GetISSRCICheckMessage(rCI_request)));
		}
	}

	protected override void SolveMatrix()
	{
		fgmres(ref n, x, b, ref RCI_request, iparm, dparm, tmp);
	}

	protected override double[] GetArrayFromTmp()
	{
		Index start = iparm[21] - 1;
		Index end = start.Value + n;
		return tmp[start..end];
	}

	protected override void PutArrayInTmp(double[] arr)
	{
		int iparmTerm = iparm[22] - 1;
		for (int i = 0; i < n; i++)
		{
			tmp[iparmTerm] = arr[i];
			iparmTerm++;
		}
	}

	protected override double GetResidual2Norm()
	{
		WriteSolnToBVec = true;
		SolverGet();
		SparseMatrix<double>.ProductMatrixVector(A, b, workingArray);
		double residual2Norm = 0.0;
		for (int i = 0; i < n; i++)
		{
			residual2Norm += System.Math.Pow(workingArray[i] - rhs[i], 2.0);
		}
		return residual2Norm;
	}

	protected override void SolverGet()
	{
		fgmresGet(ref n, x, b, ref RCI_request, iparm, dparm, tmp, ref iterCnt);
	}

	public static void fgmresInit(ref int n, double[] x, double[] b, ref int RCI_request, int[] iparm, double[] dparm, double[] tmp)
	{
		GCHandle[] handles = NativeUtilities.PinArrays(x, b, iparm, dparm, tmp);
		try
		{
			FGMRESInitNative.DFGMRES_INIT(ref n, x, b, ref RCI_request, iparm, dparm, tmp);
		}
		finally
		{
			NativeUtilities.UnpinArrays(handles);
		}
	}

	public static void fgmresCheck(ref int n, double[] x, double[] b, ref int RCI_request, int[] iparm, double[] dparm, double[] tmp)
	{
		GCHandle[] handles = NativeUtilities.PinArrays(x, b, iparm, dparm, tmp);
		try
		{
			FGMRESCheckNative.DFGMRES_CHECK(ref n, x, b, ref RCI_request, iparm, dparm, tmp);
		}
		finally
		{
			NativeUtilities.UnpinArrays(handles);
		}
	}

	public static void fgmres(ref int n, double[] x, double[] b, ref int RCI_request, int[] iparm, double[] dparm, double[] tmp)
	{
		GCHandle[] handles = NativeUtilities.PinArrays(x, b, iparm, dparm, tmp);
		try
		{
			FGMRESNative.DFGMRES(ref n, x, b, ref RCI_request, iparm, dparm, tmp);
		}
		finally
		{
			NativeUtilities.UnpinArrays(handles);
		}
	}

	public static void fgmresGet(ref int n, double[] x, double[] b, ref int RCI_request, int[] iparm, double[] dparm, double[] tmp, ref int iterCount)
	{
		GCHandle[] handles = NativeUtilities.PinArrays(x, b, iparm, dparm, tmp);
		try
		{
			FGMRESGetNative.DFGMRES_GET(ref n, x, b, ref RCI_request, iparm, dparm, tmp, ref iterCount);
		}
		finally
		{
			NativeUtilities.UnpinArrays(handles);
		}
	}
}
