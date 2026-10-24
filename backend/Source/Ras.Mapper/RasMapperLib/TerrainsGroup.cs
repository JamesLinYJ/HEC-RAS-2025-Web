using System.Collections.Generic;
using System.IO;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

public class TerrainsGroup : BaseGroup
{
	public const string sTerrains = "Terrains";

	public override string NodeName => "Terrains";

	public override string NodeText => NodeName;

	public override string XMLNodeName => NodeName;

	public TerrainLayer FindTerrain(string hdfFilename)
	{
		if (string.IsNullOrEmpty(hdfFilename))
		{
			return null;
		}
		foreach (TreeNode node in Nodes)
		{
			if (node is TerrainLayer terrainLayer && Operators.CompareString(terrainLayer.SourceFilename.ToLower(), hdfFilename.ToLower(), TextCompare: false) == 0)
			{
				return terrainLayer;
			}
		}
		return null;
	}

	public TerrainLayer FindTerrainByName(string trName)
	{
		if (string.IsNullOrEmpty(trName))
		{
			return null;
		}
		foreach (TreeNode node in Nodes)
		{
			if (node is TerrainLayer terrainLayer && Operators.CompareString(terrainLayer.Name.ToLower(), trName.ToLower(), TextCompare: false) == 0)
			{
				return terrainLayer;
			}
		}
		return null;
	}

	public List<TerrainLayer> FindAllTerrains()
	{
		List<TerrainLayer> terrainLayers = new List<TerrainLayer>();
		FindTerrains(Nodes, terrainLayers);
		return terrainLayers;
	}

	private void FindTerrains(TreeNodeCollection treeNodes, List<TerrainLayer> terrains)
	{
		foreach (TreeNode treeNode in treeNodes)
		{
			if (treeNode is TerrainLayer)
			{
				terrains.Add((TerrainLayer)treeNode);
			}
			if (treeNode.Nodes.Count > 0)
			{
				FindTerrains(treeNode.Nodes, terrains);
			}
		}
	}

	public TerrainLayer EnsureTerrain(string hdfFilename)
	{
		TerrainLayer terrainLayer = FindTerrain(hdfFilename);
		if (terrainLayer == null)
		{
			terrainLayer = new TerrainLayer(Path.GetFileNameWithoutExtension(hdfFilename), hdfFilename);
			Nodes.Add(terrainLayer);
		}
		return terrainLayer;
	}

	public int PlottedTerrainsCount()
	{
		int ordinal = 0;
		foreach (TerrainLayer allTerrain in FindAllTerrains())
		{
			if (allTerrain.Checked)
			{
				ordinal = checked(ordinal + 1);
			}
		}
		return ordinal;
	}

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
