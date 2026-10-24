using System.Collections.Generic;
using System.Linq;
using Utility.Progress;

namespace Geospatial.Vectors.MeshTopology;

public class PostMergeTriangles : MeshPostprocess
{
	public double circumcenterThreshold = 0.15;

	public PostMergeTriangles(SlidingMesh mesh)
		: base(mesh)
	{
	}

	public PostMergeTriangles(SlidingMesh mesh, double circumcenterThreshold)
		: base(mesh)
	{
		this.circumcenterThreshold = circumcenterThreshold;
	}

	public override void Apply(ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		List<int> faces = mesh.UnconstrainedFaces().ToList();
		mesh.MergeTriangles(faces, checkCenters: false, circumcenterThreshold, 0.087, reporter);
	}
}
