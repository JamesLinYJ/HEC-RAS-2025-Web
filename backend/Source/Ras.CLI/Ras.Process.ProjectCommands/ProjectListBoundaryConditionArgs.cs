using CommandLine;
using Ras.Layers;

namespace Ras.Process.ProjectCommands;

[Verb("list-boundarycondition", false, new string[] { "list-bc" }, HelpText = "Lists the boundary conditions in a RAS project as a simple delimited list. By default emits one name per line. Alias: list-bc.")]
public class ProjectListBoundaryConditionArgs : ProjectListArgsBase
{
	public const string Verb = "list-boundarycondition";

	public const string Alias = "list-bc";

	public override bool Execute()
	{
		return ProjectListHelper.Run(ProjectPath, Field, Delimiter, OutputFormat, (Project p) => p.BoundaryConditions, (BoundaryCondition b) => b.Name, (BoundaryCondition b) => b.Filename);
	}
}
