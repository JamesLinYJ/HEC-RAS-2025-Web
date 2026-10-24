using System;
using System.IO;
using System.Threading.Tasks;
using Amazon.S3;
using CommandLine;
using Utility.Progress;

namespace Ras.Process.LibraryCommands;

[Verb("push", false, null, HelpText = "Zips a project directory and uploads it to the library under the given ID.")]
public class LibraryPushArgs : LibraryCommandBase
{
	public const string Verb = "push";

	[Value(0, MetaName = "id", Required = true, HelpText = "Unique ID to store the model under (e.g. 'mymodel-v1'). Used as the S3 key.")]
	public string Id { get; set; }

	[Option('d', "directory", Required = true, HelpText = "Project directory to zip and upload.")]
	public string Directory { get; set; }

	[Option('o', "owner", Required = false, HelpText = "Owner/uploader name attached to the object metadata. Defaults to --user.")]
	public string Owner { get; set; }

	[Option('f', "force", Required = false, HelpText = "If set, overwrite an existing model with the same ID instead of failing.")]
	public bool Force { get; set; }

	public async Task<bool> ExecuteAsync()
	{
		if (!TryCreateClient(out var client))
		{
			return false;
		}
		using (client)
		{
			if (!System.IO.Directory.Exists(Directory))
			{
				Console.WriteLine("ERROR: Project directory not found: '" + Directory + "'.");
				return false;
			}
			string owner = (string.IsNullOrWhiteSpace(Owner) ? (User ?? "") : Owner);
			try
			{
				ProgressReporter reporter = ProgressReporter.ConsoleWrite();
				await client.PushDirectoryAsync(Id, Directory, owner, reporter, Force);
				Console.WriteLine("Uploaded model '" + Id + "'.");
				return true;
			}
			catch (InvalidOperationException ex)
			{
				Console.WriteLine("ERROR: " + ex.Message);
				return false;
			}
			catch (AmazonS3Exception ex2) when (LibraryCommandBase.IsAuthFailure(ex2))
			{
				return LibraryCommandBase.ReportAuthFailure(ex2);
			}
		}
	}
}
