using System.Threading.Tasks;
using CommandLine;
using Ras.Updates;
using Utility.Communication;
using Utility.Extensions;
using Utility.Progress;

namespace Ras.Process.DevCommands;

[Verb("delist", false, null, HelpText = "Delists a software entry from the global manifest. This does NOT remove the specific JSON entry, so people can still acquire it by name.")]
internal class Delist : BaseDevAuthCommand
{
	public const string Verb = "delist";

	[Option('v', "version", Required = true, HelpText = "Version of the software to delist, , e.g. '2.0.0.412-beta2'. This can also be a key to an existing manifest entry, e.g. 'ras-1_0_0_441.json'.")]
	public string Version { get; set; }

	[Option('g', "global", Required = false, HelpText = "Key to the Global JSON manifest. Defaults to 'globalmanifest.json'")]
	public string GlobalManifest { get; set; } = "globalmanifest.json";

	public async Task<bool> ExecuteAsync()
	{
		using S3TransferHelper s3u = GetS3();
		ProgressReporter cw = ProgressReporter.ConsoleWrite();
		ReleaseCollection releaseCollection = await ReleaseHelpers.TryGetReleaseCollectionAsync(s3u, GlobalManifest);
		int firstIndexOf = releaseCollection.AllReleases.FirstIndexOf((ReleaseDetails rd) => rd.Version.RoughlyEquals(Version));
		if (firstIndexOf == -1)
		{
			cw.ReportMessage("Version '" + Version + "' not found.");
			return false;
		}
		releaseCollection.AllReleases.RemoveAt(firstIndexOf);
		await ReleaseHelpers.PushReleaseCollectionAsync(s3u, GlobalManifest, releaseCollection, isDryRun: false, cw);
		cw.ReportMessage("Complete!");
		return true;
	}
}
