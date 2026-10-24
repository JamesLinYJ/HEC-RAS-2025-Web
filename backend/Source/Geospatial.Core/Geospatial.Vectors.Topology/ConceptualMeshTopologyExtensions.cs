using Utility.Logging;

namespace Geospatial.Vectors.Topology;

public static class ConceptualMeshTopologyExtensions
{
	public static class MessageCodes
	{
		public static readonly Topology.TopologyMessageClass QuadPatchLessThanFourArcs = new Topology.TopologyMessageClass("ConceptualMeshTopology_QuadPatchLessThanFourArcs", MessageSeverity.Error, Topology.RegionCompletionSeverity.Ineffectual, "Region {ControlPointIndex} (Internal FID {RegionIndex}) is a quad patch, but does not have enough arcs. It needs at least 4.");

		public static Message GetQuadPatchLessThanFourArcs(int ctrlPtIdx, int rgnIdx)
		{
			return QuadPatchLessThanFourArcs.Create(ctrlPtIdx, rgnIdx);
		}
	}

	public static OperationResult CompleteAsConceptualMesh(this Topology topology, TopologyBuilderParams buildParams = null, TopologyBuildDiagnostics diagnostics = null)
	{
		OperationResult operationResult = topology.Complete(buildParams, diagnostics);
		if (!operationResult.Result)
		{
			return operationResult;
		}
		topology.FreezeOne();
		for (int i = 0; i < topology.RegionControlPoints.Count; i++)
		{
			if (topology.TryGetControlPointContainingRegion(i, out var regionIndex) && topology.GetRegionData(regionIndex).PatchType == PatchType.Quad && topology.CompletedRegions[regionIndex].OuterRing.Arcs.Count < 4)
			{
				operationResult.AddMessage(MessageCodes.QuadPatchLessThanFourArcs.Create(i, regionIndex));
				operationResult.Result = false;
			}
		}
		topology.ThawOne();
		return operationResult;
	}
}
