using Geospatial.Vectors.MeshTopology;
using Utility.Progress;

namespace Geospatial.Vectors.Topology;

public class DistortStep : TopologyStep
{
	public int count = 1;

	public double jitter = 0.25;

	public double nudgeConvex = -1.0;

	public override bool Apply(SlidingMesh mesh, MeshRegionMetadata md, ISizeFunction sizeFunction, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		MeshUnsmoother meshUnsmoother = new MeshUnsmoother(mesh, mesh.InternalNodes());
		meshUnsmoother.jitter = jitter;
		meshUnsmoother.nudgeConvex = nudgeConvex;
		meshUnsmoother.Optimize(count, reporter);
		return true;
	}
}
