using CommandLine;
using Ras.Process.Helpers;

namespace Ras.Process.ProjectCommands;

public abstract class ProjectListArgsBase
{
	[Value(0, MetaName = "project", Required = false, HelpText = "Path to a project (*.ras) or directory containing one. When omitted, the current working directory is searched.")]
	public string ProjectPath { get; set; }

	[Option("field", Required = false, HelpText = "Which value to emit per entry: Name (default), Absolute (absolute path to the *.h5 file), or Relative (path relative to the project directory).")]
	public ProjectListField Field { get; set; }

	[Option("delimiter", Required = false, HelpText = "Delimiter between entries when --format is Text. Defaults to the platform newline. Supports the escapes \\n, \\r, \\t, \\0, \\\\.")]
	public string Delimiter { get; set; }

	[Option("format", Required = false, HelpText = "Output format: Text (default; delimited list), Json (array of strings), or Markdown (bulleted list).")]
	public OutputFormat OutputFormat { get; set; }

	public abstract bool Execute();
}
