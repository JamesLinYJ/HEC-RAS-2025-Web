using CommandLine;
using Ras.Layers;

namespace Ras.Process.ProjectCommands;

[Verb("list-plan", false, new string[] { "list-p" }, HelpText = "Lists the plans in a RAS project. By default emits a simple delimited list of names; pass --detailed for a multi-field per-plan report. Alias: list-p.")]
public class ProjectListPlanArgs : ProjectListArgsBase
{
	public const string Verb = "list-plan";

	public const string Alias = "list-p";

	[Option("detailed", Required = false, HelpText = "Emit a multi-field per-plan report (file, short ID, geometry/BC association, solver, time window, steps). When set, --field and --delimiter are ignored; --format still selects Text/Json/Markdown.")]
	public bool Detailed { get; set; }

	public override bool Execute()
	{
		if (Detailed)
		{
			return ProjectListPlanDetailedHelper.Run(ProjectPath, OutputFormat);
		}
		return ProjectListHelper.Run(ProjectPath, Field, Delimiter, OutputFormat, (Project p) => p.Plans, (Plan p) => p.Name, (Plan p) => p.Filename);
	}
}
