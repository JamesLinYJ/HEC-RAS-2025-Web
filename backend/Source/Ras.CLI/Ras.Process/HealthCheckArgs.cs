using System;
using System.Text.RegularExpressions;
using System.Threading;
using CommandLine;
using Geospatial.GDALAssist;
using OSGeo.GDAL;
using Utility.Extensions;
using Utility.Progress;

namespace Ras.Process;

[Verb("healthcheck", false, null, HelpText = "Initializes and tests various RAS dependencies.")]
public class HealthCheckArgs
{
	public const string Verb = "healthcheck";

	private const int MinGdalMajor = 3;

	private const int MinGdalMinor = 12;

	public bool Execute()
	{
		bool condition = false;
		try
		{
			GDALSetup.InitializeMultiplatform();
			Console.WriteLine("GDAL initialized: " + GDALSetup.LoadedVersion + " " + GDALSetup.ToolDirectory.InDoubleQuotes());
			if (!CheckGdalVersion(GDALSetup.LoadedVersion, out var error))
			{
				Console.WriteLine(error);
				condition = true;
			}
			if (!CheckGdalManagedBindings(out var error2))
			{
				Console.WriteLine(error2);
				condition = true;
			}
			Projector.DebugHealthCheck();
			Console.WriteLine("Reprojection healthcheck passed.");
			ProgressReporter progressReporter = ProgressReporter.ConsoleWrite();
			for (int i = 0; i <= 100; i++)
			{
				progressReporter.ReportProgress(i);
				Thread.Sleep(10);
			}
		}
		catch (Exception innerException)
		{
			Console.WriteLine("GDAL failed to load.");
			Console.WriteLine(innerException.Message);
			Console.WriteLine("@" + innerException.StackTrace);
			while (innerException.InnerException != null)
			{
				innerException = innerException.InnerException;
				Console.WriteLine(innerException.Message);
				Console.WriteLine("@" + innerException.StackTrace);
			}
			condition = true;
		}
		return !condition;
	}

	private static bool CheckGdalVersion(string versionString, out string error)
	{
		error = null;
		Match match = Regex.Match(versionString ?? "", "GDAL\\s+(\\d+)\\.(\\d+)");
		if (!match.Success)
		{
			error = "Could not parse GDAL version from: " + (versionString ?? "(null)");
			return false;
		}
		int parse = int.Parse(match.Groups[1].Value);
		int parse2 = int.Parse(match.Groups[2].Value);
		if (parse < 3 || (parse == 3 && parse2 < 12))
		{
			error = $"GDAL version {parse}.{parse2} is older than the required {3}.{12} line. Update the native GDAL build at {GDALSetup.ToolDirectory.InDoubleQuotes()}.";
			return false;
		}
		return true;
	}

	private static bool CheckGdalManagedBindings(out string error)
	{
		error = null;
		try
		{
			using (Gdal.OpenShared("__ras_healthcheck_nonexistent__.tif", Access.GA_ReadOnly))
			{
			}
		}
		catch (EntryPointNotFoundException ex)
		{
			error = "GDAL managed bindings are out of sync with the native library: " + ex.Message;
			return false;
		}
		catch
		{
		}
		return true;
	}
}
