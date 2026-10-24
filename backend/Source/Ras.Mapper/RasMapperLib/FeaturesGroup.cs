using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

public class FeaturesGroup : BaseGroup
{
	private const string sFeaturesFolder = "Features";

	private const string sFlightPathsFolder = "Flight Paths";

	public const string sProfileLines = "Profile Lines";

	public PolylineFeatureLayer ProfileLineLayer;

	public override string NodeName => "Features";

	public override string NodeText => NodeName;

	public override string XMLNodeName => NodeName;

	public override void LoadCompleted()
	{
		base.LoadCompleted();
		EnsureProfileLinesLayer();
	}

	public void EnsureProfileLinesLayer()
	{
		TreeNode[] find = Nodes.Find("Profile Lines", recurse: false);
		checked
		{
			if (find.Count() == 0)
			{
				CreateNewProfileLinesLayer();
			}
			else
			{
				PolylineFeatureLayer polylineFeatureLayer = null;
				int count = find.Count() - 1;
				for (int i = 0; i <= count; i++)
				{
					polylineFeatureLayer = find[i] as PolylineFeatureLayer;
					if (polylineFeatureLayer != null)
					{
						break;
					}
				}
				if (polylineFeatureLayer != null)
				{
					ProfileLineLayer = new PolylineFeatureLayer("Profile Lines", polylineFeatureLayer.SourceFilename, canRemove: false, canRename: false);
					int count2 = Nodes.Count - 1;
					for (int j = 0; j <= count2; j++)
					{
						if (Operators.CompareString(Nodes[j].Name, "Profile Lines", TextCompare: false) == 0)
						{
							Nodes.RemoveAt(j);
							Nodes.Insert(j, ProfileLineLayer);
							break;
						}
					}
				}
				else
				{
					CreateNewProfileLinesLayer();
				}
			}
			ProfileLineLayer.AddAutoNameColumn();
			ProfileLineLayer.SetToNamedEditor();
		}
	}

	private void CreateNewProfileLinesLayer()
	{
		ProfileLineLayer = new PolylineFeatureLayer("Profile Lines", SharedData.GetUniqueFilename("Profile Lines", "shp", "Features"), canRemove: false, canRename: false);
		Nodes.Add(ProfileLineLayer);
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
}
