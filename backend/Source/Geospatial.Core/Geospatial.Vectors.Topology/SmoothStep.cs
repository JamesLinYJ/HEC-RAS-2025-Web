using System.Collections.Generic;
using Geospatial.Vectors.MeshTopology;
using Utility.Progress;

namespace Geospatial.Vectors.Topology;

public class SmoothStep : TopologyStep
{
	public int count = 10;

	public bool scaled = true;

	public bool useArea = true;

	public bool sliding = true;

	public override bool Apply(SlidingMesh mesh, MeshRegionMetadata md, ISizeFunction sizeFunction, ProgressReporter reporter = null)
	{
		if (scaled)
		{
			MeshScaledSmoother meshScaledSmoother = new MeshScaledSmoother(mesh, ActiveNodes(mesh));
			meshScaledSmoother.weightByArea = useArea;
			meshScaledSmoother.Optimize(count, reporter);
		}
		else
		{
			new MeshSmoother(mesh, ActiveNodes(mesh)).Optimize(count, reporter);
		}
		return true;
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
