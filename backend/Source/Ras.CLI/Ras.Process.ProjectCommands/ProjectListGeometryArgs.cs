using CommandLine;
using Ras.Layers;

namespace Ras.Process.ProjectCommands;

[Verb("list-geometry", false, new string[] { "list-g" }, HelpText = "Lists the geometries in a RAS project as a simple delimited list. By default emits one name per line. Alias: list-g.")]
public class ProjectListGeometryArgs : ProjectListArgsBase
{
	public const string Verb = "list-geometry";

	public const string Alias = "list-g";

	public override bool Execute()
	{
		return ProjectListHelper.Run(ProjectPath, Field, Delimiter, OutputFormat, (Project p) => p.Geometries, (Geometry g) => g.Name, (Geometry g) => g.Filename);
	}
}
