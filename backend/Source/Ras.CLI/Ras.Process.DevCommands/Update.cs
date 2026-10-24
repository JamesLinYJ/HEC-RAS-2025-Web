using System;
using System.Threading.Tasks;
using CommandLine;
using Ras.Updates;
using Utility.Progress;

namespace Ras.Process.DevCommands;

[Verb("update", false, null, HelpText = "Update RAS to a given version.")]
public class Update
{
	public const string Verb = "update";

	[Option('v', "version", Required = true, HelpText = "The version to update to.")]
	public string Version { get; set; }

	[Option('b', "bucket", Required = false, HelpText = "The bucket to pull the update from")]
	public string Bucket { get; set; }

	public async Task<bool> ExecuteAsync()
	{
		ProgressReporter prog = ProgressReporter.ConsoleWrite();
		if (!OperatingSystem.IsWindows())
		{
			prog.ReportError("In-place update is implemented by the Windows RasUpdateHelper.exe workflow. Install the macOS application bundle to update on macOS.");
			return false;
		}
		string version = Version;
		string bucket = Bucket;
		(bool, Func<Task>) tuple = await Updater.TryUpdateInPlace(version, launchExeOnReboot: false, null, prog, bucket);
		if (tuple.Item1)
		{
			await tuple.Item2();
			return true;
		}
		return false;
	}
}
