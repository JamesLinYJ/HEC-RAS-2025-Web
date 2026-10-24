using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using CommandLine;
using Ras.Helpers;

namespace Ras.Process.DevCommands;

[Verb("pushproject", false, null, HelpText = "Uploads a project in the given directory and builds a manifest. Updates the global example-project manifest.")]
public class PushProject
{
	public const string Verb = "pushproject";

	private const string DefaultModelRepoServer = "https://ras-modelrepo.hecdev.net";

	private const string ModelRepoBucket = "ras-testing";

	[Option('s', "server", Required = false, HelpText = "S3 server. Defaults to https://s3.hecdev.net")]
	public string Server { get; set; } = "https://s3.hecdev.net";

	[Option('b', "bucket", Required = false, HelpText = "S3 bucket. Defaults to ras-public-examples")]
	public string Bucket { get; set; } = "ras-public-examples";

	[Option("dry-run", Required = false, HelpText = "Executes the code to prepare a release without actually uploading or modifying any files.")]
	public bool DryRun { get; set; }

	[Option('u', "user", Required = true, HelpText = "Username for S3 authentication.")]
	public string User { get; set; }

	[Option('p', "password", Required = true, HelpText = "Password for S3 authentication.")]
	public string Password { get; set; }

	[Option("write-to", Required = false, HelpText = "If set, write the manifest JSON to this location. Can be used with '--dry-run'")]
	public string JsonOutputPath { get; set; }

	[Option('f', "force", Required = false, HelpText = "If set, any existing JSON manifest will be overwritten. Default behavior is to return an error if a manifest with the expected key is present on the remote server.")]
	public bool ForceOverwriteExisting { get; set; }

	[Option('d', "directory", Required = true, HelpText = "Project directory to scan and upload.")]
	public string Directory { get; set; }

	[Option("minver", Required = false, HelpText = "Minimum compatibile software version. Expect SemVer 2.0 version number, e.g. '2.0.0.412-beta2'")]
	public string MinimumVersion { get; set; }

	[Option("maxver", Required = false, HelpText = "Maximum compatibile software version. Expect SemVer 2.0 version number, e.g. '2.0.0.412-beta2'")]
	public string MaximumVersion { get; set; }

	[Option("date", Required = false, HelpText = "Publish date. Any number of formats are acceptible, e.g. '05Jan2025'. Defaults to current Date/Time.")]
	public DateTime PublishDate { get; set; }

	[Option('g', "global", Required = false, HelpText = "Key to the Global JSON manifest. Defaults to 'globalmanifest.json'")]
	public string GlobalManifestKey { get; set; } = "globalmanifest.json";

	[Option('r', "use-repo", Required = false, HelpText = "If set, this project will register the model on the RAS model repo and skip writing to the global manifest")]
	public bool UseRepository { get; set; }

	[Option('n', "repo-name", Required = false, HelpText = "The name the project will take when uploaded to the testing repo. Defaults to project file name.")]
	public string RepositoryProjectName { get; set; }

	[Option("no-poi", Required = false, HelpText = "If set, this project will be sent to the model repo server without a POI shapefile")]
	public bool NoPOI { get; set; }

	[Option("repo-server", Required = false, HelpText = "Model repository server. Defaults to https://ras-modelrepo.hecdev.net")]
	public string ModelRepoServer { get; set; } = "https://ras-modelrepo.hecdev.net";

	public async Task<bool> ExecuteAsync()
	{
		if (UseRepository && !File.Exists(Path.Combine(Directory, "Features", "POI.shp")) && !NoPOI)
		{
			Console.WriteLine("There is no /Features/POI.shp file for this project. If you'd like to upload a model to the repository without points of interest, run the command with the --no-poi flag.");
			return false;
		}
		var (operationResult, exampleProjectManifest) = await ProjectHelpers.PushProject(new PushProjectParams
		{
			Server = Server,
			Bucket = (UseRepository ? "ras-testing" : Bucket),
			S3Key = User,
			S3Secret = Password,
			Directory = Directory,
			SkipGlobalManifest = UseRepository,
			GlobalManifestKey = GlobalManifestKey,
			PublishDate = PublishDate,
			DryRun = DryRun,
			JsonOutputPath = JsonOutputPath,
			ForceOverwriteExisting = ForceOverwriteExisting,
			MinimumVersion = MinimumVersion,
			MaximumVersion = MaximumVersion,
			ConsoleOutput = true
		});
		if (!operationResult.Result)
		{
			return false;
		}
		if (!UseRepository)
		{
			return true;
		}
		HttpClient client = new HttpClient();
		Dictionary<string, string> localValue = new Dictionary<string, string>
		{
			{
				"s3ManifestKey",
				exampleProjectManifest.Details.ManifestJSONKey
			},
			{ "rasVersion", "N/A" },
			{ "name", RepositoryProjectName }
		};
		try
		{
			(await client.PostAsJsonAsync(ModelRepoServer + "/project/register/", localValue)).EnsureSuccessStatusCode();
			Console.WriteLine("Succesfully registered project to repository");
			return true;
		}
		catch (Exception ex)
		{
			Console.WriteLine("ERROR: Failed to regiser project to repository: " + ex.Message);
			return false;
		}
	}
}
