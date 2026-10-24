using System.IO;
using Utility.Extensions;

namespace Ras;

public static class NameHelpers
{
	public static string ParseNameFromFile(string filename, string expectedExtension)
	{
		if (string.IsNullOrEmpty(filename))
		{
			return string.Empty;
		}
		string fileName = Path.GetFileName(filename);
		if (fileName.RoughlyEndsWith(expectedExtension))
		{
			return fileName.Substring(0, fileName.Length - expectedExtension.Length);
		}
		return Path.GetFileNameWithoutExtension(filename);
	}

	public static string ParseNameFromFile(string filename, params string[] expectedExtensions)
	{
		if (string.IsNullOrEmpty(filename))
		{
			return string.Empty;
		}
		string fileName = Path.GetFileName(filename);
		foreach (string expectedExtension in expectedExtensions)
		{
			if (fileName.RoughlyEndsWith(expectedExtension))
			{
				return fileName.Substring(0, fileName.Length - expectedExtension.Length);
			}
		}
		return Path.GetFileNameWithoutExtension(filename);
	}
}
