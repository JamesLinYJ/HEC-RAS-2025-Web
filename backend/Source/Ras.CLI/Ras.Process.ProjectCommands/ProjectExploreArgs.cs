using System;
using System.IO;
using CommandLine;
using Ras.Process.RasCommands.Explorer;

namespace Ras.Process.ProjectCommands;

[Verb("explore", false, null, HelpText = "Interactive RAS project explorer with tree navigation.")]
public class ProjectExploreArgs
{
	public const string Verb = "explore";

	[Value(0, Required = true, HelpText = "RAS project file or directory to explore (*.ras)")]
	public string FilePath { get; set; }

	public bool Execute()
	{
		string filePath = FilePath;
		if (Directory.Exists(FilePath))
		{
			string[] files = Directory.GetFiles(FilePath, "*.ras");
			if (files.Length == 0)
			{
				Console.WriteLine("No .ras files found in directory: " + FilePath);
				return false;
			}
			if (files.Length > 1)
			{
				Console.WriteLine("Multiple .ras files found in directory. Please specify one: " + FilePath);
				string[] filesArray = files;
				foreach (string path in filesArray)
				{
					Console.WriteLine("  " + Path.GetFileName(path));
				}
				return false;
			}
			filePath = files[0];
		}
		if (!File.Exists(filePath))
		{
			Console.WriteLine("File not found: " + filePath);
			return false;
		}
		if (!filePath.EndsWith(".ras", StringComparison.OrdinalIgnoreCase))
		{
			Console.WriteLine("Expected .ras project file: " + filePath);
			return false;
		}
		try
		{
			return new RasExplorer(filePath).Run();
		}
		catch (Exception ex)
		{
			Console.WriteLine("Error opening project: " + ex.Message);
			return false;
		}
	}
}
