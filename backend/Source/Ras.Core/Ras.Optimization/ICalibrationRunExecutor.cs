using System.Threading.Tasks;
using Utility.Progress;

namespace Ras.Optimization;

public interface ICalibrationRunExecutor
{
	Task<RunResult> EvaluateAsync(int runId, double[] physical, bool forcePersist, ProgressReporter pr = null);
}
