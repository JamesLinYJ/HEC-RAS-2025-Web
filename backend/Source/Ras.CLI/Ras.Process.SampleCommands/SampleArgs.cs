using CommandLine;

namespace Ras.Process.SampleCommands;

[Verb("sample", false, null, HelpText = "Command group. Browse, download, and publish RAS example/sample projects.")]
public class SampleArgs
{
	public const string Verb = "sample";

	public bool Execute()
	{
		return false;
	}
}
