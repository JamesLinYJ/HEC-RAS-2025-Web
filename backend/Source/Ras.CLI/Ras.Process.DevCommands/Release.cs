using System.Linq;
using System.Threading.Tasks;
using CommandLine;
using Ras.Updates;
using Utility.Communication;
using Utility.Extensions;
using Utility.Progress;

namespace Ras.Process.DevCommands;

[Verb("release", false, null, HelpText = "Release the software! Adds a specific release manifest to a global version manifest.")]
internal class Release : BaseDevAuthCommand
{
	public const string Verb = "release";

	[Option('m', "manifest", Required = true, HelpText = "Key to the versioned JSON manifest, e.g. 'ras-1_0_0_441.json'")]
	public string SourceKey { get; set; }

	[Option('g', "global", Required = false, HelpText = "Key to the Global JSON manifest. Defaults to 'globalmanifest.json'")]
	public string GlobalManifestKey { get; set; } = "globalmanifest.json";

	[Option('f', "force", Required = false, HelpText = "Force ovewrite the entry if one already exists with the given version. Defaults to false.")]
	public bool Force { get; set; }

	public async Task<bool> ExecuteAsync()
	{
		using S3TransferHelper s3u = GetS3();
		ProgressReporter cw = ProgressReporter.ConsoleWrite();
		if (!(await s3u.ObjectExists(SourceKey)))
		{
			cw.ReportMessage("Error: Local manifest " + SourceKey.InSingleQuotes() + " does not exist.");
			return false;
		}
		ReleaseManifest parsed = await ReleaseHelpers.TryGetReleaseManfiestAsync(s3u, SourceKey);
		if (parsed == null)
		{
			cw.ReportMessage("Error: Local manifest " + SourceKey.InSingleQuotes() + " could not be parsed.");
			return false;
		}
		cw.ReportMessage("Local Manifest " + SourceKey.InSingleQuotes() + ":");
		cw.ReportMessage(parsed.Details.SerializeJSON());
		ReleaseCollection releaseCollection;
		if (await s3u.ObjectExists(GlobalManifestKey))
		{
			cw.ReportMessage("Global manifest " + GlobalManifestKey.InSingleQuotes() + " already exists.");
			releaseCollection = await ReleaseHelpers.TryGetReleaseCollectionAsync(s3u, GlobalManifestKey);
			if (releaseCollection == null)
			{
				cw.ReportMessage("Error: Global manifest " + GlobalManifestKey.InSingleQuotes() + " could not be parsed.");
				return false;
			}
		}
		else
		{
			cw.ReportMessage("Global manifest " + GlobalManifestKey.InSingleQuotes() + " does not exist, creating a new one.");
			releaseCollection = new ReleaseCollection();
		}
		ReleaseDetails releaseDetails = releaseCollection.AllReleases.FirstOrDefault((ReleaseDetails rd) => rd.Version == parsed.Details.Version);
		if (releaseDetails != null)
		{
			if (!Force)
			{
				cw.ReportMessage("ERROR: Entry with matching version " + parsed.Details.Version.InParentheses() + " found. If you wish to overwrite this entry, re-run with '--force'");
				return false;
			}
			cw.ReportMessage("Entry with matching version " + parsed.Details.Version.InParentheses() + " found, overwriting...");
			releaseCollection.AllReleases.Remove(releaseDetails);
		}
		releaseCollection.AllReleases.Insert(0, parsed.Details);
		await ReleaseHelpers.PushReleaseCollectionAsync(s3u, GlobalManifestKey, releaseCollection, DryRun, cw);
		cw.ReportMessage("Complete!");
		return true;
	}
}
