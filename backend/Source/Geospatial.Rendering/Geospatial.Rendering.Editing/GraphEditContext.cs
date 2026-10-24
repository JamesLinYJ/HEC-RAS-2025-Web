using System;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;
using Geospatial.Vectors.Graph;
using Geospatial.Vectors.Topology;
using Utility.DataStructures;

namespace Geospatial.Rendering.Editing;

public class GraphEditContext : NodeArcEditContext
{
	private sealed class BatchUpdateScope : IDisposable
	{
		private GraphEditContext _owner;

		public BatchUpdateScope(GraphEditContext owner)
		{
			_owner = owner;
		}

		public void Dispose()
		{
			if (_owner != null)
			{
				_owner.BatchUpdateEnded?.Invoke();
				_owner = null;
			}
		}
	}

	public Any<EditableGraph> AnyEditableGraph;

	public EditableGraph EditableGraph => AnyEditableGraph;

	public Graph Graph => EditableGraph.Vector;

	public override BaseTopology Network => Graph;

	public override IEditableNodeArcNetwork EditableNetwork => EditableGraph;

	public override int NodePixelHitDistance => 6;

	public event Action BatchUpdateStarted;

	public event Action BatchUpdateEnded;

	public override EditOperation CreateTranslateSelectedNodesOperation(HitTestArgs hit, Vector delta)
	{
		return new TranslateSelectedGraphNodesOperation(this, hit, delta);
	}

	public override DeleteArcPointsOperation CreateDeleteArcPointsOperation(int arcFid, Func<IPointCollection> getPointCollection, SelectedSet selectedPoints)
	{
		return new DeleteGraphArcPointsOperation(this, arcFid, getPointCollection, selectedPoints);
	}

	public override void Select(Extent ext, SelectMode mode)
	{
		GraphRenderLayer.Select(Graph, ArcSelectedSet, NodeSelectedSet, ext, SelectionOptions.Get(LassoSelectionHandler.Options.HitMode), mode, SelectionOptions.Get(GraphSelectionOptions.Scope));
	}

	public override void Select(Polyline line, SelectMode mode)
	{
		GraphRenderLayer.Select(Graph, ArcSelectedSet, NodeSelectedSet, line, mode, SelectionOptions.Get(GraphSelectionOptions.Scope));
	}

	public override void Select(Polygon lasso, MarqueeHitMode hitMode, SelectMode mode)
	{
		GraphRenderLayer.Select(Graph, ArcSelectedSet, NodeSelectedSet, lasso, hitMode, mode, SelectionOptions.Get(GraphSelectionOptions.Scope));
	}

	public IDisposable BeginBatchUpdate()
	{
		BatchUpdateStarted?.Invoke();
		return new BatchUpdateScope(this);
	}
}
