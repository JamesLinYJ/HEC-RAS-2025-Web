using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommandLine;
using Ras.Process.Platform;
using Utility.Communication;
using Utility.Files;
using Utility.Progress;

namespace Ras.Process.DevCommands;

[Verb("test", false, null, HelpText = "Pulls and launches the macOS app bundle or Windows desktop executable for a release.")]
public class Test : BaseDevCommand
{
	public const string Verb = "test";

	[Option('v', "version", Required = true, SetName = "version", HelpText = "Version string to look up in the global manifest. (e.g. '1.0.0.411').")]
	public string Version { get; set; }

	[Option('u', "user", Required = false, HelpText = "Username for S3 authentication. Authentication is only necessary for some buckets.")]
	public string User { get; set; }

	[Option('p', "password", Required = false, HelpText = "Password for S3 authentication. Authentication is only necessary for some buckets.")]
	public string Password { get; set; }

	public override S3TransferHelper GetS3()
	{
		return new S3TransferHelper(Server, Bucket, User, Password, forcePathStyle: true);
	}

	public async Task<bool> ExecuteAsync()
	{
		ProgressReporter reporter = ProgressReporter.ConsoleWrite();
		if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
		{
			reporter.ReportError("Release testing is supported on macOS and Windows.");
			return false;
		}

		string testBuildsDirectory = OperatingSystem.IsWindows()
			? RasSpecialDirectories.TestBuildsDirectory
			: Path.Combine(RasSpecialDirectories.RootAppDataDirectory, "TestBuilds");
		Pull pull = new Pull
		{
			User = User,
			Password = Password,
			Version = Version,
			Destination = Path.Combine(testBuildsDirectory, Version),
			Bucket = Bucket,
			ForceOverwriteExisting = true
		};
		if (!(await pull.ExecuteAsync()))
		{
			return false;
		}
		if (OperatingSystem.IsWindows())
		{
			string path = Path.Combine(pull.Destination, "Ras.Interface.WinUI.exe");
			if (!File.Exists(path))
			{
				reporter.ReportError("The pulled release does not contain the Windows desktop executable.");
				return false;
			}
			FileHelpers.LaunchExplorerAndSelect(path);
			System.Diagnostics.Process.Start(path);
			return true;
		}

		string? appBundle = Directory.GetDirectories(pull.Destination, "*.app", SearchOption.AllDirectories)
			.OrderBy(path => path, StringComparer.Ordinal)
			.FirstOrDefault();
		if (appBundle == null)
		{
			reporter.ReportError("The pulled release does not contain a macOS .app bundle.");
			return false;
		}
		FileHelpers.LaunchExplorerAndSelect(appBundle);
		ProcessStartInfo startInfo = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
		startInfo.ArgumentList.Add("-a");
		startInfo.ArgumentList.Add(appBundle);
		return ExternalProcessLauncher.Start(startInfo);
	}
}
