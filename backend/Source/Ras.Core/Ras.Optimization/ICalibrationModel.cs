using System.Collections.Generic;
using System.Threading.Tasks;
using Ras.Engine;
using Utility.Progress;

namespace Ras.Optimization;

public interface ICalibrationModel
{
	IReadOnlyDictionary<string, float> BaselineManning { get; }

	TurbulenceParams BaselineTurbulence { get; }

	CalibrationContext CreateContext();

	Task<ModelRunResult> RunAsync(CalibrationContext ctx, int runId, RunArtifactOptions artifacts, int coresPerRun, ProgressReporter pr);
}
