using System.Collections.Generic;
using Utility.Progress;

namespace Geospatial.Vectors.MeshTopology;

public abstract class MeshOptimizer
{
	public SlidingMesh mesh;

	public IList<int> nodes;

	public int CurrentIteration { get; protected set; }

	public MeshOptimizer(SlidingMesh mesh, IList<int> nodes = null)
	{
		this.mesh = mesh;
		this.nodes = nodes;
	}

	public bool Optimize(int iterations, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		float fractionOfTotal = 1f / (float)iterations;
		bool result = false;
		for (int i = 0; i < iterations; i++)
		{
			ProgressReporter reporter2 = reporter.WithProgressModifier((int)((float)i / (float)iterations * 100f), fractionOfTotal);
			CurrentIteration = i;
			if (!OptimizeOnce(reporter2))
			{
				break;
			}
			result = true;
			if (reporter.CancelRequested)
			{
				return result;
			}
		}
		reporter.ReportProgress(100);
		return result;
	}

	public abstract bool OptimizeOnce(ProgressReporter reporter = null);

	protected virtual IList<int> DefaultNodes()
	{
		return mesh.MovableNodes();
	}
}
