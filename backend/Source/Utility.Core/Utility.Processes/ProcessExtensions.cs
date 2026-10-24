using System.Diagnostics;

namespace Utility.Processes;

public static class ProcessExtensions
{
	public static ProcessStartInfo CloneWith(this ProcessStartInfo psi, string args)
	{
		return new ProcessStartInfo
		{
			FileName = psi.FileName,
			CreateNoWindow = psi.CreateNoWindow,
			UseShellExecute = psi.UseShellExecute,
			RedirectStandardOutput = psi.RedirectStandardOutput,
			RedirectStandardError = psi.RedirectStandardError,
			Arguments = args
		};
	}
}
