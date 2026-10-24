using Geospatial.Vectors.MeshTopology;
using Utility.Progress;

namespace Geospatial.Vectors.Topology;

public class OptSkewStep : TopologyStep
{
	public int count = 2;

	public override bool Apply(SlidingMesh mesh, MeshRegionMetadata md, ISizeFunction sizeFunction, ProgressReporter reporter = null)
	{
		new OptimizeNodeSkew(mesh, mesh.MovableNodes()).Optimize(count, reporter);
		return true;
	}
}
