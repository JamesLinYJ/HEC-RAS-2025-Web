using Geospatial.Vectors.MeshTopology;
using Utility.Progress;

namespace Geospatial.Vectors.Topology;

public class RemoveNodesStep : TopologyStep
{
	public bool perimeter = true;

	public double threshold = 0.5;

	public double deltaCost = 0.05;

	public double skew = 2.0;

	public double cellArea;

	public double faceSmooth = 30.0;

	public double faceLength = 0.1;

	public int count = -1;

	public override bool Apply(SlidingMesh mesh, MeshRegionMetadata md, ISizeFunction sizeFunction, ProgressReporter reporter = null)
	{
		return new PostMergeFaces(mesh, sizeFunction)
		{
			enableMerge = false,
			enableCollapse = true,
			enablePerimeter = perimeter,
			deltaCostThreshold = deltaCost,
			minCostThreshold = threshold,
			sizeFunction = sizeFunction,
			skewFactor = skew,
			cellScaleFactor = cellArea,
			faceSmoothFactor = faceSmooth,
			faceScaleFactor = faceLength
		}.OptimizeAdaptive((count < 1) ? mesh.FaceCount : count, reporter);
	}
}
