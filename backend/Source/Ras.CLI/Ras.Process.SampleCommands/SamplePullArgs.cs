using System;
using System.IO;
using System.Threading.Tasks;
using CommandLine;
using Ras.Helpers;

namespace Ras.Process.SampleCommands;

[Verb("pull", false, null, HelpText = "Downloads a sample project from S3 into the specified directory.")]
public class SamplePullArgs
{
	public const string Verb = "pull";

	[Value(0, MetaName = "title", Required = false, HelpText = "Title of the sample project to pull, as shown by 'ras sample list'. Mutually exclusive with --manifest.")]
	public string Title { get; set; }

	[Option('m', "manifest", Required = false, HelpText = "S3 manifest key to pull directly. Mutually exclusive with the positional title. Use this to pull a sample that isn't listed in the global manifest.")]
	public string ManifestKey { get; set; }

	[Value(1, MetaName = "directory", Required = false, HelpText = "Destination directory. Must be empty (or pass --force to overwrite). When omitted, defaults to the 'DefaultProjectFolder' AppData override (if set) or '{MyDocuments}/RAS Projects', with a per-sample subfolder appended.")]
	public string Directory { get; set; }

	[Option('s', "server", Required = false, HelpText = "S3 server. Defaults to the sample's manifest entry (or https://s3.hecdev.net when used with --manifest).")]
	public string Server { get; set; }

	[Option('b', "bucket", Required = false, HelpText = "S3 bucket. Defaults to the sample's manifest entry (or ras-public-examples when used with --manifest).")]
	public string Bucket { get; set; }

	[Option('u', "user", Required = false, HelpText = "Username for S3 authentication. Public buckets do not require this.")]
	public string User { get; set; } = "";

	[Option('p', "password", Required = false, HelpText = "Password for S3 authentication. Public buckets do not require this.")]
	public string Password { get; set; } = "";

	[Option('c', "cache", Required = false, HelpText = "Cache directory for downloaded blobs. Defaults to the standard RAS example-project cache.")]
	public string CacheDirectory { get; set; }

	[Option('f', "force", Required = false, HelpText = "If set, the destination directory will be wiped before pulling.")]
	public bool ForceOverwriteExisting { get; set; }

	[Option("dry-run", Required = false, HelpText = "Walks through the pull without writing any files.")]
	public bool DryRun { get; set; }

	public async Task<bool> ExecuteAsync()
	{
		string useServer = Server;
		string useBucket = Bucket;
		string manifestKey = ManifestKey;
		if (!string.IsNullOrWhiteSpace(Title) && !string.IsNullOrWhiteSpace(manifestKey))
		{
			Console.WriteLine("ERROR: Provide either a positional title or --manifest, not both.");
			return false;
		}
		string subfolderName;
		if (!string.IsNullOrWhiteSpace(Title))
		{
			SampleResolver.ResolveResult resolveResult = await SampleResolver.TryResolveAsync(Title, "sample pull");
			if (!resolveResult.Success)
			{
				return false;
			}
			manifestKey = resolveResult.Details.ManifestJSONKey;
			subfolderName = resolveResult.Details.Title;
			if (string.IsNullOrWhiteSpace(useServer))
			{
				useServer = resolveResult.Details.Server;
			}
			if (string.IsNullOrWhiteSpace(useBucket))
			{
				useBucket = resolveResult.Details.Bucket;
			}
		}
		else
		{
			if (string.IsNullOrWhiteSpace(manifestKey))
			{
				Console.WriteLine("ERROR: One of --title or --manifest is required.");
				return false;
			}
			subfolderName = Path.GetFileNameWithoutExtension(manifestKey);
		}
		if (string.IsNullOrWhiteSpace(useServer))
		{
			useServer = "https://s3.hecdev.net";
		}
		if (string.IsNullOrWhiteSpace(useBucket))
		{
			useBucket = "ras-public-examples";
		}
		string directory = Directory;
		if (string.IsNullOrWhiteSpace(directory))
		{
			string resolve = SampleOutputDirectoryResolver.Resolve(subfolderName);
			if (!ForceOverwriteExisting)
			{
				resolve = MakeUniqueDirectoryPath(resolve);
			}
			directory = resolve;
			Console.WriteLine("No destination directory provided. Using " + directory);
		}
		return (await ProjectHelpers.PullProject(new PullProjectParams
		{
			Server = useServer,
			Bucket = useBucket,
			S3Key = User,
			S3Secret = Password,
			ManifestKey = manifestKey,
			Directory = directory,
			CacheDirectory = CacheDirectory,
			ForceOverwriteExisting = ForceOverwriteExisting,
			DryRun = DryRun,
			ConsoleOutput = true
		})).Item1.Result;
	}

	private static string MakeUniqueDirectoryPath(string baseDirectory)
	{
		if (!IsOccupied(baseDirectory))
		{
			return baseDirectory;
		}
		for (int i = 1; i < 1000; i++)
		{
			string baseDirectoryTerm = baseDirectory + " (" + i + ")";
			if (!IsOccupied(baseDirectoryTerm))
			{
				return baseDirectoryTerm;
			}
		}
		return baseDirectory;
	}

	private static bool IsOccupied(string dir)
	{
		if (System.IO.Directory.Exists(dir))
		{
			return System.IO.Directory.GetFileSystemEntries(dir).Length != 0;
		}
		return false;
	}
}
