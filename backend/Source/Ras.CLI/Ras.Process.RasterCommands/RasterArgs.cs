using CommandLine;

namespace Ras.Process.RasterCommands;

[Verb("raster", false, null, HelpText = "Command group. Translate and compute on raster files.")]
public class RasterArgs
{
	public const string Verb = "raster";

	public bool Execute()
	{
		return false;
	}
}
