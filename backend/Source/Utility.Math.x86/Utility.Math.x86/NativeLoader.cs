using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Utility.Math.x86;

internal static class NativeLoader
{
	public const string rasNativeDll = "RasNativeParallel.dll";

	private const uint LOAD_WITH_ALTERED_SEARCH_PATH = 8u;

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern nint LoadLibraryEx(string lbFileName, nint hFile, uint dwFlags);

	static NativeLoader()
	{
		string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
		string[] strings = new string[2]
		{
			baseDirectory,
			Path.Combine(baseDirectory, "runtimes\\win-x64\\native")
		};
		string[] stringsArray = strings;
		foreach (string path in stringsArray)
		{
			string path2 = Path.Combine(baseDirectory, path, "RasNativeParallel.dll");
			if (File.Exists(path2) && LoadLibraryEx(path2, IntPtr.Zero, 8u) != IntPtr.Zero)
			{
				return;
			}
		}
		throw new DllNotFoundException("Could not load 'RasNativeParallel.dll' from any of: " + string.Join(", ", strings));
	}

	internal static void EnsureLoaded()
	{
	}
}
