using CommandLine;
using Ras.Layers;

namespace Ras.Process.ProjectCommands;

[Verb("list-terrain", false, new string[] { "list-t" }, HelpText = "Lists the terrains in a RAS project as a simple delimited list. By default emits one name per line. Alias: list-t.")]
public class ProjectListTerrainArgs : ProjectListArgsBase
{
	public const string Verb = "list-terrain";

	public const string Alias = "list-t";

	public override bool Execute()
	{
		return ProjectListHelper.Run(ProjectPath, Field, Delimiter, OutputFormat, (Project p) => p.Terrains, (Terrain t) => t.Name, (Terrain t) => t.Filename);
	}
}
