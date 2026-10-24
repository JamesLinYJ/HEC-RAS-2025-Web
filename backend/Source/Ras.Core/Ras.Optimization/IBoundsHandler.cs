namespace Ras.Optimization;

public interface IBoundsHandler
{
	int Dimension { get; }

	double[] PhysicalMin { get; }

	double[] PhysicalMax { get; }

	ParameterScale[] Scales { get; }

	double[] ToSearch(double[] physical);

	double[] ToPhysical(double[] search);

	double[] SearchScale();
}
