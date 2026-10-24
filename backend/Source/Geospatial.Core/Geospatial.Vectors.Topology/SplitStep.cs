using Geospatial.Vectors.MeshTopology;
using Utility.Progress;

namespace Geospatial.Vectors.Topology;

public class SplitStep : TopologyStep
{
	public double threshold = 0.5;

	public override bool Apply(SlidingMesh mesh, MeshRegionMetadata md, ISizeFunction sizeFunction, ProgressReporter reporter = null)
	{
		new PostSplitQuads(mesh, 1.0 - threshold).Apply(reporter);
		return true;
	}
}
