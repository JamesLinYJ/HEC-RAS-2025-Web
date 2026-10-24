using System.Collections.Generic;
using System.IO;

namespace RasMapperLib;

public class MapLayersGroup : BaseGroup
{
	public const string sMapLayers = "Map Layers";

	public const string sMapLayersXML = "MapLayers";

	public override string NodeName => "Map Layers";

	public override string NodeText => "Map Layers";

	public override string XMLNodeName => "MapLayers";

	public List<string> RemoveMissingFiles(bool refresh = false)
	{
		List<string> stringList = new List<string>();
		List<TreeNode> treeNodes = new List<TreeNode>();
		foreach (TreeNode node in Nodes)
		{
			if (node != null)
			{
				Layer layer = node as Layer;
				if (!File.Exists(layer.SourceFilename))
				{
					treeNodes.Add(node);
					stringList.Add(node.ToString() + " - " + layer.SourceFilename.ToString());
				}
			}
		}
		foreach (TreeNode treeNode in treeNodes)
		{
			Nodes.Remove(treeNode);
		}
		return stringList;
	}
}
