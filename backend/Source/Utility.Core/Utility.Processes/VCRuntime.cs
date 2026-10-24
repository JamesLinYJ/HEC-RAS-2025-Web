using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Utility.Processes;

[SupportedOSPlatform("windows")]
public class VCRuntime
{
	public VersionNumber Version { get; private set; }

	public string VersionString => Version.Major + "." + Version.Minor + "." + Version.Build + "." + Version.Rebuild + " (" + Version.Architecture.ToString() + ")";

	public string FriendlyName
	{
		get
		{
			string[] parts = RegistryValue.Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length <= 2)
			{
				return RegistryValue;
			}
			string part = parts[0];
			for (int i = 1; i < parts.Length - 2; i++)
			{
				part = part + " " + parts[i];
			}
			return part;
		}
	}

	public string RegistryValue { get; }

	public VCRuntime(string regValue)
	{
		RegistryValue = regValue;
		Version = ParseVersion(regValue);
	}

	public static List<VCRuntime> ReadCppRegistryKeys()
	{
		List<string> stringList = new List<string>();
		using (RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Classes\\Installer\\Products"))
		{
			if (registryKey != null)
			{
				string[] subKeyNames = registryKey.GetSubKeyNames();
				foreach (string subKeyName in subKeyNames)
				{
					object getValue = Registry.GetValue(registryKey.Name + "\\" + subKeyName, "ProductName", null);
					if (getValue != null && getValue.ToString().StartsWith("Microsoft Visual C++"))
					{
						stringList.Add(getValue.ToString());
					}
				}
			}
		}
		using (RegistryKey registryKey2 = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Classes\\Installer\\Dependencies\\"))
		{
			if (registryKey2 != null)
			{
				string[] subKeyNames = registryKey2.GetSubKeyNames();
				foreach (string subKeyName2 in subKeyNames)
				{
					object getValue2 = Registry.GetValue(registryKey2.Name + "\\" + subKeyName2, "DisplayName", null);
					if (getValue2 != null && getValue2.ToString().StartsWith("Microsoft Visual C++"))
					{
						stringList.Add(getValue2.ToString());
					}
				}
			}
		}
		return Sort(from str in stringList.Distinct()
			select new VCRuntime(str));
	}

	public static List<VCRuntime> Sort(IEnumerable<VCRuntime> runtimes)
	{
		return (from rt in runtimes
			orderby rt.Version.Major, rt.Version.Minor, rt.Version.Build, rt.Version.Rebuild, rt.Version.Architecture
			select rt).ToList();
	}

	private static VersionNumber ParseVersion(string displayName)
	{
		VersionNumber result = default;
		string[] parts = displayName.Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length <= 2)
		{
			return result;
		}
		string[] partsArray = parts.Last().Split('.');
		if (partsArray == null || partsArray.Length <= 1)
		{
			return result;
		}
		if (displayName.Contains("x86"))
		{
			result.Architecture = Architechture.x86;
		}
		else if (displayName.Contains("x64"))
		{
			result.Architecture = Architechture.x64;
		}
		else
		{
			result.Architecture = Architechture.Unknown;
		}
		string s = partsArray[0];
		string s2 = partsArray[1];
		string s3 = "";
		string s4 = "";
		if (partsArray.Length >= 3)
		{
			s3 = partsArray[2];
		}
		if (partsArray.Length >= 4)
		{
			s4 = partsArray[3];
		}
		if (!int.TryParse(s, out result.Major))
		{
			return result;
		}
		if (!int.TryParse(s2, out result.Minor))
		{
			return result;
		}
		if (!int.TryParse(s3, out result.Build))
		{
			return result;
		}
		int.TryParse(s4, out result.Rebuild);
		return result;
	}
}
