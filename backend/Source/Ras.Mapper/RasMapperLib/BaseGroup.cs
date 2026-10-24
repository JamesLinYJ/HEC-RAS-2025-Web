using System;
using System.Collections.Generic;
using System.Xml;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Utilities;

namespace RasMapperLib;

public abstract class BaseGroup : TreeNode
{
	private bool _expandWhenLoaded;

	public abstract string NodeName { get; }

	public abstract string NodeText { get; }

	public abstract string XMLNodeName { get; }

	public BaseGroup()
	{
		_expandWhenLoaded = false;
		Name = NodeName;
		Text = NodeText;
	}

	public virtual void LoadCompleted()
	{
		if (_expandWhenLoaded)
		{
			Expand();
		}
	}

	public List<IAnimatingLayer> GetAnimatableLayers(bool includeUncheckedLayers)
	{
		List<IAnimatingLayer> iAnimatingLayers = new List<IAnimatingLayer>();
		foreach (Layer node in Nodes)
		{
			if (node.Checked || includeUncheckedLayers)
			{
				iAnimatingLayers.AddRange(node.GetAnimatableLayers(includeUncheckedLayers));
			}
		}
		return iAnimatingLayers;
	}

	public List<Layer> FindAllLayers()
	{
		List<Layer> layers = new List<Layer>();
		FindLayersRecurse(Nodes, layers);
		return layers;
	}

	public Layer FindLayer(string layerName)
	{
		Layer layer = null;
		FindLayerRecurse(Nodes, layerName, ref layer);
		return layer;
	}

	private void FindLayerRecurse(TreeNodeCollection treeNodes, string layername, ref Layer layer)
	{
		foreach (TreeNode treeNode in treeNodes)
		{
			if (Operators.CompareString(treeNode.Name, layername, TextCompare: false) == 0)
			{
				layer = treeNode as Layer;
				break;
			}
			if (treeNode.Nodes.Count > 0)
			{
				FindLayerRecurse(treeNode.Nodes, layername, ref layer);
				if (layer != null)
				{
					break;
				}
			}
		}
	}

	private void FindLayersRecurse(TreeNodeCollection treeNodes, List<Layer> layers)
	{
		foreach (TreeNode treeNode in treeNodes)
		{
			Layer layer = (Layer)treeNode;
			if (layer != null)
			{
				layers.Add(layer);
			}
			if (treeNode.Nodes.Count != 0)
			{
				FindLayersRecurse(treeNode.Nodes, layers);
			}
		}
	}

	public IEnumerable<T> ChildrenOfType<T>() where T : Layer
	{
		checked
		{
			int count = Nodes.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				if (Nodes[i] is Layer layer && layer is T)
				{
					yield return (T)layer;
				}
			}
		}
	}

	public IEnumerable<T> ChildrenOfExactType<T>() where T : Layer
	{
		checked
		{
			int count = Nodes.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				if (Nodes[i] is Layer layer && (object)layer.GetType() == typeof(T))
				{
					yield return (T)layer;
				}
			}
		}
	}

	public Layer FindLayerBySourceFilename(string filename)
	{
		Layer layer = null;
		if (filename != null)
		{
			FindLayerBySourceFilenameRecurse(Nodes, filename, ref layer);
		}
		return layer;
	}

	private void FindLayerBySourceFilenameRecurse(TreeNodeCollection treeNodes, string filename, ref Layer layer)
	{
		foreach (TreeNode treeNode in treeNodes)
		{
			if (Operators.CompareString((treeNode as Layer).SourceFilename.ToLower(), filename.ToLower(), TextCompare: false) == 0)
			{
				layer = treeNode as Layer;
				break;
			}
			if (treeNode.Nodes.Count > 0)
			{
				FindLayerBySourceFilenameRecurse(treeNode.Nodes, filename, ref layer);
				if (layer != null)
				{
					break;
				}
			}
		}
	}

	public List<FeatureLayer> FindAllFeatureLayers()
	{
		List<FeatureLayer> featureLayers = new List<FeatureLayer>();
		FindLayersWithFeatureTypes(Nodes, null, featureLayers);
		return featureLayers;
	}

	public List<FeatureLayer> FindAllFeatureLayers(Type featureType)
	{
		List<FeatureLayer> featureLayers = new List<FeatureLayer>();
		FindLayersWithFeatureTypes(Nodes, featureType, featureLayers);
		return featureLayers;
	}

	private void FindLayersWithFeatureTypes(TreeNodeCollection treeNodes, Type featureType, List<FeatureLayer> featurelayers)
	{
		foreach (TreeNode treeNode in treeNodes)
		{
			if (treeNode is FeatureLayer)
			{
				FeatureLayer featureLayer = (FeatureLayer)treeNode;
				if ((object)featureType == null || featureLayer.AvailableFeatureTypes().Contains(featureType))
				{
					featurelayers.Add(featureLayer);
				}
			}
			if (treeNode.Nodes.Count > 0)
			{
				FindLayersWithFeatureTypes(treeNode.Nodes, featureType, featurelayers);
			}
		}
	}

	public void LoadTreeNodeState(XmlElement xmlNode)
	{
		Checked = XML.BooleanAttribute(xmlNode, "Checked");
		if (XML.BooleanAttribute(xmlNode, "Expanded"))
		{
			_expandWhenLoaded = true;
		}
	}

	public void SaveTreeNodeState(XmlElement xmlNode)
	{
		if (Checked)
		{
			xmlNode.SetAttribute("Checked", Conversions.ToString(Value: true));
		}
		if (IsExpanded)
		{
			xmlNode.SetAttribute("Expanded", Conversions.ToString(Value: true));
		}
	}

	public void AddIfNotContains(TreeNode tn)
	{
		if (!Nodes.Contains(tn))
		{
			Nodes.Add(tn);
		}
	}
}
