using System.Numerics;

namespace Utility.Matrices;

public class LeastSquares<T> where T : unmanaged, IFloatingPointIeee754<T>, IRootFunctions<T>
{
	public static bool Solve(T[,] A, T[] x, T[] b)
	{
		T[,] a = DenseMatrix<T>.TransposeMultiply(A, A);
		T[] b2 = DenseMatrix<T>.TransposeMultiply(A, b);
		int n = x.Length;
		Gauss<T>.SolveDenseInPlace(a, x, b2, n);
		return true;
	}
}
