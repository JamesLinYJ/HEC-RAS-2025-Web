using System.Threading.Tasks;
using CommandLine;
using Ras.Helpers;
using Utility.Logging;

namespace Ras.Process.DevCommands;

[Verb("pullproject", false, null, HelpText = "Pulls a project in the given directory from the given manifest file.")]
public class PullProject
{
	public const string Verb = "pullproject";

	[Option('s', "server", Required = false, HelpText = "S3 server. Defaults to https://s3.hecdev.net")]
	public string Server { get; set; } = "https://s3.hecdev.net";

	[Option('b', "bucket", Required = false, HelpText = "S3 bucket. Defaults to ras-public-examples")]
	public string Bucket { get; set; } = "ras-public-examples";

	[Option('u', "user", Required = false, HelpText = "Username for S3 authentication.")]
	public string User { get; set; } = "";

	[Option('p', "password", Required = false, HelpText = "Password for S3 authentication.")]
	public string Password { get; set; } = "";

	[Option('m', "manifest", Required = true, HelpText = "Key containing a Manifest JSON file.")]
	public string ManifestKey { get; set; }

	[Option('d', "directory", Required = true, HelpText = "Directory for the extracted files.")]
	public string Directory { get; set; }

	[Option("dry-run", Required = false, HelpText = "Executes the code to prepare a release without actually uploading or modifying any files.")]
	public bool DryRun { get; set; }

	[Option('f', "force", Required = false, HelpText = "If set, destination directory will be wiped (if needed).")]
	public bool ForceOverwriteExisting { get; set; }

	[Option('c', "cache", Required = false, HelpText = "Location for an intermediate cache directory. If unset, defaults to a folder in your temp directory.")]
	public string CacheDirectory { get; set; }

	public async Task<bool> ExecuteAsync()
	{
		OperationResult operationResult = (await ProjectHelpers.PullProject(new PullProjectParams
		{
			Server = Server,
			Bucket = Bucket,
			S3Key = User,
			S3Secret = Password,
			ManifestKey = ManifestKey,
			Directory = Directory,
			DryRun = DryRun,
			ForceOverwriteExisting = ForceOverwriteExisting,
			CacheDirectory = CacheDirectory,
			ConsoleOutput = true
		})).Item1;
		return operationResult.Result;
	}
}
