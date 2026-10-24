using Geospatial.Vectors.MeshTopology;
using Utility.Progress;

namespace Geospatial.Vectors.Topology;

public class RemoveFacesStep : TopologyStep
{
	public double threshold = 0.1;

	public override bool Apply(SlidingMesh mesh, MeshRegionMetadata md, ISizeFunction sizeFunction, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		new PostMergeTriangles(mesh, threshold).Apply(reporter);
		return true;
	}
}
