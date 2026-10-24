using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Ras.Layers;
using Ras.Updates;
using Utility.Communication;
using Utility.Extensions;
using Utility.Files;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Helpers;

public class ProjectHelpers
{
	private static readonly MessageClass InvalidManifest = new MessageClass("S3_InvalidManifest", MessageSeverity.Error, "ERROR: Invalid manifest");

	private static readonly MessageClass ShaDisagreement = new MessageClass("S3_ShaDisagreement", MessageSeverity.Error, "ERROR: SHA disagreement on {Filename} {ShaHashA} vs {ShaHashB}");

	public static async Task<(OperationResult, ExampleProjectManifest)> PullProject(PullProjectParams pullParams)
	{
		if (pullParams.Manifest == null && string.IsNullOrEmpty(pullParams.ManifestKey))
		{
			return (OperationResult.Fail(InvalidManifest.Create()), null);
		}
		S3TransferHelper s3u = GetS3(pullParams.Server, pullParams.Bucket, pullParams.S3Key, pullParams.S3Secret);
		try
		{
			ProgressReporter reporter = pullParams.Reporter ?? (pullParams.ConsoleOutput ? ProgressReporter.ConsoleWrite() : null);
			if (Directory.Exists(pullParams.Directory))
			{
				if (Directory.GetFiles(pullParams.Directory).Length != 0)
				{
					if (!pullParams.ForceOverwriteExisting)
					{
						TryConsoleWrite(reporter, "ERROR: Directory " + pullParams.Directory.InSingleQuotes() + " already exists. If you intend to overwrite this directory, re-run with '--force'.");
						return (OperationResult.Fail(FileMessageCodes.GetDirectoryNotFound(pullParams.Directory)), null);
					}
					TryConsoleWrite(reporter, DryRunPrefix(pullParams.DryRun, "Cleaning existing directory " + pullParams.Directory.InSingleQuotes() + "..."));
					if (!pullParams.DryRun)
					{
						Directory.Delete(pullParams.Directory, recursive: true);
						Directory.CreateDirectory(pullParams.Directory);
					}
				}
			}
			else
			{
				Directory.CreateDirectory(pullParams.Directory);
			}
			if (string.IsNullOrEmpty(pullParams.CacheDirectory))
			{
				pullParams.CacheDirectory = RasSpecialDirectories.ExampleProjectCacheDirectory;
			}
			TryConsoleWrite(reporter, "Using cache directory " + pullParams.CacheDirectory.InSingleQuotes());
			ExampleProjectManifest manifest = pullParams.Manifest;
			if (manifest == null)
			{
				string tmpFn = Path.GetTempFileName();
				await s3u.PullSingleFileAsync(tmpFn, pullParams.ManifestKey);
				manifest = ExampleProjectManifest.DeserializeJSON(File.ReadAllText(tmpFn));
			}
			HashSet<string> dupes = new HashSet<string>();
			ParallelOptions parallelOptions = new ParallelOptions
			{
				MaxDegreeOfParallelism = 16
			};
			OperationResult hashFailOp = null;
			int totalFiles = manifest.Files.Count;
			int completedFiles = 0;
			reporter?.ReportMessage($"Downloading {totalFiles} file(s)...");
			await Parallel.ForAsync(0, manifest.Files.Count, parallelOptions, async (int fIdx, CancellationToken token) =>
			{
				if (!(reporter?.CancelRequested ?? false))
				{
					MappedFile mappedFile2 = manifest.Files[fIdx];
					lock (dupes)
					{
						if (!dupes.Add(mappedFile2.SHA256))
						{
							return;
						}
					}
					string path = Path.Combine(pullParams.CacheDirectory, mappedFile2.SHA256);
					TryConsoleWrite(reporter, path);
					if (File.Exists(path))
					{
						MappedFile mappedFile3 = MappedFile.ForFile(path, pullParams.CacheDirectory);
						if (mappedFile3.SHA256 != mappedFile2.SHA256)
						{
							TryConsoleWrite(reporter, "ERROR: SHA256 disagreement with " + mappedFile2.Filename.InSingleQuotes() + ", exiting...");
							hashFailOp = OperationResult.Fail(ShaDisagreement.Create(mappedFile2.Filename, mappedFile3.SHA256, mappedFile2.SHA256));
						}
						else
						{
							TryConsoleWrite(reporter, "Using cached: " + mappedFile2.Filename.InSingleQuotes());
							reporter?.ReportProgressFraction(Interlocked.Increment(ref completedFiles), totalFiles);
						}
					}
					else
					{
						if (!pullParams.DryRun)
						{
							await s3u.PullSingleFileAsync(path, mappedFile2.SHA256);
						}
						reporter?.ReportProgressFraction(Interlocked.Increment(ref completedFiles), totalFiles);
					}
				}
			});
			if (reporter?.CancelRequested ?? false)
			{
				return (OperationResult.Fail("Download cancelled."), null);
			}
			if (hashFailOp != null)
			{
				return (hashFailOp, null);
			}
			if (!pullParams.DryRun)
			{
				for (int intervalIndex = 0; intervalIndex < manifest.Files.Count; intervalIndex++)
				{
					MappedFile mappedFile = manifest.Files[intervalIndex];
					string path2 = Path.Combine(pullParams.CacheDirectory, mappedFile.SHA256);
					string path3 = Path.Combine(pullParams.Directory, mappedFile.Filename);
					string sourceFileName = path2.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
					path3 = path3.Replace('\\', Path.DirectorySeparatorChar);
					path3 = path3.Replace('/', Path.DirectorySeparatorChar);
					Directory.CreateDirectory(Path.GetDirectoryName(path3));
					File.Copy(sourceFileName, path3, overwrite: true);
				}
			}
			TryConsoleWrite(reporter, DryRunPrefix(pullParams.DryRun, manifest.Files.Count + " files copied to " + pullParams.Directory.InSingleQuotes()));
			return (OperationResult.Success(), manifest);
		}
		finally
		{
			if (s3u != null)
			{
				((IDisposable)s3u).Dispose();
			}
		}
	}

	public static async Task<(OperationResult, ExampleProjectManifest)> PushProject(PushProjectParams pushParams)
	{
		ProgressReporter reporter = pushParams.Reporter ?? (pushParams.ConsoleOutput ? ProgressReporter.ConsoleWrite() : null);
		if (!Directory.Exists(pushParams.Directory))
		{
			TryConsoleWrite(reporter, "ERROR: Directory does not exist.");
			return (OperationResult.Fail(FileMessageCodes.GetDirectoryNotFound(pushParams.Directory)), null);
		}
		if (string.IsNullOrEmpty(pushParams.GlobalManifestKey))
		{
			TryConsoleWrite(reporter, "ERROR: invalid global manifest key.");
			return (OperationResult.Fail(InvalidManifest.Create()), null);
		}
		Project prj = new Project(pushParams.Directory);
		string title = Path.GetFileNameWithoutExtension(prj.Filename);
		using S3TransferHelper s3u = GetS3(pushParams.Server, pushParams.Bucket, pushParams.S3Key, pushParams.S3Secret);
		if (pushParams.PublishDate == DateTime.MinValue)
		{
			pushParams.PublishDate = DateTime.Now;
		}
		string publishDate = pushParams.PublishDate.ToString("yyyy-MMM-dd HH-mm-ss");
		string sanitizedJsonKey = title + "-" + publishDate + ".json";
		if (await s3u.ObjectExists(sanitizedJsonKey))
		{
			if (!pushParams.ForceOverwriteExisting)
			{
				TryConsoleWrite(reporter, "ERROR: Manifest JSON file " + sanitizedJsonKey.InSingleQuotes() + " already exists. If you intend to overwrite this manifest, re-run with '--force'");
				return (OperationResult.Fail(FileMessageCodes.GetFileNotFound(sanitizedJsonKey)), null);
			}
			TryConsoleWrite(reporter, "Manifest JSON file " + sanitizedJsonKey.InSingleQuotes() + " already exists. (Will be overwritten)");
		}
		string thumbnailKey = "";
		string path = Path.Combine(pushParams.Directory, title + ".thumbnail.png");
		if (File.Exists(path))
		{
			using FileStream source = new FileStream(path, FileMode.Open, FileAccess.Read);
			thumbnailKey = Convert.ToHexString(SHA256.HashData(source));
		}
		List<string> tags = new List<string>();
		tags.Add("2D");
		if (prj.BoundaryConditions.Any((BoundaryCondition bc) => bc.Precipitation.IsReallyEnabledForCompute()))
		{
			tags.Add("Precipitation");
		}
		ExampleProjectDetails details = new ExampleProjectDetails
		{
			Title = title,
			Description = prj.Description,
			Tags = tags,
			PublishDate = pushParams.PublishDate,
			MinimumVersion = pushParams.MinimumVersion,
			MaximumVersion = pushParams.MaximumVersion,
			Server = pushParams.Server,
			Bucket = pushParams.Bucket,
			ThumbnailKey = thumbnailKey,
			ManifestJSONKey = sanitizedJsonKey
		};
		TryConsoleWrite(reporter, "Hashing all files in " + pushParams.Directory.InDoubleQuotes() + "...");
		ExampleProjectManifest manifest = ExampleProjectManifest.ForDirectory(pushParams.Directory, details, reporter);
		string asJson = manifest.SerializeJSON();
		if (!string.IsNullOrEmpty(pushParams.JsonOutputPath))
		{
			File.WriteAllText(pushParams.JsonOutputPath, asJson);
			TryConsoleWrite(reporter, "JSON manifest written to " + pushParams.JsonOutputPath.InDoubleQuotes());
		}
		TryConsoleWrite(reporter, "Uploading...");
		int nReuse = 0;
		long bytesUploaded = 0L;
		Stopwatch sw = Stopwatch.StartNew();
		for (int fIdx = 0; fIdx < manifest.Files.Count; fIdx++)
		{
			if (reporter != null && reporter.CancelRequested)
			{
				return (OperationResult.Fail("Push cancelled."), null);
			}
			MappedFile file = manifest.Files[fIdx];
			string sHA = file.SHA256;
			if (await s3u.ObjectExists(sHA))
			{
				TryConsoleWrite(reporter, "Exists: " + file.Filename + " " + file.SHA256.InParentheses());
				Interlocked.Increment(ref nReuse);
			}
			else
			{
				TryConsoleWrite(reporter, DryRunPrefix(pushParams.DryRun, "Uploading... " + file.Filename + " as " + file.SHA256));
				Interlocked.Add(ref bytesUploaded, file.Length);
				if (!pushParams.DryRun)
				{
					string filename = Path.Combine(pushParams.Directory, file.Filename);
					await s3u.PushSingleFileAsync(filename, file.SHA256);
				}
			}
			reporter?.ReportProgressFraction(fIdx + 1, manifest.Files.Count);
		}
		sw.Stop();
		TryConsoleWrite(reporter, $"Uploaded {FileHelpers.PrettyByteCount(bytesUploaded)} in {sw.Elapsed.ToString()} ({nReuse} / {manifest.Files.Count} files already present on server)");
		TryConsoleWrite(reporter, DryRunPrefix(pushParams.DryRun, "Uploading JSON Manifest to " + sanitizedJsonKey.InSingleQuotes()));
		if (!pushParams.DryRun)
		{
			string tmpfn = Path.GetTempFileName();
			await File.WriteAllTextAsync(tmpfn, asJson);
			await s3u.PushSingleFileAsync(tmpfn, sanitizedJsonKey);
			File.Delete(tmpfn);
		}
		TryConsoleWrite(reporter, details.SerializeJSON());
		if (pushParams.SkipGlobalManifest)
		{
			return (OperationResult.Success(), manifest);
		}
		string tmpFn = Path.GetTempFileName();
		ExampleProjectCollection exampleProjectCollection;
		if (await s3u.ObjectExists(pushParams.GlobalManifestKey))
		{
			TryConsoleWrite(reporter, "Global manifest " + pushParams.GlobalManifestKey.InSingleQuotes() + " already exists.");
			await s3u.PullSingleFileAsync(tmpFn, pushParams.GlobalManifestKey);
			exampleProjectCollection = ExampleProjectCollection.DeserializeJSON(File.ReadAllText(tmpFn));
		}
		else
		{
			TryConsoleWrite(reporter, "Global manifest " + pushParams.GlobalManifestKey.InSingleQuotes() + " does not exist, creating a new one.");
			exampleProjectCollection = new ExampleProjectCollection();
		}
		ExampleProjectDetails exampleProjectDetails = exampleProjectCollection.AllProjects.FirstOrDefault((ExampleProjectDetails rd) => rd.ManifestJSONKey == details.ManifestJSONKey);
		if (exampleProjectDetails != null)
		{
			if (!pushParams.ForceOverwriteExisting)
			{
				TryConsoleWrite(reporter, "ERROR: Entry with matching version " + details.ManifestJSONKey.InParentheses() + " found. If you wish to overwrite this entry, re-run with '--force'");
				File.Delete(tmpFn);
				return (OperationResult.Fail(FileMessageCodes.GetFileNotFound(details.ManifestJSONKey)), null);
			}
			TryConsoleWrite(reporter, "Entry with matching version " + details.ManifestJSONKey.InParentheses() + " found, overwriting...");
			exampleProjectCollection.AllProjects.Remove(exampleProjectDetails);
		}
		exampleProjectCollection.AllProjects.Insert(0, details);
		await File.WriteAllTextAsync(tmpFn, exampleProjectCollection.SerializeJSON());
		TryConsoleWrite(reporter, DryRunPrefix(pushParams.DryRun, "Uploading global manifest file to " + pushParams.GlobalManifestKey.InSingleQuotes() + "..."));
		if (!pushParams.DryRun)
		{
			await s3u.PushSingleFileAsync(tmpFn, pushParams.GlobalManifestKey);
		}
		File.Delete(tmpFn);
		TryConsoleWrite(reporter, "Complete!");
		return (OperationResult.Success(), manifest);
	}

	private static void TryConsoleWrite(ProgressReporter reporter, string message)
	{
		reporter?.ReportMessage(message);
	}

	private static S3TransferHelper GetS3(string server, string bucket, string user, string password)
	{
		return new S3TransferHelper(server, bucket, user, password, forcePathStyle: true);
	}

	private static string DryRunPrefix(bool dryRun, string msg)
	{
		if (dryRun)
		{
			return "(SKIPPING - DRYRUN) " + msg;
		}
		return msg;
	}
}
