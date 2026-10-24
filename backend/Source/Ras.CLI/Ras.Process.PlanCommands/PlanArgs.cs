using CommandLine;

namespace Ras.Process.PlanCommands;

[Verb("plan", false, null, HelpText = "Command group. Inspect RAS plan files and the plans inside a project.")]
public class PlanArgs
{
	public const string Verb = "plan";

	public bool Execute()
	{
		return false;
	}
}
