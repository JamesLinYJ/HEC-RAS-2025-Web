using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Utilities;

namespace RasMapperLib;

public class RASMapper
{
	public TreeView tvw;

	private const int NearDistancePixelTolerance = 5;

	private const bool DRAW_OVERLAY = true;

	private const int RedrawDelayMS = 500;

	private const int TimeForUI = 20;

	private const int ConvolveRadius = 5;

	private Extent _extentDesired;





	public bool UseDirect2D;






	private FeaturesGroup _featuresGroup;

	private GeometriesGroup _geometriesGroup;

	private EventsGroup _eventsGroup;

	private ResultsGroup _resultsGroup;

	private MapLayersGroup _mapLayersGroup;

	private TerrainsGroup _terrainsGroup;

	private Stack<TreeNode> _treeNodesToExpand;

	private TreeNode _treeNodeToSelect;





	private ConcurrentBag<Layer> _nodesRequestingRedraw;



	private PointMs _measurePoints;


	private System.Drawing.Point _undefinedPoint;

	private Stack<Extent> _zoomPreviousStack;

	private Stack<Extent> _zoomNextStack;

	private List<KeyValuePair<string, Extent>> _views;

	private Dictionary<int, int> _lbFeatureNamesIdxToFID;

	private Dictionary<int, int> _fidToLbFeatureNamesIdx;



	private string _rasmapFilenameLoaded;

	public bool DisablePlotLayers { get; set; }

	private PolylineFeatureLayer _profileLines => FeaturesGroup.ProfileLineLayer;

	public int AnimationDelayMS { get; set; }

	public string RasmapFilenameLoaded => _rasmapFilenameLoaded;

	public Extent GeometriesExtent => FullTreeNodesExtent(GeometriesGroup.Nodes);

	public Extent TerrainsExtent => FullTreeNodesExtent(TerrainsGroup.Nodes);

	public FeaturesGroup FeaturesGroup => _featuresGroup;

	public GeometriesGroup GeometriesGroup => _geometriesGroup;

	public EventsGroup EventsGroup => _eventsGroup;

	public ResultsGroup ResultsGroup => _resultsGroup;

	public MapLayersGroup MapLayersGroup => _mapLayersGroup;

	public TerrainsGroup TerrainsGroup => _terrainsGroup;

	public RASMapper()
	{
		tvw = new TreeView();
		UseDirect2D = false;
		DisablePlotLayers = false;
		_treeNodesToExpand = new Stack<TreeNode>();
		_treeNodeToSelect = null;
		_nodesRequestingRedraw = new ConcurrentBag<Layer>();
		_measurePoints = new PointMs();
		_undefinedPoint = new System.Drawing.Point(int.MaxValue, int.MaxValue);
		_zoomPreviousStack = new Stack<Extent>();
		_zoomNextStack = new Stack<Extent>();
		_views = new List<KeyValuePair<string, Extent>>();
		_lbFeatureNamesIdxToFID = new Dictionary<int, int>();
		_fidToLbFeatureNamesIdx = new Dictionary<int, int>();
		AnimationDelayMS = 0;
		_rasmapFilenameLoaded = "";
		_featuresGroup = new FeaturesGroup();
		_geometriesGroup = new GeometriesGroup();
		_eventsGroup = new EventsGroup();
		_resultsGroup = new ResultsGroup();
		_mapLayersGroup = new MapLayersGroup();
		_terrainsGroup = new TerrainsGroup();
		tvw.Nodes.Add(_featuresGroup);
		tvw.Nodes.Add(_geometriesGroup);
		tvw.Nodes.Add(_eventsGroup);
		tvw.Nodes.Add(_resultsGroup);
		tvw.Nodes.Add(_mapLayersGroup);
		tvw.Nodes.Add(_terrainsGroup);
		SharedData.RasMapper = this;
	}

	public RASMapper(string rasmapFile)
		: this()
	{
		if (File.Exists(rasmapFile))
		{
			RasMapperCom.LoadRasMap(rasmapFile, "");
		}
	}

	private Extent FullTreeNodesExtent()
	{
		return FullTreeNodesExtent(tvw.Nodes);
	}

	private Extent FullTreeNodesExtent(TreeNodeCollection nodes)
	{
		Extent curExtent = null;
		TreeNodesExtent(nodes, [SpecialName] (Layer lyr) => true, [SpecialName] (TreeNode tn) => true, ref curExtent);
		return curExtent;
	}

	private Extent CheckedTreeNodesExtent(TreeNodeCollection nodes)
	{
		Extent curExtent = null;
		TreeNodesExtent(nodes, [SpecialName] (Layer lyr) => lyr.Checked, [SpecialName] (TreeNode tn) => tn.Checked, ref curExtent);
		return curExtent;
	}

	private void TreeNodesExtent(TreeNodeCollection searchNodes, Func<Layer, bool> includeInExtent, Func<TreeNode, bool> shouldRecurse, ref Extent curExtent)
	{
		foreach (TreeNode searchNode in searchNodes)
		{
			if (searchNode is Layer && !(searchNode is WMSLayer) && includeInExtent(searchNode as Layer))
			{
				Extent extent = ((Layer)searchNode).Extent;
				if (extent != null && extent.IsFinite())
				{
					curExtent = Extent.Union(curExtent, extent);
				}
			}
			if (shouldRecurse(searchNode))
			{
				TreeNodesExtent(searchNode.Nodes, includeInExtent, shouldRecurse, ref curExtent);
			}
		}
	}

	public void LoadRASMapFile()
	{
		if (File.Exists(SharedData.RasMapFilename) && SharedData.RasMapDoc != null)
		{
			LoadRASMapFile(SharedData.RasMapDoc, SharedData.RasMapFilename);
		}
	}

	public void LoadRASMapFile(XmlDocument doc, string rasmapFilename)
	{
		if (doc == null)
		{
			return;
		}
		_rasmapFilenameLoaded = rasmapFilename;
		XmlNode xmlNode = doc.SelectSingleNode("RASMapper");
		if (xmlNode == null)
		{
			return;
		}
		try
		{
			XmlNode xmlNode2 = xmlNode.SelectSingleNode("RASProjectionFilename");
			if (xmlNode2 != null)
			{
				SharedData.SRSFilename = RasMapperLib.Utilities.Path.MakeAbsolute(XML.StringAttribute(xmlNode2, "Filename"), SharedData.RasMapFilename);
			}
			string textElementText = XML.GetTextElementText(xmlNode, "Version");
			if (Operators.CompareString(textElementText, "", TextCompare: false) != 0)
			{
				string[] parts = textElementText.Split('.');
				if (parts.Count() == 3)
				{
					int.Parse(parts[0]);
					int.Parse(parts[1]);
					int.Parse(parts[2]);
				}
			}
			XMLLoad(xmlNode);
			xmlNode.SelectSingleNode("SedimentPlotFavorites");
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			StatusMessageAdd("Error reading RASMap file: " + SharedData.RasMapFilename);
			ProjectData.ClearProjectError();
		}
	}

	public void SaveRASMapFile()
	{
		XmlDocument rasMapFileDoc = GetRasMapFileDoc();
		string backupRasMapFilename = SharedData.BackupRasMapFilename;
		if (File.Exists(SharedData.RasMapFilename))
		{
			if (!File.Exists(backupRasMapFilename))
			{
				File.Move(SharedData.RasMapFilename, backupRasMapFilename);
			}
			else
			{
				new FileInfo(SharedData.RasMapFilename);
				new FileInfo(backupRasMapFilename);
				File.Delete(backupRasMapFilename);
				File.Move(SharedData.RasMapFilename, backupRasMapFilename);
			}
		}
		rasMapFileDoc.Save(SharedData.RasMapFilename);
	}

	public XmlDocument GetRasMapFileDoc()
	{
		XmlDocument xmlDocument = new XmlDocument();
		XmlNode xmlNode = xmlDocument.CreateElement("RASMapper");
		xmlDocument.AppendChild(xmlNode);
		string textValue = "2.0.0";
		XML.AddTextElement(xmlDocument, xmlNode, "Version", textValue);
		if (Operators.CompareString(SharedData.SRSFilename, "", TextCompare: false) != 0)
		{
			XmlElement xmlElement = xmlDocument.CreateElement("RASProjectionFilename");
			string relative = RasMapperLib.Utilities.Path.MakeRelative(SharedData.SRSFilename, SharedData.RasMapFilename);
			xmlElement.SetAttribute("Filename", relative);
			xmlNode.AppendChild(xmlElement);
		}
		XMLSave(xmlDocument, xmlNode);
		return xmlDocument;
	}

	private void XMLSave(XmlDocument doc, XmlNode node)
	{
		SaveGroup(_featuresGroup, doc, node);
		SaveGroup(_geometriesGroup, doc, node);
		SaveGroup(_eventsGroup, doc, node);
		SaveGroup(_resultsGroup, doc, node);
		SaveGroup(_mapLayersGroup, doc, node);
		SaveGroup(_terrainsGroup, doc, node);
		if (_extentDesired != null)
		{
			XmlNode xmlNode = doc.CreateElement("CurrentView");
			node.AppendChild(xmlNode);
			_extentDesired.XMLSave(doc, xmlNode);
		}
		if (_views.Count > 0)
		{
			XmlNode xmlNode2 = doc.CreateElement("NamedViews");
			node.AppendChild(xmlNode2);
			foreach (KeyValuePair<string, Extent> view in _views)
			{
				XmlElement xmlElement = doc.CreateElement("NamedView");
				xmlNode2.AppendChild(xmlElement);
				xmlElement.SetAttribute("Name", view.Key.ToString());
				xmlElement.SetAttribute("MinX", view.Value.MinX.ToString());
				xmlElement.SetAttribute("MaxX", view.Value.MaxX.ToString());
				xmlElement.SetAttribute("MinY", view.Value.MinY.ToString());
				xmlElement.SetAttribute("MaxY", view.Value.MaxY.ToString());
			}
		}
		XmlNode newChild = doc.CreateElement("VelocitySettings");
		node.AppendChild(newChild);
		XML.AddTextElement(doc, node, "Units", SharedData.UnitsSystem());
		string textValue = "";
		if (SharedData.IsHorizontalRenderingMode())
		{
			textValue = "horizontal";
		}
		else
		{
			textValue = ((!SharedData.IsSlopingRenderingMode()) ? "hybrid" : "sloping");
		}
		XML.AddTextElement(doc, node, "RenderMode", textValue);
		XML.AddTextElement(doc, node, "MarksWarpMethod", SharedData.UseMarksNewWarpMethod.ToString());
		SharedData.SaveCurrentSettingsToXML(doc, node);
	}

	private void XMLLoad(XmlNode node)
	{
		_extentDesired = node.SelectSingleNode("CurrentView") is XmlElement view ? Extent.XMLLoad(view) : null;
		DisablePlotLayers = true;
		_treeNodesToExpand.Clear();
		_treeNodeToSelect = null;
		LoadGroup(_featuresGroup, node);
		LoadGroup(_geometriesGroup, node);
		LoadGroup(_eventsGroup, node);
		LoadGroup(_resultsGroup, node);
		LoadGroup(_mapLayersGroup, node);
		LoadGroup(_terrainsGroup, node);
		foreach (TreeNode treeNodesToExpand in _treeNodesToExpand)
		{
			treeNodesToExpand.Expand();
		}
		_treeNodesToExpand.Clear();
		if (_treeNodeToSelect != null)
		{
			tvw.SelectedNode = _treeNodeToSelect;
		}
		_treeNodeToSelect = null;
		tvw.TopNode = _featuresGroup;
		node.SelectNodes("NamedViews/NamedView");
		node.SelectNodes("ProfileLines/ProfileLine");
		int ordinal = 0;
		if (ordinal > 0)
		{
			SharedData.ReportMessageGreen(ordinal + " Profile Lines migrated to Profile Lines under the Features group");
		}
		_profileLines.SaveFeatureTable();
		string left = XML.StringTextElement(node, "RenderMode");
		if (Operators.CompareString(left, "horizontal", TextCompare: false) == 0)
		{
			SharedData.SetHorizontalRenderingMode();
		}
		else if (Operators.CompareString(left, "sloping", TextCompare: false) == 0)
		{
			SharedData.SetSlopingRenderingMode();
		}
		else
		{
			SharedData.SetHybridRenderingMode();
		}
		if (Operators.CompareString(XML.StringTextElement(node, "MarksWarpMethod").ToLowerInvariant(), "true", TextCompare: false) == 0)
		{
			SharedData.UseMarksNewWarpMethod = true;
		}
		else
		{
			SharedData.UseMarksNewWarpMethod = false;
		}
		DisablePlotLayers = false;
	}

	public void AfterLoad()
	{
		FeaturesGroup.EnsureProfileLinesLayer();
	}

	private void SaveGroup(BaseGroup group, XmlDocument doc, XmlNode xmlnode)
	{
		XmlNode xmlNode = doc.CreateElement(group.XMLNodeName);
		xmlnode.AppendChild(xmlNode);
		group.SaveTreeNodeState((XmlElement)xmlNode);
		foreach (Layer node in group.Nodes)
		{
			XmlElement xmlElement = node.XMLSave(doc);
			if (xmlElement != null)
			{
				xmlNode.AppendChild(xmlElement);
			}
		}
	}

	private void LoadGroup(BaseGroup group, XmlNode xmlnode)
	{
		XmlNode xmlNode = xmlnode.SelectSingleNode(group.XMLNodeName);
		if (xmlNode != null)
		{
			group.LoadTreeNodeState((XmlElement)xmlNode);
			group.Nodes.AddRange(Layer.LoadLayersNew((XmlElement)xmlNode).ToArray());
		}
		group.LoadCompleted();
	}

	private void SaveLayers(TreeNode parentNode, XmlDocument doc, XmlNode xmlnode)
	{
		foreach (Layer node in parentNode.Nodes)
		{
			if (!string.IsNullOrEmpty(node.Name))
			{
				XmlElement xmlElement = node.XMLSave(doc);
				if (xmlElement != null)
				{
					xmlnode.AppendChild(xmlElement);
				}
				SaveLayers(node, doc, xmlElement);
			}
		}
	}

	public void StatusMessageAdd(string msg)
	{
	}

	public void StatusMessageAdd(string msg, bool replacePRev)
	{
	}
}
