using System;
using System.Numerics;
using Utility.Logging;

namespace Utility.Matrices;

public class Diagonal<T> : IPreconditioner<T> where T : unmanaged, IFloatingPoint<T>
{
	private int _n;

	private T[] _array;

	public string PrecondName => "Diagonal";

	public bool IsPreconditioner { get; set; }

	public virtual OperationResult Prepare(SparseMatrix<T> A)
	{
		if (A == null)
		{
			return OperationResult.Fail(MatrixSolverMessageCodes.GetNullMatrix(PrecondName));
		}
		if (A.RowCount <= 0)
		{
			return OperationResult.Fail(MatrixSolverMessageCodes.GetInvalidMatrixDimension(PrecondName, A.RowCount, A.NonZeroCount));
		}
		_n = A.RowCount;
		_array = new T[_n];
		for (int i = 0; i < _n; i++)
		{
			_array[i] = T.One / A.GetDataDiagonalRow(i);
		}
		return OperationResult.Success();
	}

	public virtual void Apply(T[] destination, T[] rhs)
	{
		if (_array == null)
		{
			throw new InvalidOperationException("Preconditioner has not been prepared.");
		}
		if (destination.Length < _n)
		{
			throw new ArgumentException("Destination is shorter than the prepared matrix.", "destination");
		}
		if (rhs.Length < _n)
		{
			throw new ArgumentException("Right-hand side is shorter than the prepared matrix.", "rhs");
		}
		for (int i = 0; i < _n; i++)
		{
			destination[i] = rhs[i] * _array[i];
		}
	}
}
