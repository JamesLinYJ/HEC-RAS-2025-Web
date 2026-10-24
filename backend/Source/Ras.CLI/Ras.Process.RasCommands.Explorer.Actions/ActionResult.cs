using Utility.Logging;

namespace Ras.Process.RasCommands.Explorer.Actions;

public class ActionResult
{
	public bool Success { get; init; }

	public string Message { get; init; }

	public string OutputPath { get; init; }

	public OperationResult OperationResult { get; init; }

	public static ActionResult Succeeded(string message = null, string outputPath = null)
	{
		return new ActionResult
		{
			Success = true,
			Message = message,
			OutputPath = outputPath
		};
	}

	public static ActionResult Failed(string message)
	{
		return new ActionResult
		{
			Success = false,
			Message = message
		};
	}

	public static ActionResult FromOperationResult(OperationResult or, string successMessage = null)
	{
		return new ActionResult
		{
			Success = or.Result,
			Message = (or.Result ? successMessage : or.GetConcatenatedMessages()),
			OperationResult = or
		};
	}
}
