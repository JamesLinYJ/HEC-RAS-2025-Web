using CommandLine;

namespace Ras.Process.HelpersCommands;

[Verb("helpers", false, null, HelpText = "Command group. General-purpose file utilities (zip, extract).")]
public class HelpersArgs
{
	public const string Verb = "helpers";

	public bool Execute()
	{
		return false;
	}
}
