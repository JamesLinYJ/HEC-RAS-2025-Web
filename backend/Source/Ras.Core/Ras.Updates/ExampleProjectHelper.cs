using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Utility.Communication;
using Utility.Extensions;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Updates;

public static class ExampleProjectHelper
{
	public static class MessageCodes
	{
		public static readonly MessageClass S3SHADoesntMatch = new MessageClass("S3_SHADoesntMatch", MessageSeverity.Error, "S3 downloaded file: {Filename} doesn't match SHA256. Expected: {ExpectedSHA}, Actual: {ActualSHA}");

		public static Message GetS3SHADoesntMatch(string filename, string shaExpected, string shaActual)
		{
			return S3SHADoesntMatch.Create(filename, shaExpected, shaActual);
		}
	}

	public const string DefaultS3Server = "https://s3.hecdev.net";

	public const string DefaultS3Bucket = "ras-public-examples";

	public const string DefaultGlobalManifestKey = "globalmanifest.json";

	private static readonly ILogger _logger = LogHelper.GetLogger("ExampleProjectHelper");

	public static async Task<ExampleProjectCollection> TryGetAllExamplesAsync()
	{
		return await TryGetJsonAsT("globalmanifest.json", (string ss) => ExampleProjectCollection.DeserializeJSON(ss));
	}

	public static async Task<ExampleProjectManifest> TryGetExampleManifestAsync(ExampleProjectDetails project)
	{
		return await TryGetJsonAsT(project.ManifestJSONKey, (string ss) => ExampleProjectManifest.DeserializeJSON(ss));
	}

	public static async Task<OperationResult> TryGetExampleProjectAsync(ExampleProjectManifest manifest, string destinationDir, ProgressReporter pr = null)
	{
		try
		{
			if (Directory.Exists(destinationDir) && Directory.GetFiles(destinationDir).Length != 0)
			{
				throw new ArgumentException("destinationDir is not an empty directory.");
			}
			if (pr == null)
			{
				pr = ProgressReporter.None();
			}
			S3TransferHelper s3u = GetS3();
			try
			{
				string cacheDirectory = RasSpecialDirectories.ExampleProjectCacheDirectory;
				long sumLengthBytes = manifest.Files.Sum((MappedFile f) => f.Length);
				long curBytesTransferred = 0L;
				object progLock = new object();
				object errorLock = new object();
				OperationResult or = new OperationResult(success: true);
				HashSet<string> dupes = new HashSet<string>();
				ParallelOptions parallelOptions = new ParallelOptions
				{
					MaxDegreeOfParallelism = 16
				};
				ProgressReporter subPr = pr.WithProgressModifier(0, 0.95f);
				await Parallel.ForAsync(0, manifest.Files.Count, parallelOptions, async (int fIdx, CancellationToken token) =>
				{
					MappedFile file = manifest.Files[fIdx];
					lock (dupes)
					{
						if (!dupes.Add(file.SHA256))
						{
							return;
						}
					}
					string expectedFn = Path.Combine(cacheDirectory, file.SHA256);
					if (File.Exists(expectedFn))
					{
						MappedFile mappedFile2 = MappedFile.ForFile(expectedFn, cacheDirectory);
						if (mappedFile2.SHA256 != file.SHA256)
						{
							subPr.ReportMessage("Corrupt file found, re-downloading: " + file.Filename.InSingleQuotes());
							await s3u.PullSingleFileAsync(expectedFn, file.SHA256);
							mappedFile2 = MappedFile.ForFile(expectedFn, cacheDirectory);
							if (mappedFile2.SHA256 != file.SHA256)
							{
								lock (errorLock)
								{
									subPr.ReportMessage("ERROR: SHA256 disagreement with " + file.Filename.InSingleQuotes() + ", exiting...");
									or.Result = false;
									or.AddMessage(MessageCodes.GetS3SHADoesntMatch(file.Filename, file.SHA256, mappedFile2.SHA256));
									return;
								}
							}
						}
						subPr.ReportMessage("Using cached: " + file.Filename.InSingleQuotes());
					}
					else
					{
						subPr.ReportMessage("Downloading: " + file.Filename.InSingleQuotes());
						await s3u.PullSingleFileAsync(expectedFn, file.SHA256);
					}
					lock (progLock)
					{
						curBytesTransferred += file.Length;
						subPr.ReportProgressFraction((float)curBytesTransferred / (float)sumLengthBytes);
					}
				});
				if (!or.Result)
				{
					return or;
				}
				subPr = pr.WithProgressModifier(95, 0.05f);
				curBytesTransferred = 0L;
				for (int intervalIndex = 0; intervalIndex < manifest.Files.Count; intervalIndex++)
				{
					MappedFile mappedFile = manifest.Files[intervalIndex];
					string sourceFileName = Path.Combine(cacheDirectory, mappedFile.SHA256);
					string path = Path.Combine(destinationDir, mappedFile.Filename);
					Directory.CreateDirectory(Path.GetDirectoryName(path));
					File.Copy(sourceFileName, path, overwrite: true);
					curBytesTransferred += mappedFile.Length;
					subPr.ReportProgressFraction((float)curBytesTransferred / (float)sumLengthBytes);
				}
				pr.ReportMessage(manifest.Files.Count + " files copied to " + destinationDir.InSingleQuotes());
				pr.ReportProgress(100);
				return or;
			}
			finally
			{
				if (s3u != null)
				{
					((IDisposable)s3u).Dispose();
				}
			}
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Unknown error querying S3.");
			return OperationResult.Fail("Querying S3 Example Project", ex);
		}
	}

	private static async Task<T> TryGetJsonAsT<T>(string key, Func<string, T> deserialize)
	{
		_ = 1;
		try
		{
			using S3TransferHelper s3u = GetS3();
			if (await s3u.ObjectExists(key))
			{
				_logger.LogInformation("Object {Key} found on {Server} / {Bucket}.", key, "https://s3.hecdev.net", "ras-public-examples");
				string tmpFn = RasSpecialDirectories.GetTempFile();
				await s3u.PullSingleFileAsync(tmpFn, key);
				T result = deserialize(File.ReadAllText(tmpFn));
				File.Delete(tmpFn);
				return result;
			}
			_logger.LogWarning("Object {Key} does not exist on {Server} / {Bucket}.", key, "https://s3.hecdev.net", "ras-public-examples");
			return default;
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Unknown error querying S3.");
			return default;
		}
	}

	private static S3TransferHelper GetS3()
	{
		return new S3TransferHelper("https://s3.hecdev.net", "ras-public-examples", "", "", forcePathStyle: true);
	}
}
