using System;
using System.Collections.Generic;

namespace RasMapperLib;

public class EllipseEditable : Ellipse, IEditableFeature
{
	public EllipseEditable(Ellipse ellips)
		: base(ellips.Center, ellips.RadiusX, ellips.RadiusY, ellips.AngleRotation, ellips.Section)
	{
	}

	public EllipseEditable(PointM c, double rad)
		: base(c, rad)
	{
	}

	public void TransformFeature(Transform transformer)
	{
		PointM PointM = Center;
		transformer.TransformPointM(ref PointM);
		Initialize(PointM, RadiusX, RadiusY, AngleRotation, Section);
	}

	void IEditableFeature.TransformFeature(Transform transformer)
	{
		//ILSpy generated this explicit interface implementation from .override directive in TransformFeature
		this.TransformFeature(transformer);
	}

	public void TransformPoint(Transform transformer, int index)
	{
		throw new NotImplementedException();
	}

	void IEditableFeature.TransformPoint(Transform transformer, int index)
	{
		//ILSpy generated this explicit interface implementation from .override directive in TransformPoint
		this.TransformPoint(transformer, index);
	}

	public void TransformPoints(Transform transformer, IList<int> indexes)
	{
		ModificationBoundingBoxIndices modificationBoundingBoxIndices = (ModificationBoundingBoxIndices)indexes[0];
		if (modificationBoundingBoxIndices == ModificationBoundingBoxIndices.Rotation)
		{
			RotateFeature(transformer);
		}
		else
		{
			ScaleFeature(transformer, modificationBoundingBoxIndices);
		}
	}

	void IEditableFeature.TransFormPoints(Transform transformer, IList<int> indexes)
	{
		//ILSpy generated this explicit interface implementation from .override directive in TransformPoints
		this.TransformPoints(transformer, indexes);
	}

	public void DeletePoints(IList<int> indexes)
	{
		throw new NotImplementedException();
	}

	void IEditableFeature.DeletePoints(IList<int> indexes)
	{
		//ILSpy generated this explicit interface implementation from .override directive in DeletePoints
		this.DeletePoints(indexes);
	}

	public void DeletePoints(int start, int count)
	{
		throw new NotImplementedException();
	}

	void IEditableFeature.DeletePoints(int start, int count)
	{
		//ILSpy generated this explicit interface implementation from .override directive in DeletePoints
		this.DeletePoints(start, count);
	}

	public void InsertPoint(PointM point, int index)
	{
		throw new NotImplementedException();
	}

	void IEditableFeature.InsertPoint(PointM point, int index)
	{
		//ILSpy generated this explicit interface implementation from .override directive in InsertPoint
		this.InsertPoint(point, index);
	}

	public void InsertPoints(int startIdx, IList<PointM> points)
	{
		throw new NotImplementedException();
	}

	void IEditableFeature.InsertPoints(int startIdx, IList<PointM> points)
	{
		//ILSpy generated this explicit interface implementation from .override directive in InsertPoints
		this.InsertPoints(startIdx, points);
	}

	public void InsertPoints(IList<int> sortedIndexes, IList<PointM> points)
	{
		throw new NotImplementedException();
	}

	void IEditableFeature.InsertPoints(IList<int> sortedIndexes, IList<PointM> points)
	{
		//ILSpy generated this explicit interface implementation from .override directive in InsertPoints
		this.InsertPoints(sortedIndexes, points);
	}

	public PointM Point(int index)
	{
		throw new NotImplementedException();
	}

	PointM IEditableFeature.Point(int index)
	{
		//ILSpy generated this explicit interface implementation from .override directive in Point
		return this.Point(index);
	}

	public PointM[] GetPoints(int[] indices)
	{
		throw new NotImplementedException();
	}

	PointM[] IEditableFeature.GetPoints(int[] indices)
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetPoints
		return this.GetPoints(indices);
	}

	public Feature Save()
	{
		return new Ellipse(Center, RadiusX, RadiusY, AngleRotation);
	}

	Feature IEditableFeature.Save()
	{
		//ILSpy generated this explicit interface implementation from .override directive in Save
		return this.Save();
	}
}
