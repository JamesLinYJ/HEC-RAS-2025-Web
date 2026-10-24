using System;
using System.Collections.Generic;
using System.IO;
using CommandLine;
using H5Assist.Differences;
using Utility.Extensions;
using Utility.Files;
using Utility.Progress;

namespace Ras.Process.H5Commands;

[Verb("create-patch", false, null, HelpText = "Decomposes an HDF5 file into a shared base hash-store and a lightweight patch file.")]
public class CreatePatchArgs
{
	public const string Verb = "create-patch";

	[Option('s', "source", Required = true, HelpText = "Source HDF5 file to decompose (must exist).")]
	public string Source { get; set; }

	[Option('b', "base", Required = true, HelpText = "Base hash-store file (created if absent, appended if exists).")]
	public string Base { get; set; }

	[Option('p', "patch", Required = true, HelpText = "Output patch file path.")]
	public string Patch { get; set; }

	[Option("min-size", Required = false, Default = 65536L, HelpText = "Minimum compressed bytes for a dataset to be extracted to base.")]
	public long MinSize { get; set; }

	[Option("allowed-paths", Required = false, HelpText = "Pipe-delimited path prefixes to include, e.g. /Results|/Geometry")]
	public string AllowedPaths { get; set; }

	[Option("dry-run", Required = false, HelpText = "Validate inputs and print plan without writing any files.")]
	public bool DryRun { get; set; }

	[Option("verbose", Required = false, HelpText = "Print each extracted dataset (path, hash, size).")]
	public bool Verbose { get; set; }

	public bool Execute()
	{
		ProgressReporter progressReporter = ProgressReporter.ConsoleWrite();
		if (!File.Exists(Source))
		{
			progressReporter.ReportMessage("Source file does not exist: " + Source.InDoubleQuotes());
			return false;
		}
		DiffOptions options = BuildOptions();
		if (DryRun)
		{
			progressReporter.ReportMessage("Dry run — no files will be written.");
			progressReporter.ReportMessage("  Source: " + Source.InDoubleQuotes());
			progressReporter.ReportMessage("  Base:   " + Base.InDoubleQuotes() + (File.Exists(Base) ? " (exists)" : " (will be created)"));
			progressReporter.ReportMessage("  Patch:  " + Patch.InDoubleQuotes());
			progressReporter.ReportMessage("  Min size: " + FileHelpers.PrettyByteCount(MinSize));
			if (!string.IsNullOrEmpty(AllowedPaths))
			{
				progressReporter.ReportMessage("  Allowed paths: " + AllowedPaths);
			}
			List<ExtractedDataset> scan = H5Patch.Scan(Source, options);
			long onDiskBytesSum = 0L;
			foreach (ExtractedDataset scan2 in scan)
			{
				onDiskBytesSum += scan2.OnDiskBytes;
			}
			progressReporter.ReportMessage($"  {scan.Count} datasets would be extracted ({FileHelpers.PrettyByteCount(onDiskBytesSum)} total).");
			if (Verbose)
			{
				foreach (ExtractedDataset scan3 in scan)
				{
					progressReporter.ReportMessage("    " + scan3.Path + "  size=" + FileHelpers.PrettyByteCount(scan3.OnDiskBytes));
				}
			}
			return true;
		}
		string directoryName = Path.GetDirectoryName(Path.GetFullPath(Base));
		string directoryName2 = Path.GetDirectoryName(Path.GetFullPath(Patch));
		if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
		{
			progressReporter.ReportMessage("Base file directory does not exist: " + directoryName.InDoubleQuotes());
			return false;
		}
		if (!string.IsNullOrEmpty(directoryName2) && !Directory.Exists(directoryName2))
		{
			progressReporter.ReportMessage("Patch file directory does not exist: " + directoryName2.InDoubleQuotes());
			return false;
		}
		PatchCreationLog patchCreationLog = H5Patch.CreatePatch(Source, Base, Patch, options);
		progressReporter.ReportMessage($"Extracted {patchCreationLog.Extracted.Count} datasets ({patchCreationLog.NewBaseDatasets} new, {patchCreationLog.DeduplicatedDatasets} already present), {patchCreationLog.DatasetsKeptInPatch} kept in patch, {FileHelpers.PrettyByteCount(patchCreationLog.TotalExtractedBytes)} moved to base.");
		if (Verbose)
		{
			foreach (ExtractedDataset extracted in patchCreationLog.Extracted)
			{
				progressReporter.ReportMessage($"  {extracted.Path}  hash={extracted.Hash}  size={FileHelpers.PrettyByteCount(extracted.OnDiskBytes)}");
			}
		}
		return true;
	}

	private DiffOptions BuildOptions()
	{
		DiffOptions diffOptions = new DiffOptions
		{
			MinCompressedSizeBytes = MinSize
		};
		if (!string.IsNullOrEmpty(AllowedPaths))
		{
			string[] collection = AllowedPaths.Split('|', StringSplitOptions.RemoveEmptyEntries);
			diffOptions.AllowedPaths.AddRange(collection);
		}
		return diffOptions;
	}
}
