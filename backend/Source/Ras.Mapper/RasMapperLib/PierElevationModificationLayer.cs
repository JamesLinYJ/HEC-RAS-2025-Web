using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using Utility.Extensions;

namespace RasMapperLib;

public class PierElevationModificationLayer : PierFeatureLayer, IElevationModificationLayer
{
	public override bool CanMoveToAnotherLevel { get; }

	public override bool CanMoveWithinLevel { get; }

	public override bool CanRename
	{
		get
		{
			if (!IsEditing)
			{
				return false;
			}
			return true;
		}
	}

	public override bool CanRemove
	{
		get
		{
			if (IsEditing)
			{
				return true;
			}
			return false;
		}
	}

	public override bool UsesParentSourceFilename { get; }

	public override bool ForwardEditRequestsToParent { get; }

	public override string HDFGroup => "/Modifications/" + LayerName;

	public PierElevationModificationLayer(string lyrName)
		: base(lyrName)
	{
		CanMoveToAnotherLevel = false;
		CanMoveWithinLevel = false;
		UsesParentSourceFilename = true;
		ForwardEditRequestsToParent = true;
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		ElevationModificationGroup.InitializeElevationModTypeSerializer();
		AddManagedAttributeColumn("Name", "Name", typeof(string));
		AddManagedAttributeColumn("Elevation Value", "Elevation Value", typeof(float));
		AddManagedAttributeColumn("Elevation Type", "Elevation Type", typeof(ElevationModificationType), null, DataColumnViewState.GrayedOut);
	}

	protected override TerrainLayer GetAssociatedTerrainLayer()
	{
		TreeNode treeNode = this;
		while (treeNode != null)
		{
			treeNode = treeNode.Parent;
			if (treeNode is TerrainLayer)
			{
				return (TerrainLayer)treeNode;
			}
		}
		return base.GetAssociatedTerrainLayer();
	}

	public void ObstructRaster(RasterM raster, float[] values)
	{
		if (FeatureCount() != 0)
		{
			throw new NotImplementedException("System.Drawing.Matrix removed.");
		}
	}

	void IElevationModificationLayer.ObstructRaster(RasterM raster, float[] values)
	{
		//ILSpy generated this explicit interface implementation from .override directive in ObstructRaster
		this.ObstructRaster(raster, values);
	}

	public void ObstructProfiles(IList<Polyline> polylines, IList<Profile> profiles)
	{
		if (FeatureCount() == 0)
		{
			return;
		}
		BitArray bitArray = new BitArray(profiles.Count);
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Pier pier = Pier(i);
				if (!RasMapperLib.Pier.IsValid(pier))
				{
					continue;
				}
				ElevationModificationType elevationModificationType = GetElevationModificationType(i);
				float elevationModificationValue = GetElevationModificationValue(i);
				if (float.IsNaN(elevationModificationValue))
				{
					continue;
				}
				Extent extent = pier.ModificationBoundingBox.GetExtent();
				int count2 = polylines.Count - 1;
				for (int j = 0; j <= count2; j++)
				{
					Polyline polyline = polylines[j];
					if (!extent.Overlaps(polyline))
					{
						continue;
					}
					Profile pf = profiles[j];
					float polylineLength = (float)polyline.Length;
					if (!bitArray[j])
					{
						pf.SortByStation();
						bitArray[j] = true;
					}
					if (pier.Contains(polyline))
					{
						ElevationModificationGroup.ReplaceProfileSubsection(ref pf, polylineLength, 0f, polylineLength, elevationModificationType, elevationModificationValue);
						profiles[j] = pf;
						continue;
					}
					List<Tuple<double, double>> containedSubsegments = pier.GetContainedSubsegments(polyline);
					if (!containedSubsegments.IsNullOrEmpty())
					{
						int count3 = containedSubsegments.Count - 1;
						for (int k = 0; k <= count3; k++)
						{
							Tuple<double, double> tuple = containedSubsegments[k];
							ElevationModificationGroup.ReplaceProfileSubsection(ref pf, polylineLength, (float)tuple.Item1, (float)tuple.Item2, elevationModificationType, elevationModificationValue);
							profiles[j] = pf;
						}
					}
				}
			}
		}
	}

	void IElevationModificationLayer.ObstructProfiles(IList<Polyline> polylines, IList<Profile> profiles)
	{
		//ILSpy generated this explicit interface implementation from .override directive in ObstructProfiles
		this.ObstructProfiles(polylines, profiles);
	}

	public void ObstructPoints(IPoints points, float[] valuesPerPoint, SpatialIndex<int> pointSpIdx)
	{
		FeatureCount();
	}

	void IElevationModificationLayer.ObstructPoints(IPoints points, float[] valuesPerPoint, SpatialIndex<int> pointSpIdx)
	{
		//ILSpy generated this explicit interface implementation from .override directive in ObstructPoints
		this.ObstructPoints(points, valuesPerPoint, pointSpIdx);
	}

	public float GetElevationModificationValue(int fid)
	{
		return DataTableExtensions.GetSingle(FeatureTable(), fid, "Elevation Value");
	}

	public void SetElevationModificationValue(int fid, float elevationModificationValue)
	{
		FeatureRow(fid)["Elevation Value"] = elevationModificationValue;
	}

	public ElevationModificationType GetElevationModificationType(int fid)
	{
		object objectValue = RuntimeHelpers.GetObjectValue(FeatureRow(fid)["Elevation Type"]);
		if (objectValue == null || Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)))
		{
			return ElevationModificationType.SetValue;
		}
		return (ElevationModificationType)Conversions.ToInteger(objectValue);
	}

	public void SetElevationModificationType(int fid, ElevationModificationType modType)
	{
		FeatureRow(fid)["Elevation Type"] = modType;
	}
}
