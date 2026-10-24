using System.Threading;
using System.Threading.Tasks;

namespace Ras.ExperimentalComputeBackend;

public interface IComputeBackend
{
	string Name { get; }

	ComputeBackendInfo Info { get; }

	ValueTask<bool> IsAvailableAsync(CancellationToken ct = default(CancellationToken));

	Task<ComputeOutcome> ComputeAsync(ComputeRequest request, ComputeContext context, CancellationToken ct = default(CancellationToken));
}
