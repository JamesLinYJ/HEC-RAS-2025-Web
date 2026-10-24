using Ras.Layers;

namespace Ras.Optimization;

public sealed class ModelRunResult
{
	public bool Succeeded { get; init; }

	public string FailureReason { get; init; }

	public Result Result { get; init; }

	public string ResultDirectory { get; init; }
}
