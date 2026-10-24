using CommandLine;

namespace Ras.Process.DevCommands;

[Verb("dev", false, null, HelpText = "Command group (hidden). Developer-only utilities: updates, manifests, project push/pull.")]
public class DevArgs
{
	public const string Verb = "dev";

	public bool Execute()
	{
		return false;
	}
}
