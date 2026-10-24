using CommandLine;

namespace Ras.Process.ResultCommands;

[Verb("result", false, null, HelpText = "Command group. Inspect and generate maps from RAS results.")]
public class ResultArgs
{
	public const string Verb = "result";

	public bool Execute()
	{
		return false;
	}
}
