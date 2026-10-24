using System;
using System.Collections.Generic;

namespace RasMapperLib;

public class TriangleEditable : Triangle, IEditableFeature
{
	public TriangleEditable(Triangle tri)
		: base(tri.Center, tri.PointA, tri.PointB, tri.PointC, tri.AngleRotation)
	{
	}

	public void TransformFeature(Transform transformer)
	{
		PointM PointM = PointA.PointM();
		PointM PointM2 = PointB.PointM();
		PointM PointM3 = PointC.PointM();
		PointM PointM4 = Center;
		transformer.TransformPointM(ref PointM);
		transformer.TransformPointM(ref PointM2);
		transformer.TransformPointM(ref PointM3);
		transformer.TransformPointM(ref PointM4);
		Initialize(PointM4, new Point(PointM), new Point(PointM2), new Point(PointM3), AngleRotation);
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

	public void TransFormPoints(Transform transformer, IList<int> indexes)
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
		//ILSpy generated this explicit interface implementation from .override directive in TransFormPoints
		this.TransFormPoints(transformer, indexes);
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
		return new Triangle(Center, PointA, PointB, PointC, AngleRotation);
	}

	Feature IEditableFeature.Save()
	{
		//ILSpy generated this explicit interface implementation from .override directive in Save
		return this.Save();
	}
}
