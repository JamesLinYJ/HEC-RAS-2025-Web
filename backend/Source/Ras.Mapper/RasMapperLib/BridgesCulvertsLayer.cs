using System;
using System.Collections.Generic;
using System.Data;
using System.Runtime.CompilerServices;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

public class BridgesCulvertsLayer : PolylineFeatureLayer, IGeometryLayer, IVirtualFeatureLayer, IProvideWidth
{
	public const string sBridgeCulvert = "Bridges/Culverts";

	public const string HDFGroupName = "Geometry/Structures";

	private readonly StructureLayer _structLayer;

	private List<DataColumnGroup> _uniqueColumnGroups;

	public override string SimpleFeatureTypeName { get; set; }

	public override bool ForwardEditRequestsToParent { get; }

	public override bool CanMoveToAnotherLevel { get; }

	public override bool CanMoveWithinLevel { get; }

	public override bool CanRename { get; }

	public override bool CanRemove { get; }

	public override bool UsesParentSourceFilename { get; }

	public override bool RefreshOnNodeSelection { get; }

	public override Predicate<DataRow> FeatureRowFilter => [SpecialName] (DataRow row) =>
	{
		string row2 = row["Type"].ToString();
		return string.IsNullOrEmpty(row2) || Operators.CompareString(row2, "Bridge", TextCompare: false) == 0 || Operators.CompareString(row2, "Culvert", TextCompare: false) == 0 || Operators.CompareString(row2, "Multiple Opening", TextCompare: false) == 0;
	};

	public RASGeometry Geometry => _structLayer?.Geometry;

	public FeatureLayer Layer => this;

	public FeatureLayer SourceLayer => _structLayer;
string IGeometryLayer.HDFGroupName=> "Geometry/Structures";

	public string CountAttributeName => "Bridge/Culvert Count";

	public BridgesCulvertsLayer(StructureLayer structLayer)
		: base("Bridges/Culverts")
	{
		SimpleFeatureTypeName = "Bridge";
		ForwardEditRequestsToParent = true;
		CanMoveToAnotherLevel = false;
		CanMoveWithinLevel = false;
		CanRename = false;
		CanRemove = false;
		UsesParentSourceFilename = true;
		RefreshOnNodeSelection = true;
		_structLayer = structLayer;
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

	public override List<PlotOption> GetStaticPlotOptions()
	{
		if (_structLayer == null)
		{
			return base.GetStaticPlotOptions();
		}
		return _structLayer.GetStaticPlotOptions();
	}

	public override List<PlotOption> GetDefaultPlotOptions()
	{
		if (_structLayer == null)
		{
			return base.GetDefaultPlotOptions();
		}
		return _structLayer.GetDefaultPlotOptions();
	}

	private Profile GetUSXSProfile(int bridgeFID)
	{
		string uSRiverName = _structLayer.GetUSRiverName(bridgeFID);
		string uSReachName = _structLayer.GetUSReachName(bridgeFID);
		string uSRiverStationName = _structLayer.GetUSRiverStationName(bridgeFID);
		string name = RASXS.FormNameUsingRRRS(uSRiverName, uSReachName, uSRiverStationName);
		int featureByName = _structLayer.Geometry.XS.GetFeatureByName(name);
		Profile profile = _structLayer.Geometry.XS.GetProfile(featureByName);
		if (profile == null)
		{
			return null;
		}
		return profile;
	}

	private Profile GetDSXSProfile(int bridgeFID)
	{
		string dSRiverName = _structLayer.GetDSRiverName(bridgeFID);
		string dSReachName = _structLayer.GetDSReachName(bridgeFID);
		string dSRiverStationName = _structLayer.GetDSRiverStationName(bridgeFID);
		string name = RASXS.FormNameUsingRRRS(dSRiverName, dSReachName, dSRiverStationName);
		int featureByName = _structLayer.Geometry.XS.GetFeatureByName(name);
		Profile profile = _structLayer.Geometry.XS.GetProfile(featureByName);
		if (profile == null)
		{
			return null;
		}
		return profile;
	}

	private int GetDSXSID(int bridgeFID)
	{
		string uSRiverName = _structLayer.GetUSRiverName(bridgeFID);
		string uSReachName = _structLayer.GetUSReachName(bridgeFID);
		string uSRiverStationName = _structLayer.GetUSRiverStationName(bridgeFID);
		string name = RASXS.FormNameUsingRRRS(uSRiverName, uSReachName, uSRiverStationName);
		return _structLayer.Geometry.XS.GetFeatureByName(name);
	}

	public void HDFLoadFeatureTable(H5Reader hr)
	{
	}

	void IGeometryLayer.HDFLoadFeatureTable(H5Reader hr)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFLoadFeatureTable
		this.HDFLoadFeatureTable(hr);
	}

	public void HDFSaveFeatureTable(H5Writer hw)
	{
	}

	void IGeometryLayer.HDFSaveFeatureTable(H5Writer hw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFSaveFeatureTable
		this.HDFSaveFeatureTable(hw);
	}

	public override List<DataColumnGroup> GetUniqueColumnGroups()
	{
		return _structLayer.GetUniqueColumnGroups();
	}

	public override List<DataColumnGroup> GetUniqueColumnGroupCopies()
	{
		if (_uniqueColumnGroups == null)
		{
			List<DataColumnGroup> uniqueColumnGroupCopies = _structLayer.GetUniqueColumnGroupCopies();
			_uniqueColumnGroups = new List<DataColumnGroup>();
			foreach (DataColumnGroup uniqueColumnGroupCopy in uniqueColumnGroupCopies)
			{
				if (Operators.CompareString(uniqueColumnGroupCopy.DisplayName, "River, Reach, River Station", TextCompare: false) == 0 || Operators.CompareString(uniqueColumnGroupCopy.DisplayName, "Geospatial Relevant", TextCompare: false) == 0)
				{
					uniqueColumnGroupCopy.IsVisible = true;
				}
				else
				{
					uniqueColumnGroupCopy.IsVisible = false;
				}
				_uniqueColumnGroups.Add(uniqueColumnGroupCopy);
			}
		}
		return _uniqueColumnGroups;
	}

	public override string GetFeatureDisplayLabel(int fid)
	{
		return _structLayer.GetFeatureDisplayLabel(fid);
	}

	public float GetWidth(int fid)
	{
		return ((IProvideWidth)_structLayer).GetWidth(fid);
	}

	float IProvideWidth.GetWidth(int fid)
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetWidth
		return this.GetWidth(fid);
	}

	public override string GetFeatureName(int fid)
	{
		return _structLayer.GetFeatureName(fid);
	}

	public override void SetFeatureName(int fid, string newName)
	{
		_structLayer.SetFeatureName(fid, newName);
	}
}
