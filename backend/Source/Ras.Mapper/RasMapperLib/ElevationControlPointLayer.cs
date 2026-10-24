using System;
using System.Collections.Generic;
using System.IO;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Utilities;

namespace RasMapperLib;

public class ElevationControlPointLayer : PointFeatureLayer
{
	public const string sElevation = "Elevation";

	public Func<PointM, bool, float> GetValueAtPoint;

	public Func<int, Dictionary<int, PointM>> GetElevationChangingPointOnAllPerimeters;

	public override bool CanMoveToAnotherLevel { get; }

	public override bool CanMoveWithinLevel { get; }

	public override bool CanRename { get; }

	public override bool UsesParentSourceFilename { get; }

	public override bool ForwardEditRequestsToParent { get; }

	internal override bool AllowZAndM { get; set; }

	public virtual string HDFGroup { get; }

	public ElevationControlPointLayer()
		: base("Elevation Point Layer")
	{
		CanMoveToAnotherLevel = false;
		CanMoveWithinLevel = false;
		CanRename = false;
		UsesParentSourceFilename = true;
		ForwardEditRequestsToParent = true;
		AllowZAndM = false;
		UsesParentSourceFilename = false;
		ForwardEditRequestsToParent = false;
		CanRename = true;
	}

	public ElevationControlPointLayer(string layerName, string fn)
		: base(layerName, fn)
	{
		CanMoveToAnotherLevel = false;
		CanMoveWithinLevel = false;
		CanRename = false;
		UsesParentSourceFilename = true;
		ForwardEditRequestsToParent = true;
		AllowZAndM = false;
		UsesParentSourceFilename = false;
		ForwardEditRequestsToParent = false;
		CanRename = true;
	}

	public ElevationControlPointLayer(string layerName, string hdfPath, bool useParentSourceFileName, Func<PointM, bool, float> getValueAtPoint, Func<int, Dictionary<int, PointM>> getElevationChangingPointOnAllPerimeters)
		: base(layerName)
	{
		CanMoveToAnotherLevel = false;
		CanMoveWithinLevel = false;
		CanRename = false;
		UsesParentSourceFilename = true;
		ForwardEditRequestsToParent = true;
		AllowZAndM = false;
		HDFGroup = hdfPath;
		UsesParentSourceFilename = useParentSourceFileName;
		ForwardEditRequestsToParent = useParentSourceFileName;
		CanRename = !useParentSourceFileName;
		GetValueAtPoint = getValueAtPoint;
		GetElevationChangingPointOnAllPerimeters = getElevationChangingPointOnAllPerimeters;
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddManagedAttributeColumn("Name", "Name", typeof(string));
		AddManagedAttributeColumn("Elevation", "Elevation", typeof(float));
	}

	protected override void LoadFeatureTable()
	{
		if (string.IsNullOrEmpty(SourceFilename) || !SourceFileExists)
		{
			return;
		}
		try
		{
			if (Operators.CompareString(System.IO.Path.GetExtension(SourceFilename).ToLowerInvariant(), ".shp", TextCompare: false) == 0)
			{
				ShapefileStorage.LoadFeatureTable(SourceFilename, this);
				return;
			}
			using H5Reader h5Reader = new H5Reader(SourceFilename);
			if (h5Reader.ExistsAsGroup(HDFGroup))
			{
				HDF5Storage.ReadPointFeatures(this, h5Reader, HDFGroup + "/Points");
				string hdfGroupTerm = HDFGroup + "/Attributes";
				if (h5Reader.ExistsAsDataset(hdfGroupTerm))
				{
					ReadManagedColumns(h5Reader, hdfGroupTerm);
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

	protected override bool SaveFeatureTableInternal()
	{
		if (!IsFeatureTableLoaded())
		{
			return false;
		}
		if (string.IsNullOrEmpty(SourceFilename))
		{
			return false;
		}
		if (FeatureCount() == 0 && !SourceFileExists)
		{
			return true;
		}
		string toLowerInvariant = System.IO.Path.GetExtension(SourceFilename).ToLowerInvariant();
		using (new FeatureTableReloadSuppressor(this))
		{
			switch (toLowerInvariant)
			{
			case ".shp":
			{
				string sourceFilename = SourceFilename;
				string summary = "";
				return ShapefileStorage.WriteShapefile(this, sourceFilename, ref summary);
			}
			case ".h5":
			case ".hdf":
			{
				using (H5Writer h5Writer = new H5Writer(SourceFilename))
				{
					if (FeatureCount() == 0)
					{
						h5Writer.Delete(HDFGroup);
						return true;
					}
					string datasetName = HDFGroup + "/Attributes";
					WriteManagedColumns(h5Writer, datasetName);
					HDF5Storage.WritePointFeatures(this, h5Writer, HDFGroup + "/Points");
				}
				break;
			}
			}
		}
		return true;
	}

	public byte[] GetDataHash()
	{
		int featureCount = FeatureCount();
		if (featureCount == 0)
		{
			return new byte[1];
		}
		Hash hash = new Hash();
		checked
		{
			double[] doubleValues = new double[featureCount * 2 - 1 + 1];
			float[] floatValues = new float[featureCount - 1 + 1];
			int count = featureCount - 1;
			for (int i = 0; i <= count; i++)
			{
				Point point = Point(i);
				doubleValues[i * 2] = point.X;
				doubleValues[i * 2 + 1] = point.Y;
				floatValues[i] = GetElevation(i);
			}
			hash.Add(doubleValues);
			hash.Add(floatValues);
			return hash.HashBytes();
		}
	}

	public float GetElevation(int fid)
	{
		float single = DataTableExtensions.GetSingle(FeatureTable(), fid, "Elevation");
		if (float.IsNaN(single) && !float.IsNaN((float)Point(fid).Z))
		{
			SetElevation(fid, (float)Point(fid).Z);
			return DataTableExtensions.GetSingle(FeatureTable(), fid, "Elevation");
		}
		return single;
	}

	public void SetElevation(int fid, float elevation)
	{
		FeatureRow(fid)["Elevation"] = elevation;
	}
}
