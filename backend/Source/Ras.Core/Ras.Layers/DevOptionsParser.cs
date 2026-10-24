using System;
using System.Collections.Generic;

namespace Ras.Layers;

internal static class DevOptionsParser
{
	public static DevOptionsReader Parse(string devOptions)
	{
		Dictionary<string, string> ordinalIgnoreCase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (!string.IsNullOrWhiteSpace(devOptions))
		{
			string[] parts = devOptions.Split('\n');
			for (int i = 0; i < parts.Length; i++)
			{
				string trim = parts[i].Trim();
				if (trim.Length != 0 && !trim.StartsWith('#') && !trim.StartsWith("//"))
				{
					int indexOf = trim.IndexOf(' ');
					if (indexOf < 0)
					{
						ordinalIgnoreCase[trim] = null;
						continue;
					}
					string key = trim.Substring(0, indexOf);
					string trim2 = trim;
					int indexOfOffset = indexOf + 1;
					string trim3 = trim2.Substring(indexOfOffset, trim2.Length - indexOfOffset).Trim();
					ordinalIgnoreCase[key] = trim3;
				}
			}
		}
		return new DevOptionsReader(ordinalIgnoreCase);
	}
}
