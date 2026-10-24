using CommandLine;

namespace Ras.Process.ProjectCommands;

[Verb("project", false, null, HelpText = "Command group. Create, inspect, and modify RAS projects.")]
public class ProjectArgs
{
	public const string Verb = "project";

	public bool Execute()
	{
		return false;
	}
}
