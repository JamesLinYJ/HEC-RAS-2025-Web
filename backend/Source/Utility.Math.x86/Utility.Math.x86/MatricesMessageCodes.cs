using Utility.Logging;

namespace Utility.Math.x86;

public static class MatricesMessageCodes
{
	public static readonly MatrixMessageDefinition PardisoError = new MatrixMessageDefinition("Matrices_Pardiso", MessageSeverity.Error, "Pardiso error: {errorMsg}");

	public static readonly MatrixMessageDefinition ISSRCIInitError = new MatrixMessageDefinition("Matrices_ISSRCI_Init", MessageSeverity.Error, "ISS RCI Init failure");

	public static readonly MatrixMessageDefinition ISSRCICheckError = new MatrixMessageDefinition("Matrices_ISSRCI_Check", MessageSeverity.Error, "ISS RCI Check error: {errorMsg}");

	public static readonly MatrixMessageDefinition ISSRCICheckWarning = new MatrixMessageDefinition("Matrices_ISSRCI_Check", MessageSeverity.Warning, "ISS RCI Check warning: {warningMsg}");

	public static readonly MatrixMessageDefinition ISSRCISolveError = new MatrixMessageDefinition("Matrices_ISSRCI_Solve", MessageSeverity.Error, "ISS RCI Solve error: {errorMsg}");

	public static readonly MatrixMessageDefinition ILUPrecondError = new MatrixMessageDefinition("Matrices_ILU_Precond", MessageSeverity.Error, "ILU PreConditioner error: {errorMsg}");

	public static readonly MatrixMessageDefinition NullMatrix = new MatrixMessageDefinition("Matrices_NullMatrix", MessageSeverity.Error, "{solverName}: Matrix argument is null");

	public static readonly MatrixMessageDefinition NullArray = new MatrixMessageDefinition("Matrices_NullArray", MessageSeverity.Error, "{solverName}: {arrayName} array is null");

	public static readonly MatrixMessageDefinition ArrayLengthMismatch = new MatrixMessageDefinition("Matrices_ArrayLengthMismatch", MessageSeverity.Error, "{solverName}: {arrayName} length ({actual}) is less than matrix row count ({expected})");

	public static readonly MatrixMessageDefinition SolverNotPrepared = new MatrixMessageDefinition("Matrices_NotPrepared", MessageSeverity.Error, "{solverName}: Solve called before Prepare");

	public static Message GetPardisoError(string msg)
	{
		return PardisoError.Create(msg);
	}

	public static Message GetISSRCIInitError()
	{
		return ISSRCIInitError.Create();
	}

	public static Message GetISSRCICheckError(string msg)
	{
		return ISSRCICheckError.Create(msg);
	}

	public static Message GetISSRCICheckWarning(string msg)
	{
		return ISSRCICheckWarning.Create(msg);
	}

	public static Message GetISSRCISolveError(string msg)
	{
		return ISSRCISolveError.Create(msg);
	}

	public static Message GetILUPreCondError(string msg)
	{
		return ILUPrecondError.Create(msg);
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

	public static Message GetSolverNotPrepared(string solverName)
	{
		return SolverNotPrepared.Create(solverName);
	}
}
