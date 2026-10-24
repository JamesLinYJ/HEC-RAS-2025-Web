using System.IO;
using System.Reflection;

namespace RasMapperLib;

public class Application
{
	public static string ExecutablePath()
	{
		return Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
	}
}
