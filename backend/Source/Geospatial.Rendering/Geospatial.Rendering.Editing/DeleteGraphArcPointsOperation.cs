using System;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;
using Geospatial.Vectors.Graph;
using Utility.Memory;

namespace Geospatial.Rendering.Editing;

public class DeleteGraphArcPointsOperation : DeleteArcPointsOperation
{
	private readonly GraphEditContext _context;

	private GraphArc _deletedArc;

	private object[] _deletedArcMetadata;

	public DeleteGraphArcPointsOperation(GraphEditContext context, int arcFid, Func<IPointCollection> getPointCollection, SelectedSet selectedPoints)
		: base(context, arcFid, getPointCollection, selectedPoints)
	{
		_context = context;
	}

	protected override void DeleteArc()
	{
		Graph graph = _context.Graph;
		_deletedArc = graph.Arcs[_arcFid].Clone();
		_deletedArcMetadata = (object[])graph.ArcMetadata.Rows[_arcFid].ItemArray.Clone();
		graph.RemoveArc(_arcFid);
		_context.OnArcRemoved(_arcFid);
	}

	protected override void RestoreArc()
	{
		TableRow tableRow = _context.Graph.InsertArc(_arcFid, _deletedArc.Clone());
		_deletedArcMetadata.CopyTo(tableRow.ItemArray, 0);
		_context.OnArcInserted(_arcFid);
	}
}
