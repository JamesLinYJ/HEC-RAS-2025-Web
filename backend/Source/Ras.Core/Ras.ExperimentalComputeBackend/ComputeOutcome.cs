using Ras.Layers;
using Utility.Logging;

namespace Ras.ExperimentalComputeBackend;

public readonly struct ComputeOutcome
{
	public OperationResult Operation { get; init; }

	public Result Result { get; init; }

	public bool TryGetResult(out Result result)
	{
		result = Result;
		if (Operation != null && Operation.Result)
		{
			return Result != null;
		}
		return false;
	}

	public static ComputeOutcome Fail(OperationResult op)
	{
		return new ComputeOutcome
		{
			Operation = op
		};
	}

	public static ComputeOutcome Success(Result result)
	{
		return new ComputeOutcome
		{
			Operation = OperationResult.Success(),
			Result = result
		};
	}
}
