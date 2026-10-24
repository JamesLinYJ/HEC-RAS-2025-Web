namespace Ras.ExperimentalComputeBackend;

public readonly struct ComputeResourceHints
{
	public bool PreferGpu { get; init; }

	public int CpuCores { get; init; }

	public int RamMb { get; init; }

	public int GpuCount { get; init; }
}
