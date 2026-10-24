using System.Collections.Generic;
using Ras.Layers;
using Ras.Layers.Terrains;

namespace Ras.Process.RasCommands.Explorer.SubLayers;

public static class TerrainSubLayerProvider
{
	public static IEnumerable<RasTreeNode> GetSubLayers(Terrain terrain, RasTreeNode parent)
	{
		foreach (ITerrainInput layer in terrain.Layers)
		{
			string typeName = layer.TypeName;
			string subLayerIds = layer.Name ?? "Unnamed";
			string displaySuffix = "[" + typeName + "]";
			if (layer is RasterTerrainInput rasterTerrainInput)
			{
				displaySuffix = (rasterTerrainInput.IsValid(out var _) ? ("[" + typeName + "]") : ("[" + typeName + "] (missing)"));
			}
			yield return new RasTreeNode(subLayerIds, parent.FullPath + "/" + subLayerIds, RasNodeType.SubLayer, parent)
			{
				DataSource = layer,
				DisplaySuffix = displaySuffix
			};
		}
	}
}
