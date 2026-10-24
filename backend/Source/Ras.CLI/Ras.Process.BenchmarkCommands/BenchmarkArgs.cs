using CommandLine;

namespace Ras.Process.BenchmarkCommands;

[Verb("benchmark", false, null, HelpText = "Command group. Run performance benchmarks on RAS files.")]
public class BenchmarkArgs
{
	public const string Verb = "benchmark";

	public bool Execute()
	{
		return false;
	}
}
