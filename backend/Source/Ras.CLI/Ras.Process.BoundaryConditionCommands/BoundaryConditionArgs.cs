using CommandLine;

namespace Ras.Process.BoundaryConditionCommands;

[Verb("boundarycondition", false, new string[] { "bc" }, HelpText = "Command group. Inspect and interact with RAS boundary condition files. Alias: bc.")]
public class BoundaryConditionArgs
{
	public const string Verb = "boundarycondition";

	public const string Alias = "bc";

	public bool Execute()
	{
		return false;
	}
}
