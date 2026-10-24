using System;
using System.IO;
using CommandLine;
using Ras.Process.H5Commands.Explorer;

namespace Ras.Process.H5Commands;

[Verb("explore", false, null, HelpText = "Interactive HDF5 file explorer with tree navigation and data viewing.")]
public class ExploreArgs
{
	public const string Verb = "explore";

	[Value(0, Required = true, HelpText = "HDF5 file to explore (*.hdf, *.h5)")]
	public string FilePath { get; set; }

	public bool Execute()
	{
		if (!File.Exists(FilePath))
		{
			Console.WriteLine("File not found: " + FilePath);
			return false;
		}
		try
		{
			return new H5Explorer(FilePath).Run();
		}
		catch (Exception ex)
		{
			Console.WriteLine("Error opening file: " + ex.Message);
			return false;
		}
	}
}
