using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;
using Utility.DataStructures;

namespace Geospatial.Rendering.Editing;

public class SplitPolylineOperation : EditOperation
{
	public struct SplitPolylineResult
	{
		public int OriginalFid;

		public Polyline OriginalPolyline;
	}

	private readonly IPolylineCollection _collection;

	private readonly List<SplitLineResult> _splits;

	private readonly List<SplitPolylineResult> _splitResults = new List<SplitPolylineResult>();

	public override string Description
	{
		get
		{
			if (_splits.Count != 1)
			{
				return "Split polylines";
			}
			return "Split polyline";
		}
	}

	public SplitPolylineOperation(IPolylineCollection collection, CompactList<SplitLineResult> splits)
		: base(null)
	{
		_collection = collection;
		_splits = splits.ToList();
	}

	protected override bool ApplyInternal()
	{
		_splitResults.Clear();
		List<SplitLineResult> splits = new List<SplitLineResult>(_splits);
		splits.Sort((SplitLineResult a, SplitLineResult b) => b.FID.CompareTo(a.FID));
		foreach (SplitLineResult split in splits)
		{
			Polyline polyline = _collection[split.FID];
			_splitResults.Add(new SplitPolylineResult
			{
				OriginalFid = split.FID,
				OriginalPolyline = polyline
			});
			Polyline subsectionBefore = polyline.SubsectionBefore(split.Point);
			Polyline subsectionAfter = polyline.SubsectionAfter(split.Point);
			_collection.RemoveAt(split.FID);
			_collection.Add(subsectionBefore);
			_collection.Add(subsectionAfter);
		}
		return _splitResults.Count > 0;
	}

	protected override bool UndoInternal()
	{
		for (int i = 0; i < _splitResults.Count; i++)
		{
			_collection.RemoveAt(_collection.Count - 1);
			_collection.RemoveAt(_collection.Count - 1);
		}
		for (int countOffset = _splitResults.Count - 1; countOffset >= 0; countOffset--)
		{
			SplitPolylineResult splitPolylineResult = _splitResults[countOffset];
			if (splitPolylineResult.OriginalFid >= _collection.Count)
			{
				_collection.Add(splitPolylineResult.OriginalPolyline);
			}
			else
			{
				_collection.Insert(splitPolylineResult.OriginalFid, splitPolylineResult.OriginalPolyline);
			}
		}
		return true;
	}
}
