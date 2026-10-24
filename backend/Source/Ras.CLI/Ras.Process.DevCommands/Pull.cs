using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using Ras.Updates;
using Utility.Communication;
using Utility.Extensions;
using Utility.Files;
using Utility.Progress;

namespace Ras.Process.DevCommands;

[Verb("pull", false, null, HelpText = "Downloads the software and extracts it to the given directory.")]
public class Pull : BaseDevCommand
{
	public const string Verb = "pull";

	[Option('d', "destination", Required = true, HelpText = "Directory for the extracted files.")]
	public string Destination { get; set; }

	[Option('c', "cache", Required = false, HelpText = "Location for an intermediate cache directory. If unset, defaults to a folder in your temp directory.")]
	public string CacheDirectory { get; set; }

	[Option("skip-cache", Required = false, HelpText = "If set, ignores the cache directory and downloads all necessary files.")]
	public bool SkipCache { get; set; }

	[Option('f', "force", Required = false, HelpText = "If set, destination directory will be wiped (if needed).")]
	public bool ForceOverwriteExisting { get; set; }

	[Option('m', "manifest", Required = true, HelpText = "Key containing a Manifest JSON file. Mutually exclusive with --version.", SetName = "manifest")]
	public string ManifestKey { get; set; }

	[Option("parallel", Required = false, HelpText = "Degree of parallelism for S3 pulls. Defaults to 16.")]
	public int DegreeOfParallelism { get; set; } = 16;

	[Option('v', "version", Required = true, SetName = "version", HelpText = "Version string to look up in the global manifest. (e.g. '1.0.0.411').This can include wildcards for the most recent version within a release line. For example, '-v 1.*' will return the most recent stable release with Major=1, e.g. '1.4.2'. Wildcard versions will only return a version with the highest degree of stability available in that line. This means that if ANY official release is avaliable in the 1.* line, we will not return a build with lower stability (nightly, alpha, beta). In the prior example, 1.5-beta2 would not be eligible because 1.4.2 was an official release. To get the 1.5-beta2 build, you can either request the raw version string (1.5.0.1419-beta2), use a more specific wildcard (1.5.*) or change the target stability with '--target-stability beta'.")]
	public string Version { get; set; }

	[Option("target-stability", Required = false, HelpText = "Allowable values are ['official', 'beta', 'alpha', 'nightly']. Used in combination with --version to control wildcard character interpretation. Defaults to 'official'.")]
	public string TargetStability { get; set; }

	[Option('g', "global", Required = false, HelpText = "Key to the Global JSON manifest. Defaults to 'globalmanifest.json'", SetName = "version")]
	public string GlobalManifest { get; set; } = "globalmanifest.json";

	[Option('u', "user", Required = false, HelpText = "Username for S3 authentication. Authentication is only necessary for some buckets.")]
	public string User { get; set; }

	[Option('p', "password", Required = false, HelpText = "Password for S3 authentication. Authentication is only necessary for some buckets.")]
	public string Password { get; set; }

	public override S3TransferHelper GetS3()
	{
		return new S3TransferHelper(Server, Bucket, User, Password, forcePathStyle: true);
	}

	public async Task<bool> ExecuteAsync()
	{
		using S3TransferHelper s3u = GetS3();
		ProgressReporter cw = ProgressReporter.ConsoleWrite();
		if (string.IsNullOrEmpty(Destination))
		{
			cw.ReportMessage("Error - destination directory not set.");
			return false;
		}
		if (Directory.Exists(Destination))
		{
			if (Directory.GetFiles(Destination).Length != 0)
			{
				if (!ForceOverwriteExisting)
				{
					cw.ReportMessage("ERROR: Directory " + Destination.InSingleQuotes() + " already exists. If you intend to overwrite this directory, re-run with '--force'.");
					return false;
				}
				cw.ReportMessage(DryRunPrefix("Cleaning existing directory " + Destination.InSingleQuotes() + "..."));
				if (!DryRun)
				{
					Directory.Delete(Destination, recursive: true);
					Directory.CreateDirectory(Destination);
				}
			}
		}
		else
		{
			Directory.CreateDirectory(Destination);
		}
		if (SkipCache)
		{
			CacheDirectory = Path.Combine(RasSpecialDirectories.TempDirectory, "CACHE_" + Guid.NewGuid());
			cw.ReportMessage("No caching.");
		}
		else
		{
			if (string.IsNullOrEmpty(CacheDirectory))
			{
				CacheDirectory = RasSpecialDirectories.UpdaterCacheDirectory;
			}
			cw.ReportMessage("Using cache directory " + CacheDirectory.InSingleQuotes());
		}
		string manifestKey = ManifestKey;
		if (string.IsNullOrEmpty(manifestKey))
		{
			if (!(await s3u.ObjectExists(GlobalManifest)))
			{
				cw.ReportMessage("ERROR: Global manifest not found in the S3 Bucket. " + GlobalManifest.InSingleQuotes());
				return false;
			}
			ReleaseDetails releaseDetails = (await BaseDevCommand.PullDeserialize(s3u, GlobalManifest, (string s) => ReleaseCollection.DeserializeJSON(s))).AllReleases.FirstOrDefault((ReleaseDetails v) => v.Version == Version);
			if (releaseDetails == null)
			{
				string possibleKey = Push.BuildManifestKeyFromVersion(Version);
				if (!(await s3u.ObjectExists(possibleKey)))
				{
					cw.ReportMessage("ERROR: No suitable manifest found for '" + Version + "'.");
					cw.ReportMessage("  Checked: Global Manifest '" + GlobalManifest + "'.");
					cw.ReportMessage("  Checked: Default Manifest file '" + possibleKey + "'.");
					return false;
				}
				manifestKey = possibleKey;
			}
			else
			{
				manifestKey = releaseDetails.ManifestJSONKey;
			}
		}
		ReleaseManifest versionManifest = await BaseDevCommand.PullDeserialize(s3u, manifestKey, (string s) => ReleaseManifest.DeserializeJSON(s));
		if (!(await DownloadFiles(versionManifest, s3u, cw)))
		{
			return false;
		}
		if (!DryRun)
		{
			for (int intervalIndex = 0; intervalIndex < versionManifest.Files.Count; intervalIndex++)
			{
				MappedFile mappedFile = versionManifest.Files[intervalIndex];
				string sourceFileName = Path.Combine(CacheDirectory, mappedFile.SHA256);
				string path = Path.Combine(Destination, mappedFile.Filename);
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				File.Copy(sourceFileName, path, overwrite: true);
			}
		}
		cw.ReportMessage(DryRunPrefix(versionManifest.Files.Count + " files copied to " + Destination.InSingleQuotes()));
		if (SkipCache)
		{
			Directory.Delete(CacheDirectory, recursive: true);
		}
		return true;
	}

	private async Task<bool> DownloadFiles(ReleaseManifest versionManifest, S3TransferHelper s3u, ProgressReporter cw)
	{
		bool success = true;
		HashSet<string> dupes = new HashSet<string>();
		int totalCached = 0;
		int totalDownloaded = 0;
		long bytesDownloaded = 0L;
		Stopwatch sw = Stopwatch.StartNew();
		if (DegreeOfParallelism <= 0)
		{
			cw.ReportError("Parallelism argument must be positive.");
			return false;
		}
		ParallelOptions parallelOptions = new ParallelOptions
		{
			MaxDegreeOfParallelism = DegreeOfParallelism
		};
		await Parallel.ForAsync(0, versionManifest.Files.Count, parallelOptions, async (int fIdx, CancellationToken token) =>
		{
			MappedFile file = versionManifest.Files[fIdx];
			lock (dupes)
			{
				if (!dupes.Add(file.SHA256))
				{
					return;
				}
			}
			string path = Path.Combine(CacheDirectory, file.SHA256);
			if (File.Exists(path))
			{
				if (MappedFile.ForFile(path, CacheDirectory).SHA256 != file.SHA256)
				{
					cw.ReportMessage("ERROR: SHA256 disagreement with " + file.Filename.InSingleQuotes() + ", exiting...");
					success = false;
				}
				else
				{
					if (!Verbose)
					{
						lock (dupes)
						{
							totalCached++;
							cw.ReportProgressFraction((float)(totalCached + totalDownloaded) / (float)versionManifest.Files.Count);
							return;
						}
					}
					cw.ReportMessage("Using cached: " + file.Filename.InSingleQuotes());
				}
			}
			else
			{
				if (Verbose)
				{
					cw.ReportMessage(DryRunPrefix("Downloading: " + file.Filename.InSingleQuotes()));
				}
				else
				{
					lock (dupes)
					{
						totalDownloaded++;
						cw.ReportProgressFraction((float)(totalCached + totalDownloaded) / (float)versionManifest.Files.Count);
					}
				}
				if (!DryRun)
				{
					await s3u.PullSingleFileAsync(path, file.SHA256);
					bytesDownloaded += file.Length;
				}
			}
		});
		sw.Stop();
		cw.ReportProgress(100);
		cw.ReportMessage(versionManifest.Files.Count + " files acquired. (" + totalCached + "/" + (totalCached + totalDownloaded) + " from cache)");
		cw.ReportMessage(FileHelpers.PrettyByteCount(bytesDownloaded) + " downloaded");
		cw.ReportMessage("Operation took " + sw.Elapsed);
		return success;
	}
}
