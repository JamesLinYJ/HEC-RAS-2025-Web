using Geospatial.Vectors.MeshTopology;
using Utility.Progress;

namespace Geospatial.Vectors.Topology;

public class SmoothAnglesStep : TopologyStep
{
	public int count = 10;

	public override bool Apply(SlidingMesh mesh, MeshRegionMetadata md, ISizeFunction sizeFunction, ProgressReporter reporter = null)
	{
		new MeshAngleSmoother(mesh, mesh.MovableNodes()).Optimize(count, reporter);
		return true;
	}
}
