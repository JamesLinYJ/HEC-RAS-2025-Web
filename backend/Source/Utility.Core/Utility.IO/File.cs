using System.Collections.Generic;
using System.IO;

namespace Utility.IO;

public static class File
{
	public static void ParseKVPDelimitedFile(string filename, char delimeter, out Dictionary<string, string> kvp)
	{
		string[] allLines = System.IO.File.ReadAllLines(filename);
		kvp = new Dictionary<string, string>();
		string[] allLinesArray = allLines;
		foreach (string allLine in allLinesArray)
		{
			string[] parts = allLine.Split(delimeter);
			if (parts.Length >= 2)
			{
				string key = parts[0].Trim();
				string kvp2 = "";
				kvp2 = ((parts.Length != 2) ? allLine.Substring(allLine.IndexOf('=') + 1) : parts[1].Trim());
				if (!kvp.ContainsKey(key))
				{
					kvp[key] = kvp2;
				}
			}
		}
	}

	public static void ParseKVPDelimitedFile(string filename, char delimeter, out Dictionary<string, List<string>> kvp)
	{
		ParseKVPDelimitedFile(System.IO.File.ReadAllLines(filename), delimeter, out kvp);
	}

	public static void ParseKVPDelimitedFile(string[] lines, char delimeter, out Dictionary<string, List<string>> kvp)
	{
		kvp = new Dictionary<string, List<string>>();
		for (int i = 0; i < lines.Length; i++)
		{
			string[] parts = lines[i].Split(delimeter);
			if (parts.Length == 2)
			{
				string key = parts[0].Trim();
				string trim = parts[1].Trim();
				if (kvp.ContainsKey(key))
				{
					kvp[key].Add(trim);
					continue;
				}
				List<string> kvpList = new List<string>();
				kvpList.Add(trim);
				kvp[key] = kvpList;
			}
		}
	}
}
