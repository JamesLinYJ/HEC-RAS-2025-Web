using System;
using Utility.Extensions;
using Utility.Logging;

namespace Ras.Engine;

public static class EngineMessageCodes
{
	public static readonly MessageClass SolverCourantViolation = new MessageClass("Solver_CourantViolation", MessageSeverity.Error, "Solver failed: Could not honor Courant condition: Cell {MeshCellIndex} required a timestep of less than {TimeSeconds} seconds.");

	public static readonly MessageClass ExceededMaxRetries = new MessageClass("Solver_ExceedMaxRetries", MessageSeverity.Error, "Solver failed: Exceeded max retry count ({MaxRetryCount})");

	public static readonly MessageClass UserCancelled = new MessageClass("Solver_UserCancelled", MessageSeverity.Error, "Solver run was cancelled.");

	public static readonly MessageClass Completed = new MessageClass("Solver_Completed", MessageSeverity.Information, "Computations completed in {EllapsedTime}.");

	public static Message GetSolverCourantViolation(int cellIndex, double minDt)
	{
		return SolverCourantViolation.Create(cellIndex, minDt);
	}

	public static Message GetExceededMaxRetries(int maxAllowable)
	{
		return ExceededMaxRetries.Create(maxAllowable);
	}

	public static Message GetUserCancelled()
	{
		return UserCancelled.Create();
	}

	public static Message GetCompleted(TimeSpan ellapsedTime)
	{
		return Completed.Create(ellapsedTime.ToPrettyString());
	}
}
