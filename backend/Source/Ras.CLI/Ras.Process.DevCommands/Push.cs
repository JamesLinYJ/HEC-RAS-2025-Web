using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using Ras.Updates;
using Utility.Communication;
using Utility.Extensions;
using Utility.Files;
using Utility.Progress;

namespace Ras.Process.DevCommands;

[Verb("push", false, null, HelpText = "Uploads the software in the given directory and builds a manifest. Does not update the global manifest.")]
public class Push : BaseDevAuthCommand
{
	public const string Verb = "push";

	[Option("write-to", Required = false, HelpText = "If set, write the manifest JSON to this location. Can be used with '--dry-run'")]
	public string WriteJSON { get; set; }

	[Option('f', "force", Required = false, HelpText = "If set, any existing JSON manifest will be overwritten. Default behavior is to return an error if a manifest with the expected key is present on the remote server.")]
	public bool ForceOverwriteExisting { get; set; }

	[Option('d', "directory", Required = true, HelpText = "Location of the code to scan and upload.")]
	public string Directory { get; set; }

	[Option('n', "name", Required = true, HelpText = "Friendly version name, e.g. 'RAS 2.0 Beta 2'")]
	public string Name { get; set; }

	[Option('v', "version", Required = true, HelpText = "SemVer compatible version number, e.g. '2.0.0.412-beta2'. Mutually exclusive with '--version-from'.", SetName = "version")]
	public string Version { get; set; }

	[Option("version-from", Required = true, HelpText = "Filename to scrape version information from. This is path-relative to the 'directory' argument. Mutually exclusive with '--version'.", SetName = "version-from")]
	public string VersionFromFile { get; set; }

	[Option("date", Required = false, HelpText = "Release date. Any number of formats are acceptible, e.g. '05Jan2025'")]
	public DateTime ReleaseDate { get; set; }

	[Option("release-notes-url", Required = false, HelpText = "URL for full release notes, e.g. 'https://hec.usace.army.mil/ras/releases/2_0_1099.html'")]
	public string ReleaseNotesURL { get; set; }

	[Option("summary-file", Required = false, HelpText = "Summary release notes, small enough to fit into the client. '-Item 1\\n-And here's item 2\\n-Hey look it's faster'")]
	public string ReleaseNotesSummaryFile { get; set; }

	public async Task<bool> ExecuteAsync()
	{
		S3TransferHelper s3u = GetS3();
		try
		{
			ProgressReporter cw = ProgressReporter.ConsoleWrite();
			if (string.IsNullOrEmpty(Version))
			{
				string path = Path.Combine(Directory, VersionFromFile);
				if (!File.Exists(path))
				{
					cw.ReportMessage("Expected version file missing: " + VersionFromFile.InDoubleQuotes());
					return false;
				}
				FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(path);
				Version = versionInfo.ProductVersion;
			}
			string sanitizedJsonKey = BuildManifestKeyFromVersion(Version);
			if (await s3u.ObjectExists(sanitizedJsonKey))
			{
				if (!ForceOverwriteExisting)
				{
					cw.ReportMessage("ERROR: Manifest JSON file " + sanitizedJsonKey.InSingleQuotes() + " already exists. If you intend to overwrite this manifest, re-run with '--force'");
					return false;
				}
				cw.ReportMessage("Manifest JSON file " + sanitizedJsonKey.InSingleQuotes() + " already exists. (Will be overwritten)");
			}
			if (ReleaseDate == DateTime.MinValue)
			{
				ReleaseDate = DateTime.Now;
			}
			string releaseNotesSummary = "";
			if (!string.IsNullOrEmpty(ReleaseNotesSummaryFile))
			{
				if (!File.Exists(ReleaseNotesSummaryFile))
				{
					cw.ReportMessage("ERROR: Release notes file " + ReleaseNotesSummaryFile.InSingleQuotes() + " does not exist.");
					return false;
				}
				releaseNotesSummary = File.ReadAllText(ReleaseNotesSummaryFile);
			}
			ReleaseDetails details = new ReleaseDetails
			{
				Name = Name,
				Type = ParseFromVersion(Version),
				Version = Version,
				Server = Server,
				Bucket = Bucket,
				ReleaseDate = ReleaseDate,
				ReleaseNotesSummary = releaseNotesSummary,
				ReleaseNotesURL = ReleaseNotesURL,
				ManifestJSONKey = sanitizedJsonKey
			};
			cw.ReportMessage("Hashing all files in " + Directory.InDoubleQuotes() + "...");
			ReleaseManifest manifest = ReleaseManifest.ForDirectory(Directory, details, cw);
			string asJson = manifest.SerializeJSON();
			if (!string.IsNullOrEmpty(WriteJSON))
			{
				File.WriteAllText(WriteJSON, asJson);
				cw.ReportMessage("JSON manifest written to " + WriteJSON.InDoubleQuotes());
			}
			cw.ReportMessage("Uploading...");
			int nReuse = 0;
			long bytesUploaded = 0L;
			Stopwatch sw = Stopwatch.StartNew();
			ParallelOptions parallelOptions = new ParallelOptions
			{
				MaxDegreeOfParallelism = 16
			};
			await Parallel.ForAsync(0, manifest.Files.Count, parallelOptions, async (int fIdx, CancellationToken token) =>
			{
				MappedFile file = manifest.Files[fIdx];
				string sHA = file.SHA256;
				if (await s3u.ObjectExists(sHA))
				{
					cw.ReportMessage("Exists: " + file.Filename + " " + file.SHA256.InParentheses());
					Interlocked.Increment(ref nReuse);
				}
				else
				{
					cw.ReportMessage(DryRunPrefix("Uploading... " + file.Filename + " as " + file.SHA256));
					Interlocked.Add(ref bytesUploaded, file.Length);
					if (!DryRun)
					{
						string filename = Path.Combine(Directory, file.Filename);
						await s3u.PushSingleFileAsync(filename, file.SHA256);
					}
				}
			});
			sw.Stop();
			cw.ReportMessage("Uploaded " + FileHelpers.PrettyByteCount(bytesUploaded) + " in " + sw.Elapsed.ToString() + " (" + nReuse + "/" + manifest.Files.Count + " files already present on server)");
			cw.ReportMessage(DryRunPrefix("Uploading JSON Manifest to " + sanitizedJsonKey.InSingleQuotes()));
			if (!DryRun)
			{
				string tmpfn = Path.GetTempFileName();
				await File.WriteAllTextAsync(tmpfn, asJson);
				await s3u.PushSingleFileAsync(tmpfn, sanitizedJsonKey);
				File.Delete(tmpfn);
			}
			cw.ReportMessage(details.SerializeJSON());
			cw.ReportMessage("Upload Complete!");
			cw.ReportMessage("");
			cw.ReportMessage("To download & test this release, run the following command: ");
			string getFileNameWithoutExtensionPathTerm = Path.Combine(RasSpecialDirectories.TempDirectory, "ras-test-" + Path.GetFileNameWithoutExtension(sanitizedJsonKey));
			if (OperatingSystem.IsMacOS())
			{
				getFileNameWithoutExtensionPathTerm = Path.Combine(RasSpecialDirectories.RootAppDataDirectory, "TestBuilds", "ras-test-" + Path.GetFileNameWithoutExtension(sanitizedJsonKey));
				cw.ReportMessage("> ras dev pull --force -m " + sanitizedJsonKey.InDoubleQuotes() + " -d " + getFileNameWithoutExtensionPathTerm.InDoubleQuotes() + " && open -a " + Path.Combine(getFileNameWithoutExtensionPathTerm, "HEC-RAS 2025.app").InDoubleQuotes());
			}
			else
			{
				cw.ReportMessage("> ras dev pull --force -m " + sanitizedJsonKey.InDoubleQuotes() + " -d " + getFileNameWithoutExtensionPathTerm.InDoubleQuotes() + " && explorer.exe " + getFileNameWithoutExtensionPathTerm.InDoubleQuotes() + " && " + Path.Combine(getFileNameWithoutExtensionPathTerm, "ras.exe").InDoubleQuotes());
			}
			cw.ReportMessage("");
			cw.ReportMessage("To push this release to all users, run the following command: ");
			cw.ReportMessage("> ras dev release -m " + sanitizedJsonKey.InDoubleQuotes() + " -u USERNAME -p PASSWORD");
			return true;
		}
		finally
		{
			if (s3u != null)
			{
				((IDisposable)s3u).Dispose();
			}
		}
	}

	public static string BuildManifestKeyFromVersion(string version)
	{
		string version2 = version;
		if (version2.Contains('-'))
		{
			version2 = version2.Substring(0, version2.IndexOf('-'));
		}
		return "ras-" + version2.Replace('.', '_') + ".json";
	}

	private static ReleaseType ParseFromVersion(string version)
	{
		if (version.Contains('-'))
		{
			string[] parts = version.Split('-');
			if (parts.Length != 2)
			{
				throw new Exception("Malformed version number, expected 0 or 1 hyphens.");
			}
			string part = parts[1];
			if (part.Contains("alpha"))
			{
				return ReleaseType.Alpha;
			}
			if (part.Contains("beta"))
			{
				return ReleaseType.Beta;
			}
			return ReleaseType.Nightly;
		}
		return ReleaseType.Official;
	}
}
