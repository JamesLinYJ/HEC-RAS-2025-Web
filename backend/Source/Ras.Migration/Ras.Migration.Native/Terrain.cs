using System;
using System.Diagnostics;
using System.IO;
using H5Assist;
using Utility.Extensions;
using Utility.Progress;

namespace Ras.Migration.Native;

public class Terrain
{
	public string Name { get; private set; }

	public string Filename { get; private set; }

	public Terrain(string filename)
	{
		Filename = filename;
	}

	public void Export7thGeneration(string exportDirectory, ProgressReporter reporter)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		if (!File.Exists(Filename))
		{
			reporter.ReportError("Could not migrate " + Filename.InDoubleQuotes() + " (does not exist)");
			return;
		}
		try
		{
			string directoryName = Path.GetDirectoryName(Filename);
			string path = EnsureTerrainDirectory(exportDirectory);
			string fileName = Path.GetFileName(Filename);
			fileName = Path.ChangeExtension(fileName, "h5");
			File.Copy(Filename, Path.Combine(path, fileName));
			using (H5Reader h5Reader = new H5Reader(Filename))
			{
				foreach (string groupName in h5Reader.GetGroupNames("/Terrain"))
				{
					string destFileName2 = Path.Combine(directoryName, groupName + ".tif");
					string destFileName = Path.Combine(path, groupName + ".tif");
					if (!File.Exists(destFileName2))
					{
						reporter.ReportError("Could not find/migrate file " + destFileName2.InDoubleQuotes());
					}
					else
					{
						File.Copy(destFileName2, destFileName);
					}
				}
			}
			stopwatch.Stop();
			reporter.ReportMessage("Converted " + Path.GetFileName(Filename) + " " + Name.InParentheses() + " to " + Path.GetFileName(fileName).InDoubleQuotes() + " in " + stopwatch.ElapsedMilliseconds + " ms.");
		}
		catch (Exception ex)
		{
			reporter.ReportError("Could not migrate " + Filename.InDoubleQuotes() + " - " + ex.Message);
		}
	}

	private static string EnsureTerrainDirectory(string exportDirectory)
	{
		string path = Path.Combine(exportDirectory, "Terrains");
		if (!Directory.Exists(path))
		{
			Directory.CreateDirectory(path);
		}
		return path;
	}
}
