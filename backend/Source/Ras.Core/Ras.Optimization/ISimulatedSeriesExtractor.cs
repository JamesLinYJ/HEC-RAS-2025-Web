using Ras.Layers;
using Utility.Progress;

namespace Ras.Optimization;

public interface ISimulatedSeriesExtractor
{
	bool TryExtractSeries(Gauge gauge, Result result, out double[] timesSeconds, out double[] values, ProgressReporter pr = null);
}
