using System.Collections.Generic;

namespace Testing.Core.Regression;

public class AddTestRequest
{
	public string CommitHash { get; set; }

	public string BuildNumber { get; set; }

	public List<string> CommitHashes { get; set; }

	public List<ModelRepoIdName> TestObjectIdNames { get; set; }

	public int NumRuntimeIterations { get; set; }

	public bool Nightly { get; set; }

	public string Tag { get; set; }

	public int? CpuCores { get; set; }

	public int? RamMb { get; set; }

	public string Solver { get; set; } = "CPU";
}
