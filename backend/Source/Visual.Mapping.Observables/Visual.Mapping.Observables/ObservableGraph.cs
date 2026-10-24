using Geospatial.Vectors.Graph;
using Geospatial.Vectors.Topology;
using Utility.Logging;

namespace Visual.Mapping.Observables;

public class ObservableGraph : ObservableBaseTopology
{
	private ObservableFeatureSet<ObservableGraphArc> _arcSet;

	private int _batchDepth;

	private bool _nodesDirty;

	private bool _arcsDirty;

	public Graph Graph => base.Topology as Graph;

	public override ObservableFeatureSet<ObservableGraphArc> Arcs => _arcSet ?? (_arcSet = new ObservableFeatureSet<ObservableGraphArc>(() => Graph.Arcs.Count, (int fid) => new ObservableGraphArc(this, fid)));

	public ObservableGraph(Graph graph)
		: base(graph)
	{
	}

	public override OperationResult Complete(TopologyBuilderParams builderParams = null)
	{
		return LatestBuildResult;
	}

	public void BeginBatchUpdate()
	{
		_batchDepth++;
	}

	public void EndBatchUpdate()
	{
		if (_batchDepth == 0)
		{
			return;
		}
		_batchDepth--;
		if (_batchDepth <= 0)
		{
			if (_nodesDirty)
			{
				_nodesDirty = false;
				Nodes.RebuildInPlace();
			}
			if (_arcsDirty)
			{
				_arcsDirty = false;
				Arcs.RebuildInPlace();
			}
		}
	}

	public void AlertNodesChanged()
	{
		if (Nodes.IsLoaded)
		{
			if (_batchDepth > 0)
			{
				_nodesDirty = true;
			}
			else
			{
				Nodes.RebuildInPlace();
			}
		}
	}

	public void AlertArcsChanged()
	{
		if (Arcs.IsLoaded)
		{
			if (_batchDepth > 0)
			{
				_arcsDirty = true;
			}
			else
			{
				Arcs.RebuildInPlace();
			}
		}
	}
}
