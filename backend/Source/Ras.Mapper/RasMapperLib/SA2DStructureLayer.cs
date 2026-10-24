using System;
using System.Collections.Generic;
using System.Data;
using System.Runtime.CompilerServices;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

public class SA2DStructureLayer : PolylineFeatureLayer, IGeometryLayer, IVirtualFeatureLayer, IProvideWidth, IEnforceBreaklineLayer, IHasProfileProperties
{
	public const string sSA2D = "SA/2D Connections";

	public const string HDFGroupName = "Geometry/Structures";

	private StructureLayer _structLayer;

	private List<DataColumnGroup> _uniqueColumnGroups;

	public override string SimpleFeatureTypeName { get; set; }

	public override bool ForwardEditRequestsToParent { get; }

	public override bool CanMoveToAnotherLevel { get; }

	public override bool CanMoveWithinLevel { get; }

	public override bool CanRename { get; }

	public override bool CanRemove { get; }

	public override bool UsesParentSourceFilename { get; }

	public override bool RefreshOnNodeSelection { get; }

	public override Predicate<DataRow> FeatureRowFilter => [SpecialName] (DataRow row) => string.IsNullOrEmpty(row["Type"].ToString()) || Operators.CompareString(row["Type"].ToString(), "Connection", TextCompare: false) == 0;

	public RASGeometry Geometry => _structLayer?.Geometry;

	public FeatureLayer Layer => this;

	public FeatureLayer SourceLayer => _structLayer;

	public string NearSpacingColumnName => ((IEnforceBreaklineLayer)_structLayer).NearSpacingColumnName;

	public string NearSpacingDiskName => ((IEnforceBreaklineLayer)_structLayer).NearSpacingDiskName;

	public string FarSpacingColumnName => ((IEnforceBreaklineLayer)_structLayer).FarSpacingColumnName;

	public string FarSpacingDiskName => ((IEnforceBreaklineLayer)_structLayer).FarSpacingDiskName;

	public string NearRepeatsColumnName => ((IEnforceBreaklineLayer)_structLayer).NearRepeatsColumnName;

	public string NearRepeatsDiskName => ((IEnforceBreaklineLayer)_structLayer).NearRepeatsDiskName;
string IGeometryLayer.HDFGroupName=> "Geometry/Structures";

	public string CountAttributeName => "Connection Count";

	public SA2DStructureLayer(StructureLayer structLayer)
		: base("SA/2D Connections")
	{
		SimpleFeatureTypeName = "SA/2D Connection";
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
		List<PlotOption> staticPlotOptions = ((_structLayer != null) ? _structLayer.GetStaticPlotOptions() : base.GetStaticPlotOptions());
		staticPlotOptions.Add(new PlotOption("Plot Breach"));
		return staticPlotOptions;
	}

	public override List<PlotOption> GetDefaultPlotOptions()
	{
		List<PlotOption> defaultPlotOptions = ((_structLayer != null) ? _structLayer.GetDefaultPlotOptions() : base.GetDefaultPlotOptions());
		defaultPlotOptions.Add(new PlotOption("Plot Breach"));
		return defaultPlotOptions;
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
				if (Operators.CompareString(uniqueColumnGroupCopy.DisplayName, "Connection Information", TextCompare: false) == 0 || Operators.CompareString(uniqueColumnGroupCopy.DisplayName, "Geospatial Relevant", TextCompare: false) == 0 || Operators.CompareString(uniqueColumnGroupCopy.DisplayName, "Upstream Information", TextCompare: false) == 0 || Operators.CompareString(uniqueColumnGroupCopy.DisplayName, "Downstream Information", TextCompare: false) == 0 || Operators.CompareString(uniqueColumnGroupCopy.DisplayName, "Breakline Spacing", TextCompare: false) == 0 || Operators.CompareString(uniqueColumnGroupCopy.DisplayName, "Mode", TextCompare: false) == 0)
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

	public override void OnStartEditSession()
	{
		base.OnStartEditSession();
		checked
		{
			_structLayer.IsEditingDataSemaphore++;
		}
	}

	public override void AfterStoppedEditing()
	{
		base.AfterStoppedEditing();
		checked
		{
			_structLayer.IsEditingDataSemaphore--;
			SelectedFeatures.ClearSelection();
		}
	}

	public bool GetIsBridge(int fid)
	{
		return Operators.CompareString(_structLayer.GetMode(fid), "Bridge Opening", TextCompare: false) == 0;
	}

	public bool GetIsInternal(int fid)
	{
		if (Operators.CompareString(_structLayer.GetUSType(fid), "2D", TextCompare: false) == 0 && Operators.CompareString(_structLayer.GetDSType(fid), "2D", TextCompare: false) == 0)
		{
			return Operators.CompareString(_structLayer.GetUSSA2DName(fid), _structLayer.GetDSSA2DName(fid), TextCompare: false) == 0;
		}
		return false;
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

	public Profile GetProfile(int xsid)
	{
		return ((IHasProfileProperties)_structLayer).GetProfile(xsid);
	}

	Profile IHasProfileProperties.GetProfile(int xsid)
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetProfile
		return this.GetProfile(xsid);
	}

	public void SetProfile(int xsid, Profile pf)
	{
		((IHasProfileProperties)_structLayer).SetProfile(xsid, pf);
	}

	void IHasProfileProperties.SetProfile(int xsid,Profile pf)
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetProfile
		this.SetProfile(xsid, pf);
	}

	public IProfileInfo GetProfileProperties(int xsid)
	{
		return ((IHasProfileProperties)_structLayer).GetProfileProperties(xsid);
	}

	IProfileInfo IHasProfileProperties.GetProfileProperties(int xsid)
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetProfileProperties
		return this.GetProfileProperties(xsid);
	}

	public void SetProfileProperties(int xsid, IProfileInfo pfInfo)
	{
		((IHasProfileProperties)_structLayer).SetProfileProperties(xsid, pfInfo);
	}

	void IHasProfileProperties.SetProfileProperties(int xsid,IProfileInfo pfInfo)
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetProfileProperties
		this.SetProfileProperties(xsid, pfInfo);
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
