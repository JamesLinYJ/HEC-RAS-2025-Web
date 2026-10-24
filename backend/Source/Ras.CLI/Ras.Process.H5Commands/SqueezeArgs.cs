using System.Collections.Generic;
using System.IO;
using CommandLine;
using H5Assist;
using Utility.Extensions;
using Utility.Files;
using Utility.Progress;

namespace Ras.Process.H5Commands;

[Verb("squeeze", false, null, HelpText = "Squeezes an HDF5 file in-place to make it as small as possible.")]
public class SqueezeArgs
{
	public const string Verb = "squeeze";

	[Option('s', "source", Required = true, HelpText = "HDF5 file to squeeze OR a directory. Expected extension (*.hdf, *.h5).")]
	public string Source { get; set; }

	[Option("recurse", Required = false, HelpText = "If source is a directory, this option will recurse through subdirectories for the ")]
	public bool Recurse { get; set; }

	public bool Execute()
	{
		ProgressReporter progressReporter = ProgressReporter.ConsoleWrite();
		if (File.Exists(Source))
		{
			progressReporter.ReportMessage("Squeezing file...");
			long savingsInBytes;
			return SqueezeAndReportSavings(Source, progressReporter, out savingsInBytes);
		}
		if (Directory.Exists(Source))
		{
			List<string> stringList = new List<string>();
			SearchOption searchOption = (Recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
			stringList.AddRange(Directory.GetFiles(Source, "*.h5", searchOption));
			stringList.AddRange(Directory.GetFiles(Source, "*.hdf", searchOption));
			long savingsInBytes2Sum = 0L;
			foreach (string textValue in stringList)
			{
				progressReporter.ReportMessage("Squeezing " + textValue.InDoubleQuotes() + " ...");
				SqueezeAndReportSavings(textValue, progressReporter, out var savingsInBytes2);
				savingsInBytes2Sum += savingsInBytes2;
			}
			progressReporter.ReportMessage("Squeezing Directory saved a total of " + FileHelpers.PrettyByteCount(savingsInBytes2Sum));
			return true;
		}
		progressReporter.ReportMessage("Source does not exist (file or directory).");
		return false;
	}

	public static bool SqueezeAndReportSavings(string filename, ProgressReporter pr, out long savingsInBytes)
	{
		long length = new FileInfo(filename).Length;
		H5Writer.Squeeze(filename);
		long length2 = new FileInfo(filename).Length;
		pr.ReportMessage(SqueezeMessage(length, length2));
		savingsInBytes = length - length2;
		return true;
	}

	public static string SqueezeMessage(long oldFileSize, long newFileSize)
	{
		long newFileSizeAboveOldFileSize = newFileSize - oldFileSize;
		float oldFileSizeTerm = 100 * newFileSizeAboveOldFileSize / oldFileSize;
		string x = ((newFileSizeAboveOldFileSize > 0) ? "+" : "") + oldFileSizeTerm.ToString("F0") + "%";
		return "Squeezed file went from " + FileHelpers.PrettyByteCount(oldFileSize) + " to " + FileHelpers.PrettyByteCount(newFileSize) + ". " + x.InParentheses();
	}
}
