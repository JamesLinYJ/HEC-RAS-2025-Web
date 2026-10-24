using Geospatial.Vectors.MeshTopology;
using Utility.Progress;

namespace Geospatial.Vectors.Topology;

public class ForceDisplacementStep : TopologyStep
{
	public int count = 10;

	public bool cull;

	public bool insert;

	public override bool Apply(SlidingMesh mesh, MeshRegionMetadata md, ISizeFunction sizeFunction, ProgressReporter reporter = null)
	{
		return ForceDisplacement.ForceDisplacementOptimizer(mesh, sizeFunction, count, cull, insert);
	}
}
