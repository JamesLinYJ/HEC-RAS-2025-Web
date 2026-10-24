using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;

namespace Geospatial.Rendering.Editing;

public class PasteArcOperation : TopologyEditOperation
{
	public const int FallbackToHitTest = -1;

	private IReadOnlyList<Polyline> _arcs;

	private IReadOnlyList<object[]>? _metadataRows;

	private List<int> _addedArcs;

	private List<int> _addedNodes;

	public override string Description => "Paste Arcs";

	public PasteArcOperation(TopologyEditContext context, IReadOnlyList<Polyline> arcs, IReadOnlyList<object[]>? metadataRows = null)
		: base(context, null)
	{
		_arcs = arcs;
		_metadataRows = metadataRows;
		if (_metadataRows != null && _metadataRows.Count != _arcs.Count)
		{
			throw new ArgumentException("Metadata count must match arcs count or be null.", "metadataRows");
		}
	}

	protected override bool ApplyInternal()
	{
		if (_arcs.Count == 0)
		{
			return true;
		}
		_addedArcs = new List<int>();
		_addedNodes = new List<int>();
		bool arcsPolygonTerm = _arcs[0] is Polygon;
		Topology topology = _context.Topology;
		for (int i = 0; i < _arcs.Count; i++)
		{
			Polyline polyline = _arcs[i];
			if (polyline.Count < 2 || polyline.Length == 0.0)
			{
				continue;
			}
			topology.AddNode(polyline.First(), out var fid);
			int fid2;
			if (arcsPolygonTerm)
			{
				fid2 = fid;
			}
			else
			{
				topology.AddNode(polyline.Last(), out fid2);
			}
			_addedNodes.Add(fid);
			_addedNodes.Add(fid2);
			_context.OnNodeAdded(fid);
			_context.OnNodeAdded(fid2);
			List<Point> points = null;
			if (polyline.Count > 2)
			{
				points = new List<Point>(polyline.Count - 2);
				for (int j = 1; j < polyline.Count - 1; j++)
				{
					points.Add(polyline[j]);
				}
			}
			topology.AddArc(fid, fid2, points, out var fid3);
			topology.Arcs[fid3].ExternallyTracked_DateOfGenesis = DateTime.UtcNow.Ticks;
			if (_metadataRows != null && _metadataRows.Count > 0)
			{
				topology.ArcMetadata.TrySetRow(fid3, _metadataRows[i]);
			}
			_context.OnArcAdded(fid3);
			_addedArcs.Add(fid3);
		}
		return true;
	}

	protected override bool UndoInternal()
	{
		Topology topology = _context.Topology;
		foreach (int orderDescending in _addedArcs.OrderDescending())
		{
			topology.RemoveArc(orderDescending);
			_context.OnArcRemoved(orderDescending);
		}
		foreach (int orderDescending2 in _addedNodes.OrderDescending())
		{
			topology.RemoveNode(orderDescending2);
			_context.OnNodeRemoved(orderDescending2);
		}
		return true;
	}
}
