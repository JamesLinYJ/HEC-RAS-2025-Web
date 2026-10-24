using System.Collections.Generic;
using System.Linq;

namespace RasMapperLib;

public class PolygonEditable : Polygon, IEditableFeature
{
	private int _editCount;

	public int EditCount => _editCount;

	public PolygonEditable(Polygon polygon)
	{
		_editCount = 0;
		_pointM = polygon.CopyPointList();
		_partStartIndex = polygon.PartStartArrayToList();
	}

	public PolygonEditable(PointMs pms)
	{
		_editCount = 0;
		_pointM = new PointMs();
		pms.CopyTo(_pointM);
		if (Count == 1)
		{
			_pointM.Add(_pointM[0]);
		}
		else if (!_pointM.First().Equal(_pointM.Last()))
		{
			_pointM.Add(_pointM.First());
		}
		PointDataChanged();
	}

	public override string ToString()
	{
		return "PolygonEditable" + PointLabel(HasZ, HasM);
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

	public Polygon Save()
	{
		return new Polygon(CopyPoints(), _partStartIndex);
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
		checked
		{
			if (index == _pointM.Count - 1)
			{
				index = 0;
			}
			IList<PointM> pointM;
			int index2;
			PointM PointM = (pointM = _pointM)[index2 = index];
			transformer.TransformPointM(ref PointM);
			pointM[index2] = PointM;
			if (index == 0)
			{
				PointM = (pointM = _pointM)[index2 = _pointM.Count - 1];
				transformer.TransformPointM(ref PointM);
				pointM[index2] = PointM;
			}
			PointDataChanged();
		}
	}

	void IEditableFeature.TransformPoint(Transform transformer, int index)
	{
		//ILSpy generated this explicit interface implementation from .override directive in TransformPoint
		this.TransformPoint(transformer, index);
	}

	public void TransformPoints(Transform transformer, IList<int> indexes)
	{
		checked
		{
			foreach (int index2 in indexes)
			{
				int index22 = index2;
				if (index22 == _pointM.Count - 1)
				{
					index22 = 0;
				}
				IList<PointM> pointM;
				int index;
				PointM PointM = (pointM = _pointM)[index = index22];
				transformer.TransformPointM(ref PointM);
				pointM[index] = PointM;
				if (index22 == 0)
				{
					PointM = (pointM = _pointM)[index = _pointM.Count - 1];
					transformer.TransformPointM(ref PointM);
					pointM[index] = PointM;
				}
			}
			PointDataChanged();
		}
	}

	void IEditableFeature.TransFormPoints(Transform transformer, IList<int> indexes)
	{
		//ILSpy generated this explicit interface implementation from .override directive in TransformPoints
		this.TransformPoints(transformer, indexes);
	}

	public void InsertPoint(PointM point, int index)
	{
		if (index == _pointM.Count)
		{
			index = 0;
		}
		_pointM.Insert(index, point);
		checked
		{
			if (index == 0)
			{
				_pointM[_pointM.Count - 1] = _pointM[0];
			}
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

	public void InsertPoints(int startIdx, IList<PointM> points)
	{
		InsertPoints_InternalMutable(startIdx, points);
		checked
		{
			if (_partStartIndex != null)
			{
				int count = _partStartIndex.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					if (_partStartIndex[i] > startIdx)
					{
						_partStartIndex[i] += points.Count;
					}
				}
			}
			PointDataChanged();
		}
	}

	void IEditableFeature.InsertPoints(int startIdx, IList<PointM> points)
	{
		//ILSpy generated this explicit interface implementation from .override directive in InsertPoints
		this.InsertPoints(startIdx, points);
	}

	public void InsertPoints(IList<int> sortedIndexes, IList<PointM> points)
	{
		InsertPoints_InternalMutable(sortedIndexes, points);
		checked
		{
			if (_partStartIndex != null)
			{
				int count = _partStartIndex.Count - 1;
				for (int i = 0; i <= count; i++)
				{
				}
			}
		}
	}

	void IEditableFeature.InsertPoints(IList<int> sortedIndexes, IList<PointM> points)
	{
		//ILSpy generated this explicit interface implementation from .override directive in InsertPoints
		this.InsertPoints(sortedIndexes, points);
	}

	public void DeletePoints(IList<int> indexes)
	{
		ByteBitArray byteBitArray = new ByteBitArray(Count);
		foreach (int index in indexes)
		{
			byteBitArray[index] = true;
		}
		bool condition = false;
		checked
		{
			if (byteBitArray[0] || byteBitArray[_pointM.Count - 1])
			{
				byteBitArray[0] = true;
				byteBitArray[_pointM.Count - 1] = true;
				condition = true;
			}
			for (int i = _pointM.Count - 1; i >= 0; i += -1)
			{
				if (byteBitArray[i])
				{
					_pointM.RemoveAt(i);
				}
			}
			if (condition && _pointM.Count > 1)
			{
				PointM pointM = _pointM[0];
				_pointM.Add(pointM);
			}
			PointDataChanged();
		}
	}

	void IEditableFeature.DeletePoints(IList<int> indexes)
	{
		//ILSpy generated this explicit interface implementation from .override directive in DeletePoints
		this.DeletePoints(indexes);
	}

	public void DeletePoints(int start, int count)
	{
		DeletePoints(Enumerable.Range(start, count).ToArray());
	}

	void IEditableFeature.DeletePoints(int start, int count)
	{
		//ILSpy generated this explicit interface implementation from .override directive in DeletePoints
		this.DeletePoints(start, count);
	}
}
