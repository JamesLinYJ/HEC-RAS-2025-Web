using System;
using System.IO;
using CommandLine;
using Utility.Extensions;
using Utility.Files;
using Utility.Progress;

namespace Ras.Process.HelpersCommands;

[Verb("hash", false, null, HelpText = "Hashes the given file with various hashing algorithms.")]
public class HelpersHashArgs
{
	public const string Verb = "hash";

	[Option('s', "source", Required = true, HelpText = "File to compute hashes on.")]
	public string Filename { get; set; }

	[Option('a', "algorithm", Required = false, HelpText = "Algorithm to compute. If blank, will compute and print all, separated by newlines. Expected one of {RashXX3, SHA256, MD5}.")]
	public string Algorithm { get; set; }

	public bool Execute()
	{
		ProgressReporter progressReporter = ProgressReporter.ConsoleWrite();
		if (!File.Exists(Filename))
		{
			progressReporter.ReportMessage("File does not exist.");
			return false;
		}
		if (string.IsNullOrEmpty(Algorithm))
		{
			Console.WriteLine("RashXX3=" + FileHelpers.ComputeRashXX3(Filename));
			Console.WriteLine("SHA256=" + FileHelpers.ComputeFileSHA256(Filename));
			Console.WriteLine("MD5=" + FileHelpers.ComputeFileMD5(Filename));
			return true;
		}
		if (Algorithm.RoughlyEquals("RashXX3"))
		{
			Console.WriteLine(FileHelpers.ComputeRashXX3(Filename));
			return true;
		}
		if (Algorithm.RoughlyEquals("SHA256"))
		{
			Console.WriteLine(FileHelpers.ComputeFileSHA256(Filename));
			return true;
		}
		if (Algorithm.RoughlyEquals("MD5"))
		{
			Console.WriteLine(FileHelpers.ComputeFileMD5(Filename));
			return true;
		}
		progressReporter.ReportMessage("Algorithm not recognized: " + Algorithm);
		return false;
	}
}
