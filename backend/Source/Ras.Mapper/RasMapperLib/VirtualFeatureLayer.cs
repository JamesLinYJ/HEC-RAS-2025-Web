using System;
using System.Collections.Generic;
using System.Data;
using System.Xml;
using RasMapperLib.Utilities;

namespace RasMapperLib;

public class VirtualFeatureLayer : FeatureLayer, IVirtualFeatureLayer
{
	private const string sParentIdentifiers = "ParentIdentifiers";

	private FeatureLayer _sourceLayer;

	private string _countAttributeName;

	private List<DataColumnGroup> _uniqueColumnGroups;

	public override bool ForwardEditRequestsToParent { get; }

	public override bool CanMoveToAnotherLevel { get; }

	public override bool CanMoveWithinLevel { get; }

	public override bool CanRename { get; }

	public override bool CanRemove { get; }

	public override bool UsesParentSourceFilename { get; }

	public override Type BaseGroupType { get; }

	public override bool CanChangeCheck { get; }

	public override bool CanEdit { get; }

	public override bool CanExportAsPoints { get; }

	public override bool CanExportAsShapefile { get; }

	public override bool CanFeaturesBeCopied { get; }

	public override bool CanLabelFeatures { get; }

	public override bool CanRenderWithSiblings { get; }

	public override bool CanZoomToLayer { get; }

	public override string DefaultLayerName { get; }

	public override string FeatureColumnName { get; }

	public override string LayerPropertiesText { get; }

	public override bool RefreshOnNodeSelection { get; }

	public override string SimpleFeatureTypeName { get; set; }

	public override string SourceFilename { get; set; }

	public override Predicate<DataRow> FeatureRowFilter { get; }

	public override Extent Extent
	{
		get
		{
			ComputeExtent();
			return base.Extent;
		}
	}

	public FeatureLayer Layer => this;

	public FeatureLayer SourceLayer => _sourceLayer;

	public string CountAttributeName => _countAttributeName;

	public VirtualFeatureLayer(FeatureLayer sourceLayer, string countAttributeName, Predicate<DataRow> featureRowFilter = null)
		: base(sourceLayer.Name)
	{
		DefaultLayerName = "Virtual Feature Layer";
		_sourceLayer = sourceLayer;
		_countAttributeName = countAttributeName;
		FeatureRowFilter = featureRowFilter;
		ForwardEditRequestsToParent = _sourceLayer.ForwardEditRequestsToParent;
		CanMoveToAnotherLevel = _sourceLayer.CanMoveToAnotherLevel;
		CanMoveWithinLevel = _sourceLayer.CanMoveWithinLevel;
		CanRename = _sourceLayer.CanRename;
		CanRemove = _sourceLayer.CanRemove;
		UsesParentSourceFilename = _sourceLayer.UsesParentSourceFilename;
		BaseGroupType = _sourceLayer.BaseGroupType;
		CanChangeCheck = _sourceLayer.CanChangeCheck;
		CanEdit = _sourceLayer.CanEdit;
		CanExportAsPoints = _sourceLayer.CanExportAsPoints;
		CanExportAsShapefile = _sourceLayer.CanExportAsShapefile;
		CanFeaturesBeCopied = _sourceLayer.CanFeaturesBeCopied;
		CanLabelFeatures = _sourceLayer.CanLabelFeatures;
		CanRenderWithSiblings = _sourceLayer.CanRenderWithSiblings;
		CanZoomToLayer = _sourceLayer.CanZoomToLayer;
		FeatureColumnName = _sourceLayer.FeatureColumnName;
		LayerPropertiesText = _sourceLayer.LayerPropertiesText;
		RefreshOnNodeSelection = _sourceLayer.RefreshOnNodeSelection;
		SimpleFeatureTypeName = _sourceLayer.SimpleFeatureTypeName;
		SourceFilename = _sourceLayer.SourceFilename;
		AvailablePlotOptions = new PlotOptionCollection();
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

	protected internal override string GetCMSTitle(int fid)
	{
		if (SourceLayer == null)
		{
			return base.GetCMSTitle(fid);
		}
		return SourceLayer.GetCMSTitle(fid);
	}

	public override List<PlotOption> GetStaticPlotOptions()
	{
		if (SourceLayer == null)
		{
			return base.GetStaticPlotOptions();
		}
		return SourceLayer.GetStaticPlotOptions();
	}

	public override List<PlotOption> GetDefaultPlotOptions()
	{
		if (SourceLayer == null)
		{
			return base.GetDefaultPlotOptions();
		}
		return SourceLayer.GetDefaultPlotOptions();
	}

	public override Feature Feature(int fId)
	{
		return SourceLayer.Feature(fId);
	}

	protected override void XMLSaveMetadata(XmlDocument doc, XmlElement xmlEl)
	{
		base.XMLSaveMetadata(doc, xmlEl);
		string parentIdentifiers = GetParentIdentifiers();
		xmlEl.SetAttribute("ParentIdentifiers", parentIdentifiers);
	}

	public override string GetParentIdentifiers()
	{
		string parentTerm = "";
		TreeNode parent = Parent;
		int ordinal = 0;
		while (parent != null && ordinal == 0)
		{
			if (!(parent is VirtualFeatureLayer))
			{
				ordinal = checked(ordinal + 1);
			}
			parentTerm = parentTerm + parent.Name + "|";
			parent = parent.Parent;
		}
		return parentTerm.TrimEnd('|');
	}

	public static string GetParentIdentifierFromXML(XmlElement xmlEl)
	{
		return XML.StringAttribute(xmlEl, "ParentIdentifiers");
	}

	public override List<DataColumnGroup> GetUniqueColumnGroups()
	{
		return _sourceLayer.GetUniqueColumnGroups();
	}

	public override List<DataColumnGroup> GetUniqueColumnGroupCopies()
	{
		if (_uniqueColumnGroups == null)
		{
			_uniqueColumnGroups = _sourceLayer.GetUniqueColumnGroupCopies();
		}
		return _uniqueColumnGroups;
	}
}
