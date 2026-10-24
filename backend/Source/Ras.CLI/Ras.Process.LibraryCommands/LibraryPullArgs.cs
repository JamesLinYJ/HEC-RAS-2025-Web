using System;
using System.IO;
using System.Threading.Tasks;
using Amazon.S3;
using CommandLine;
using Utility.Progress;

namespace Ras.Process.LibraryCommands;

[Verb("pull", false, null, HelpText = "Downloads a model from the library and extracts it into a directory.")]
public class LibraryPullArgs : LibraryCommandBase
{
	public const string Verb = "pull";

	[Value(0, MetaName = "id", Required = true, HelpText = "ID of the model to pull, as shown by 'ras library list'.")]
	public string Id { get; set; }

	[Option('d', "directory", Required = false, HelpText = "Parent directory to extract into. A subfolder named after the model is created beneath it. When omitted, defaults to '{MyDocuments}/RAS Projects'.")]
	public string Directory { get; set; }

	public async Task<bool> ExecuteAsync()
	{
		if (!TryCreateClient(out var client))
		{
			return false;
		}
		using (client)
		{
			string parent = Directory;
			if (string.IsNullOrWhiteSpace(parent))
			{
				parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "RAS Projects");
				Console.WriteLine("No --directory provided. Using " + parent);
			}
			try
			{
				if (!(await client.ExistsAsync(Id)))
				{
					Console.WriteLine("ERROR: No model with ID '" + Id + "' found in the library.");
					return false;
				}
				ProgressReporter reporter = ProgressReporter.ConsoleWrite();
				Console.WriteLine("Extracted to " + await client.PullAndExtractAsync(Id, parent, reporter));
				return true;
			}
			catch (AmazonS3Exception ex) when (LibraryCommandBase.IsAuthFailure(ex))
			{
				return LibraryCommandBase.ReportAuthFailure(ex);
			}
		}
	}
}
