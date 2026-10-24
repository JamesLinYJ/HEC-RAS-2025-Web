using Utility.Logging;

namespace Utility.Matrices;

public static class MatrixSolverMessageCodes
{
	public static readonly MessageClass SolverMaxIterations = new MessageClass("Matrices_MaxIterations", MessageSeverity.Warning, "{solverName} reached max iterations ({iterations}) without converging. MaxError={maxError}");

	public static readonly MessageClass SolverDiverged = new MessageClass("Matrices_Diverged", MessageSeverity.Error, "{solverName} diverged at iteration {iterations}. MaxError={maxError}");

	public static readonly MessageClass SolverCrashed = new MessageClass("Matrices_Crashed", MessageSeverity.Error, "{solverName} crashed with NaN/Inf at iteration {iterations}");

	public static readonly MessageClass PreconditionerError = new MessageClass("Matrices_Preconditioner", MessageSeverity.Error, "Preconditioner {precondName} error: {errorMsg}");

	public static readonly MessageClass NullMatrix = new MessageClass("Matrices_NullMatrix", MessageSeverity.Error, "{solverName}: Matrix argument is null");

	public static readonly MessageClass NullArray = new MessageClass("Matrices_NullArray", MessageSeverity.Error, "{solverName}: {arrayName} array is null");

	public static readonly MessageClass ArrayLengthMismatch = new MessageClass("Matrices_ArrayLengthMismatch", MessageSeverity.Error, "{solverName}: {arrayName} length ({actual}) is less than matrix row count ({expected})");

	public static readonly MessageClass InvalidMatrixDimension = new MessageClass("Matrices_InvalidDimension", MessageSeverity.Error, "{solverName}: Matrix has invalid dimensions (rows={rows}, nonzero={nonzero})");

	public static readonly MessageClass SolverNotPrepared = new MessageClass("Matrices_NotPrepared", MessageSeverity.Error, "{solverName}: Solve called before Prepare");

	public static readonly MessageClass SolverOptionsError = new MessageClass("Matrices_OptionsError", MessageSeverity.Error, "{solverName}: Problem with solver options");

	public static Message GetSolverMaxIterations<T>(string solverName, int iterations, T maxError)
	{
		return SolverMaxIterations.Create(solverName, iterations, maxError);
	}

	public static Message GetSolverDiverged<T>(string solverName, int iterations, T maxError)
	{
		return SolverDiverged.Create(solverName, iterations, maxError);
	}

	public static Message GetSolverCrashed(string solverName, int iterations)
	{
		return SolverCrashed.Create(solverName, iterations);
	}

	public static Message GetPreconditionerError(string precondName, string errorMsg)
	{
		return PreconditionerError.Create(precondName, errorMsg);
	}

	public static Message GetNullMatrix(string solverName)
	{
		return NullMatrix.Create(solverName);
	}

	public static Message GetNullArray(string solverName, string arrayName)
	{
		return NullArray.Create(solverName, arrayName);
	}

	public static Message GetArrayLengthMismatch(string solverName, string arrayName, int actual, int expected)
	{
		return ArrayLengthMismatch.Create(solverName, arrayName, actual, expected);
	}

	public static Message GetInvalidMatrixDimension(string solverName, int rows, int nonzero)
	{
		return InvalidMatrixDimension.Create(solverName, rows, nonzero);
	}

	public static Message GetSolverNotPrepared(string solverName)
	{
		return SolverNotPrepared.Create(solverName);
	}

	public static Message GetSolverOptionsError(string solverName)
	{
		return SolverOptionsError.Create(solverName);
	}
}
