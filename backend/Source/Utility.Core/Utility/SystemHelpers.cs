using System.Diagnostics;

namespace Utility;

public static class SystemHelpers
{
	public static void LaunchBrowser(string url)
	{
		Process.Start(new ProcessStartInfo
		{
			FileName = "cmd",
			WindowStyle = ProcessWindowStyle.Hidden,
			UseShellExecute = false,
			CreateNoWindow = true,
			Arguments = "/c start " + url
		});
	}
}
