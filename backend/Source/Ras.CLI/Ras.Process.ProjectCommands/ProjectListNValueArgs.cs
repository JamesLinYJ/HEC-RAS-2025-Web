using CommandLine;
using Ras.Layers;

namespace Ras.Process.ProjectCommands;

[Verb("list-nvalue", false, new string[] { "list-n" }, HelpText = "Lists the Manning's n-value layers in a RAS project as a simple delimited list. By default emits one name per line. Alias: list-n.")]
public class ProjectListNValueArgs : ProjectListArgsBase
{
	public const string Verb = "list-nvalue";

	public const string Alias = "list-n";

	public override bool Execute()
	{
		return ProjectListHelper.Run(ProjectPath, Field, Delimiter, OutputFormat, (Project p) => p.NValues, (NValueLayer n) => n.Name, (NValueLayer n) => n.Filename);
	}
}
