using Geospatial.Vectors.Topology;
using Ras.Observables;
using Utility.Logging;

namespace Visual.Mapping.Observables;

public class ObservableChannelModTopology : ObservableTopology
{
	private ObservableFeatureSet<ObservableArc> _arcSet;

	private ObservableFeatureSet<ObservableChannelModControlPoint> _controlPointSet;

	public ObservableChannelModTerrainInput TerrainInput { get; private set; }

	public override ObservableFeatureSet<ObservableArc> Arcs => _arcSet ?? (_arcSet = new ObservableFeatureSet<ObservableArc>(() => Topology.Arcs.Count, (int fid) => new ObservableArc(this, fid)));

	public override ObservableFeatureSet<ObservableChannelModControlPoint> ControlPoints => _controlPointSet ?? (_controlPointSet = new ObservableFeatureSet<ObservableChannelModControlPoint>(() => Topology.RegionControlPoints.Count, (int fid) => new ObservableChannelModControlPoint(this, fid)));

	public ObservableChannelModTopology(ObservableChannelModTerrainInput terrainInput, Topology topology)
		: base(topology)
	{
		TerrainInput = terrainInput;
	}

	public override OperationResult Complete(TopologyBuilderParams builderParams = null)
	{
		OperationResult operationResult = Topology.Complete(builderParams);
		TerrainInput.Layer.Prepare(operationResult);
		LatestBuildResult = operationResult;
		return operationResult;
	}

	public override void CloneIntoBackingTopology(Topology source, Topology destination)
	{
		source.Clone(destination);
	}

	public override bool IsReservedColumn(string columnName)
	{
		return MeshExtensions.IsWellKnownColumn(columnName);
	}
}
