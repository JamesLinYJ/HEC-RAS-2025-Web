using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Rasters.Classifications;
using H5Assist.VB;

namespace RasMapperLib;

public class ClassificationOverrideLayer : PolygonFeatureLayer, IGeometryLayer
{
	public const string sClassification = "Classification";

	private Func<LandCoverLayer> _getLC;

	private Classification _mergedClassification;

	private string HDFGroupName { get; }

	public override bool CanRename { get; }

	public LandCoverLayer LandCover => _getLC();

	public Classification MergedClassification
	{
		get
		{
			if (_mergedClassification != null)
			{
				return _mergedClassification;
			}
			List<string> classNames = GetValidClassificationNames().ToList();
			LandCoverLayer landCoverLayer = _getLC();
			if (landCoverLayer == null)
			{
				_mergedClassification = new Classification();
			}
			else
			{
				_mergedClassification = new Classification(landCoverLayer.Classification.GetAllClassifications());
			}
			Classification.AddRangeAsAvailableIDs(_mergedClassification, classNames);
			return _mergedClassification;
		}
	}

	public RASGeometry Geometry { get; }

	public ClassificationOverrideLayer(RASGeometry geo, string lyrName, string geoSubFolder, Func<LandCoverLayer> getLC)
		: base(lyrName, geo.SourceFilename)
	{
		CanRename = false;
		Geometry = geo;
		_getLC = getLC;
		HDFGroupName = "Geometry/" + geoSubFolder;
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddManagedAttributeColumn("Classification", "Classification", typeof(string));
	}

	public void HDFLoadFeatureTable(H5Reader hr)
	{
		if (hr.ExistsAsGroup(HDFGroupName))
		{
			HDF5Storage.ReadPolyFeatures(this, hr, HDFGroupName + "/Polygon");
			EnsurePositivePolygonOrientations();
			string hdfGroupNameTerm = HDFGroupName + "/Attributes";
			if (hr.ExistsAsDataset(hdfGroupNameTerm))
			{
				ReadManagedColumns(hr, hdfGroupNameTerm);
			}
		}
	}

	void IGeometryLayer.HDFLoadFeatureTable(H5Reader hr)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFLoadFeatureTable
		this.HDFLoadFeatureTable(hr);
	}

	public void HDFSaveFeatureTable(H5Writer hw)
	{
		if (FeatureCount() == 0)
		{
			hw.Delete(HDFGroupName);
			return;
		}
		hw.CreateGroup(HDFGroupName);
		HDF5Storage.WritePolygonFeatures(this, hw, HDFGroupName + "/Polygon");
		WriteManagedColumns(hw, HDFGroupName + "/Attributes");
	}

	void IGeometryLayer.HDFSaveFeatureTable(H5Writer hw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFSaveFeatureTable
		this.HDFSaveFeatureTable(hw);
	}

	protected override void OnFeatureChanged(FeatureChangedEventArgs args)
	{
		base.OnFeatureChanged(args);
	}

	public void NotifyDataChanged()
	{
		_mergedClassification = null;
	}

	private HashSet<string> GetValidClassificationNames()
	{
		HashSet<string> validClassificationNames = new HashSet<string>();
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				string classification = GetClassification(i);
				if (LandCoverLayer.IsValidClassificationName(classification))
				{
					validClassificationNames.Add(classification);
				}
			}
			return validClassificationNames;
		}
	}

	public void SetClassification(int fid, string classification)
	{
		FeatureRow(fid)["Classification"] = classification.Trim();
	}

	public string GetClassification(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "Classification").Trim();
	}

	string IGeometryLayer.HDFGroupName => HDFGroupName;
}
