using Utility.Progress;

namespace Geospatial.Vectors.MeshTopology;

public abstract class MeshPostprocess
{
	public SlidingMesh mesh;

	public MeshPostprocess(SlidingMesh mesh)
	{
		this.mesh = mesh;
	}

	public virtual void Apply(ProgressReporter reporter = null)
	{
	}
}
