using System;
using System.IO;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

public abstract class ObstructionAreaLayer : PolygonFeatureLayer, IGeometryLayer
{
	public const string sElevation = "Elevation";

	public abstract string HDFGroupName { get; }

	public RASGeometry Geometry { get; }

	private string IGeometryLayer_HDFGroupName => HDFGroupName;

	public ObstructionAreaLayer(string name, RASGeometry geometry)
		: base(name, geometry.SourceFilename)
	{
		Geometry = geometry;
	}

	public void HDFLoadFeatureTable(H5Reader hr)
	{
		if (string.IsNullOrEmpty(SourceFilename) || !SourceFileExists)
		{
			return;
		}
		checked
		{
			try
			{
				if (Operators.CompareString(Path.GetExtension(SourceFilename).ToLowerInvariant(), ".shp", TextCompare: false) == 0)
				{
					ShapefileStorage.LoadFeatureTable(SourceFilename, this);
				}
				else
				{
					using H5Reader h5Reader = new H5Reader(SourceFilename);
					if (!h5Reader.ExistsAsGroup(HDFGroupName))
					{
						return;
					}
					HDF5Storage.ReadPolyFeatures(this, h5Reader, HDFGroupName + "/Polygon");
					string hdfGroupNameTerm = HDFGroupName + "/Attributes";
					if (!h5Reader.ExistsAsDataset(hdfGroupNameTerm))
					{
						return;
					}
					ReadManagedColumns(h5Reader, hdfGroupNameTerm);
				}
				int count = FeatureCount() - 1;
				for (int i = 0; i <= count; i++)
				{
					SetAutoUpdate(i, autoUpdate: false);
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

	void IGeometryLayer.HDFLoadFeatureTable(H5Reader hr)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFLoadFeatureTable
		this.HDFLoadFeatureTable(hr);
	}

	public void HDFSaveFeatureTable(H5Writer hw)
	{
		if (!IsFeatureTableLoaded() || string.IsNullOrEmpty(SourceFilename) || (FeatureCount() == 0 && !SourceFileExists))
		{
			return;
		}
		string toLowerInvariant = Path.GetExtension(SourceFilename).ToLowerInvariant();
		using (new FeatureTableReloadSuppressor(this))
		{
			switch (toLowerInvariant)
			{
			case ".shp":
			{
				string sourceFilename = SourceFilename;
				string summary = "";
				ShapefileStorage.WriteShapefile(this, sourceFilename, ref summary);
				break;
			}
			case ".h5":
			case ".hdf":
			{
				using H5Writer h5Writer = new H5Writer(SourceFilename);
				if (FeatureCount() == 0)
				{
					h5Writer.Delete(HDFGroupName);
					break;
				}
				string datasetName = HDFGroupName + "/Attributes";
				WriteManagedColumns(h5Writer, datasetName);
				HDF5Storage.WritePolygonFeatures(this, hw, HDFGroupName + "/Polygon");
				break;
			}
			}
		}
	}

	void IGeometryLayer.HDFSaveFeatureTable(H5Writer hw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFSaveFeatureTable
		this.HDFSaveFeatureTable(hw);
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddManagedAttributeColumn("Name", "Name", typeof(string));
		AddManagedAttributeColumn("Elevation", "Elevation", typeof(float));
		AddManagedAttributeColumn("Auto Update", "AutoUpdate", typeof(bool), null, DataColumnViewState.Invisible);
	}

	public void Stream(BinaryWriter bw)
	{
		bw.Write(FeatureCount());
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Polygon(i).Stream(bw);
			}
		}
	}

	protected bool NeedToRecompute()
	{
		RASGeometry geometry = Geometry;
		if (geometry.IsResultFile)
		{
			return false;
		}
		if (geometry.XS.FeatureCount() == 0)
		{
			return false;
		}
		return FeatureCount() == 0;
	}

	protected override void OnFeatureChanged(FeatureChangedEventArgs args)
	{
		base.OnFeatureChanged(args);
		if (args.ChangeType != FeatureChangedType.FeatureAdded)
		{
			return;
		}
		foreach (int featureIndicesChanged in args.FeatureIndicesChanged)
		{
			SetAutoUpdate(featureIndicesChanged, autoUpdate: true);
		}
	}

	public float GetElevation(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Elevation");
	}

	public void SetElevation(int fid, float elevation)
	{
		FeatureRow(fid)["Elevation"] = elevation;
	}

	public bool GetAutoUpdate(int fid)
	{
		return DataTableExtensions.GetBoolean(FeatureTable(), fid, "Auto Update");
	}

	public void SetAutoUpdate(int fid, bool autoUpdate)
	{
		FeatureRow(fid)["Auto Update"] = autoUpdate;
	}

	public string GetName(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "Name");
	}

	public void SetName(int fid, string assignedName)
	{
		FeatureRow(fid)["Name"] = assignedName;
	}
}
