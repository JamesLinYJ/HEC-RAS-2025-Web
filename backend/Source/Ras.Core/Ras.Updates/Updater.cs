using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.Communication;
using Utility.Extensions;
using Utility.Logging;
using Utility.Processes;
using Utility.Progress;
using Utility.Web;

namespace Ras.Updates;

public static class Updater
{
	public const string DefaultS3Server = "https://s3.hecdev.net";

	public const string AlphaReleasesS3Bucket = "ras-public-releases";

	public const string BetaReleasesS3Bucket = "ras-public-releases";

	public const string OfficialReleasesS3Bucket = "ras-official-releases";

	public const string StagingReleasesS3Bucket = "ras-public-releases-staging";

	public const string NightlyReleasesS3Bucket = "ras-live-builds";

	public const string DefaultGlobalManifestKey = "globalmanifest.json";

	private static readonly ILogger _logger = LogHelper.GetLogger("Updater");

	public static async Task<(bool success, Func<Task> applyUpdate)> TryUpdateInPlace(string version, bool launchExeOnReboot = true, string overwriteGlobalManifest = null, ProgressReporter prog = null, string overrideBucket = null, bool backupOldVersion = false, string user = null, string password = null)
	{
		string curExe = Assembly.GetEntryAssembly().Location;
		curExe = Path.GetFullPath(curExe);
		string curExeDir = Path.GetDirectoryName(curExe);
		if (Path.GetExtension(curExe) != ".exe")
		{
			curExe = Path.ChangeExtension(curExe, ".exe");
		}
		if (!DirectoryHelpers.CanWrite(curExeDir))
		{
			throw new Exception("Cannot write to target directory " + curExeDir + " - Is this an admin protected folder?");
		}
		ConsoleProgressParser consoleProgressParser = new ConsoleProgressParser();
		if (prog != null)
		{
			consoleProgressParser.MessageReported += (ProgressReporter sender, Message msg) =>
			{
				prog.ReportMessage(msg);
			};
			consoleProgressParser.ProgressReported += (ProgressReporter sender, int v) =>
			{
				prog.ReportProgress(v);
			};
		}
		string extractDirectory = Path.Combine(RasSpecialDirectories.TempRasDirectory, Guid.NewGuid().ToString());
		string inDoubleQuotesOverwriteGlobalManifestTerm = "";
		if (!string.IsNullOrEmpty(overwriteGlobalManifest))
		{
			inDoubleQuotesOverwriteGlobalManifestTerm = "-g " + overwriteGlobalManifest.InDoubleQuotes();
		}
		string inDoubleQuotesOverrideBucketTerm = "";
		if (!string.IsNullOrEmpty(overrideBucket))
		{
			inDoubleQuotesOverrideBucketTerm = "-b " + overrideBucket.InDoubleQuotes();
		}
		Process process = ((!string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(password)) ? (await ProcessHelper.RunAsync("ras.exe", $"dev pull -u {user} -p {password} -v {version.InDoubleQuotes()} -d {extractDirectory.InDoubleQuotes()} {inDoubleQuotesOverwriteGlobalManifestTerm} {inDoubleQuotesOverrideBucketTerm}", consoleProgressParser)) : (await ProcessHelper.RunAsync("ras.exe", $"dev pull -v {version.InDoubleQuotes()} -d {extractDirectory.InDoubleQuotes()} {inDoubleQuotesOverwriteGlobalManifestTerm} {inDoubleQuotesOverrideBucketTerm}", consoleProgressParser)));
		if (process.ExitCode != 0)
		{
			return (success: false, applyUpdate: null);
		}
		string tempMoveDir = Path.Combine(RasSpecialDirectories.TempRasDirectory, Guid.NewGuid().ToString());
		int attempts = 18;
		int delay = 5000;
		int curAttempts = 0;
		bool moved = false;
		while (!moved && curAttempts++ < attempts)
		{
			try
			{
				Directory.Move(extractDirectory, tempMoveDir);
				moved = true;
			}
			catch
			{
				prog.ReportMessage($"Trying to access downloaded files... (attempt {curAttempts})");
				await Task.Delay(delay);
			}
		}
		extractDirectory = tempMoveDir;
		prog.ReportMessage("Opening updater");
		Func<Task> combinePathTerm = async () =>
		{
			string updateHelperPath = Path.Combine(RasSpecialDirectories.TempDirectory, "RasUpdateHelper.exe");
			using HttpClient getHelperClient = new HttpClient();
			HttpResponseMessage httpResponseMessage = await getHelperClient.GetAsync(HECFWLink.BuildURL("ras-2025-update-helper"));
			if (!httpResponseMessage.IsSuccessStatusCode)
			{
				throw new Exception("Could not get update helper executable");
			}
			byte[] bytes = await httpResponseMessage.Content.ReadAsByteArrayAsync();
			if (File.Exists(updateHelperPath))
			{
				File.Delete(updateHelperPath);
			}
			await File.WriteAllBytesAsync(updateHelperPath, bytes);
			string path = Path.Combine(curExeDir, Path.GetFileName(curExe));
			if (!launchExeOnReboot || Path.GetExtension(curExe) != ".exe")
			{
				path = null;
			}
			string arguments = $"-s {extractDirectory.InDoubleQuotes()} -d {curExeDir.InDoubleQuotes()} ";
			if (path != null)
			{
				arguments = arguments + " -r " + path.InDoubleQuotes();
			}
			if (backupOldVersion)
			{
				arguments += " --no-clean";
			}
			Process.Start(new ProcessStartInfo
			{
				FileName = updateHelperPath,
				WorkingDirectory = RasSpecialDirectories.TempDirectory,
				UseShellExecute = true,
				Arguments = arguments
			});
		};
		return (success: true, applyUpdate: combinePathTerm);
	}

	public static async Task<ReleaseCollection> TryGetReleasesAsync(string overrideManifestKey = "", string overrideBucket = null, string user = "", string password = "")
	{
		_ = 1;
		try
		{
			using S3TransferHelper s3u = GetS3(overrideBucket, user, password);
			string manifestKey = (string.IsNullOrEmpty(overrideManifestKey) ? "globalmanifest.json" : overrideManifestKey);
			if (await s3u.ObjectExists(manifestKey))
			{
				_logger.LogInformation("Global manifest {ManifestKey} found on {Server} / {Bucket}.", manifestKey, "https://s3.hecdev.net", "ras-public-releases");
				string tmpFn = RasSpecialDirectories.GetTempFile();
				await s3u.PullSingleFileAsync(tmpFn, manifestKey);
				ReleaseCollection result = ReleaseCollection.DeserializeJSON(File.ReadAllText(tmpFn));
				File.Delete(tmpFn);
				return result;
			}
			_logger.LogWarning("Global manifest {ManifestKey} does not exist on {Server} / {Bucket}.", manifestKey, "https://s3.hecdev.net", "ras-public-releases");
			return null;
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Error checking for update manifest.");
			return null;
		}
	}

	private static S3TransferHelper GetS3(string overrideBucket = null, string user = "", string password = "")
	{
		return new S3TransferHelper("https://s3.hecdev.net", (overrideBucket == null) ? "ras-public-releases" : overrideBucket, user, password, forcePathStyle: true);
	}
}
