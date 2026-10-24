using System;
using System.IO;
using System.IO.Compression;
using CommandLine;
using Ras.Process.Helpers;
using Utility.Compression;
using Utility.Extensions;
using Utility.Progress;

namespace Ras.Process.HelpersCommands;

[Verb("zip", false, null, HelpText = "Compresses a directory into a .zip archive.")]
public class HelpersZipArgs
{
	public const string Verb = "zip";

	[Value(0, MetaName = "source", Required = true, HelpText = "Source directory to compress.")]
	public string SourceDirectory { get; set; }

	[Value(1, MetaName = "output", Required = true, HelpText = "Output .zip filename. If file already exists, must be combined with --force argument.")]
	public string OutputFilename { get; set; }

	[Option('f', "force", Required = false, HelpText = "Force overwrite existing target file. Defaults to false.")]
	public bool Force { get; set; }

	[Option('l', "level", Required = false, HelpText = "Compression level: Optimal (default), Fastest, NoCompression, or SmallestSize.")]
	public CompressionLevel Level { get; set; }

	public bool Execute()
	{
		if (string.IsNullOrEmpty(SourceDirectory) || !Directory.Exists(SourceDirectory))
		{
			Console.WriteLine("Source directory does not exist: " + (SourceDirectory ?? "").InDoubleQuotes());
			return false;
		}
		if (!CliHelpers.CheckDeleteOutputFile(OutputFilename, Force))
		{
			return false;
		}
		ProgressReporter reporter = ProgressReporter.ConsoleWrite();
		CompressionHelper.ZipDirectory(SourceDirectory, OutputFilename, Level, (string _) => true, (string _) => true, reporter);
		Console.WriteLine("Wrote " + OutputFilename.InDoubleQuotes());
		return true;
	}
}
