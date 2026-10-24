using Geospatial.Vectors.Topology;
using Utility.Logging;

namespace Visual.Mapping.Observables;

public class ObservableConceptualMeshTopology : ObservableTopology
{
	private ObservableFeatureSet<ObservableConceptualMeshArc> _arcSet;

	private ObservableFeatureSet<ObservableConceptualMeshControlPoint> _controlPointSet;

	public override ObservableFeatureSet<ObservableConceptualMeshArc> Arcs => _arcSet ?? (_arcSet = new ObservableFeatureSet<ObservableConceptualMeshArc>(() => Topology.Arcs.Count, (int fid) => new ObservableConceptualMeshArc(this, fid)));

	public override ObservableFeatureSet<ObservableConceptualMeshControlPoint> ControlPoints => _controlPointSet ?? (_controlPointSet = new ObservableFeatureSet<ObservableConceptualMeshControlPoint>(() => Topology.RegionControlPoints.Count, (int fid) => new ObservableConceptualMeshControlPoint(this, fid)));

	public ObservableConceptualMeshTopology(Topology topology)
		: base(topology)
	{
	}

	public override OperationResult Complete(TopologyBuilderParams builderParams = null)
	{
		return LatestBuildResult = Topology.CompleteAsConceptualMesh(builderParams);
	}

	public override void CloneIntoBackingTopology(Topology source, Topology destination)
	{
		source.CloneWithMeshGenerationHooks(destination);
	}

	public override bool IsReservedColumn(string columnName)
	{
		return MeshExtensions.IsWellKnownColumn(columnName);
	}
}
