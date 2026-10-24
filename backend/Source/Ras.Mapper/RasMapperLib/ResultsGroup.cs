using System.Collections.Generic;
using System.IO;
using Microsoft.VisualBasic.CompilerServices;
using Utility.Extensions;

namespace RasMapperLib;

public class ResultsGroup : BaseGroup
{
	public static string sResults = "Results";

	public override string NodeName => sResults;

	public override string NodeText => sResults;

	public override string XMLNodeName => sResults;

	public IEnumerable<RASResults> Results()
	{
		foreach (TreeNode node in Nodes)
		{
			if (node is RASResults rASResults)
			{
				yield return rASResults;
			}
		}
	}

	public RASResults FindResult(string hdfFilename)
	{
		foreach (RASResults rasResults in Results())
		{
			if (!string.IsNullOrEmpty(rasResults.SourceFilename) && rasResults.SourceFilename.RoughlyEquals(hdfFilename))
			{
				return rasResults;
			}
		}
		return null;
	}

	public List<string> FindAllResultFiles()
	{
		List<string> allResultFiles = new List<string>();
		FindResultFiles(Nodes, allResultFiles);
		return allResultFiles;
	}

	private void FindResultFiles(TreeNodeCollection treeNodes, List<string> filenames)
	{
		foreach (TreeNode treeNode in treeNodes)
		{
			if (treeNode is RASResults)
			{
				filenames.Add(((RASResults)treeNode).SourceFilename);
			}
			if (treeNode.Nodes.Count > 0)
			{
				FindResultFiles(treeNode.Nodes, filenames);
			}
		}
	}

	public List<RASResults> FindAllResults()
	{
		List<RASResults> rasResults = new List<RASResults>();
		FindResults(Nodes, rasResults);
		return rasResults;
	}

	private void FindResults(TreeNodeCollection treeNodes, List<RASResults> results)
	{
		foreach (TreeNode treeNode in treeNodes)
		{
			if (treeNode is RASResults)
			{
				results.Add((RASResults)treeNode);
			}
			if (treeNode.Nodes.Count > 0)
			{
				FindResults(treeNode.Nodes, results);
			}
		}
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

	public RASResults EnsureResult(string hdfFilename)
	{
		RASResults rASResults = FindResult(hdfFilename);
		if (rASResults == null)
		{
			rASResults = new RASResults(hdfFilename);
			rASResults.EnsureDefaultMaps();
			Nodes.Add(rASResults);
		}
		else
		{
			rASResults.EnsureDataMatchesCurrentFileData();
			string planShortID = rASResults.PlanAttributes.PlanShortID;
			if (Operators.CompareString(planShortID, rASResults.Name, TextCompare: false) != 0)
			{
				rASResults.Name = planShortID;
				rASResults.Text = planShortID;
			}
		}
		return rASResults;
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
				if (layer is RASResults && !File.Exists(layer.SourceFilename))
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
