using System;
using System.Collections.Generic;

namespace RasMapperLib;

public abstract class AffineFeature : Feature, IAreaGeometry
{
	protected double _angleRotation;

	protected Polygon _modificationBoundingBox;

	protected bool _useModificationBoundingBox;

	protected bool _canDrawModificationBoundingBox;

	protected PointM _mmbbRotationPoint;

	protected PointM _center;

	public Polygon ModificationBoundingBox
	{
		get
		{
			return _modificationBoundingBox;
		}
		private set
		{
			_modificationBoundingBox = value;
		}
	}

	public PointM MMBBRotationPoint
	{
		get
		{
			return _mmbbRotationPoint;
		}
		private set
		{
			_mmbbRotationPoint = value;
		}
	}

	public double AngleRotation => _angleRotation;

	public PointM Center => _center;

	public bool CanDrawModificationBoundingBox
	{
		get
		{
			if (UseModificationBoundingBox)
			{
				return _canDrawModificationBoundingBox;
			}
			return false;
		}
		set
		{
			_canDrawModificationBoundingBox = value;
		}
	}

	public bool UseModificationBoundingBox
	{
		get
		{
			return _useModificationBoundingBox;
		}
		set
		{
			_useModificationBoundingBox = value;
			CanDrawModificationBoundingBox = value;
		}
	}

	protected AffineFeature()
	{
		_useModificationBoundingBox = true;
		_canDrawModificationBoundingBox = true;
	}

	public void MakeModificationBoundingBox(PointM center)
	{
		Extent unrotatedBoundingBox = GetUnrotatedBoundingBox();
		PointMs pointMs = new PointMs();
		pointMs.Add(new PointM(unrotatedBoundingBox.UpperLeft.X, unrotatedBoundingBox.UpperLeft.Y));
		pointMs.Add(new PointM(unrotatedBoundingBox.UpperMidside.X, unrotatedBoundingBox.UpperMidside.Y));
		pointMs.Add(new PointM(unrotatedBoundingBox.UpperRight.X, unrotatedBoundingBox.UpperRight.Y));
		pointMs.Add(new PointM(unrotatedBoundingBox.RightMidside.X, unrotatedBoundingBox.RightMidside.Y));
		pointMs.Add(new PointM(unrotatedBoundingBox.LowerRight.X, unrotatedBoundingBox.LowerRight.Y));
		pointMs.Add(new PointM(unrotatedBoundingBox.LowerMidside.X, unrotatedBoundingBox.LowerMidside.Y));
		pointMs.Add(new PointM(unrotatedBoundingBox.LowerLeft.X, unrotatedBoundingBox.LowerLeft.Y));
		pointMs.Add(new PointM(unrotatedBoundingBox.LeftMidside.X, unrotatedBoundingBox.LeftMidside.Y));
		pointMs.Add(new PointM(unrotatedBoundingBox.UpperLeft.X, unrotatedBoundingBox.UpperLeft.Y));
		double distanceToUpperMidsideTerm = unrotatedBoundingBox.UpperMidside.DistanceTo(unrotatedBoundingBox.UpperRight) / 4.0;
		pointMs.Add(new PointM(unrotatedBoundingBox.UpperRight.X + distanceToUpperMidsideTerm, unrotatedBoundingBox.UpperRight.Y + distanceToUpperMidsideTerm));
		pointMs = RotatePointsAroundCenterPoint(pointMs, center, AngleRotation);
		checked
		{
			_mmbbRotationPoint = pointMs[pointMs.Count - 1];
			pointMs.RemoveAt(pointMs.Count - 1);
			_modificationBoundingBox = new Polygon(pointMs);
		}
	}

	internal static PointMs RotatePointsAroundCenterPoint(PointMs pointList, PointM center, double angleRotationDegrees)
	{
		throw new NotImplementedException();
	}

	public abstract Extent GetUnrotatedBoundingBox();

	public abstract Polygon DiscretizeToPolygon();

	public abstract void ScaleFeature(Transform transformer, ModificationBoundingBoxIndices index);

	public abstract void RotateFeature(Transform transformer);

	public override bool MBROverlaps(Feature Feature)
	{
		if (Feature == null)
		{
			return false;
		}
		if (double.IsNaN(Feature.MinX) || double.IsNaN(Feature.MinY))
		{
			return false;
		}
		if (ModificationBoundingBox.MaxX < Feature.MinX)
		{
			return false;
		}
		if (ModificationBoundingBox.MinX > Feature.MaxX)
		{
			return false;
		}
		if (ModificationBoundingBox.MinY > Feature.MaxY)
		{
			return false;
		}
		if (ModificationBoundingBox.MaxY < Feature.MinY)
		{
			return false;
		}
		return true;
	}

	public override bool MBROverlaps(PointM PointM)
	{
		if (ModificationBoundingBox.MaxX < PointM.X)
		{
			return false;
		}
		if (ModificationBoundingBox.MinX > PointM.X)
		{
			return false;
		}
		if (ModificationBoundingBox.MinY > PointM.Y)
		{
			return false;
		}
		if (ModificationBoundingBox.MaxY < PointM.Y)
		{
			return false;
		}
		return true;
	}

	protected static bool IsCornerIndex(ModificationBoundingBoxIndices index)
	{
		if (index != ModificationBoundingBoxIndices.UpperLeftOne && index != ModificationBoundingBoxIndices.UpperLeftTwo && index != ModificationBoundingBoxIndices.UpperRight && index != ModificationBoundingBoxIndices.LowerRight)
		{
			return index == ModificationBoundingBoxIndices.LowerLeft;
		}
		return true;
	}

	protected static bool IsTopIndex(ModificationBoundingBoxIndices index)
	{
		if (index != ModificationBoundingBoxIndices.UpperLeftOne && index != ModificationBoundingBoxIndices.UpperLeftTwo && index != ModificationBoundingBoxIndices.UpperMidside)
		{
			return index == ModificationBoundingBoxIndices.UpperRight;
		}
		return true;
	}

	protected static bool IsBottomIndex(ModificationBoundingBoxIndices index)
	{
		if (index != ModificationBoundingBoxIndices.LowerLeft && index != ModificationBoundingBoxIndices.LowerMidside)
		{
			return index == ModificationBoundingBoxIndices.LowerRight;
		}
		return true;
	}

	protected static bool IsSideIndex(ModificationBoundingBoxIndices index)
	{
		if (index != ModificationBoundingBoxIndices.LeftMidside && index != ModificationBoundingBoxIndices.UpperMidside && index != ModificationBoundingBoxIndices.RightMidside)
		{
			return index == ModificationBoundingBoxIndices.LowerMidside;
		}
		return true;
	}

	public abstract bool Contains(Polyline pline);

	bool IAreaGeometry.Contains(Polyline pline)
	{
		//ILSpy generated this explicit interface implementation from .override directive in Contains
		return this.Contains(pline);
	}

	public abstract List<Tuple<double, double>> GetContainedSubsegments(Polyline polyline);

	List<Tuple<double, double>> IAreaGeometry.GetContainedSubsegments(Polyline polyline)
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetContainedSubsegments
		return this.GetContainedSubsegments(polyline);
	}

	public abstract bool Contains(PointM ptm);

	bool IAreaGeometry.Contains(PointM ptm)
	{
		//ILSpy generated this explicit interface implementation from .override directive in Contains
		return this.Contains(ptm);
	}

	public virtual SegmentM GetSegmentCenterRotation()
	{
		return new SegmentM(Center, MMBBRotationPoint);
	}

	public virtual SegmentM GetSegmentCenterRotationTranslated(PointM delta)
	{
		return new SegmentM(Center, new PointM(MMBBRotationPoint.X + delta.X, MMBBRotationPoint.Y + delta.Y));
	}
}
