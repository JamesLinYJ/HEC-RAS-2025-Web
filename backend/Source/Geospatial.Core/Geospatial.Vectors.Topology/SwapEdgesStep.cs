using Geospatial.Vectors.MeshTopology;
using Utility.Progress;

namespace Geospatial.Vectors.Topology;

public class SwapEdgesStep : TopologyStep
{
	public int threshold = -1;

	public bool shorten;

	public bool delaunay = true;

	public override bool Apply(SlidingMesh mesh, MeshRegionMetadata md, ISizeFunction sizeFunction, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		PostFaceFlip postFaceFlip = new PostFaceFlip(mesh);
		postFaceFlip.degreeThreshold = threshold;
		postFaceFlip.useFaceLength = shorten;
		postFaceFlip.useDelaunay = delaunay;
		postFaceFlip.Apply(reporter);
		return true;
	}
}
