using System;
using System.IO;
using System.IO.Compression;
using CommandLine;
using Utility.Extensions;

namespace Ras.Process.HelpersCommands;

[Verb("extract", false, null, HelpText = "Extracts a .zip archive into a target directory.")]
public class HelpersExtractArgs
{
	public const string Verb = "extract";

	[Value(0, MetaName = "source", Required = true, HelpText = "Source .zip archive to extract.")]
	public string SourceArchive { get; set; }

	[Value(1, MetaName = "output", Required = true, HelpText = "Output directory. Created if missing. If non-empty, must be combined with --force argument.")]
	public string OutputDirectory { get; set; }

	[Option('f', "force", Required = false, HelpText = "Overwrite existing files in the output directory. Defaults to false.")]
	public bool Force { get; set; }

	public bool Execute()
	{
		if (string.IsNullOrEmpty(SourceArchive) || !File.Exists(SourceArchive))
		{
			Console.WriteLine("Source archive does not exist: " + (SourceArchive ?? "").InDoubleQuotes());
			return false;
		}
		if (string.IsNullOrEmpty(OutputDirectory))
		{
			Console.WriteLine("Output directory not provided.");
			return false;
		}
		if (Directory.Exists(OutputDirectory) && Directory.EnumerateFileSystemEntries(OutputDirectory).GetEnumerator().MoveNext() && !Force)
		{
			Console.WriteLine("Output directory " + OutputDirectory.InDoubleQuotes() + " is not empty. Rerun with --force to overwrite.");
			return false;
		}
		Directory.CreateDirectory(OutputDirectory);
		Console.WriteLine("Extracting " + SourceArchive.InDoubleQuotes() + " -> " + OutputDirectory.InDoubleQuotes());
		ZipFile.ExtractToDirectory(SourceArchive, OutputDirectory, Force);
		Console.WriteLine("Done.");
		return true;
	}
}
