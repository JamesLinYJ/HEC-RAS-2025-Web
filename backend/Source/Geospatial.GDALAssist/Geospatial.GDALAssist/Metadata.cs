using System;
using System.Collections.Generic;
using System.Linq;

namespace Geospatial.GDALAssist;

public static class Metadata
{
	public static void GetFileDebugInfo(string filename, out string stdOut, out string stdErr)
	{
		Executables.GdalInfo("\"" + filename + "\"", out stdOut, out stdErr);
	}

	public static string[] GetGDALFormats()
	{
		string StdOut = "";
		string StdErr = "";
		Executables.GdalInfo(" --formats", out StdOut, out StdErr);
		List<string> parts = StdOut.Split(new string[1] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).ToList();
		parts.RemoveAt(0);
		for (int i = 0; i < parts.Count; i++)
		{
			parts[i] = parts[i].Trim();
		}
		return parts.ToArray();
	}
}
