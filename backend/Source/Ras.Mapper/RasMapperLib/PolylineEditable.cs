using System.Collections.Generic;
using System.Linq;

namespace RasMapperLib;

public class PolylineEditable : Polyline, IEditableFeature
{
	private int _editCount;

	public int EditCount => _editCount;

	public override double Length
	{
		get
		{
			double segmentLengthSum = 0.0;
			checked
			{
				int count = SegmentCount() - 1;
				for (int i = 0; i <= count; i++)
				{
					segmentLengthSum += Segment(i).Length;
				}
				return segmentLengthSum;
			}
		}
	}

	public PolylineEditable(Polyline polyline)
	{
		_editCount = 0;
		_pointM = polyline.CopyPointList();
		_partStartIndex = polyline.PartStartArrayToList();
		PointDataChanged();
	}

	public PolylineEditable()
		: this(new PointMs())
	{
	}

	public PolylineEditable(PointMs pms)
	{
		_editCount = 0;
		_pointM = new PointMs();
		pms.CopyTo(_pointM);
		PointDataChanged();
	}

	private PolylineEditable(PointMs pms, bool yesIWantToUseThisBufferPlaceholderArgument)
	{
		_editCount = 0;
		_pointM = pms;
		PointDataChanged();
	}

	public static PolylineEditable CreateFromBuffer(PointMs pms)
	{
		return new PolylineEditable(pms, yesIWantToUseThisBufferPlaceholderArgument: true);
	}

	public override string ToString()
	{
		return "PolylineEditable" + PointLabel(HasZ, HasM);
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

	public Polyline Save()
	{
		return new Polyline(CopyPoints(), _partStartIndex);
	}

	public PointM Point(int index)
	{
		return _pointM[index];
	}

	PointM IEditableFeature.Point(int index)
	{
		//ILSpy generated this explicit interface implementation from .override directive in Point
		return this.Point(index);
	}

	public PointM[] GetPoints(int[] indices)
	{
		checked
		{
			PointM[] pointMs = new PointM[indices.Count() - 1 + 1];
			int count = indices.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				pointMs[i] = _pointM[indices[i]];
			}
			return pointMs;
		}
	}

	PointM[] IEditableFeature.GetPoints(int[] indices)
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetPoints
		return this.GetPoints(indices);
	}

	public void TransformFeature(Transform transformer)
	{
		checked
		{
			int count = _pointM.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				IList<PointM> pointM;
				int index;
				PointM PointM = (pointM = _pointM)[index = i];
				transformer.TransformPointM(ref PointM);
				pointM[index] = PointM;
			}
			PointDataChanged();
		}
	}

	void IEditableFeature.TransformFeature(Transform transformer)
	{
		//ILSpy generated this explicit interface implementation from .override directive in TransformFeature
		this.TransformFeature(transformer);
	}

	public void TransformPoint(Transform transformer, int index)
	{
		IList<PointM> pointM;
		int index2;
		PointM PointM = (pointM = _pointM)[index2 = index];
		transformer.TransformPointM(ref PointM);
		pointM[index2] = PointM;
		PointDataChanged();
	}

	void IEditableFeature.TransformPoint(Transform transformer, int index)
	{
		//ILSpy generated this explicit interface implementation from .override directive in TransformPoint
		this.TransformPoint(transformer, index);
	}

	public void TransformPoints(Transform transformer, IList<int> indexes)
	{
		foreach (int index2 in indexes)
		{
			IList<PointM> pointM;
			int index;
			PointM PointM = (pointM = _pointM)[index = index2];
			transformer.TransformPointM(ref PointM);
			pointM[index] = PointM;
		}
		PointDataChanged();
	}

	void IEditableFeature.TransFormPoints(Transform transformer, IList<int> indexes)
	{
		//ILSpy generated this explicit interface implementation from .override directive in TransformPoints
		this.TransformPoints(transformer, indexes);
	}

	public void AddPoint(PointM point)
	{
		InsertPoint(point, Count);
	}

	public void InsertPoint(PointM point, int index)
	{
		_pointM.Insert(index, point);
		checked
		{
			if (_partStartIndex != null)
			{
				int count = _partStartIndex.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					if (_partStartIndex[i] > index)
					{
						_partStartIndex[i]++;
					}
				}
			}
			PointDataChanged();
		}
	}

	void IEditableFeature.InsertPoint(PointM point, int index)
	{
		//ILSpy generated this explicit interface implementation from .override directive in InsertPoint
		this.InsertPoint(point, index);
	}

	public new void ResetLength()
	{
		base.ResetLength();
	}

	public void DeletePoints(IList<int> indexes)
	{
		DeletePoints_InternalMutable(indexes);
	}

	void IEditableFeature.DeletePoints(IList<int> indexes)
	{
		//ILSpy generated this explicit interface implementation from .override directive in DeletePoints
		this.DeletePoints(indexes);
	}

	public void DeletePoints(int start, int count)
	{
		DeletePoints_InternalMutable(start, count);
	}

	void IEditableFeature.DeletePoints(int start, int count)
	{
		//ILSpy generated this explicit interface implementation from .override directive in DeletePoints
		this.DeletePoints(start, count);
	}

	public void InsertPoints(int startIdx, IList<PointM> points)
	{
		InsertPoints_InternalMutable(startIdx, points);
	}

	void IEditableFeature.InsertPoints(int startIdx, IList<PointM> points)
	{
		//ILSpy generated this explicit interface implementation from .override directive in InsertPoints
		this.InsertPoints(startIdx, points);
	}

	public void InsertPoints(IList<int> sortedIndexes, IList<PointM> points)
	{
		InsertPoints_InternalMutable(sortedIndexes, points);
	}

	void IEditableFeature.InsertPoints(IList<int> sortedIndexes, IList<PointM> points)
	{
		//ILSpy generated this explicit interface implementation from .override directive in InsertPoints
		this.InsertPoints(sortedIndexes, points);
	}
}
