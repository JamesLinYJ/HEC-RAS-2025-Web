using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace RasMapperLib;

public class AffineFeatureLayer : FeatureLayer
{
	public Ellipse this[int index] => (Ellipse)AffineFeature(index);

	public AffineFeatureLayer(string name)
		: base(name)
	{
	}

	public AffineFeatureLayer(string name, string filename)
		: base(name, filename)
	{
	}

	public AffineFeatureLayer(string name, DataTable useFeatureTable)
		: base(name, useFeatureTable)
	{
	}

	public virtual int SpatialQueryModificationBoundingBox(int fid, PointM pt, double dist)
	{
		if (!AffineFeature(fid).UseModificationBoundingBox)
		{
			return -1;
		}
		AffineFeature affineFeature = AffineFeature(fid);
		Polyline modificationBoundingBox = affineFeature.ModificationBoundingBox;
		if (modificationBoundingBox == null)
		{
			return -1;
		}
		if (modificationBoundingBox.Count == 0)
		{
			return -1;
		}
		if (affineFeature.MMBBRotationPoint.CloseTo(pt, dist))
		{
			return 9;
		}
		SpatialIndex<int> pointSpatialIndex = modificationBoundingBox.GetPointSpatialIndex();
		if (pointSpatialIndex == null)
		{
			return -1;
		}
		List<int> elements = pointSpatialIndex.GetElements(pt.X, pt.Y, dist);
		foreach (int element in elements)
		{
			if (modificationBoundingBox.PointM(element).CloseTo(pt, dist))
			{
				return element;
			}
		}
		return -1;
	}

	public AffineFeature AffineFeature(int fid)
	{
		return (AffineFeature)Feature(fid);
	}

	protected override void OnFeatureChanged(FeatureChangedEventArgs args)
	{
		base.OnFeatureChanged(args);
		if (args.ChangeType == FeatureChangedType.FeatureDeleted)
		{
			return;
		}
		foreach (int featureIndicesChanged in args.FeatureIndicesChanged)
		{
			AffineFeature(featureIndicesChanged).MakeModificationBoundingBox(AffineFeature(featureIndicesChanged).Center);
		}
	}

	public double GetAngleRotation(int fid)
	{
		return AffineFeature(fid).AngleRotation;
	}

	public override List<Type> AvailableFeatureTypes()
	{
		return new Type[1] { typeof(AffineFeature) }.ToList();
	}

	public DataRow AddFeature(AffineFeature afFeature)
	{
		return AddFeature((Feature)afFeature);
	}
}
