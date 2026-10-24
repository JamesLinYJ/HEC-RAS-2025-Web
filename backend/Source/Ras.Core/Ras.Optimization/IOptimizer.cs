using System;
using System.Threading.Tasks;
using Utility.Progress;

namespace Ras.Optimization;

public interface IOptimizer
{
	string Name { get; }

	Task<OptimizationOutcome> MinimizeAsync(IBoundsHandler bounds, double[] initialPhysical, Func<double[][], Task<double[]>> evaluateBatchAsync, StoppingCriteria stopping, ProgressReporter pr = null);
}
