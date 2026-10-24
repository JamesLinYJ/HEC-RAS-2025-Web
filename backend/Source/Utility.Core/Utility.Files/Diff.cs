using System;
using System.IO;

namespace Utility.Files;

public class Diff
{
	public static void MakeBaseDiff(string dir, bool recurse = true)
	{
		if (!Directory.Exists(dir))
		{
			throw new Exception(dir + " does not exist.");
		}
	}
}
