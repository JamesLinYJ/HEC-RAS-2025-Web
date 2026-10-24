using System;
using System.Diagnostics;
using System.IO;
using CommandLine;
using Ras.Layers;
using Utility.Progress;

namespace Ras.Process;

[Verb("ui", false, new string[] { "gui" }, HelpText = "Opens the HEC-RAS desktop application.")]
public class ShowGUI
{
	public const string Verb = "ui";

	public const string GuiExeName = "Ras.Interface.WinUI.exe";

	public string ProjectFile { get; set; } = string.Empty;

	[Option("stream", Required = false, HelpText = "Connect the desktop application to the named stream pipe.")]
	public string StreamPipeName { get; set; } = string.Empty;

	[Option("safemode", Required = false, HelpText = "Start the desktop application in safe mode.")]
	public bool SafeMode { get; set; }

	[Option("crashed", Required = false, HelpText = "Mark this as a post-crash restart.")]
	public bool PostCrash { get; set; }

	[Option("debug", Required = false, HelpText = "Enable the desktop application's debug tools.")]
	public bool DebugMode { get; set; }

	internal static bool IsLaunchVerb(string value)
	{
		return string.Equals(value, "ui", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(value, "gui", StringComparison.OrdinalIgnoreCase);
	}

	public static bool LooksLikeProjectFile(string stringValue)
	{
		if (!string.IsNullOrEmpty(stringValue))
		{
			return Project.IsValidProjectFile(stringValue).Result;
		}
		return false;
	}

	public bool Execute()
	{
		ProgressReporter progressReporter = ProgressReporter.ConsoleWrite();
		try
		{
			ProcessStartInfo startInfo = CreateStartInfo(AppContext.BaseDirectory);
			if (!Platform.ExternalProcessLauncher.Start(startInfo))
			{
				progressReporter.ReportMessage("Could not start the HEC-RAS desktop application.");
				return false;
			}
			return true;
		}
		catch (Exception exception)
		{
			progressReporter.ReportMessage("Could not start the HEC-RAS desktop application: " + exception.Message);
			return false;
		}
	}

	internal ProcessStartInfo CreateStartInfo(string applicationDirectory)
	{
		if (OperatingSystem.IsMacOS())
		{
			string appBundlePath = Environment.GetEnvironmentVariable("HECRAS_APP_PATH");
			if (string.IsNullOrWhiteSpace(appBundlePath))
			{
				appBundlePath = FindAppBundle(applicationDirectory);
			}
			if (string.IsNullOrWhiteSpace(appBundlePath) || !Directory.Exists(appBundlePath))
			{
				throw new DirectoryNotFoundException("Could not locate the HEC-RAS application bundle. Set HECRAS_APP_PATH to the installed .app path.");
			}

			ProcessStartInfo startInfo = new ProcessStartInfo("/usr/bin/open")
			{
				UseShellExecute = false
			};
			startInfo.ArgumentList.Add("-a");
			startInfo.ArgumentList.Add(Path.GetFullPath(appBundlePath));
			startInfo.ArgumentList.Add("--args");
			AddLaunchArguments(startInfo);
			return startInfo;
		}

		if (OperatingSystem.IsWindows())
		{
			string path = Path.Combine(applicationDirectory, GuiExeName);
			if (!File.Exists(path)) throw new FileNotFoundException("The desktop application is not installed beside ras.", path);
			ProcessStartInfo startInfo = new ProcessStartInfo(path) { UseShellExecute = false };
			AddLaunchArguments(startInfo);
			return startInfo;
		}

		throw new PlatformNotSupportedException("The HEC-RAS desktop application is available on macOS.");
	}

	private void AddLaunchArguments(ProcessStartInfo startInfo)
	{
		if (!string.IsNullOrWhiteSpace(ProjectFile))
		{
			startInfo.ArgumentList.Add(Path.GetFullPath(ProjectFile));
		}
		if (!string.IsNullOrWhiteSpace(StreamPipeName))
		{
			startInfo.ArgumentList.Add("-stream=" + StreamPipeName);
		}
		if (SafeMode) startInfo.ArgumentList.Add("-safemode");
		if (PostCrash) startInfo.ArgumentList.Add("-crashed");
		if (DebugMode) startInfo.ArgumentList.Add("-debug");
	}

	private static string? FindAppBundle(string applicationDirectory)
	{
		DirectoryInfo? directory = new DirectoryInfo(applicationDirectory);
		while (directory != null)
		{
			if (directory.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) return directory.FullName;
			directory = directory.Parent;
		}
		return null;
	}
}
