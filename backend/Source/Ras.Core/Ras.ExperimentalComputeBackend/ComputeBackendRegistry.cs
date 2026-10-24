using System.Collections.Generic;
using System.Linq;

namespace Ras.ExperimentalComputeBackend;

public class ComputeBackendRegistry : IComputeBackendRegistry
{
	private readonly List<IComputeBackend> _backends;

	public IReadOnlyList<IComputeBackend> Backends => _backends;

	public IComputeBackend Default { get; }

	public ComputeBackendRegistry(IEnumerable<IComputeBackend> backends, string defaultId = null)
	{
		_backends = backends.ToList();
		Default = _backends.FirstOrDefault((IComputeBackend b) => b.Info.Id == defaultId) ?? _backends.FirstOrDefault();
	}

	public bool TryGet(string backendId, out IComputeBackend backend)
	{
		backend = _backends.FirstOrDefault((IComputeBackend b) => b.Info.Id == backendId);
		return backend != null;
	}
}
