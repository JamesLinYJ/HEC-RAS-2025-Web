using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using H5Assist.Caching.VB;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib.Render;

public class MCRenderer : Renderer
{
	protected class MCRun
	{
		public string OutputFile;

		public RASResults Result;

		public MCRun(string filename)
		{
			OutputFile = filename;
			Result = new RASResults(filename);
		}
	}

	protected string _suffix;

	protected List<MCRun> _mcRuns;

	protected MCRenderer(RASResults res, CacheCollection cache = null)
		: base(res, cache, needsVelocityFlowData: false, needsPostProcessFile: false)
	{
		_mcRuns = new List<MCRun>();
		string[] parts = _hdfFilename.Split('.');
		if (parts.Count() < 3)
		{
			throw new Exception("Bad results filename: " + _hdfFilename);
		}
		_suffix = checked("." + parts[parts.Count() - 2] + "." + parts[parts.Count() - 1]);
		string path = Path.GetDirectoryName(_hdfFilename) + Conversions.ToString(Path.DirectorySeparatorChar) + "run";
		if (!Directory.Exists(path))
		{
			throw new Exception("No Monte Carlo runs found");
		}
		string[] directories = Directory.GetDirectories(path);
		foreach (string directory in directories)
		{
			string name = new DirectoryInfo(directory).Name;
			string directoryConversionsTerm = directory + Conversions.ToString(Path.DirectorySeparatorChar) + "run" + name + _suffix;
			if (File.Exists(directoryConversionsTerm))
			{
				_mcRuns.Add(new MCRun(directoryConversionsTerm));
			}
		}
	}
}
