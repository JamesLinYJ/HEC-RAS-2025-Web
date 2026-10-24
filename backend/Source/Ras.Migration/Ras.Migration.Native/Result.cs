using System.IO;
using H5Assist;
using Utility.Extensions;

namespace Ras.Migration.Native;

public class Result
{
	public Plan Plan { get; private set; }

	public string Name => Plan.Name;

	public string Filename { get; private set; }

	public Result(string filename)
	{
		Filename = filename;
	}

	public static bool IsValid(string file)
	{
		if (!File.Exists(file))
		{
			return false;
		}
		if (!file.RoughlyEndsWith(".hdf"))
		{
			return false;
		}
		try
		{
			using H5Reader h5Reader = new H5Reader(file);
			return h5Reader.ExistsAsGroup("/Results");
		}
		catch
		{
			return false;
		}
	}
}
