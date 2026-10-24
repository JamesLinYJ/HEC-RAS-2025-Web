using Ras.Layers;

namespace Ras.ExperimentalComputeBackend;

public readonly struct ComputeRequest
{
	public string ReadyToRunPath { get; init; }

	public string ResultOutputPath { get; init; }

	public string DisplayName { get; init; }

	public Result PreparedResult { get; init; }

	public ComputeResourceHints Hints { get; init; }

	public string ExperimentKey { get; init; }
}
