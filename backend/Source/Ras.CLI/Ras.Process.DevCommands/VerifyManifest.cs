using System.Threading.Tasks;
using CommandLine;
using Utility.Progress;

namespace Ras.Process.DevCommands;

[Verb("verify", false, null, HelpText = "Downloads a manifest and validates it against the S3 server to verify all expected keys are present.")]
public class VerifyManifest : BaseDevCommand
{
	public const string Verb = "verify";

	public Task<bool> ExecuteAsync()
	{
		ProgressReporter.ConsoleWrite().ReportMessage("TODO!");
		return Task.FromResult(result: false);
	}
}
