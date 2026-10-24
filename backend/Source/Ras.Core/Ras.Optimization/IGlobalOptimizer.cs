namespace Ras.Optimization;

public interface IGlobalOptimizer : IOptimizer
{
	void Seed(double[][] physicalPoints, double[] objectives);
}
