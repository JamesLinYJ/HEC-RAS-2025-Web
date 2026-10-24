using System;
using System.Collections.Generic;
using CommandLine;
using H5Assist.Differences;
using Utility.Progress;

namespace Ras.Process.H5Commands;

[Verb("diff", false, null, HelpText = "Prints the differences between two HDF5 files.")]
public class DiffArgs
{
	public const string Verb = "diff";

	[Value(0, Required = true, HelpText = "Base HDF5 File for comparison. (*.hdf, *.h5)")]
	public string BaseFile { get; set; }

	[Value(1, Required = true, HelpText = "Test HDF5 File to compare to Base.")]
	public string TestFile { get; set; }

	[Option("filter", Required = false, HelpText = "Filter options. String is pipe-delimited. Entries use '!' to disinclude and '+' to include in the output.E.G. '-/Results' to disinclude the /Results group, or '+/Geometry' to ONLY search the /Geometry group.")]
	public string Filter { get; set; }

	public bool Execute()
	{
		try
		{
			if (OperatingSystem.IsWindows())
			{
				Console.SetBufferSize(Console.BufferWidth, 32766);
			}
		}
		catch
		{
		}
		List<string> stringList = null;
		if (!string.IsNullOrEmpty(Filter))
		{
			stringList = new List<string>();
			stringList.AddRange(Filter.Split('|', StringSplitOptions.RemoveEmptyEntries));
		}
		H5Diff.Diff(BaseFile, TestFile, ProgressReporter.ConsoleWrite(), stringList);
		return true;
	}
}
