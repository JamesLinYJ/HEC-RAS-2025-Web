using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;

namespace Geospatial.Rendering.Editing;

public class PasteControlPointsOperation : TopologyEditOperation
{
	public enum Mode
	{
		Prepend,
		Append
	}

	public const int FallbackToHitTest = -1;

	private IReadOnlyList<Point> _points;

	private IReadOnlyList<object[]> _metadataRows;

	private Range1D _addedRange;

	private Mode _mode;

	public override string Description => "Paste Control Points";

	public PasteControlPointsOperation(TopologyEditContext context, IReadOnlyList<Point> points, IReadOnlyList<object[]> metadataRows, Mode mode)
		: base(context, null)
	{
		_points = points;
		_metadataRows = metadataRows;
		_mode = mode;
		if (_metadataRows != null && _metadataRows.Count != _points.Count)
		{
			throw new ArgumentException("Metadata count must match points count.", "metadataRows");
		}
	}

	protected override bool ApplyInternal()
	{
		if (_points.Count == 0)
		{
			return true;
		}
		Topology topology = _context.Topology;
		int count = topology.RegionControlPoints.Count;
		if (_mode == Mode.Append)
		{
			for (int i = 0; i < _points.Count; i++)
			{
				Point pt = _points[i];
				topology.AddRegionControlPoint(pt, out var fid);
				topology.ControlPointMetadata.TrySetRow(fid, _metadataRows[i]);
			}
			_addedRange = new Range1D(count, count + _points.Count - 1);
			_context.OnControlPointsAdded(_addedRange.Count);
		}
		else
		{
			for (int countOffset = _points.Count - 1; countOffset >= 0; countOffset--)
			{
				Point pt2 = _points[countOffset];
				int ordinal = 0;
				topology.InsertRegionControlPoint(ordinal, pt2);
				topology.ControlPointMetadata.TrySetRow(ordinal, _metadataRows[countOffset]);
			}
			_addedRange = new Range1D(0, _points.Count - 1);
			_context.OnControlPointsInserted(Enumerable.Range(_addedRange.Start, _addedRange.Count));
		}
		return true;
	}

	protected override bool UndoInternal()
	{
		Topology topology = _context.Topology;
		for (int end = _addedRange.End; end >= _addedRange.Start; end--)
		{
			topology.RemoveRegionControlPoint(end);
		}
		_context.OnControlPointsRemoved(Enumerable.Range(_addedRange.Start, _addedRange.Count));
		return true;
	}
}
