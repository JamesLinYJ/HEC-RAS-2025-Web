using System.IO;

namespace Ras.Migration.Native;

public class MapLayer
{
	public string Filename { get; set; }

	public string LayerName { get; set; }

	public MapLayer(string filename, string layerName = "")
	{
		Filename = filename;
		if (string.IsNullOrEmpty(layerName))
		{
			LayerName = Path.GetFileNameWithoutExtension(filename);
		}
		else
		{
			LayerName = layerName;
		}
	}
}
