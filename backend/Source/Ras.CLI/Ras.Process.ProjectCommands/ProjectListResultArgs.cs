using CommandLine;
using Ras.Layers;

namespace Ras.Process.ProjectCommands;

[Verb("list-result", false, new string[] { "list-r" }, HelpText = "Lists the results in a RAS project as a simple delimited list. By default emits one name per line. Alias: list-r.")]
public class ProjectListResultArgs : ProjectListArgsBase
{
	public const string Verb = "list-result";

	public const string Alias = "list-r";

	public override bool Execute()
	{
		return ProjectListHelper.Run(ProjectPath, Field, Delimiter, OutputFormat, (Project p) => p.Results, (Result r) => r.Name, (Result r) => r.Filename);
	}
}
