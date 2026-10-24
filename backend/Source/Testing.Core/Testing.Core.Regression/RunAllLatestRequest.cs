namespace Testing.Core.Regression;

public class RunAllLatestRequest
{
	public string Scope { get; set; }

	public int NumRuntimeIterations { get; set; }

	public string Tag { get; set; }

	public bool Nightly { get; set; }

	public int? CpuCores { get; set; }

	public int? RamMb { get; set; }
}
