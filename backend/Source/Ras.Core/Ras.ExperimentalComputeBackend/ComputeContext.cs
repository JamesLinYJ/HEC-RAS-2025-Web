using Utility;
using Utility.Progress;

namespace Ras.ExperimentalComputeBackend;

public readonly struct ComputeContext
{
	public ProgressReporter Reporter { get; init; }

	public ISynchronizationContext UiSync { get; init; }
}
