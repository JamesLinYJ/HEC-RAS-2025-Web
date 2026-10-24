using System.Collections.Generic;
using Spectre.Console.Rendering;

namespace Ras.Process.RasCommands.Explorer;

public class RasNodeInfoData
{
	public string Name { get; set; }

	public string Filename { get; set; }

	public Dictionary<string, string> Associations { get; set; } = new Dictionary<string, string>();

	public Dictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();

	public string ExtentInfo { get; set; }

	public InfoTable Table { get; set; }

	public IRenderable Preview { get; set; }
}
