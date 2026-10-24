using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommandLine;
using Ras.Updates;
using Utility.Communication;
using Utility.Extensions;
using Utility.Progress;

namespace Ras.Process.DevCommands;

[Verb("delistproject", false, null, HelpText = "Delists a project from the global manifest.")]
public class DelistProject
{
	public const string Verb = "delistproject";

	[Option('s', "server", Required = false, HelpText = "S3 server. Defaults to https://s3.hecdev.net")]
	public string Server { get; set; } = "https://s3.hecdev.net";

	[Option('b', "bucket", Required = false, HelpText = "S3 bucket. Defaults to ras-public-examples")]
	public string Bucket { get; set; } = "ras-public-examples";

	[Option('u', "user", Required = true, HelpText = "Username for S3 authentication.")]
	public string User { get; set; }

	[Option('n', "name", Required = true, HelpText = "Name of project to delist.")]
	public string Name { get; set; }

	[Option('p', "password", Required = true, HelpText = "Password for S3 authentication.")]
	public string Password { get; set; }

	[Option('g', "global", Required = false, HelpText = "Key to the Global JSON manifest. Defaults to 'globalmanifest.json'")]
	public string GlobalManifestKey { get; set; } = "globalmanifest.json";

	public async Task<bool> ExecuteAsync()
	{
		ProgressReporter cw = ProgressReporter.ConsoleWrite();
		if (string.IsNullOrEmpty(GlobalManifestKey))
		{
			cw.ReportMessage("Error - invalid global manifest key.");
			return false;
		}
		using S3TransferHelper s3u = GetS3();
		string tmpFn = Path.GetTempFileName();
		ExampleProjectCollection exampleProjectCollection;
		if (await s3u.ObjectExists(GlobalManifestKey))
		{
			cw.ReportMessage("Pulling global manifest " + GlobalManifestKey.InSingleQuotes());
			await s3u.PullSingleFileAsync(tmpFn, GlobalManifestKey);
			try
			{
				string now = DateTime.Now.ToString("yyyy-MMM-dd-HH-mm-ss");
				string globalManifestKeyNowTerm = GlobalManifestKey + "_backup_" + now;
				cw.ReportMessage("Backing up global manifest " + GlobalManifestKey.InSingleQuotes() + " -> " + globalManifestKeyNowTerm);
				await s3u.CopyKeyAsync(GlobalManifestKey, globalManifestKeyNowTerm);
			}
			catch (Exception ex)
			{
				cw.ReportMessage("Error backing up: " + ex.Message);
			}
			exampleProjectCollection = ExampleProjectCollection.DeserializeJSON(File.ReadAllText(tmpFn));
		}
		else
		{
			cw.ReportMessage("Global manifest " + GlobalManifestKey.InSingleQuotes() + " does not exist exiting...");
			exampleProjectCollection = new ExampleProjectCollection();
		}
		List<ExampleProjectDetails> exampleProjectDetails = exampleProjectCollection.AllProjects.Where((ExampleProjectDetails rd) => rd.Title == Name).ToList();
		if (exampleProjectDetails != null)
		{
			foreach (ExampleProjectDetails exampleProjectDetail in exampleProjectDetails)
			{
				cw.ReportMessage("Removing Entry: " + exampleProjectDetail.ManifestJSONKey);
				exampleProjectCollection.AllProjects.Remove(exampleProjectDetail);
			}
			await File.WriteAllTextAsync(tmpFn, exampleProjectCollection.SerializeJSON());
			cw.ReportMessage(DryRunPrefix("Uploading global manifest file to " + GlobalManifestKey.InSingleQuotes() + "..."));
			await s3u.PushSingleFileAsync(tmpFn, GlobalManifestKey);
			File.Delete(tmpFn);
			cw.ReportMessage("Complete!");
			return true;
		}
		cw.ReportMessage("No entries found with matching name.");
		return false;
	}

	public S3TransferHelper GetS3()
	{
		return new S3TransferHelper(Server, Bucket, User, Password, forcePathStyle: true);
	}

	protected string DryRunPrefix(string msg)
	{
		return msg;
	}
}
