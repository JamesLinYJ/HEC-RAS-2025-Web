using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace RasMapperLib;

public class PointEditable : Point, IEditableFeature
{
	public PointEditable(Point pt)
		: base(pt)
	{
	}

	public PointEditable(PointM pt)
		: base(pt)
	{
	}

	private Feature SaveGeneric()
	{
		return Save();
	}

	Feature IEditableFeature.Save()
	{
		//ILSpy generated this explicit interface implementation from .override directive in SaveGeneric
		return this.SaveGeneric();
	}

	public Point Save()
	{
		return new Point(X, Y, Z, M);
	}

	public void TransformFeature(Transform transformer)
	{
		PointM PointM = base.PointM();
		transformer.TransformPointM(ref PointM);
		_x = PointM.X;
		_y = PointM.Y;
		_z = PointM.Z;
		_m = PointM.M;
	}

	void IEditableFeature.TransformFeature(Transform transformer)
	{
		//ILSpy generated this explicit interface implementation from .override directive in TransformFeature
		this.TransformFeature(transformer);
	}

	public void TransformPoint(Transform transformer, int index)
	{
		PointM PointM = base.PointM();
		transformer.TransformPointM(ref PointM);
		_x = PointM.X;
		_y = PointM.Y;
		_z = PointM.Z;
		_m = PointM.M;
	}

	void IEditableFeature.TransformPoint(Transform transformer, int index)
	{
		//ILSpy generated this explicit interface implementation from .override directive in TransformPoint
		this.TransformPoint(transformer, index);
	}

	public void TransformPoints(Transform transformer, IList<int> indexes)
	{
		throw new Exception("Bad TransformPoints call to Point");
	}

	void IEditableFeature.TransFormPoints(Transform transformer, IList<int> indexes)
	{
		//ILSpy generated this explicit interface implementation from .override directive in TransformPoints
		this.TransformPoints(transformer, indexes);
	}

	[Browsable(false)]
	[EditorBrowsable(EditorBrowsableState.Always)]
	public void Hidden()
	{
	}

	public void DeletePoints(IList<int> indexes)
	{
		throw new Exception("Bad DeletePoints call to Point");
	}

	void IEditableFeature.DeletePoints(IList<int> indexes)
	{
		//ILSpy generated this explicit interface implementation from .override directive in DeletePoints
		this.DeletePoints(indexes);
	}

	public void InsertPoint(PointM point, int index)
	{
		throw new Exception("Bad InsertPoint call to Point");
	}

	void IEditableFeature.InsertPoint(PointM point, int index)
	{
		//ILSpy generated this explicit interface implementation from .override directive in InsertPoint
		this.InsertPoint(point, index);
	}

	public PointM Point(int index)
	{
		return PointM();
	}

	PointM IEditableFeature.Point(int index)
	{
		//ILSpy generated this explicit interface implementation from .override directive in Point
		return this.Point(index);
	}

	public PointM[] GetPoints(int[] indices)
	{
		return new PointM[2]
		{
			PointM(),
			default
		};
	}

	PointM[] IEditableFeature.GetPoints(int[] indices)
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetPoints
		return this.GetPoints(indices);
	}

	public void DeletePoints(int start, int count)
	{
		throw new Exception("Bad DeletePoints call to PointM");
	}

	void IEditableFeature.DeletePoints(int start, int count)
	{
		//ILSpy generated this explicit interface implementation from .override directive in DeletePoints
		this.DeletePoints(start, count);
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
}
