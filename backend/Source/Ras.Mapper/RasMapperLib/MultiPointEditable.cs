using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Geospatial.DataStructures;

namespace RasMapperLib;

public class MultiPointEditable : MultiPoint, IEditableFeature
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__14_002D0
	{
		public int startIdx;

		public _Closure_0024__14_002D0(_Closure_0024__14_002D0 other)
		{
			if (other != null)
			{
				startIdx = other.startIdx;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__14_002D1
	{
		public int idx;

		public _Closure_0024__14_002D0 outerClosure;

		public _Closure_0024__14_002D1(_Closure_0024__14_002D1 other)
		{
			if (other != null)
			{
				idx = other.idx;
			}
		}

		[SpecialName]
		internal int _Lambda_0024__0(int oldidx)
		{
			checked
			{
				if (oldidx >= outerClosure.startIdx + idx)
				{
					oldidx++;
				}
				return oldidx;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__18_002D0
	{
		public List<int> mutableIndexes;

		public _Closure_0024__18_002D0(_Closure_0024__18_002D0 other)
		{
			if (other != null)
			{
				mutableIndexes = other.mutableIndexes;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__18_002D1
	{
		public int idx;

		public _Closure_0024__18_002D0 outerClosure;

		public _Closure_0024__18_002D1(_Closure_0024__18_002D1 other)
		{
			if (other != null)
			{
				idx = other.idx;
			}
		}

		[SpecialName]
		internal int _Lambda_0024__0(int oldidx)
		{
			if (oldidx >= outerClosure.mutableIndexes[idx])
			{
				oldidx = checked(oldidx - 1);
			}
			return oldidx;
		}
	}

	private Extent _extent;

	public MultiPointEditable(MultiPoint points)
	{
		_pointM = points.CopyPointList();
		EnsureSpatialIndex();
	}

	public MultiPointEditable(PointMs pms)
	{
		_pointM = new PointMs();
		pms.CopyTo(_pointM);
		EnsureSpatialIndex();
		PointDataChanged();
	}

	protected override void PointDataChanged()
	{
		base.PointDataChanged();
		_extent = null;
	}

	private void RefreshSpatialIndex()
	{
		if (UsingQuadTree)
		{
			_pointQuadTree = null;
		}
		else
		{
			_pointSpatialIndex = null;
		}
		EnsureSpatialIndex();
	}

	public override Extent ComputeExtent()
	{
		if (_extent == null)
		{
			_extent = new Extent(_pointM);
		}
		return _extent;
	}

	public override string ToString()
	{
		return "PointsEditable" + PointLabel(HasZ, HasM);
	}

	public void Add(PointM ptm)
	{
		InsertPoint(ptm, Count);
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

	public MultiPoint Save()
	{
		return new MultiPoint(_pointM);
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
			RefreshSpatialIndex();
		}
	}

	void IEditableFeature.TransformFeature(Transform transformer)
	{
		//ILSpy generated this explicit interface implementation from .override directive in TransformFeature
		this.TransformFeature(transformer);
	}

	public void TransformPoint(Transform transformer, int index)
	{
		PointM pointM = _pointM[index];
		IList<PointM> pointM2;
		int index2;
		PointM PointM = (pointM2 = _pointM)[index2 = index];
		transformer.TransformPointM(ref PointM);
		pointM2[index2] = PointM;
		PointM pointM3 = _pointM[index];
		PointDataChanged();
		if (!EnsureSpatialIndex())
		{
			_pointQuadTree.RemoveCoordinate(pointM.X, pointM.Y, 1E-05);
			_pointQuadTree.Insert(pointM3.X, pointM3.Y, index);
		}
	}

	void IEditableFeature.TransformPoint(Transform transformer, int index)
	{
		//ILSpy generated this explicit interface implementation from .override directive in TransformPoint
		this.TransformPoint(transformer, index);
	}

	public void TransformPoints(Transform transformer, IList<int> indexes)
	{
		bool ensureSpatialIndex = EnsureSpatialIndex();
		checked
		{
			int count = indexes.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				int index2 = indexes[i];
				PointM pointM = _pointM[index2];
				IList<PointM> pointM2;
				int index;
				PointM PointM = (pointM2 = _pointM)[index = index2];
				transformer.TransformPointM(ref PointM);
				pointM2[index] = PointM;
				PointM pointM3 = _pointM[index2];
				if (!ensureSpatialIndex)
				{
					_pointQuadTree.RemoveCoordinate(pointM.X, pointM.Y, 1E-05);
					_pointQuadTree.Insert(pointM3.X, pointM3.Y, index2);
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
		_pointM.Insert(index, point);
		PointDataChanged();
		if (EnsureSpatialIndex())
		{
			return;
		}
		checked
		{
			if (index < Count - 1)
			{
				if (1.0 / ((double)_pointM.Count + 1.0) < 0.000123)
				{
					VB_0024AnonymousDelegate_0<int, int> specialNameOldidxTerm = [SpecialName] (int oldidx) =>
					{
						if (oldidx >= index)
						{
							oldidx++;
						}
						return oldidx;
					};
					QuadTree<int> pointQuadTree = _pointQuadTree;
					VB_0024AnonymousDelegate_0<int, int> vB_0024AnonymousDelegate_0 = specialNameOldidxTerm;
					pointQuadTree.ModifyKeysParallel((vB_0024AnonymousDelegate_0 == null) ? null : new Func<int, int>(vB_0024AnonymousDelegate_0.Invoke));
					_pointQuadTree.Insert(point.X, point.Y, index);
				}
				else
				{
					RefreshSpatialIndex();
				}
			}
			else if (index == Count - 1)
			{
				_pointQuadTree.Insert(point.X, point.Y, index);
			}
		}
	}

	void IEditableFeature.InsertPoint(PointM point, int index)
	{
		//ILSpy generated this explicit interface implementation from .override directive in InsertPoint
		this.InsertPoint(point, index);
	}

	public void InsertPoints(int startIdx, IList<PointM> points)
	{
		_Closure_0024__14_002D0 capturedLocals = new _Closure_0024__14_002D0(null);
		capturedLocals.startIdx = startIdx;
		InsertPoints_InternalMutable(capturedLocals.startIdx, points);
		if (EnsureSpatialIndex())
		{
			return;
		}
		checked
		{
			if (capturedLocals.startIdx < Count - points.Count - 1)
			{
				if ((double)points.Count / ((double)_pointM.Count - (double)points.Count) < 0.000123)
				{
					int count = points.Count - 1;
					_Closure_0024__14_002D1 capturedLocals2 = default;
					for (int i = 0; i <= count; i++)
					{
						capturedLocals2 = new _Closure_0024__14_002D1(capturedLocals2);
						capturedLocals2.outerClosure = capturedLocals;
						capturedLocals2.idx = i;
						VB_0024AnonymousDelegate_0<int, int> lambda_0024__0 = capturedLocals2._Lambda_0024__0;
						QuadTree<int> pointQuadTree = _pointQuadTree;
						VB_0024AnonymousDelegate_0<int, int> lambda_0024__02 = lambda_0024__0;
						pointQuadTree.ModifyKeysParallel((lambda_0024__02 == null) ? null : new Func<int, int>(lambda_0024__02.Invoke));
						_pointQuadTree.Insert(points[i].X, points[i].Y, capturedLocals2.outerClosure.startIdx + i);
					}
				}
				else
				{
					RefreshSpatialIndex();
				}
			}
			else if (capturedLocals.startIdx == Count - points.Count)
			{
				int count2 = points.Count - 1;
				for (int j = 0; j <= count2; j++)
				{
					_pointQuadTree.Insert(points[j].X, points[j].Y, capturedLocals.startIdx + j);
				}
			}
			else
			{
				RefreshSpatialIndex();
			}
		}
	}

	void IEditableFeature.InsertPoints(int startIdx, IList<PointM> points)
	{
		//ILSpy generated this explicit interface implementation from .override directive in InsertPoints
		this.InsertPoints(startIdx, points);
	}

	public void InsertPoints(IList<int> sortedIndexes, IList<PointM> insertPoints)
	{
		InsertPoints_InternalMutable(sortedIndexes, insertPoints);
		RefreshSpatialIndex();
	}

	void IEditableFeature.InsertPoints(IList<int> sortedIndexes, IList<PointM> insertPoints)
	{
		//ILSpy generated this explicit interface implementation from .override directive in InsertPoints
		this.InsertPoints(sortedIndexes, insertPoints);
	}

	public void SetPoint(int idx, PointM pt)
	{
		if (!EnsureSpatialIndex())
		{
			PointM pointM = _pointM[idx];
			_pointQuadTree.RemoveCoordinate(pointM.X, pointM.Y);
			_pointQuadTree.Insert(pt.X, pt.Y, idx);
		}
		_pointM[idx] = pt;
	}

	public void DeletePoints(IList<int> indexes)
	{
		bool refreshNeeded = true;
		refreshNeeded = DeletePointsQuadTreeRoutine(indexes, refreshNeeded);
		DeletePoints_InternalMutable(indexes);
		if (refreshNeeded)
		{
			RefreshSpatialIndex();
		}
	}

	void IEditableFeature.DeletePoints(IList<int> indexes)
	{
		//ILSpy generated this explicit interface implementation from .override directive in DeletePoints
		this.DeletePoints(indexes);
	}

	private bool DeletePointsQuadTreeRoutine(IList<int> indexes, bool refreshNeeded)
	{
		checked
		{
			if (!EnsureSpatialIndex())
			{
				if ((double)indexes.Count / (double)_pointM.Count < 0.000123)
				{
					_Closure_0024__18_002D0 capturedLocals = new _Closure_0024__18_002D0(null);
					refreshNeeded = false;
					capturedLocals.mutableIndexes = new List<int>(indexes);
					int count = capturedLocals.mutableIndexes.Count - 1;
					_Closure_0024__18_002D1 capturedLocals2 = default;
					for (int i = 0; i <= count; i++)
					{
						capturedLocals2 = new _Closure_0024__18_002D1(capturedLocals2);
						capturedLocals2.outerClosure = capturedLocals;
						capturedLocals2.idx = i;
						if (!_pointQuadTree.RemoveCoordinate(_pointM[indexes[i]].X, _pointM[indexes[i]].Y, capturedLocals2.outerClosure.mutableIndexes[i]))
						{
							throw new Exception("Didn't find index " + capturedLocals2.outerClosure.mutableIndexes[i] + " to delete!");
						}
						VB_0024AnonymousDelegate_0<int, int> lambda_0024__0 = capturedLocals2._Lambda_0024__0;
						QuadTree<int> pointQuadTree = _pointQuadTree;
						VB_0024AnonymousDelegate_0<int, int> lambda_0024__02 = lambda_0024__0;
						pointQuadTree.ModifyKeysParallel((lambda_0024__02 == null) ? null : new Func<int, int>(lambda_0024__02.Invoke));
						int ordinal = i + 1;
						int count2 = capturedLocals2.outerClosure.mutableIndexes.Count - 1;
						for (int j = ordinal; j <= count2; j++)
						{
							if (capturedLocals2.outerClosure.mutableIndexes[j] >= capturedLocals2.outerClosure.mutableIndexes[i])
							{
								capturedLocals2.outerClosure.mutableIndexes[j]--;
							}
						}
					}
				}
			}
			else
			{
				refreshNeeded = false;
			}
			return refreshNeeded;
		}
	}

	public void DeletePoints(int start, int count)
	{
		List<int> intList = new List<int>();
		checked
		{
			int count2 = count - 1;
			for (int i = 0; i <= count2; i++)
			{
				intList.Add(start + i);
			}
			bool refreshNeeded = true;
			refreshNeeded = DeletePointsQuadTreeRoutine(intList, refreshNeeded);
			DeletePoints_InternalMutable(start, count);
			if (refreshNeeded)
			{
				RefreshSpatialIndex();
			}
		}
	}

	void IEditableFeature.DeletePoints(int start, int count)
	{
		//ILSpy generated this explicit interface implementation from .override directive in DeletePoints
		this.DeletePoints(start, count);
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
}
