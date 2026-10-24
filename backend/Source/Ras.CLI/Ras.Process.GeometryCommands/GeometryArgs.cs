using CommandLine;

namespace Ras.Process.GeometryCommands;

[Verb("geometry", false, null, HelpText = "Command group. Inspect and interact with RAS geometry files.")]
public class GeometryArgs
{
	public const string Verb = "geometry";

	public bool Execute()
	{
		return false;
	}
}
