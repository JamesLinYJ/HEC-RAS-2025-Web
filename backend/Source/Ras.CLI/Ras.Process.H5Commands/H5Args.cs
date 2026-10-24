using CommandLine;

namespace Ras.Process.H5Commands;

[Verb("h5", false, null, HelpText = "Command group. Inspect, diff, and patch HDF5 files.")]
public class H5Args
{
	public const string Verb = "h5";

	public bool Execute()
	{
		return false;
	}
}
