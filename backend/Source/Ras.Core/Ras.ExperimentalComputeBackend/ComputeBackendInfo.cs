namespace Ras.ExperimentalComputeBackend;

public readonly struct ComputeBackendInfo
{
	public string Id { get; init; }

	public string DisplayName { get; init; }

	public bool IsRemote { get; init; }

	public bool RequiresUpload { get; init; }

	public bool SupportsGpu { get; init; }

	public bool SupportsCpu { get; init; }

	public int MaxConcurrency { get; init; }

	public bool SupportsExperiments { get; init; }

	public bool SupportsBatchStatus { get; init; }
}
