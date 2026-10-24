using System;
using System.Diagnostics;
using System.IO;
using Geospatial.GDALAssist;
using Ras.Layers;
using Utility.Extensions;

namespace Ras.Process.Helpers;

internal static class CliHelpers
{
	public static void StartStep(string msg)
	{
		Console.Write(msg);
	}

	public static void EndStep(string msg, Stopwatch sw)
	{
		Console.WriteLine("\r" + msg + " (" + sw.ElapsedMilliseconds + "ms)");
	}

	public static void TimeStep(string msg, Action inv)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		StartStep(msg);
		inv();
		stopwatch.Stop();
		EndStep(msg, stopwatch);
	}

	public static T TimeStep<T>(string msg, Func<T> gen)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		StartStep(msg);
		T result = gen();
		stopwatch.Stop();
		EndStep(msg, stopwatch);
		return result;
	}

	public static bool CheckDeleteOutputFile(string OutputFilename, bool Force)
	{
		if (File.Exists(OutputFilename))
		{
			if (!Force)
			{
				Console.WriteLine(OutputFilename.InDoubleQuotes() + " already exists.");
				return false;
			}
			File.Delete(OutputFilename);
		}
		return true;
	}

	public static bool CheckInitializeGDAL()
	{
		TimeStep("Initializing GDAL...", () =>
		{
			GDALSetup.InitializeMultiplatform();
		});
		return true;
	}

	public static bool TryResolveProjection(string projectionArg, out Projection prj)
	{
		prj = null;
		if (string.IsNullOrEmpty(projectionArg))
		{
			return true;
		}
		if (projectionArg.RoughlyStartsWith("EPSG:"))
		{
			if (!int.TryParse(projectionArg.Substring("EPSG:".Length), out var result))
			{
				Console.WriteLine("Could not parse EPSG code: " + projectionArg.InDoubleQuotes());
				return false;
			}
			prj = new EPSGProjection(result);
			return true;
		}
		if (projectionArg.RoughlyStartsWith("PROJ:"))
		{
			string trim = projectionArg.Substring("PROJ:".Length).Trim();
			if (string.IsNullOrEmpty(trim))
			{
				Console.WriteLine("PROJ string is empty: " + projectionArg.InDoubleQuotes());
				return false;
			}
			prj = new Proj4Projection(trim);
			return true;
		}
		if (!File.Exists(projectionArg))
		{
			Console.WriteLine("Expected ESRI .prj file does not exist: " + projectionArg.InDoubleQuotes());
			return false;
		}
		prj = new ESRIProjection(projectionArg);
		return true;
	}

	public static bool TryParseCoreFileType(string raw, out CoreFileType type)
	{
		switch ((raw ?? "").Trim().ToLowerInvariant())
		{
		case "terrain":
			type = CoreFileType.Terrain;
			return true;
		case "geometry":
			type = CoreFileType.Geometry;
			return true;
		case "plan":
			type = CoreFileType.Plan;
			return true;
		case "result":
			type = CoreFileType.Result;
			return true;
		case "boundarycondition":
		case "bc":
			type = CoreFileType.BoundaryCondition;
			return true;
		default:
			type = CoreFileType.Geometry;
			return false;
		}
	}

	public static bool TryResolveFile<T>(string filename, Func<string, T> ctor, out T result)
	{
		result = default;
		if (string.IsNullOrEmpty(filename))
		{
			Console.WriteLine("Filename not provided.");
			return false;
		}
		if (!File.Exists(filename))
		{
			Console.WriteLine(filename.InDoubleQuotes() + " does not exist.");
			return false;
		}
		result = ctor(filename);
		return true;
	}

	public static bool TryResolveCoreFile<T>(string filename, CoreFileType expected, Func<string, T> ctor, out T result)
	{
		result = default;
		if (string.IsNullOrEmpty(filename))
		{
			Console.WriteLine("Filename not provided.");
			return false;
		}
		if (!File.Exists(filename))
		{
			Console.WriteLine(filename.InDoubleQuotes() + " does not exist.");
			return false;
		}
		if (!LooksLikeCoreFile(filename, expected))
		{
			return false;
		}
		result = ctor(filename);
		return true;
	}

	public static bool LooksLikeCoreFile(string filename, CoreFileType expected)
	{
		if (!File.Exists(filename))
		{
			Console.WriteLine(filename.InDoubleQuotes() + " does not exist.");
			return false;
		}
		string extension = Path.GetExtension(filename);
		if (!extension.RoughlyEquals(".h5") && !extension.RoughlyEquals(".hdf"))
		{
			Console.WriteLine(filename.InDoubleQuotes() + " does not look like a valid RAS " + DescribeCoreType(expected) + " file.");
			return false;
		}
		bool isValidCoreFile = Project.IsValidCoreFile(filename, out var fileType);
		if (isValidCoreFile && IsAcceptableCoreType(fileType, expected))
		{
			return true;
		}
		if (!isValidCoreFile)
		{
			Console.WriteLine(filename.InDoubleQuotes() + " does not look like a valid RAS " + DescribeCoreType(expected) + " file.");
		}
		else
		{
			Console.WriteLine($"{filename.InDoubleQuotes()} looks like a RAS {DescribeCoreType(fileType)} file, not a {DescribeCoreType(expected)} file.");
		}
		return false;
	}

	private static bool IsAcceptableCoreType(CoreFileType actual, CoreFileType expected)
	{
		if (actual == expected)
		{
			return true;
		}
		if (actual == CoreFileType.Result && (expected == CoreFileType.Geometry || expected == CoreFileType.Plan || expected == CoreFileType.BoundaryCondition))
		{
			return true;
		}
		return false;
	}

	private static string DescribeCoreType(CoreFileType type)
	{
		return type switch
		{
			CoreFileType.Terrain => "terrain", 
			CoreFileType.Geometry => "geometry", 
			CoreFileType.Plan => "plan", 
			CoreFileType.Result => "result", 
			CoreFileType.BoundaryCondition => "boundary condition", 
			_ => type.ToString(), 
		};
	}
}
