using System;
using System.Diagnostics;
using SystemProcess = System.Diagnostics.Process;

namespace Ras.Process.Platform;

internal static class DesktopClipboard
{
	public static bool TryCopy(string text)
	{
		ProcessStartInfo startInfo;
		if (OperatingSystem.IsMacOS())
		{
			startInfo = CreateStartInfo("/usr/bin/pbcopy");
		}
		else if (OperatingSystem.IsWindows())
		{
			startInfo = CreateStartInfo("clip.exe");
		}
		else if (OperatingSystem.IsLinux())
		{
			startInfo = CreateStartInfo("wl-copy");
		}
		else
		{
			return false;
		}

		try
		{
			using SystemProcess? process = SystemProcess.Start(startInfo);
			if (process == null) return false;
			process.StandardInput.Write(text);
			process.StandardInput.Close();
			if (!process.WaitForExit(2000))
			{
				process.Kill(entireProcessTree: true);
				return false;
			}
			return process.ExitCode == 0;
		}
		catch
		{
			return false;
		}
	}

	private static ProcessStartInfo CreateStartInfo(string fileName)
	{
		return new ProcessStartInfo(fileName)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardInput = true
		};
	}
}
