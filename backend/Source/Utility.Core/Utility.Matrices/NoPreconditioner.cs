using System;
using System.Numerics;
using Utility.Logging;

namespace Utility.Matrices;

public class NoPreconditioner<T> : IPreconditioner<T> where T : unmanaged, IFloatingPoint<T>
{
	private int _n;

	public string PrecondName => "NoPreconditioner";

	public bool IsPreconditioner
	{
		get
		{
			return true;
		}
		set
		{
		}
	}

	public virtual OperationResult Prepare(SparseMatrix<T> A)
	{
		if (A == null)
		{
			return OperationResult.Fail(MatrixSolverMessageCodes.GetNullMatrix(PrecondName));
		}
		_n = A.RowCount;
		return OperationResult.Success();
	}

	public virtual void Apply(T[] destination, T[] rhs)
	{
		if (destination.Length < _n)
		{
			throw new ArgumentException("Destination is shorter than the prepared matrix.", "destination");
		}
		if (rhs.Length < _n)
		{
			throw new ArgumentException("Right-hand side is shorter than the prepared matrix.", "rhs");
		}
		Array.Copy(rhs, destination, _n);
	}
}
