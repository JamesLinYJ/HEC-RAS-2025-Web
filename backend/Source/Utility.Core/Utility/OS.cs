using System;
using System.Runtime.InteropServices;

namespace Utility;

public static class OS
{
	public static void PrependToPathIfDNE(string dir)
	{
		string variable;
		char c;
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			variable = "PATH";
			c = ';';
		}
		else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
		{
			variable = "DYLD_LIBRARY_PATH";
			c = ':';
		}
		else
		{
			variable = "LD_LIBRARY_PATH";
			c = ':';
		}
		string environmentVariable = Environment.GetEnvironmentVariable(variable);
		bool condition = false;
		if (!string.IsNullOrEmpty(environmentVariable))
		{
			string[] parts = environmentVariable.Split(c);
			for (int i = 0; i < parts.Length; i++)
			{
				if (string.Equals(parts[i], dir, StringComparison.CurrentCultureIgnoreCase))
				{
					condition = true;
					break;
				}
			}
		}
		if (!condition)
		{
			environmentVariable = dir + c + environmentVariable;
			Environment.SetEnvironmentVariable(variable, environmentVariable);
		}
	}
}
