using System;
using System.IO;

namespace Ras;

public static class RasSpecialDirectories
{
	public static bool SkipBinariesInUserProfile = true;

	public static readonly string RootAppDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HEC", "RAS");

	private static readonly string _gisModeSessionName = Guid.NewGuid().ToString().Substring(0, 8);

	public static readonly string ExampleProjectCacheDirectory = Path.Combine(RootAppDataDirectory, "ExampleProjectCache");

	public static readonly string TestingUtilityDownloadsDirectory = Path.Combine(RootAppDataDirectory, "TestingUtilityDownloads");

	public static string TempDirectory
	{
		get
		{
			if (!SkipBinariesInUserProfile)
			{
				return Path.GetTempPath();
			}
			if (!OperatingSystem.IsWindows())
			{
				return Path.Combine(Path.GetTempPath(), "HEC", "RAS", "_TEMP");
			}
			return "C:\\Temp\\_TEMP";
		}
	}

	public static string TempRasDirectory => Path.Combine(TempDirectory, "RAS_TMP");

	public static string TestBuildsDirectory => Path.Combine("C:\\Temp", "ras-test-builds");

	public static string GisModeExportDirectory => Path.Combine(TempRasDirectory, "Exports", _gisModeSessionName);

	public static string UpdaterCacheDirectory
	{
		get
		{
			if (!SkipBinariesInUserProfile)
			{
				return Path.Combine(RootAppDataDirectory, "UpdateCache");
			}
			return Path.Combine(TempDirectory, "UpdateCache");
		}
	}

	public static string GetTempFile()
	{
		Directory.CreateDirectory(TempRasDirectory);
		string path = Path.Combine(TempRasDirectory, Path.GetRandomFileName());
		using (File.Create(path))
		{
			return path;
		}
	}
}
