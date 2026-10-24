using CommandLine;

namespace Ras.Process.LibraryCommands;

[Verb("library", false, null, HelpText = "Command group (hidden). Browse, download, and publish developer model-library projects.")]
public class LibraryArgs
{
	public const string Verb = "library";

	public bool Execute()
	{
		return false;
	}
}
