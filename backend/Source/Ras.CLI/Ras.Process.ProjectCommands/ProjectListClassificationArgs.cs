using CommandLine;
using Ras.Layers;

namespace Ras.Process.ProjectCommands;

[Verb("list-classification", false, new string[] { "list-c" }, HelpText = "Lists the classification layers in a RAS project as a simple delimited list. By default emits one name per line. Alias: list-c.")]
public class ProjectListClassificationArgs : ProjectListArgsBase
{
	public const string Verb = "list-classification";

	public const string Alias = "list-c";

	public override bool Execute()
	{
		return ProjectListHelper.Run(ProjectPath, Field, Delimiter, OutputFormat, (Project p) => p.ClassificationLayers, (ClassificationLayer c) => c.Name, (ClassificationLayer c) => c.Filename);
	}
}
