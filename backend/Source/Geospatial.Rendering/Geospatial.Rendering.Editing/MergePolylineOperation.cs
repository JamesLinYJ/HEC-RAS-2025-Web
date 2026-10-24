using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public class MergePolylineOperation : EditOperation
{
	private readonly IPolylineCollection _collection;

	private readonly List<int> _originalFids;

	private readonly double _tolerance;

	private readonly List<(int fid, Polyline polyline)> _originalPolylines = new List<(int, Polyline)>();

	private List<Polyline> _mergedPolylines;

	private List<int> _unmergedOriginalFids;

	public override string Description
	{
		get
		{
			if (_originalFids.Count != 2)
			{
				return $"Merge {_originalFids.Count} polylines";
			}
			return "Merge polylines";
		}
	}

	public MergePolylineOperation(IPolylineCollection collection, List<int> fids, double tolerance)
		: base(null)
	{
		_collection = collection;
		_originalFids = fids.OrderByDescending((int f) => f).ToList();
		_tolerance = tolerance;
	}

	protected override bool ApplyInternal()
	{
		_originalPolylines.Clear();
		List<Polyline> polylines = new List<Polyline>();
		foreach (int originalFid in _originalFids)
		{
			Polyline polyline = _collection[originalFid];
			_originalPolylines.Add((originalFid, polyline.Clone()));
			polylines.Add(polyline);
		}
		PolylineMergeHelpers.MergeResult mergeResult = PolylineMergeHelpers.MergePolylines(polylines, _tolerance, MergeMode.PreferClosest);
		_mergedPolylines = mergeResult.MergedPolylines;
		_unmergedOriginalFids = new List<int>();
		foreach (int unmergedIndex in mergeResult.UnmergedIndices)
		{
			_unmergedOriginalFids.Add(_originalFids[unmergedIndex]);
		}
		if (_mergedPolylines.Count == 0)
		{
			return true;
		}
		foreach (int originalFid2 in _originalFids)
		{
			_collection.RemoveAt(originalFid2);
		}
		foreach (Polyline mergedPolyline in _mergedPolylines)
		{
			_collection.Add(mergedPolyline);
		}
		foreach (int unmergedIndex2 in mergeResult.UnmergedIndices)
		{
			_collection.Add(polylines[unmergedIndex2]);
		}
		return true;
	}

	protected override bool UndoInternal()
	{
		if (_mergedPolylines.Count == 0)
		{
			return true;
		}
		int count = _mergedPolylines.Count + _unmergedOriginalFids.Count;
		for (int i = 0; i < count; i++)
		{
			_collection.RemoveAt(_collection.Count - 1);
		}
		foreach (var (localValue, localValue2) in _originalPolylines.OrderBy(((int fid, Polyline polyline) x) => x.fid))
		{
			if (localValue >= _collection.Count)
			{
				_collection.Add(localValue2);
			}
			else
			{
				_collection.Insert(localValue, localValue2);
			}
		}
		return true;
	}
}
