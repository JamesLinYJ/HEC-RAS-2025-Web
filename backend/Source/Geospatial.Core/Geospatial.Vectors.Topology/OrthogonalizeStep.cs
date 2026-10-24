using System.Collections.Generic;
using Geospatial.Vectors.MeshTopology;
using Utility.Progress;

namespace Geospatial.Vectors.Topology;

public class OrthogonalizeStep : TopologyStep
{
	public int method = 1;

	public double scaler = 1.0;

	public double alpha = 0.8;

	public int count = 10;

	public bool sliding = true;

	public bool useArea = true;

	public int smoothmethod = 1;

	public bool nudgeTriangles = true;

	public override bool Apply(SlidingMesh mesh, MeshRegionMetadata md, ISizeFunction sizeFunction, ProgressReporter reporter = null)
	{
		return new MeshOrthogonalizer(mesh, ActiveNodes(mesh))
		{
			method = method,
			scaler = scaler,
			alpha = alpha,
			sliding = sliding,
			useArea = useArea,
			smoothmethod = smoothmethod,
			nudgeTriangles = nudgeTriangles
		}.Optimize(count, reporter);
	}

	private IList<int> ActiveNodes(SlidingMesh mesh)
	{
		if (sliding)
		{
			return mesh.MovableNodes();
		}
		return mesh.InternalNodes();
	}
}
