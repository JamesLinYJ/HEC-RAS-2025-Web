using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using Geospatial.GDALAssist;
using H5Assist.VB;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Utilities;
using Utility.Extensions;

namespace RasMapperLib;

public abstract class Layer : TreeNode
{
	public class BadFileException : Exception
	{
		public BadFileException(string msg)
			: base(msg)
		{
		}
	}

	public PlotOptionCollection AvailablePlotOptions;

	public PlotOptionCollection SelectedPlotOptions;

	private string _errorMessage;

	public bool PaintAtBottomHack;

	private bool _expandWhenLoaded;

	private bool _containsData;

	public static readonly Dictionary<string, Type> LayerNameTypeMap;

	private bool _isEditingData;

	private string _srcFilename;

	private List<Action> _onEditCompleteActions;

	private List<Action<bool>> _onEditSaveActions;

	protected Extent _extent;

	private static readonly List<string> _ignorePoList;

	private const string APPDATA_REPLACEMENT = "%LocalAppData%";

	private static readonly string APPDATA_PATH;

	public bool ResetBitmap { get; set; }

	public virtual string DefaultLayerName => GetType().Name;

	public virtual string CanWatch
	{
		get
		{
			if (this is RASResultsMap)
			{
				return Conversions.ToString(Value: true);
			}
			if (this is InterpolatedLayer)
			{
				return Conversions.ToString(Value: true);
			}
			if (this is LandCoverLayer)
			{
				return Conversions.ToString(Value: true);
			}
			return Conversions.ToString(Value: false);
		}
	}

	public bool ContainsData => _containsData;

	public virtual bool CanEdit { get; }

	public virtual bool CanMoveToAnotherLevel { get; }

	public virtual bool CanMoveWithinLevel { get; }

	public virtual bool MustHaveUniqueNameInGroup { get; }

	public virtual bool CanRename { get; }

	public virtual bool CanRemove { get; }

	public virtual bool CanDelete
	{
		get
		{
			if (this is IGeometryLayer)
			{
				return false;
			}
			return CanRemove && File.Exists(SourceFilename);
		}
	}

	public virtual bool CanChangeCheck { get; }

	public virtual bool CanZoomToLayer { get; }

	public virtual bool CanRenderWithSiblings { get; }

	public virtual bool RefreshOnNodeSelection { get; }

	public virtual Type BaseGroupType { get; }

	public virtual string LayerPropertiesText { get; }

	public bool IsDebugLayer { get; set; }

	public override void OnStartEditSession()
	{
		if (!CanEdit) throw new InvalidOperationException("This layer does not support data editing.");
		base.OnStartEditSession();
		_isEditingData = true;
	}

	public override void StopEditing(bool save)
	{
		base.StopEditing(save);
		_isEditingData = false;
	}

	public bool IsEditing => IsEditingData;

	public virtual bool IsEditingData => _isEditingData;

	public string ErrorMessage
	{
		get
		{
			return _errorMessage;
		}
		set
		{
			if (Operators.CompareString(_errorMessage, value, TextCompare: false) != 0)
			{
				_errorMessage = value;
			}
		}
	}

	public virtual bool UsesParentSourceFilename { get; }

	public virtual string SourceFilename
	{
		get
		{
			if (UsesParentSourceFilename)
			{
				if (!(Parent is Layer { SourceFilename: var sourceFilename }))
				{
					return "";
				}
				return sourceFilename;
			}
			return _srcFilename;
		}
		set
		{
			if (!UsesParentSourceFilename)
			{
				string srcFilename = _srcFilename;
				_srcFilename = value;
				OnSourceFilenameChanged(srcFilename);
			}
		}
	}

	public bool SourceFileExists
	{
		get
		{
			if (string.IsNullOrEmpty(SourceFilename))
			{
				return false;
			}
			return File.Exists(SourceFilename);
		}
	}

	public DateTime SourceFileDate
	{
		get
		{
			if (File.Exists(SourceFilename))
			{
				return File.GetLastWriteTime(SourceFilename);
			}
			return DateTime.MinValue;
		}
	}

	public string SourceFileDateString
	{
		get
		{
			if (File.Exists(SourceFilename))
			{
				return DSS.ConvertToDSSDateTime(File.GetLastWriteTime(SourceFilename));
			}
			return "";
		}
	}

	public virtual bool ForwardEditRequestsToParent { get; }

	public virtual Extent Extent => _extent;

	protected virtual string SourceFileDoesntExistMessage => "Error: File '" + System.IO.Path.GetFileName(SourceFilename) + "' doesn't exist.";

	static Layer()
	{
		_ignorePoList = new List<string>(new string[1] { "Mesh Edges" });
		APPDATA_PATH = Environment.GetEnvironmentVariable("LOCALAPPDATA");
		LayerNameTypeMap = new Dictionary<string, Type>();
		LayerNameTypeMap["LandClassification"] = typeof(LandCoverLayer);
		LayerNameTypeMap["LandCover"] = typeof(LandCoverLayer);
		LayerNameTypeMap["ManningsNLayer"] = typeof(LandCoverLayer);
		LayerNameTypeMap["Layer"] = typeof(Layer);
		List<Type> typeValues = (from t in Assembly.GetExecutingAssembly().GetTypes()
			where t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(Layer))
			select t).ToList();
		foreach (Type layerNameTypeMap in typeValues)
		{
			if (TryConstructEmpty(layerNameTypeMap) != null)
			{
				string name = layerNameTypeMap.Name;
				LayerNameTypeMap[name] = layerNameTypeMap;
				LayerNameTypeMap[name.ToLowerInvariant()] = layerNameTypeMap;
			}
		}
	}

	public Layer()
	{
		AvailablePlotOptions = new PlotOptionCollection();
		SelectedPlotOptions = new PlotOptionCollection();
		_errorMessage = "";
		ResetBitmap = false;
		PaintAtBottomHack = false;
		_expandWhenLoaded = false;
		CanEdit = false;
		CanMoveToAnotherLevel = true;
		CanMoveWithinLevel = true;
		MustHaveUniqueNameInGroup = true;
		CanRename = true;
		CanRemove = true;
		CanChangeCheck = true;
		CanZoomToLayer = true;
		CanRenderWithSiblings = true;
		RefreshOnNodeSelection = false;
		BaseGroupType = typeof(MapLayersGroup);
		LayerPropertiesText = "Layer Properties";
		IsDebugLayer = false;
		UsesParentSourceFilename = false;
		_srcFilename = "";
		_onEditCompleteActions = new List<Action>();
		_onEditSaveActions = new List<Action<bool>>();
		ForwardEditRequestsToParent = false;
		_extent = null;
		SetDisplayName(DefaultLayerName);
		IList<PlotOption> staticPlotOptions = GetStaticPlotOptions();
		if (staticPlotOptions != null)
		{
			foreach (PlotOption staticPlotOption in staticPlotOptions)
			{
				AvailablePlotOptions.Add(staticPlotOption);
			}
		}
		List<PlotOption> defaultPlotOptions = GetDefaultPlotOptions();
		foreach (PlotOption defaultPlotOption in defaultPlotOptions)
		{
			SelectedPlotOptions.Add(defaultPlotOption);
		}
	}

	public Layer(string lyrName)
		: this()
	{
		if (string.IsNullOrEmpty(lyrName))
		{
			lyrName = DefaultLayerName;
		}
		SetDisplayName(lyrName);
	}

	public Layer(string name, bool forwardEditRequestsToParent)
		: this(name)
	{
		ForwardEditRequestsToParent = forwardEditRequestsToParent;
	}

	public Layer(string name, string filename)
		: this(name)
	{
		SourceFilename = filename;
		if (!string.IsNullOrEmpty(SourceFilename) && !SourceFileExists)
		{
			ErrorMessage = "File '" + System.IO.Path.GetFileNameWithoutExtension(SourceFilename) + "' doesn't exist.";
		}
	}

	public void SetDisplayName(string lyrName)
	{
		Name = lyrName;
		Text = lyrName;
		ToolTipText = lyrName;
	}

	public virtual List<PlotOption> GetStaticPlotOptions()
	{
		return new List<PlotOption>();
	}

	public virtual List<PlotOption> GetDefaultPlotOptions()
	{
		return new List<PlotOption>();
	}

	protected virtual void OnSourceFilenameChanged(string oldName)
	{
	}

	public virtual List<string> GetAllSourceFiles()
	{
		List<string> allSourceFiles = new List<string>();
		if (!string.IsNullOrEmpty(SourceFilename))
		{
			allSourceFiles.Add(SourceFilename);
		}
		return allSourceFiles;
	}

	public virtual List<string> GetAllVirtualizingFiles()
	{
		return GetAllAvailableSourceFiles();
	}

	public virtual List<string> GetDependencyFiles()
	{
		return GetAllAvailableSourceFiles();
	}

	public virtual List<Layer> GetLayersThatDependOnMe()
	{
		return new List<Layer>();
	}

	public virtual List<string> GetAllAvailableSourceFiles()
	{
		return GetAllSourceFiles();
	}

	protected virtual string GetEditContextMenuTitle()
	{
		return "Edit Layer";
	}

	public void DeleteLayer()
	{
		List<string> allVirtualizingFiles = GetAllVirtualizingFiles();
		string getFileNamePathTerm = "";
		foreach (string allVirtualizingFile in allVirtualizingFiles)
		{
			if (File.Exists(allVirtualizingFile))
			{
				getFileNamePathTerm = getFileNamePathTerm + System.IO.Path.GetFileName(allVirtualizingFile) + "\r\n";
			}
		}
		string textValue = getFileNamePathTerm + "Will be sent to the recycle bin.";
		List<Layer> layersThatDependOnMe = GetLayersThatDependOnMe();
		checked
		{
			if (layersThatDependOnMe.Count > 0)
			{
				textValue += "\r\n\r\nNOTE: The following layers are dependent on this layer:\r\n";
				int count = System.Math.Min(1, layersThatDependOnMe.Count - 1);
				for (int i = 0; i <= count; i++)
				{
					textValue = textValue + layersThatDependOnMe[i].Name + "\r\n";
				}
				if (layersThatDependOnMe.Count - 2 > 0)
				{
					textValue = textValue + "And " + (layersThatDependOnMe.Count - 2) + " other layers\r\n";
				}
				textValue += "and will cease functioning without this layer.";
			}
			if (MessageBox.Show(textValue, "Delete Files?", MessageBoxButtons.OkCancel, MessageBoxIcon.Warning) == DialogResult.Cancel)
			{
				return;
			}
			RemoveLayer();
			foreach (string allVirtualizingFile2 in allVirtualizingFiles)
			{
				if (File.Exists(allVirtualizingFile2))
				{
					try
					{
						File.Delete(allVirtualizingFile2);
						SharedData.ReportMessage("Deleted " + allVirtualizingFile2);
					}
					catch (Exception ex)
					{
						ProjectData.SetProjectError(ex);
						Exception ex2 = ex;
						SharedData.ReportMessage("Tried deleting " + allVirtualizingFile2 + ", but couldn't for some reason.", replacePreviousIfPossible: false, errorMessage: true);
						ProjectData.ClearProjectError();
					}
				}
			}
		}
	}

	public void RemoveLayer()
	{
		if (IsEditing)
		{
			StopEditing(save: false);
			AfterStoppedEditing();
		}
		RemoveLayerInternal();
		if (Checked)
		{
			SharedData.RASMapperReplot();
		}
		GC.Collect();
	}

	private void RemoveLayerInternal()
	{
		Remove();
		checked
		{
			TreeNode[] nodes = new TreeNode[Nodes.Count - 1 + 1];
			Nodes.CopyTo(nodes, 0);
			int count = nodes.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (Nodes[0] is Layer layer)
				{
					layer.RemoveLayerInternal();
				}
			}
		}
	}

	public void ZoomToLayer()
	{
		Extent extent = Extent;
		if (extent != null && extent.MaxMinXYDefined)
		{
			SharedData.RASMapperZoom(extent);
		}
	}

	public void AddEditCompletionAction(Action act)
	{
		_onEditCompleteActions.Add(act);
	}

	public void AddEditSaveAction(Action<bool> act)
	{
		_onEditSaveActions.Add(act);
	}

	protected List<Layer> FlattenChildrenDFS()
	{
		List<Layer> layers = new List<Layer>();
		checked
		{
			int count = Nodes.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				TreeNode treeNode = Nodes[i];
				if (treeNode != null && treeNode is Layer layer)
				{
					layers.Add(layer);
					layers.AddRange(layer.FlattenChildrenDFS());
				}
			}
			return layers;
		}
	}

	protected List<Layer> FlattenCheckedChildrenDFS()
	{
		List<Layer> layers = new List<Layer>();
		checked
		{
			int count = Nodes.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				TreeNode treeNode = Nodes[i];
				if (treeNode != null && treeNode.Checked && treeNode is Layer layer)
				{
					layers.Add(layer);
					layers.AddRange(layer.FlattenCheckedChildrenDFS());
				}
			}
			return layers;
		}
	}

	public virtual bool SetSelection(PointM point, double nearDistance, SelectionMode selectionMode, ref bool layerNeedsRedraw)
	{
		return false;
	}

	public virtual bool SetSelection(Extent extent, SelectionMode selectionMode, SelectionRange selectionRange, ref bool layerNeedsRedraw)
	{
		return false;
	}

	protected virtual void OnSymbologyChanged()
	{
		ResetBitmap = true;
	}

	public void NotifyLayerCheckChanged()
	{
		OnLayerCheckChanged();
	}

	protected virtual void OnLayerCheckChanged()
	{
	}

	public List<IAnimatingLayer> GetAnimatableLayers(bool includeUncheckedLayers)
	{
		List<IAnimatingLayer> iAnimatingLayers = new List<IAnimatingLayer>();
		foreach (Layer node in Nodes)
		{
			if (node.Checked || includeUncheckedLayers)
			{
				if (node is IAnimatingLayer { CanAnimate: not false } animatingLayer)
				{
					iAnimatingLayers.Add(animatingLayer);
				}
				iAnimatingLayers.AddRange(node.GetAnimatableLayers(includeUncheckedLayers));
			}
		}
		return iAnimatingLayers;
	}

	public static List<Layer> LoadLayersNew(XmlElement xmlLayers)
	{
		List<Layer> layers = new List<Layer>();
		foreach (XmlNode xmlLayer in xmlLayers)
		{
			try
			{
				Layer layer = LoadLayerNew((XmlElement)xmlLayer);
				if (layer != null)
				{
					layers.Add(layer);
				}
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				ProjectData.ClearProjectError();
			}
		}
		return layers;
	}

	public static Layer LoadLayerNew(XmlElement xmlEl)
	{
		Layer layer = ConstructEmpty(xmlEl);
		if (layer == null)
		{
			SharedData.ReportMessage("Could not load layer " + GetLayerNameFromXML(xmlEl).InSingleQuotes() + ", " + GetTypeNameFromXML(xmlEl).InSingleQuotes());
			return null;
		}
		layer.XMLLoad(xmlEl);
		layer.OnLoadCompleted();
		return layer;
	}

	protected void UpdateContainsData(H5Reader hr = null)
	{
		_containsData = LayerHasData(hr);
	}

	public virtual bool LayerHasData(H5Reader hr = null)
	{
		return true;
	}

	public virtual string GetParentIdentifiers()
	{
		string parentTerm = "";
		for (TreeNode parent = Parent; parent != null; parent = parent.Parent)
		{
			parentTerm = parentTerm + parent.Name + "|";
		}
		return parentTerm.TrimEnd('|');
	}

	public XmlElement XMLSave(XmlDocument doc, bool recurse = true)
	{
		if (IsDebugLayer)
		{
			return null;
		}
		XmlElement xmlElement = doc.CreateElement("Layer");
		XMLSaveMetadata(doc, xmlElement);
		if (recurse)
		{
			XMLSaveChildren(doc, xmlElement);
		}
		if (!xmlElement.HasChildNodes && this is IGeometryLayer)
		{
			bool condition = false;
			foreach (XmlAttribute attribute in xmlElement.Attributes)
			{
				if (Operators.CompareString(attribute.Name, "Name", TextCompare: false) != 0 && Operators.CompareString(attribute.Name, "Type", TextCompare: false) != 0)
				{
					condition = true;
				}
			}
			if (condition)
			{
				return xmlElement;
			}
			return null;
		}
		return xmlElement;
	}

	protected virtual void XMLSaveMetadata(XmlDocument doc, XmlElement xmlEl)
	{
		if (Operators.CompareString(Name, DefaultLayerName, TextCompare: false) != 0)
		{
			xmlEl.SetAttribute("Name", Name);
		}
		xmlEl.SetAttribute("Type", GetType().Name);
		if (!string.IsNullOrEmpty(SourceFilename) && !(this is IGeometryLayer))
		{
			XMLSaveFilename(doc, xmlEl, SourceFilename);
		}
	}

	protected virtual void XMLSaveChildren(XmlDocument doc, XmlElement xmlEl)
	{
		foreach (Layer node in Nodes)
		{
			XmlElement xmlElement = node.XMLSave(doc);
			if (xmlElement != null)
			{
				xmlEl.AppendChild(xmlElement);
			}
		}
	}

	public void XMLLoad(XmlElement xmlEl)
	{
		XMLLoadMetadata(xmlEl);
		XMLLoadChildren(xmlEl);
	}

	protected virtual void XMLLoadMetadata(XmlElement xmlNode)
	{
		if (xmlNode != null)
		{
			string stringAttribute = XML.StringAttribute(xmlNode, "Name", DefaultLayerName);
			if (!string.IsNullOrEmpty(stringAttribute))
			{
				SetDisplayName(stringAttribute);
			}
			string sourceFilename = XMLLoadFilename(xmlNode);
			if (!string.IsNullOrEmpty(sourceFilename))
			{
				SourceFilename = sourceFilename;
			}
		}
	}

	protected virtual void XMLLoadChildren(XmlElement xmlEl)
	{
		if (xmlEl == null)
		{
			return;
		}
		XmlNodeList subLayerList = GetSubLayerList(xmlEl);
		foreach (XmlElement subLayerList2 in subLayerList)
		{
			try
			{
				if (!XMLLoadChild(subLayerList2))
				{
					Layer layer = LoadLayerNew(subLayerList2);
					if (layer != null)
					{
						Nodes.Add(layer);
					}
				}
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				ProjectData.ClearProjectError();
			}
		}
	}

	protected virtual bool XMLLoadChild(XmlElement xmlEl)
	{
		return false;
	}

	protected bool LoadXmlIntoExistingNodes(XmlElement xmlEl, bool recurse)
	{
		string typeNameFromXML = GetTypeNameFromXML(xmlEl);
		if (recurse)
		{
			throw new NotImplementedException();
		}
		foreach (Layer node in Nodes)
		{
			string name = node.GetType().Name;
			if (typeNameFromXML.RoughlyEquals(name))
			{
				node.XMLLoad(xmlEl);
				return true;
			}
		}
		return false;
	}

	public virtual void OnLoadCompleted()
	{
		if (_expandWhenLoaded)
		{
			Expand();
		}
	}

	protected static string GetTypeNameFromXML(XmlElement xmlEl)
	{
		return XML.StringAttribute(xmlEl, "Type");
	}

	protected static string GetLayerNameFromXML(XmlElement xmlEl)
	{
		return XML.StringAttribute(xmlEl, "Name");
	}

	protected static string GetRelativeFilenameFromXML(XmlElement xmlEl)
	{
		return XML.StringAttribute(xmlEl, "Filename");
	}

	protected static XmlNodeList GetSubLayerList(XmlElement xmlEl)
	{
		return xmlEl.SelectNodes("Layer");
	}

	protected static Layer ConstructEmpty(XmlNode xmlLayer)
	{
		string trim = GetTypeNameFromXML((XmlElement)xmlLayer).Trim();
		Layer result;
		if (string.IsNullOrEmpty(trim))
		{
			result = null;
		}
		else
		{
			Type getFeatureLayerTypeFromFileTerm = null;
			if (Operators.CompareString(trim.ToLower(), "featurelayer", TextCompare: false) == 0 || Operators.CompareString(trim.ToLower(), "FeatureLayer".ToLower(), TextCompare: false) == 0)
			{
				getFeatureLayerTypeFromFileTerm = GetFeatureLayerTypeFromFile(XMLLoadFilename(xmlLayer, SharedData.RasMapFilename));
				if ((object)getFeatureLayerTypeFromFileTerm == null)
				{
					getFeatureLayerTypeFromFileTerm = typeof(UnknownLayer);
				}
			}
			if ((object)getFeatureLayerTypeFromFileTerm == null && !LayerNameTypeMap.TryGetValue(trim, out getFeatureLayerTypeFromFileTerm))
			{
				result = null;
			}
			else
			{
				try
				{
					result = ConstructEmpty(getFeatureLayerTypeFromFileTerm);
				}
				catch (Exception ex)
				{
					ProjectData.SetProjectError(ex);
					Exception ex2 = ex;
					result = null;
					ProjectData.ClearProjectError();
				}
			}
		}
		return result;
	}

	private static Type GetFeatureLayerTypeFromFile(string fn)
	{
		if (string.IsNullOrEmpty(fn))
		{
			return null;
		}
		if (Operators.CompareString(System.IO.Path.GetExtension(fn).ToLower(), ".shp", TextCompare: false) == 0)
		{
			return ShapefileStorage.GetFeatureLayerType(fn);
		}
		return null;
	}

	protected static Layer TryConstructEmpty(Type lyrType)
	{
		ConstructorInfo constructor = lyrType.GetConstructor(Type.EmptyTypes);
		if ((object)constructor == null)
		{
			return null;
		}
		return constructor.Invoke(new object[0]) as Layer;
	}

	protected static Layer ConstructEmpty(Type lyrType)
	{
		return ((lyrType.GetConstructor(Type.EmptyTypes) ?? throw new Exception("Layer type " + lyrType.FullName + " needs an explicit empty constructor.")).Invoke(new object[0]) as Layer) ?? throw new Exception("Layer type " + lyrType.FullName + " could not be constructed with an explicit empty constructor.");
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

	public static Layer FromFile(string filename)
	{
		string fileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(filename);
		string toLower = System.IO.Path.GetExtension(filename).ToLower();
		if (Operators.CompareString(toLower, ".hdf", TextCompare: false) == 0)
		{
			switch (SharedData.HDFReadFileType(filename))
			{
			case "HEC Land Cover":
			{
				LandCoverLayer lyr = null;
				string errMessage = "";
				if (LandCoverLayer.TryLoadLayer(filename, ref lyr, ref errMessage))
				{
					return lyr;
				}
				return null;
			}
			case "HEC Terrain":
				return new TerrainLayer(fileNameWithoutExtension, filename);
			case "HEC-RAS Results":
				return new RASResults(filename);
			case "HEC-RAS Geometry":
				return new RASGeometry(filename);
			}
			if (SharedData.RasMapper != null)
			{
				SharedData.ReportMessage("Expecting this HDF file to have FileType attribute at root, either it does not exist or it's invalid.");
			}
		}
		if (Operators.CompareString(toLower, ".shp", TextCompare: false) == 0)
		{
			return ShapefileStorage.LoadFeatureLayerShell(filename);
		}
		if (Operators.CompareString(toLower, ".dss", TextCompare: false) == 0)
		{
			return new DSSLayer(filename);
		}
		GDALFileInfo gDALFileInfo = new GDALFileInfo(filename);
		if (gDALFileInfo.IsValidRaster)
		{
			if (gDALFileInfo.IsWMSLayer())
			{
				return new WMSLayer(fileNameWithoutExtension, filename);
			}
			if (gDALFileInfo.IsImage())
			{
				return new ImageLayer(fileNameWithoutExtension, filename);
			}
			if (gDALFileInfo.IsDiscreteByte())
			{
				return new DiscreteValueLayer(fileNameWithoutExtension, filename);
			}
			if (gDALFileInfo.IsInterpolated())
			{
				return new InterpolatedLayer(fileNameWithoutExtension, filename);
			}
		}
		if (toLower.RoughlyEquals(".grd"))
		{
			return new SMSFeatureLayer(System.IO.Path.GetFileNameWithoutExtension(filename), filename);
		}
		return null;
	}

	public static List<Layer> FromFiles(IEnumerable<string> filenames)
	{
		List<Layer> result;
		try
		{
			List<Layer> layers = new List<Layer>();
			List<string> stringList = new List<string>();
			List<string> stringListList = new List<string>();
			List<string> stringList2 = new List<string>();
			List<string> stringList3 = new List<string>();
			List<string> stringList4 = new List<string>();
			foreach (string filename in filenames)
			{
				GDALFileInfo gDALFileInfo = new GDALFileInfo(filename);
				if (gDALFileInfo.IsValidRaster)
				{
					if (gDALFileInfo.IsImage())
					{
						stringList.Add(filename);
					}
					else if (gDALFileInfo.IsDiscreteByte())
					{
						stringList3.Add(filename);
					}
					else if (gDALFileInfo.IsInterpolated())
					{
						if (gDALFileInfo.BandCount > 1)
						{
							stringList2.Add(filename);
						}
						else
						{
							stringListList.Add(filename);
						}
					}
					else
					{
						stringList4.Add(filename);
					}
				}
				else
				{
					stringList4.Add(filename);
				}
			}
			GroupRasters("image", stringList);
			GroupRasters("discrete raster data", stringList3);
			GroupRasters("interpolated raster data", stringListList);
			foreach (string textValue in stringList)
			{
				Layer layer = FromFile(textValue);
				if (layer != null)
				{
					layers.Add(layer);
				}
			}
			foreach (string textValue2 in stringList3)
			{
				Layer layer2 = FromFile(textValue2);
				if (layer2 != null)
				{
					layers.Add(layer2);
				}
			}
			foreach (string textValue3 in stringListList)
			{
				Layer layer3 = FromFile(textValue3);
				if (layer3 != null)
				{
					layers.Add(layer3);
				}
			}
			foreach (string textValue4 in stringList2)
			{
				layers.Add(new MultiBandInterpolatedLayer(System.IO.Path.GetFileNameWithoutExtension(textValue4), textValue4));
			}
			foreach (string textValue5 in stringList4)
			{
				Layer layer4 = FromFile(textValue5);
				if (layer4 != null)
				{
					layers.Add(layer4);
				}
			}
			result = layers;
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			result = null;
			ProjectData.ClearProjectError();
		}
		return result;
	}

	public static string XMLLoadFilename(XmlNode xmlLayer, string baseFilename = "")
	{
		string relativeFilenameFromXML = GetRelativeFilenameFromXML((XmlElement)xmlLayer);
		if (string.IsNullOrEmpty(relativeFilenameFromXML))
		{
			return "";
		}
		relativeFilenameFromXML = Environment.ExpandEnvironmentVariables(relativeFilenameFromXML);
		if (string.IsNullOrEmpty(baseFilename))
		{
			baseFilename = SharedData.RasMapFilename;
		}
		return RasMapperLib.Utilities.Path.MakeAbsolute(relativeFilenameFromXML, baseFilename);
	}

	public static void XMLSaveFilename(XmlDocument doc, XmlElement xmlLayer, string filename, string baseFilename = "")
	{
		string textValue = "";
		if (filename.StartsWith(APPDATA_PATH) || filename.ToLowerInvariant().StartsWith(APPDATA_PATH.ToLowerInvariant()))
		{
			textValue = "%LocalAppData%" + filename.Substring(APPDATA_PATH.Length);
		}
		else
		{
			if (Operators.CompareString(baseFilename, "", TextCompare: false) == 0)
			{
				baseFilename = SharedData.RasMapFilename;
			}
			textValue = RasMapperLib.Utilities.Path.MakeRelative(filename, baseFilename);
		}
		xmlLayer.SetAttribute("Filename", textValue);
	}

	public virtual bool AllSourceFilesExist()
	{
		return SourceFileExists;
	}

	public virtual DateTime DateLastModified()
	{
		if (!SourceFileExists)
		{
			return DateTime.MinValue;
		}
		return File.GetLastWriteTime(SourceFilename);
	}

	public virtual string DateLastModifiedString()
	{
		DateTime dateTime = DateLastModified();
		if (DateTime.Compare(dateTime, DateTime.MinValue) == 0)
		{
			return "";
		}
		return DSS.ConvertToDSSDateTime(dateTime);
	}

	public static T TryFindOrCreateLayer<T>(string fn, BaseGroup baseGroup, Func<T> construct) where T : Layer
	{
		if (string.IsNullOrEmpty(fn))
		{
			return null;
		}
		if (baseGroup != null)
		{
			if (!(baseGroup.FindLayerBySourceFilename(fn) is T result))
			{
				T construct2 = construct();
				baseGroup.Nodes.Add(construct2);
				return construct2;
			}
			return result;
		}
		return construct();
	}

	private static void GroupRasters(string rasterType, List<string> files)
	{
		if (files.Count > 1 && MessageBox.Show(Strings.Format(files.Count) + " files are " + rasterType + " files. Would you like to group these into a single virtual image layer?", "Virtual Raster", MessageBoxButtons.YesNo) == DialogResult.Yes)
		{
			string changeExtension = System.IO.Path.ChangeExtension(files.First(), ".vrt");
			string StdOut = "";
			string StdErr = "";
			Executables.GdalBuildVRT(files.ToArray(), changeExtension, out StdOut, out StdErr);
			files.Clear();
			files.Add(changeExtension);
		}
	}

	public override string ToString()
	{
		return Name;
	}
}
