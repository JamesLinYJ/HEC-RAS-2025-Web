using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Geospatial.DataStructures;
using Geospatial.Vectors;
using RasMapperLib.Utilities;

namespace RasMapperLib;

public class MultiPoint : Feature, IPoints, IEnumerable<PointM>
{
	public enum CheckState
	{
		NotChecked,
		CheckedAndCrossHatched,
		CheckedAndNotCrossHatched,
		CheckedAndDummyCrossHatched
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__119_002D0
	{
		public Dictionary<Bin<int>, CheckState> binCheckStates;

		public _Closure_0024__119_002D0(_Closure_0024__119_002D0 other)
		{
			if (other != null)
			{
				binCheckStates = other.binCheckStates;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__0(Bin<int> bin)
		{
			CheckState notChecked = CheckState.NotChecked;
			binCheckStates.TryGetValue(bin, out notChecked);
			if (notChecked == CheckState.CheckedAndCrossHatched || notChecked == CheckState.CheckedAndDummyCrossHatched)
			{
				return true;
			}
			return false;
		}

		[SpecialName]
		internal bool _Lambda_0024__1(Bin<int> bin)
		{
			CheckState notChecked = CheckState.NotChecked;
			binCheckStates.TryGetValue(bin, out notChecked);
			if (notChecked == CheckState.NotChecked)
			{
				return true;
			}
			return false;
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__120_002D0
	{
		public Dictionary<Bin<int>, CheckState> binCheckStates;

		public _Closure_0024__120_002D0(_Closure_0024__120_002D0 other)
		{
			if (other != null)
			{
				binCheckStates = other.binCheckStates;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__0(Bin<int> child)
		{
			CheckState notChecked = CheckState.NotChecked;
			binCheckStates.TryGetValue(child, out notChecked);
			if (notChecked == CheckState.CheckedAndCrossHatched || notChecked == CheckState.CheckedAndDummyCrossHatched)
			{
				return true;
			}
			return false;
		}
	}

	protected IList<PointM> _pointM;

	protected int _maxX;

	protected int _minX;

	protected int _maxY;

	protected int _minY;

	protected int _maxZ;

	protected int _minZ;

	protected int _maxM;

	protected int _minM;

	private static MultiPoint _nullPoints = new MultiPoint(new PointMs());

	public const string sDisplayQuadTreeBins = "Display Quad Tree Bins";

	private static readonly PlotOption _poDisplayQuadTreeBoundaryBins = new PlotOption("Display Quad Tree Bins", "", debugOption: true);

	public const string sDisplayDensePointsAsHatched = "Display Dense Points as Hatched";

	private static readonly PlotOption _poDisplayDensePointsAsHatched = new PlotOption("Display Dense Points as Hatched", "", debugOption: true);

	public const int SpatialIndexMinPointCount = 1000;

	protected SpatialIndex<int> _pointSpatialIndex;

	private const double DensityToDrawAsCrossHatch = 0.05;

	protected QuadTree<int> _pointQuadTree;

	protected int _quadTreeBinNumItems;

	protected Dictionary<Bin<int>, Polygon> _qtLeafBinToPolygon;

	protected Dictionary<Bin<int>, List<Bin<int>>> _binsToNeighbors;

	public virtual bool UsingQuadTree => true;

	public PointM this[int idx] => _pointM[idx];

	// C# has no syntax for parameterized property 'X'.
	public double get_X(int index)
	{
		return _pointM[index].X;
	}

	// C# has no syntax for parameterized property 'Y'.
	public double get_Y(int index)
	{
		return _pointM[index].Y;
	}

	// C# has no syntax for parameterized property 'Z'.
	public double get_Z(int index)
	{
		return _pointM[index].Z;
	}

	// C# has no syntax for parameterized property 'M'.
	public double get_M(int index)
	{
		return _pointM[index].M;
	}

	public override string SummaryText
	{
		get
		{
			string textValue = "";
			checked
			{
				int count = Count - 1;
				for (int i = 0; i <= count; i++)
				{
					textValue = textValue + _pointM[i].X + "," + _pointM[i].Y;
					if (HasZ)
					{
						textValue = textValue + "," + _pointM[i].Z;
					}
					if (HasM)
					{
						textValue = textValue + "," + _pointM[i].M;
					}
					textValue += ", ";
					if (textValue.Length > 128)
					{
						break;
					}
				}
				return textValue;
			}
		}
	}

	public override bool HasZ => _minZ > -1;

	public override bool HasM => _minM > -1;

	public override double MaxX
	{
		get
		{
			if (Count == 0 || _maxX < 0)
			{
				return double.NaN;
			}
			return _pointM[_maxX].X;
		}
	}

	public override double MinX
	{
		get
		{
			if (Count == 0 || _minX < 0)
			{
				return double.NaN;
			}
			return _pointM[_minX].X;
		}
	}

	public override double MaxY
	{
		get
		{
			if (Count == 0 || _maxY < 0)
			{
				return double.NaN;
			}
			return _pointM[_maxY].Y;
		}
	}

	public override double MinY
	{
		get
		{
			if (Count == 0 || _minY < 0)
			{
				return double.NaN;
			}
			return _pointM[_minY].Y;
		}
	}

	public override double MaxZ
	{
		get
		{
			if (HasZ)
			{
				return _pointM[_maxZ].Z;
			}
			return double.NaN;
		}
	}

	public override double MinZ
	{
		get
		{
			if (HasZ)
			{
				return _pointM[_minZ].Z;
			}
			return double.NaN;
		}
	}

	public override double MaxM
	{
		get
		{
			if (HasM)
			{
				return _pointM[_maxM].M;
			}
			return double.NaN;
		}
	}

	public override double MinM
	{
		get
		{
			if (HasM)
			{
				return _pointM[_minM].M;
			}
			return double.NaN;
		}
	}

	public override bool MaxMinXYDefined
	{
		get
		{
			if (_maxX == -1)
			{
				return false;
			}
			if (double.IsNaN(MaxY))
			{
				return false;
			}
			return true;
		}
	}

	public override bool UsesPointSymbol { get; }

	public override bool UsesPen { get; }

	public override bool UsesBrush { get; }

	public override bool UsesSurfaceFill { get; }

	public override int Count => _pointM.Count;

	public static MultiPoint NullPoints()
	{
		return _nullPoints;
	}

	protected MultiPoint()
	{
		_pointSpatialIndex = null;
		_pointQuadTree = null;
		_quadTreeBinNumItems = 100;
		_qtLeafBinToPolygon = new Dictionary<Bin<int>, Polygon>();
		_binsToNeighbors = new Dictionary<Bin<int>, List<Bin<int>>>();
		UsesPointSymbol = true;
		UsesPen = false;
		UsesBrush = false;
		UsesSurfaceFill = false;
	}

	public MultiPoint(PointMs pointMs)
	{
		_pointSpatialIndex = null;
		_pointQuadTree = null;
		_quadTreeBinNumItems = 100;
		_qtLeafBinToPolygon = new Dictionary<Bin<int>, Polygon>();
		_binsToNeighbors = new Dictionary<Bin<int>, List<Bin<int>>>();
		UsesPointSymbol = true;
		UsesPen = false;
		UsesBrush = false;
		UsesSurfaceFill = false;
		PointM[] pointM = new PointM[checked(pointMs.Count - 1 + 1)];
		_pointM = pointM;
		((List<PointM>)pointMs).CopyTo((PointM[])_pointM);
		PointDataChanged();
	}

	public MultiPoint(IList<PointM> ptms)
	{
		_pointSpatialIndex = null;
		_pointQuadTree = null;
		_quadTreeBinNumItems = 100;
		_qtLeafBinToPolygon = new Dictionary<Bin<int>, Polygon>();
		_binsToNeighbors = new Dictionary<Bin<int>, List<Bin<int>>>();
		UsesPointSymbol = true;
		UsesPen = false;
		UsesBrush = false;
		UsesSurfaceFill = false;
		checked
		{
			PointM[] pointMs = (PointM[])(_pointM = new PointM[ptms.Count - 1 + 1]);
			int count = ptms.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				pointMs[i] = ptms[i];
			}
			PointDataChanged();
		}
	}

	public override List<PlotOption> AvailablePlotOptions(bool debugMode)
	{
		return GetAvailablePlotOptions(debugMode);
	}

	public static List<PlotOption> GetAvailablePlotOptions(bool debugMode)
	{
		return new List<PlotOption>(new PlotOption[2] { _poDisplayDensePointsAsHatched, _poDisplayQuadTreeBoundaryBins });
	}

	public List<PointM> CopyPointList()
	{
		return _pointM.ToList();
	}

	public static MultiPoint FromBuffers(IList<PointM> points)
	{
		MultiPoint multiPoint = new MultiPoint(new PointMs());
		multiPoint.OverrideBuffersAndReinitialize(points);
		return multiPoint;
	}

	protected void OverrideBuffersAndReinitialize(IList<PointM> points)
	{
		_pointM = points;
		PointDataChanged();
	}

	protected virtual void PointDataChanged()
	{
		if (!UsingQuadTree)
		{
			_pointSpatialIndex = null;
		}
		PointMs.MaxMinIndexes(_pointM, ref _maxX, ref _minX, ref _maxY, ref _minY, ref _maxZ, ref _minZ, ref _maxM, ref _minM);
	}

	public IEnumerator<PointM> GetEnumerator()
	{
		foreach (PointM pointM in _pointM)
		{
			yield return pointM;
		}
	}

	IEnumerator<PointM> IEnumerable<PointM>.GetEnumerator()
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetEnumerator
		return this.GetEnumerator();
	}

	private IEnumerator IEnumerable_GetEnumerator()
	{
		return GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		//ILSpy generated this explicit interface implementation from .override directive in IEnumerable_GetEnumerator
		return this.IEnumerable_GetEnumerator();
	}

	public override PointM Centroid()
	{
		checked
		{
			int count = _pointM.Count - 1;
			double computed = default;
			double computed2 = default;
			for (int i = 0; i <= count; i++)
			{
				computed += _pointM[i].X;
				computed2 += _pointM[i].Y;
			}
			return new PointM(computed / (double)_pointM.Count, computed2 / (double)_pointM.Count);
		}
	}

	public override void MD5HashAppend(Hash hash)
	{
		hash.Add(_pointM);
	}

	public virtual Extent ComputeExtent()
	{
		if (_maxZ == -1)
		{
			if (_maxM == -1)
			{
				return GetExtentXY();
			}
			return GetExtentXYM();
		}
		if (_maxM == -1)
		{
			return GetExtentXYZ();
		}
		return GetExtentXYZM();
	}

	Extent IPoints.ComputeExtent()
	{
		//ILSpy generated this explicit interface implementation from .override directive in ComputeExtent
		return this.ComputeExtent();
	}

	private Extent GetExtentXY()
	{
		return new Extent(MaxX, MinX, MaxY, MinY);
	}

	private Extent GetExtentXYZ()
	{
		return new Extent(MaxX, MinX, MaxY, MinY, MaxZ, MinZ);
	}

	private Extent GetExtentXYM()
	{
		return new Extent(MaxX, MinX, MaxY, MinY, double.NaN, double.NaN, MaxM, MinM);
	}

	private Extent GetExtentXYZM()
	{
		return new Extent(MaxX, MinX, MaxY, MinY, MaxZ, MinZ, MaxM, MinM);
	}

	public int PointMCount()
	{
		return Count;
	}

	int IPoints.PointMCount()
	{
		//ILSpy generated this explicit interface implementation from .override directive in PointMCount
		return this.PointMCount();
	}

	public PointM PointM(int index)
	{
		return _pointM[index];
	}

	PointM IPoints.PointM(int index)
	{
		//ILSpy generated this explicit interface implementation from .override directive in PointM
		return this.PointM(index);
	}

	public PointM FirstPointM()
	{
		return _pointM.First();
	}

	PointM IPoints.FirstPointM()
	{
		//ILSpy generated this explicit interface implementation from .override directive in FirstPointM
		return this.FirstPointM();
	}

	public PointM LastPointM()
	{
		return _pointM.Last();
	}

	PointM IPoints.LastPointM()
	{
		//ILSpy generated this explicit interface implementation from .override directive in LastPointM
		return this.LastPointM();
	}

	public PointMs CopyPoints()
	{
		PointMs pointMs = new PointMs();
		pointMs.AddRange(_pointM);
		return pointMs;
	}

	protected void InsertPoints_InternalMutable(IList<int> sortedIndexes, IList<PointM> insertPoints)
	{
		if (sortedIndexes == null || insertPoints == null)
		{
			return;
		}
		if (sortedIndexes.Count != insertPoints.Count)
		{
			throw new ArgumentException();
		}
		if (sortedIndexes.Count == 0)
		{
			return;
		}
		checked
		{
			PointMs pointMs = new PointMs(_pointM.Count + insertPoints.Count);
			int ordinal = 0;
			int count = _pointM.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				if (ordinal < sortedIndexes.Count && pointMs.Count == sortedIndexes[ordinal])
				{
					int pointMCount = pointMs.Count;
					do
					{
						pointMs.Add(insertPoints[ordinal]);
						ordinal++;
						pointMCount++;
					}
					while (ordinal < sortedIndexes.Count && sortedIndexes[ordinal] == pointMCount);
				}
				pointMs.Add(_pointM[i]);
			}
			if (ordinal < insertPoints.Count)
			{
				int ordinal2 = ordinal;
				int count2 = insertPoints.Count - 1;
				for (int j = ordinal2; j <= count2; j++)
				{
					pointMs.Add(insertPoints[j]);
				}
			}
			_pointM = pointMs;
			PointDataChanged();
		}
	}

	protected void InsertPoints_InternalMutable(int startIdx, IList<PointM> points)
	{
		if (points.Count != 0)
		{
			List<PointM> output = null;
			ListExtensionMethods.InsertRange(_pointM, ref output, _pointM.Count, points);
			if (output == null)
			{
				_pointM = new List<PointM>();
			}
			else
			{
				_pointM = output;
			}
			PointDataChanged();
		}
	}

	protected void DeletePoints_InternalMutable(IList<int> indexes)
	{
		ByteBitArray byteBitArray = new ByteBitArray(Count);
		foreach (int index in indexes)
		{
			byteBitArray[index] = true;
		}
		checked
		{
			if (indexes.Count <= 2)
			{
				for (int i = _pointM.Count - 1; i >= 0; i += -1)
				{
					if (byteBitArray[i])
					{
						_pointM.RemoveAt(i);
					}
				}
			}
			else if (indexes.Count == _pointM.Count)
			{
				_pointM.Clear();
			}
			else
			{
				List<PointM> pointM = new List<PointM>(_pointM.Count - indexes.Count);
				int count = _pointM.Count - 1;
				for (int j = 0; j <= count; j++)
				{
					if (!byteBitArray[j])
					{
						pointM.Add(_pointM[j]);
					}
				}
				_pointM = pointM;
			}
			PointDataChanged();
		}
	}

	protected void DeletePoints_InternalMutable(int start, int count)
	{
		List<PointM> output = new List<PointM>();
		ListExtensionMethods.RemoveRange(_pointM, ref output, start, count);
		if (output == null)
		{
			_pointM = new List<PointM>();
		}
		else
		{
			_pointM = output;
		}
		PointDataChanged();
	}

	public void CopyTo(PointMs pointMs)
	{
		pointMs.AddRange(_pointM);
	}

	public void CopyToReverse(PointMs pointMs)
	{
		checked
		{
			for (int i = _pointM.Count - 1; i >= 0; i += -1)
			{
				pointMs.Add(_pointM[i]);
			}
		}
	}

	public override string ToString()
	{
		return "Points" + PointLabel(HasZ, HasM);
	}

	public override bool EqualXY(Feature feature)
	{
		checked
		{
			if (feature is MultiPoint)
			{
				MultiPoint multiPoint = (MultiPoint)feature;
				if (multiPoint.Count != _pointM.Count)
				{
					return false;
				}
				int count = _pointM.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					if (!_pointM[i].EqualXY(multiPoint.PointM(i)))
					{
						return false;
					}
				}
				return true;
			}
			return false;
		}
	}

	public virtual bool EnsureSpatialIndex()
	{
		checked
		{
			if (UsingQuadTree)
			{
				bool result = false;
				if ((_pointQuadTree == null || _pointQuadTree.Equals(QuadTree<int>.NullQuadTree)) && Count > 0)
				{
					result = true;
					Extent extent = ComputeExtent();
					_pointQuadTree = new QuadTree<int>(new Geospatial.Vectors.Point(extent.Left, extent.Bottom), new Geospatial.Vectors.Point(extent.Right, extent.Top), 16, _quadTreeBinNumItems, 256);
					int count = _pointM.Count - 1;
					for (int i = 0; i <= count; i++)
					{
						_pointQuadTree.Insert(_pointM[i].X, _pointM[i].Y, i);
					}
				}
				else if (_pointQuadTree == null && Count == 0)
				{
					_pointQuadTree = QuadTree<int>.NullQuadTree;
					result = true;
				}
				return result;
			}
			if (_pointSpatialIndex == null && Count > 0)
			{
				_pointSpatialIndex = new SpatialIndex<int>(ComputeExtent());
				int count2 = _pointM.Count - 1;
				for (int j = 0; j <= count2; j++)
				{
					_pointSpatialIndex.Add(_pointM[j], j);
				}
			}
			return true;
		}
	}

	public void RegenerateQuadTree(int binNumItems)
	{
		_quadTreeBinNumItems = binNumItems;
		_pointQuadTree = null;
		EnsureSpatialIndex();
	}

	public QuadTree<int> GetPointQuadTree()
	{
		if (!UsingQuadTree)
		{
			throw new Exception("This class is not using the point quad tree.");
		}
		EnsureSpatialIndex();
		return _pointQuadTree;
	}

	public SpatialIndex<int> GetPointSpatialIndex()
	{
		if (UsingQuadTree)
		{
			throw new Exception("This class is not using the point spatial index.");
		}
		EnsureSpatialIndex();
		return _pointSpatialIndex;
	}

	public override bool Intersects(PointM pointM, double nearDistance)
	{
		checked
		{
			if (_pointM.Count < 1000)
			{
				int count = _pointM.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					if (pointM.CloseTo(_pointM[i], nearDistance))
					{
						return true;
					}
				}
			}
			else
			{
				EnsureSpatialIndex();
				List<int> elements = null;
				elements = ((!UsingQuadTree) ? _pointSpatialIndex.GetElements(pointM.X, pointM.Y, nearDistance) : _pointQuadTree.GetAllValuesWithinCircle(pointM.X, pointM.Y, nearDistance));
				foreach (int element in elements)
				{
					if (pointM.CloseTo(_pointM[element], nearDistance))
					{
						return true;
					}
				}
			}
			return false;
		}
	}

	public override bool Intersects(Extent extent)
	{
		checked
		{
			if (_pointM.Count < 1000)
			{
				int count = _pointM.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					if (extent.Contains(_pointM[i]))
					{
						return true;
					}
				}
			}
			else
			{
				EnsureSpatialIndex();
				List<int> elements = null;
				elements = ((!UsingQuadTree) ? _pointSpatialIndex.GetElements(extent) : _pointQuadTree.GetAllValuesWithinRectangle(extent.Left, extent.Bottom, extent.Right, extent.Top));
				foreach (int element in elements)
				{
					if (extent.Contains(_pointM[element]))
					{
						return true;
					}
				}
			}
			return false;
		}
	}

	public int NearestPointIndex(PointM pointM, ref double distance)
	{
		int result = 0;
		distance = double.MaxValue;
		checked
		{
			int count = _pointM.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				double distance2 = _pointM[i].DistanceTo(pointM);
				if (distance2 < distance)
				{
					result = i;
					distance = distance2;
				}
			}
			return result;
		}
	}

	public List<int> SpatialQueryFIDs(Extent ext)
	{
		return _pointQuadTree.GetAllValuesWithinRectangle(ext.Left, ext.Bottom, ext.Right, ext.Top);
	}

	public List<int> SpatialQueryFIDs(PointM point, double nearDistance)
	{
		return _pointQuadTree.GetAllValuesWithinCircle(point.X, point.Y, nearDistance);
	}

	public void SelectPoints(Extent withinExtent, SelectionMode selectionMode, SelectionRange selectionRange, SelectedFeature workingSelectedSet)
	{
		if (selectionMode == SelectionMode.SelectNew)
		{
			workingSelectedSet.ClearSelection();
		}
		workingSelectedSet.OpenFeature();
		IList<int> intList = null;
		if (_pointM.Count < 1000)
		{
			intList = Enumerable.Range(0, _pointM.Count).ToList();
		}
		else
		{
			EnsureSpatialIndex();
			intList = ((!UsingQuadTree) ? _pointSpatialIndex.GetElements(withinExtent) : _pointQuadTree.GetAllValuesWithinRectangle(withinExtent.Left, withinExtent.Bottom, withinExtent.Right, withinExtent.Top));
		}
		switch (selectionMode)
		{
		case SelectionMode.SelectNew:
		case SelectionMode.Append:
		{
			foreach (int ordinal in intList)
			{
				if (withinExtent.Contains(_pointM[ordinal]))
				{
					workingSelectedSet.SelectPoint(ordinal);
					if (selectionRange == SelectionRange.SelectSingle)
					{
						break;
					}
				}
			}
			break;
		}
		case SelectionMode.Deselect:
		{
			foreach (int ordinal2 in intList)
			{
				if (withinExtent.Contains(_pointM[ordinal2]))
				{
					workingSelectedSet.RemovePoint(ordinal2);
				}
			}
			break;
		}
		case SelectionMode.Toggle:
		{
			foreach (int ordinal3 in intList)
			{
				if (withinExtent.Contains(_pointM[ordinal3]))
				{
					workingSelectedSet.TogglePoint(ordinal3);
				}
			}
			break;
		}
		}
	}

	public SelectedFeature SelectPoints(Extent withinExtent, SelectionRange selectionRange)
	{
		SelectedFeature selectedFeature = new SelectedFeature(0);
		selectedFeature.OpenFeature();
		SelectPoints(withinExtent, SelectionMode.SelectNew, selectionRange, selectedFeature);
		return selectedFeature;
	}

	public SelectedFeature SelectPoints(PointM pt, double radius, SelectionRange selectionRange)
	{
		Extent withinExtent = new Extent(pt, radius, radius);
		SelectedFeature selectedFeature = new SelectedFeature(0);
		selectedFeature.OpenFeature();
		SelectPoints(withinExtent, SelectionMode.SelectNew, selectionRange, selectedFeature);
		return selectedFeature;
	}

	public override PointM LabelPoint(FeatureLayer layer)
	{
		if (_pointM.Count > 0)
		{
			return _pointM.First();
		}
		return new PointM(double.MinValue, double.MinValue);
	}

	public void WriteExtentsToImmediateWindow()
	{
	}

	private void GetAllBinsToPolygonify(Bin<int> lastConsideredBin, Dictionary<Bin<int>, CheckState> binCheckStates, HashSet<Bin<int>> consideredBins, List<Bin<int>> binsToPolygonify)
	{
		_Closure_0024__119_002D0 capturedLocals = default;
		_Closure_0024__119_002D0 capturedLocals2 = new _Closure_0024__119_002D0(capturedLocals);
		capturedLocals2.binCheckStates = binCheckStates;
		Bin<int> parent = lastConsideredBin.Parent;
		if (parent == null)
		{
			binsToPolygonify.Add(lastConsideredBin);
			return;
		}
		int enumerable2Term = 4;
		while (enumerable2Term != 0)
		{
			IEnumerable<Bin<int>> enumerable = parent.AllChildren().Where([SpecialName] (Bin<int> bin) =>
			{
				CheckState notChecked = CheckState.NotChecked;
				capturedLocals2.binCheckStates.TryGetValue(bin, out notChecked);
				return (notChecked == CheckState.CheckedAndCrossHatched || notChecked == CheckState.CheckedAndDummyCrossHatched) ? true : false;
			});
			if (enumerable.Count() != 4)
			{
				IEnumerable<Bin<int>> enumerable2 = parent.AllChildren().Where([SpecialName] (Bin<int> bin) =>
				{
					CheckState notChecked2 = CheckState.NotChecked;
					capturedLocals2.binCheckStates.TryGetValue(bin, out notChecked2);
					return notChecked2 == CheckState.NotChecked;
				});
				enumerable2Term = enumerable2.Count();
				if (enumerable2Term == 0)
				{
					foreach (Bin<int> enumerable3 in enumerable)
					{
						if (capturedLocals2.binCheckStates[enumerable3] == CheckState.CheckedAndCrossHatched)
						{
							binsToPolygonify.Add(enumerable3);
						}
					}
					capturedLocals2.binCheckStates[parent] = CheckState.CheckedAndNotCrossHatched;
					continue;
				}
				foreach (Bin<int> enumerable22 in enumerable2)
				{
					FindOutCheckStateForBin(enumerable22, capturedLocals2.binCheckStates, consideredBins, binsToPolygonify);
				}
				continue;
			}
			capturedLocals2.binCheckStates[parent] = CheckState.CheckedAndCrossHatched;
			foreach (Bin<int> enumerable4 in enumerable)
			{
				if (!consideredBins.Contains(enumerable4) && enumerable4.IsLeafNode)
				{
					consideredBins.Add(enumerable4);
				}
			}
			GetAllBinsToPolygonify(parent, capturedLocals2.binCheckStates, consideredBins, binsToPolygonify);
			break;
		}
	}

	private void FindOutCheckStateForBin(Bin<int> bin, Dictionary<Bin<int>, CheckState> binCheckStates, HashSet<Bin<int>> consideredBins, List<Bin<int>> binsToPolygonify)
	{
		_Closure_0024__120_002D0 capturedLocals = default;
		_Closure_0024__120_002D0 capturedLocals2 = new _Closure_0024__120_002D0(capturedLocals);
		capturedLocals2.binCheckStates = binCheckStates;
		if (!bin.IsLeafNode)
		{
			FindOutCheckStateForBin(bin.FirstQuadrant, capturedLocals2.binCheckStates, consideredBins, binsToPolygonify);
			FindOutCheckStateForBin(bin.SecondQuadrant, capturedLocals2.binCheckStates, consideredBins, binsToPolygonify);
			FindOutCheckStateForBin(bin.ThirdQuadrant, capturedLocals2.binCheckStates, consideredBins, binsToPolygonify);
			FindOutCheckStateForBin(bin.FourthQuadrant, capturedLocals2.binCheckStates, consideredBins, binsToPolygonify);
			IEnumerable<Bin<int>> enumerable = bin.AllChildren().Where([SpecialName] (Bin<int> child) =>
			{
				CheckState notChecked = CheckState.NotChecked;
				capturedLocals2.binCheckStates.TryGetValue(child, out notChecked);
				return (notChecked == CheckState.CheckedAndCrossHatched || notChecked == CheckState.CheckedAndDummyCrossHatched) ? true : false;
			});
			if (enumerable.Count() != 4)
			{
				capturedLocals2.binCheckStates[bin] = CheckState.CheckedAndNotCrossHatched;
				{
					foreach (Bin<int> enumerable2 in enumerable)
					{
						if (capturedLocals2.binCheckStates[enumerable2] == CheckState.CheckedAndCrossHatched)
						{
							binsToPolygonify.Add(enumerable2);
						}
					}
					return;
				}
			}
			capturedLocals2.binCheckStates[bin] = CheckState.CheckedAndCrossHatched;
		}
		else
		{
			CheckState notChecked2 = CheckState.NotChecked;
			capturedLocals2.binCheckStates.TryGetValue(bin, out notChecked2);
			if (notChecked2 == CheckState.NotChecked)
			{
				capturedLocals2.binCheckStates[bin] = CheckState.CheckedAndNotCrossHatched;
			}
			consideredBins.Add(bin);
		}
	}

	private static double CalculateBinDensity(RasterM raster, Bin<int> bin, Polygon binPolygon)
	{
		return (double)bin.Items.Count * raster.CellSize * raster.CellSize / binPolygon.Area;
	}

	public static Polygon GetBinPolygon(Polygon perimeterPoly, DevelopmentTin perimeterDevTin, Bin<int> bin)
	{
		PointM pointM = new PointM(bin.Center.X - bin.SideLen / 2.0, bin.Center.Y - bin.SideLen / 2.0);
		PointM pointM2 = new PointM(bin.Center.X + bin.SideLen / 2.0, bin.Center.Y - bin.SideLen / 2.0);
		PointM pointM3 = new PointM(bin.Center.X - bin.SideLen / 2.0, bin.Center.Y + bin.SideLen / 2.0);
		PointM pointM4 = new PointM(bin.Center.X + bin.SideLen / 2.0, bin.Center.Y + bin.SideLen / 2.0);
		PointMs pointMs = new PointMs();
		pointMs.Add(pointM2);
		pointMs.Add(pointM4);
		pointMs.Add(pointM3);
		pointMs.Add(pointM);
		pointMs.Add(pointM2);
		Polygon polygon = new Polygon(pointMs);
		polygon = polygon.Reverse();
		if (perimeterPoly == null)
		{
			return polygon;
		}
		if (perimeterDevTin.ContainsPoint(pointM) && perimeterDevTin.ContainsPoint(pointM2) && perimeterDevTin.ContainsPoint(pointM3) && perimeterDevTin.ContainsPoint(pointM4))
		{
			return polygon;
		}
		Polygon polygon2 = GeoProcessing.IntersectPolygon(perimeterPoly, polygon);
		if (polygon2 != null)
		{
			polygon = polygon2;
		}
		return polygon;
	}

	public void CopyTo(ref double[] x, ref double[] y)
	{
		checked
		{
			x = new double[Count - 1 + 1];
			y = new double[Count - 1 + 1];
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM pointM = PointM(i);
				x[i] = pointM.X;
				y[i] = pointM.Y;
			}
		}
	}

	internal SpatialIndex<int> GeneratePointSpatialIndex()
	{
		checked
		{
			if (UsingQuadTree)
			{
				Extent extent = ComputeExtent();
				if (!extent.IsFinite())
				{
					extent = new Extent(1.0, 0.0, 1.0, 0.0);
				}
				SpatialIndex<int> spatialIndex = new SpatialIndex<int>(extent);
				int count = _pointM.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					spatialIndex.Add(_pointM[i], i);
				}
				return spatialIndex;
			}
			return _pointSpatialIndex;
		}
	}

	public string ToCSString()
	{
		return ToSString(",", " ", "", "");
	}

	public string ToCSString(string xyFormat, string zmFormat)
	{
		return ToSString(",", " ", xyFormat, zmFormat);
	}

	public string ToSSString()
	{
		return ToSString(" ", " ", "", "");
	}

	public string ToSSString(string xyFormat, string zmFormat)
	{
		return ToSString(" ", " ", xyFormat, zmFormat);
	}

	public string ToTabSString()
	{
		return ToSString("\t", "\r\n", "", "");
	}

	protected virtual string ToSString(string separatorCharacter, string pointSeparatorCharacter, string xyFormat, string zmFormat)
	{
		StringBuilder stringBuilder = new StringBuilder();
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				stringBuilder.Append(_pointM[i].ToSString(separatorCharacter, xyFormat, zmFormat));
				if (i < Count - 1)
				{
					stringBuilder.Append(pointSeparatorCharacter);
				}
			}
			return stringBuilder.ToString();
		}
	}

	public void ToImmediateWindow()
	{
		checked
		{
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
			}
		}
	}

	public override IEditableFeature GetMutableView()
	{
		return new MultiPointEditable(this);
	}

	public override Feature SetZAll(double z)
	{
		PointMs pointMs = new PointMs();
		checked
		{
			int count = PointMCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM pointM = PointM(i);
				pointMs.Add(new PointM(pointM.X, pointM.Y, z, pointM.M));
			}
			return (Feature)GetType().GetConstructor(new Type[1] { typeof(PointMs) }).Invoke(new object[1] { pointMs });
		}
	}

	public override Feature SetMAll(double m)
	{
		PointMs pointMs = new PointMs();
		checked
		{
			int count = PointMCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM pointM = PointM(i);
				pointMs.Add(new PointM(pointM.X, pointM.Y, pointM.Z, m));
			}
			return (Feature)GetType().GetConstructor(new Type[1] { typeof(PointMs) }).Invoke(new object[1] { pointMs });
		}
	}

	public override IList<double> GetZPerVertex()
	{
		return (from ptm in CopyPoints()
			select ptm.Z).ToList();
	}

	public override IList<double> GetMPerVertex()
	{
		return (from ptm in CopyPoints()
			select ptm.M).ToList();
	}

	public override Feature SetZPerVertex(IList<double> zs)
	{
		PointMs pointMs = new PointMs();
		checked
		{
			int count = PointMCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM pointM = PointM(i);
				pointMs.Add(new PointM(pointM.X, pointM.Y, zs[i], pointM.M));
			}
			return (Feature)GetType().GetConstructor(new Type[1] { typeof(PointMs) }).Invoke(new object[1] { pointMs });
		}
	}

	public override Feature SetMPerVertex(IList<double> ms)
	{
		PointMs pointMs = new PointMs();
		checked
		{
			int count = PointMCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				PointM pointM = PointM(i);
				pointMs.Add(new PointM(pointM.X, pointM.Y, pointM.Z, ms[i]));
			}
			return (Feature)GetType().GetConstructor(new Type[1] { typeof(PointMs) }).Invoke(new object[1] { pointMs });
		}
	}
}
