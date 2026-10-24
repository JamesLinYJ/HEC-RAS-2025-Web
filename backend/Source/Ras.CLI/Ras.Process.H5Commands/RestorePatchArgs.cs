using System.IO;
using CommandLine;
using H5Assist;
using H5Assist.Differences;
using Utility.Extensions;
using Utility.Progress;

namespace Ras.Process.H5Commands;

[Verb("restore-patch", false, null, HelpText = "Restores an original HDF5 file from a base hash-store and a patch file.")]
public class RestorePatchArgs
{
	public const string Verb = "restore-patch";

	[Option('b', "base", Required = true, HelpText = "Base hash-store file (must exist).")]
	public string Base { get; set; }

	[Option('p', "patch", Required = true, HelpText = "Patch file (must exist).")]
	public string Patch { get; set; }

	[Option('o', "output", Required = true, HelpText = "Output restored HDF5 file path.")]
	public string Output { get; set; }

	[Option("dry-run", Required = false, HelpText = "Read patch instructions and print plan without writing any files.")]
	public bool DryRun { get; set; }

	[Option("verbose", Required = false, HelpText = "Print detailed restore info.")]
	public bool Verbose { get; set; }

	public bool Execute()
	{
		ProgressReporter progressReporter = ProgressReporter.ConsoleWrite();
		if (!File.Exists(Base))
		{
			progressReporter.ReportMessage("Base file does not exist: " + Base.InDoubleQuotes());
			return false;
		}
		if (!File.Exists(Patch))
		{
			progressReporter.ReportMessage("Patch file does not exist: " + Patch.InDoubleQuotes());
			return false;
		}
		if (DryRun)
		{
			progressReporter.ReportMessage("Dry run — no files will be written.");
			progressReporter.ReportMessage("  Base:   " + Base.InDoubleQuotes());
			progressReporter.ReportMessage("  Patch:  " + Patch.InDoubleQuotes());
			progressReporter.ReportMessage("  Output: " + Output.InDoubleQuotes());
			using H5Reader h5Reader = new H5Reader(Patch);
			h5Reader.ReadAttribute("/", "__PATCH", out string[] values);
			progressReporter.ReportMessage($"  {values.Length} datasets would be restored from base.");
			if (Verbose)
			{
				string[] strings = values;
				foreach (string textValue in strings)
				{
					int lastIndexOf = textValue.LastIndexOf('|');
					string substring = textValue.Substring(0, lastIndexOf);
					string substring2 = textValue.Substring(lastIndexOf + 1);
					progressReporter.ReportMessage("    " + substring + "  hash=" + substring2);
				}
			}
			return true;
		}
		if (File.Exists(Output))
		{
			progressReporter.ReportMessage("Output file already exists: " + Output.InDoubleQuotes());
			return false;
		}
		string directoryName = Path.GetDirectoryName(Path.GetFullPath(Output));
		if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
		{
			progressReporter.ReportMessage("Output directory does not exist: " + directoryName.InDoubleQuotes());
			return false;
		}
		RestoreLog restoreLog = H5Patch.Restore(Base, Patch, Output);
		progressReporter.ReportMessage($"Restored {restoreLog.DatasetsRestoredFromBase} datasets from base, {restoreLog.DatasetsRestoredFromPatch} from patch.");
		if (Verbose)
		{
			progressReporter.ReportMessage($"  From base:  {restoreLog.DatasetsRestoredFromBase}");
			progressReporter.ReportMessage($"  From patch: {restoreLog.DatasetsRestoredFromPatch}");
		}
		return true;
	}
}
