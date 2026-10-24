namespace Ras.Optimization;

public interface IObjectiveMetric
{
	string Name { get; }

	double Evaluate(double[] timesSeconds, double[] observed, double[] simulated);
}
