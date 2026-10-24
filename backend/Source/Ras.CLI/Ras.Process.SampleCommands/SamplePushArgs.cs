using System;
using System.Threading.Tasks;
using CommandLine;
using Ras.Helpers;

namespace Ras.Process.SampleCommands;

[Verb("push", false, null, HelpText = "Uploads a project directory as a sample project, building its manifest and registering it in the global example-project manifest.")]
public class SamplePushArgs
{
	public const string Verb = "push";

	[Option('d', "directory", Required = true, HelpText = "Project directory to scan and upload.")]
	public string Directory { get; set; }

	[Option('s', "server", Required = false, HelpText = "S3 server. Defaults to https://s3.hecdev.net")]
	public string Server { get; set; } = "https://s3.hecdev.net";

	[Option('b', "bucket", Required = false, HelpText = "S3 bucket. Defaults to ras-public-examples")]
	public string Bucket { get; set; } = "ras-public-examples";

	[Option('u', "user", Required = true, HelpText = "Username for S3 authentication.")]
	public string User { get; set; }

	[Option('p', "password", Required = true, HelpText = "Password for S3 authentication.")]
	public string Password { get; set; }

	[Option('g', "global", Required = false, HelpText = "Global JSON manifest key to update. Defaults to 'globalmanifest.json'.")]
	public string GlobalManifestKey { get; set; } = "globalmanifest.json";

	[Option("skip-global", Required = false, HelpText = "If set, files are uploaded but the global manifest is not updated. Useful for staging.")]
	public bool SkipGlobalManifest { get; set; }

	[Option("minver", Required = false, HelpText = "Minimum compatible software version. Expect SemVer 2.0, e.g. '2.0.0.412-beta2'.")]
	public string MinimumVersion { get; set; }

	[Option("maxver", Required = false, HelpText = "Maximum compatible software version. Expect SemVer 2.0, e.g. '2.0.0.412-beta2'.")]
	public string MaximumVersion { get; set; }

	[Option("date", Required = false, HelpText = "Publish date. Defaults to the current date/time.")]
	public DateTime PublishDate { get; set; }

	[Option("write-to", Required = false, HelpText = "If set, also writes the generated manifest JSON to this local path. Works with --dry-run.")]
	public string JsonOutputPath { get; set; }

	[Option('f', "force", Required = false, HelpText = "If set, any pre-existing remote manifest with the same key will be overwritten.")]
	public bool ForceOverwriteExisting { get; set; }

	[Option("dry-run", Required = false, HelpText = "Builds the manifest and walks through the upload without writing any remote files.")]
	public bool DryRun { get; set; }

	public async Task<bool> ExecuteAsync()
	{
		return (await ProjectHelpers.PushProject(new PushProjectParams
		{
			Server = Server,
			Bucket = Bucket,
			S3Key = User,
			S3Secret = Password,
			Directory = Directory,
			SkipGlobalManifest = SkipGlobalManifest,
			GlobalManifestKey = GlobalManifestKey,
			PublishDate = PublishDate,
			JsonOutputPath = JsonOutputPath,
			ForceOverwriteExisting = ForceOverwriteExisting,
			MinimumVersion = MinimumVersion,
			MaximumVersion = MaximumVersion,
			DryRun = DryRun,
			ConsoleOutput = true
		})).Item1.Result;
	}
}
