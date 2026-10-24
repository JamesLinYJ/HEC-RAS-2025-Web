using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public class ReplacePolylinesOperation : EditOperation
{
	private readonly IPolylineCollection _collection;

	private readonly List<int> _originalFids;

	private readonly List<Polyline> _newPolylines;

	private readonly List<(int fid, Polyline polyline)> _removedPolylines = new List<(int, Polyline)>();

	public override string Description { get; }

	public ReplacePolylinesOperation(IPolylineCollection collection, IEnumerable<int> originalFids, IEnumerable<Polyline> newPolylines, string description = "Replace polylines")
		: base(null)
	{
		_collection = collection;
		_originalFids = originalFids.OrderByDescending((int f) => f).ToList();
		_newPolylines = newPolylines.ToList();
		Description = description;
	}

	protected override bool ApplyInternal()
	{
		_removedPolylines.Clear();
		foreach (int originalFid in _originalFids)
		{
			_removedPolylines.Add((originalFid, _collection[originalFid].Clone()));
		}
		foreach (int originalFid2 in _originalFids)
		{
			_collection.RemoveAt(originalFid2);
		}
		foreach (Polyline newPolyline in _newPolylines)
		{
			_collection.Add(newPolyline);
		}
		return true;
	}

	protected override bool UndoInternal()
	{
		for (int i = 0; i < _newPolylines.Count; i++)
		{
			_collection.RemoveAt(_collection.Count - 1);
		}
		foreach (var (localValue, localValue2) in _removedPolylines.OrderBy(((int fid, Polyline polyline) x) => x.fid))
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
