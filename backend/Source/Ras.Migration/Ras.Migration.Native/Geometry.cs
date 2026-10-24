using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Ras.Layers;
using Utility.Extensions;
using Utility.Files;
using Utility.IO;
using Utility.Progress;

namespace Ras.Migration.Native;

public class Geometry
{
	private Dictionary<string, string> _geomData = new Dictionary<string, string>();

	public Project Project { get; private set; }

	public string Filename { get; private set; }

	public string HDF5Filename => Filename + ".hdf";

	public string Extension => Filename?.Tail(3) ?? "";

	public string Name { get; set; }

	public string Version { get; set; }

	public FileState FileStatus { get; set; }

	public FileState H5FileStatus { get; private set; }

	public Geometry(Project prj, string gxxFilename)
	{
		Project = prj;
		Filename = gxxFilename;
		if (!System.IO.File.Exists(gxxFilename))
		{
			FileStatus = FileState.DoesNotExist;
			return;
		}
		Utility.IO.File.ParseKVPDelimitedFile(gxxFilename, '=', out _geomData);
		try
		{
			Name = _geomData["Geom Title"];
			if (_geomData.TryGetValue("Program Version", out var version))
			{
				Version = version;
			}
			else
			{
				Version = "UNKNOWN";
			}
			FileStatus = FileState.Good;
		}
		catch
		{
			FileStatus = FileState.Malformed;
		}
	}

	public void Export7thGeneration(string exportDirectory, ProgressReporter reporter)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		if (Project == null)
		{
			reporter.ReportError("No associated project for plan " + Name.InDoubleQuotes());
			return;
		}
		if (FileStatus == FileState.DoesNotExist)
		{
			reporter.ReportError("Could not migrate " + Filename.InDoubleQuotes() + " (does not exist)");
			return;
		}
		string sanitizeFilename = FileHelpers.SanitizeFilename(Name);
		Ras.Layers.Geometry geometry = Ras.Layers.Geometry.CreateNew(Path.Combine(Ras.Layers.Project.GetDefaultGeometryDirectory(exportDirectory), sanitizeFilename + ".h5"));
		if (System.IO.File.Exists(geometry.Filename))
		{
			System.IO.File.Delete(geometry.Filename);
		}
		if (System.IO.File.Exists(HDF5Filename))
		{
			string directoryName = Path.GetDirectoryName(geometry.Filename);
			if (!Directory.Exists(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			System.IO.File.Copy(HDF5Filename, geometry.Filename);
		}
		geometry.Save();
		stopwatch.Stop();
		reporter.ReportMessage("Converted " + Path.GetFileName(Filename) + " " + Name.InParentheses() + " to " + Path.GetFileName(geometry.Filename).InDoubleQuotes() + " in " + stopwatch.ElapsedMilliseconds + " ms.");
	}

	private static string EnsureGeometryDirectory(string exportDirectory)
	{
		string defaultGeometryDirectory = Ras.Layers.Project.GetDefaultGeometryDirectory(exportDirectory);
		if (!Directory.Exists(defaultGeometryDirectory))
		{
			Directory.CreateDirectory(defaultGeometryDirectory);
		}
		return defaultGeometryDirectory;
	}

	public override string ToString()
	{
		return Name;
	}
}
