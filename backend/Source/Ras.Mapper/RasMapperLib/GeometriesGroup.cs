using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

public class GeometriesGroup : BaseGroup
{
	public override string NodeName => "Geometries";

	public override string NodeText => NodeName;

	public override string XMLNodeName => NodeName;

	public List<string> RemoveMissingFiles(bool refresh = false)
	{
		List<string> stringList = new List<string>();
		List<TreeNode> treeNodes = new List<TreeNode>();
		foreach (TreeNode node in Nodes)
		{
			if (node != null)
			{
				Layer layer = node as Layer;
				if (layer is RASGeometry && !File.Exists(layer.SourceFilename))
				{
					stringList.Add(node.ToString() + " - " + layer.SourceFilename.ToString());
					treeNodes.Add(node);
				}
			}
		}
		foreach (TreeNode treeNode in treeNodes)
		{
			Nodes.Remove(treeNode);
		}
		return stringList;
	}

	public RASGeometry FindGeometry(string hdfFilename)
	{
		foreach (TreeNode node in Nodes)
		{
			if (node is RASGeometry rASGeometry && Operators.CompareString(rASGeometry.SourceFilename, "", TextCompare: false) != 0 && Operators.CompareString(rASGeometry.SourceFilename.ToLower(), hdfFilename.ToLower(), TextCompare: false) == 0)
			{
				return rASGeometry;
			}
		}
		return null;
	}

	public List<RASGeometry> FindAllGeometries()
	{
		List<RASGeometry> rasGeometries = new List<RASGeometry>();
		FindGeometries(Nodes, rasGeometries);
		return rasGeometries;
	}

	private void FindGeometries(TreeNodeCollection treeNodes, List<RASGeometry> geometries)
	{
		foreach (TreeNode treeNode in treeNodes)
		{
			if (treeNode is RASGeometry)
			{
				geometries.Add((RASGeometry)treeNode);
			}
			if (treeNode.Nodes.Count > 0)
			{
				FindGeometries(treeNode.Nodes, geometries);
			}
		}
	}

	public RASGeometry EnsureGeometry(string title, string hdfFilename)
	{
		RASGeometry rASGeometry = FindGeometry(hdfFilename);
		if (rASGeometry == null)
		{
			rASGeometry = new RASGeometry(hdfFilename);
			rASGeometry.SetDisplayName(title);
			Nodes.Add(rASGeometry);
		}
		else
		{
			if (DateTime.Compare(File.GetLastWriteTime(hdfFilename), rASGeometry.HDFFileDateLastRead) != 0)
			{
				rASGeometry.EnsureDataMatchesCurrentFileData();
			}
			if (Operators.CompareString(title, rASGeometry.Name, TextCompare: false) != 0)
			{
				rASGeometry.Name = title;
				rASGeometry.Text = title;
			}
		}
		return rASGeometry;
	}

	public RASGeometry EnsureGeometry(string hdfFilename)
	{
		RASGeometry rASGeometry = FindGeometry(hdfFilename);
		if (rASGeometry == null)
		{
			rASGeometry = new RASGeometry(hdfFilename);
			Nodes.Add(rASGeometry);
		}
		else if (DateTime.Compare(File.GetLastWriteTime(hdfFilename), rASGeometry.HDFFileDateLastRead) != 0)
		{
			rASGeometry.EnsureDataMatchesCurrentFileData();
		}
		return rASGeometry;
	}
}
