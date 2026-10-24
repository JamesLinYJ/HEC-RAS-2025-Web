using System.IO;
using System.Threading.Tasks;
using Ras.Updates;
using Utility.Communication;
using Utility.Extensions;
using Utility.Files;
using Utility.Progress;

namespace Ras.Process.DevCommands;

public static class ReleaseHelpers
{
	public static async Task<ReleaseManifest> TryGetReleaseManfiestAsync(S3TransferHelper s3u, string key)
	{
		string tmpFn = Path.GetTempFileName();
		await s3u.PullSingleFileAsync(tmpFn, key);
		ReleaseManifest result = ReleaseManifest.DeserializeJSON(File.ReadAllText(tmpFn));
		FileHelpers.TryDelete(tmpFn);
		return result;
	}

	public static async Task<ReleaseCollection> TryGetReleaseCollectionAsync(S3TransferHelper s3u, string key)
	{
		string tmpFn = Path.GetTempFileName();
		await s3u.PullSingleFileAsync(tmpFn, key);
		ReleaseCollection result = ReleaseCollection.DeserializeJSON(File.ReadAllText(tmpFn));
		FileHelpers.TryDelete(tmpFn);
		return result;
	}

	public static async Task PushReleaseCollectionAsync(S3TransferHelper s3u, string key, ReleaseCollection rlc, bool isDryRun, ProgressReporter cw)
	{
		string tmpFn = Path.GetTempFileName();
		await File.WriteAllTextAsync(tmpFn, rlc.SerializeJSON());
		cw.ReportMessage(BaseDevCommand.DryRunPrefix("Uploading global manifest file to " + key.InSingleQuotes() + "...", isDryRun));
		if (!isDryRun)
		{
			await s3u.PushSingleFileAsync(tmpFn, key);
		}
		FileHelpers.TryDelete(tmpFn);
	}
}
