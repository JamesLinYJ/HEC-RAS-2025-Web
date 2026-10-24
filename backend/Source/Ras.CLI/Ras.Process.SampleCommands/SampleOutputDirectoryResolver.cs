using System;
using System.IO;
using System.Xml.Linq;

namespace Ras.Process.SampleCommands;

internal static class SampleOutputDirectoryResolver
{
	public const string AppDataXmlFilename = "ras.xml";

	public const string AppDataDefaultProjectFolderKey = "DefaultProjectFolder";

	public static string Resolve(string subfolderName)
	{
		string resolveRoot = ResolveRoot();
		string sanitizeSegment = SanitizeSegment(subfolderName);
		if (!string.IsNullOrEmpty(sanitizeSegment))
		{
			return Path.Combine(resolveRoot, sanitizeSegment);
		}
		return resolveRoot;
	}

	public static string ResolveRoot()
	{
		string tryReadAppDataOverride = TryReadAppDataOverride();
		if (!string.IsNullOrWhiteSpace(tryReadAppDataOverride))
		{
			return tryReadAppDataOverride;
		}
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "RAS Projects");
	}

	private static string TryReadAppDataOverride()
	{
		try
		{
			string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
			if (string.IsNullOrEmpty(folderPath))
			{
				return null;
			}
			string path = Path.Combine(folderPath, "ras.xml");
			if (!File.Exists(path))
			{
				return null;
			}
			string allText = File.ReadAllText(path);
			if (string.IsNullOrWhiteSpace(allText))
			{
				return null;
			}
			foreach (XElement element in XElement.Parse(allText).Elements())
			{
				if (string.Equals(element.Attribute("key")?.Value, "DefaultProjectFolder", StringComparison.OrdinalIgnoreCase))
				{
					return element.Value;
				}
			}
			return null;
		}
		catch
		{
			return null;
		}
	}

	private static string SanitizeSegment(string s)
	{
		if (string.IsNullOrWhiteSpace(s))
		{
			return "";
		}
		char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
		foreach (char oldChar in invalidFileNameChars)
		{
			s = s.Replace(oldChar, '_');
		}
		return s.Trim();
	}
}
