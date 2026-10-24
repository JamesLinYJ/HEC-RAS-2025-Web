using CommandLine;

namespace Ras.Process.TerrainCommands;

[Verb("terrain", false, null, HelpText = "Command group. Create, edit, and interact with your RAS terrain.")]
public class TerrainArgs
{
	public const string Verb = "terrain";

	public bool Execute()
	{
		return false;
	}
}
