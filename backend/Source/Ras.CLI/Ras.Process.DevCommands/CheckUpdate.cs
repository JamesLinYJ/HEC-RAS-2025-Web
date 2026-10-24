using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using Ras.Updates;
using Utility.Logging;
using Utility.Processes;
using Utility.Progress;

namespace Ras.Process.DevCommands;

[Verb("checkupdate", false, null, HelpText = "Check if an update is valid and doesn't break anything")]
public class CheckUpdate : BaseDevAuthCommand
{
	public const string StagingBucket = "ras-public-releases-staging";

	public const string Verb = "checkupdate";

	private const string _forwardFileName = "forward-file";

	private const int _updateTimeoutMS = 60000;

	private readonly string[] _checkedExecutableNames = new string[4] { "ras.exe", "Ras.Interface.WinUI.exe", "migrate.exe", "Ras.Interface.Process.exe" };

	private ProgressReporter _reporter;

	private ConsoleProgressParser _parser;

	[Option("force-signing", Required = false, HelpText = "Turn this flag on to force executables to have a signature")]
	public bool ForceSignature { get; set; }

	[Option("test-ui", Required = false, HelpText = "If enabled, run the update from the UI (caller must do this manually via dev tooling).")]
	public bool TestUI { get; set; }

	public async Task<bool> ExecuteAsync()
	{
		_reporter = ProgressReporter.ConsoleWrite();
		if (!OperatingSystem.IsWindows())
		{
			_reporter.ReportError("The staging update check requires the Windows executable layout, Authenticode signatures, and RasUpdateHelper.exe.");
			return false;
		}
		_parser = new ConsoleProgressParser();
		_parser.MessageReported += (ProgressReporter sender, Message msg) =>
		{
			_reporter.ReportMessage(msg);
		};
		string exePath = Path.GetFullPath(Environment.ProcessPath);
		string exeName = Path.GetFileName(exePath);
		string exeDir = Path.GetDirectoryName(exePath);
		string version = FileVersionInfo.GetVersionInfo(exePath).FileVersion;
		if (TestUI && !File.Exists(Path.Combine(exeDir, "Ras.Interface.WinUI.exe")))
		{
			ReportColoredError("UI UPDATE CHECKS MUST BE DONE FROM A DIRECTORY WITH A UI EXECUTABLE");
			return false;
		}
		if (ForceSignature && !(await SignatureCheck(exeDir)))
		{
			return false;
		}
		ReportColoredMessage("\nPUSHING TO STAGING");
		if ((await ProcessHelper.RunAsync(exePath, $"dev push -u \"{User}\" -p \"{Password}\" -b \"{"ras-public-releases-staging"}\" -n {version} -v {version} -d \"{exeDir}\" -f", _parser)).ExitCode != 0)
		{
			ReportColoredError("\nPUSH TO STAGING FAILED");
			return false;
		}
		ReportColoredMessage("\nRELEASING TO STAGING");
		System.Diagnostics.Process releaseBase = await ProcessHelper.RunAsync(exePath, $"dev release -u \"{User}\" -p \"{Password}\" -b \"{"ras-public-releases-staging"}\" -m \"{Push.BuildManifestKeyFromVersion(version)}\" -f", _parser);
		if (releaseBase.ExitCode != 0)
		{
			ReportColoredError("RELEASING TO STAGING FAILED");
			return false;
		}
		ReportColoredMessage("\nPULLING FROM STAGING");
		string tempPullDir = Path.Combine(RasSpecialDirectories.TempRasDirectory, Path.GetRandomFileName());
		string tempPullExe = Path.Combine(tempPullDir, exeName);
		if ((await ProcessHelper.RunAsync(exePath, $"dev pull -b \"{"ras-public-releases-staging"}\" -v {version} -d \"{tempPullDir}\"", _parser)).ExitCode != 0)
		{
			ReportColoredError("PULL FROM STAGING FAILED");
			return false;
		}
		ReportColoredMessage("\nCOMPARING HASHES");
		List<string> files = Directory.GetFiles(exeDir).ToList();
		List<string> filesList = Directory.GetFiles(tempPullDir).ToList();
		if (files.Count != files.Count)
		{
			Directory.Delete(tempPullDir, recursive: true);
			ReportColoredError("MISSING FILES IN PULL");
			return false;
		}
		files.Sort();
		filesList.Sort();
		for (int intervalIndex = 0; intervalIndex < files.Count; intervalIndex++)
		{
			MappedFile mappedFile = MappedFile.ForFile(files[intervalIndex]);
			MappedFile mappedFile2 = MappedFile.ForFile(filesList[intervalIndex]);
			if (mappedFile.SHA256 != mappedFile2.SHA256)
			{
				Directory.Delete(tempPullDir, recursive: true);
				ReportColoredError($"HASH MISMATCH ON FILE {mappedFile}");
				return false;
			}
		}
		string[] checkedExecutableNames = _checkedExecutableNames;
		foreach (string checkedExecutableName in checkedExecutableNames)
		{
			string path2 = Path.Combine(exeDir, checkedExecutableName);
			string fileName = Path.Combine(tempPullDir, checkedExecutableName);
			if (File.Exists(path2))
			{
				ReportColoredMessage("CHECKING VERSION METADATA OF " + checkedExecutableName);
				string? fileVersion = FileVersionInfo.GetVersionInfo(path2).FileVersion;
				string fileVersion2 = FileVersionInfo.GetVersionInfo(fileName).FileVersion;
				if (fileVersion != fileVersion2)
				{
					Directory.Delete(tempPullDir, recursive: true);
					ReportColoredError("VERSION METADATA MISMATCH ON " + checkedExecutableName);
					return false;
				}
			}
		}
		ReportColoredMessage("\nPUSHING FORWARD UPDATE TO STAGING");
		string forwardGuid = Guid.NewGuid().ToString();
		string forwardFilePath = Path.Combine(exeDir, "forward-file");
		using (FileStream stream = File.Create(forwardFilePath))
		{
			using StreamWriter streamWriter = new StreamWriter(stream);
			streamWriter.Write(forwardGuid);
		}
		string forwardVersion = (version.Contains('-') ? (version + "_forward") : (version + "-forward"));
		if ((await ProcessHelper.RunAsync(exePath, $"dev push -u \"{User}\" -p \"{Password}\" -b \"{"ras-public-releases-staging"}\" -n {forwardVersion} -v {forwardVersion} -d \"{exeDir}\" -f", _parser)).ExitCode != 0)
		{
			ReportColoredError("\nPUSH FORWARD UPDATE TO STAGING FAILED");
			File.Delete(forwardFilePath);
			return false;
		}
		ReportColoredMessage("\nRELEASING FORWARD UPDATE TO STAGING");
		await ProcessHelper.RunAsync(exePath, $"dev release -u \"{User}\" -p \"{Password}\" -b \"{"ras-public-releases-staging"}\" -m \"{Push.BuildManifestKeyFromVersion(forwardVersion)}\" -f", _parser);
		if (releaseBase.ExitCode != 0)
		{
			ReportColoredError("RELEASE FORWARD TO STAGING FAILED");
			return false;
		}
		if (TestUI)
		{
			System.Diagnostics.Process.Start("explorer.exe", "\"" + tempPullDir + "\"");
			string fileName2 = Path.Combine(tempPullDir, "Ras.Interface.WinUI.exe");
			ReportColoredMessage("\nRAS will now open. Do the forward update from Development Tools -> Staging Updater.");
			Thread.Sleep(500);
			System.Diagnostics.Process.Start(fileName2).WaitForExit();
		}
		else
		{
			ReportColoredMessage("\nUPDATING PULL DIR TO FORWARD UPDATE");
			Task<System.Diagnostics.Process> task = ProcessHelper.RunAsync(tempPullExe, "dev update -v " + forwardVersion + " -b ras-public-releases-staging", _parser);
			if (!task.Wait(60000))
			{
				ReportColoredError("UPDATE FORWARD TIMED OUT");
				Directory.Delete(tempPullDir, recursive: true);
				File.Delete(forwardFilePath);
				return false;
			}
			if (task.Result.ExitCode != 0)
			{
				ReportColoredError("UPDATE FORWARD FAILED");
				Directory.Delete(tempPullDir, recursive: true);
				File.Delete(forwardFilePath);
				return false;
			}
		}
		bool condition = true;
		while (condition)
		{
			Thread.Sleep(1000);
			if (!System.Diagnostics.Process.GetProcesses().Any((System.Diagnostics.Process p) => p.ProcessName.Contains("RasUpdateHelper")))
			{
				condition = false;
			}
			else
			{
				ReportColoredMessage("WAITING FOR UPDATE TO FINISH...");
			}
		}
		ReportColoredMessage("\nVALIDATING UPDATE");
		string path = Path.Combine(tempPullDir, "forward-file");
		if (!File.Exists(path) || File.ReadAllText(path) != forwardGuid)
		{
			ReportColoredError("UPDATE INVALID");
			Directory.Delete(tempPullDir, recursive: true);
			File.Delete(forwardFilePath);
			return false;
		}
		ReportColoredMessage("UPDATE SUCCESSFUL", ConsoleColor.Green);
		ReportColoredMessage("\nCHECKING SIGNATURES");
		if (await SignatureCheck(tempPullDir))
		{
			ReportColoredMessage("\nHEALTHY", ConsoleColor.Green);
		}
		else
		{
			ReportColoredMessage("\nHEALTHY WITH INVALID SIGNATURES", ConsoleColor.Blue);
		}
		if (!TestUI)
		{
			Directory.Delete(tempPullDir, recursive: true);
		}
		else
		{
			ReportColoredMessage("\nYou can manually delete the temporary RAS directory once you exit the restarted UI instance.");
		}
		File.Delete(forwardFilePath);
		return true;
	}

	private async Task<bool> SignatureCheck(string exeDir, bool stopOnNoSignature = false)
	{
		bool valid = true;
		string[] checkedExecutableNames = _checkedExecutableNames;
		foreach (string executableName in checkedExecutableNames)
		{
			string path = Path.Combine(exeDir, executableName);
			if (!File.Exists(path))
			{
				continue;
			}
			if ((await ProcessHelper.RunAsync("powershell", $"$signature = Get-AuthenticodeSignature -FilePath '{path}'\r\n$valid = 'Valid'\r\nif ($signature.Status -eq $valid) {{\r\n  Write-Host 'FOUND VALID SIGNATURE FOR {executableName}'\r\n  Write-Host $signature.SignerCertificate.Thumbprint\r\n  Write-Host $signature.SignerCertificate.Subject\r\n  exit 0\r\n}} else {{\r\n  exit -1\r\n}}", _parser)).ExitCode != 0)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				ReportColoredWarning("THE EXECUTABLE " + executableName + " HAS NO SIGNATURE");
				Console.ResetColor();
				if (stopOnNoSignature)
				{
					return false;
				}
				valid = false;
			}
			else
			{
				_reporter.ReportMessage("");
			}
		}
		return valid;
	}

	private void ReportColoredWarning(string warning, ConsoleColor color = ConsoleColor.Red)
	{
		Console.ForegroundColor = color;
		_reporter.ReportWarning(warning);
		Console.ResetColor();
	}

	private void ReportColoredError(string warning, ConsoleColor color = ConsoleColor.Red)
	{
		Console.ForegroundColor = color;
		_reporter.ReportError(warning);
		Console.ResetColor();
	}

	private void ReportColoredMessage(string warning, ConsoleColor color = ConsoleColor.Yellow)
	{
		Console.ForegroundColor = color;
		_reporter.ReportMessage(warning);
		Console.ResetColor();
	}
}
