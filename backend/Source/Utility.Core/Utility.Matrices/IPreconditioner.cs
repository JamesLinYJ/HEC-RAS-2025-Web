using System.Numerics;
using Utility.Logging;

namespace Utility.Matrices;

public interface IPreconditioner<T> where T : unmanaged, INumber<T>
{
	string PrecondName { get; }

	bool IsPreconditioner { get; set; }

	OperationResult Prepare(SparseMatrix<T> A);

	void Apply(T[] destination, T[] rhs);
}
