using System.Collections.Generic;

namespace Ras.ExperimentalComputeBackend;

public interface IComputeBackendRegistry
{
	IReadOnlyList<IComputeBackend> Backends { get; }

	IComputeBackend Default { get; }

	bool TryGet(string backendId, out IComputeBackend backend);
}
