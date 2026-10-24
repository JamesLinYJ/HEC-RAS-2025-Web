namespace Ras.Synthetics.Analysis;

public readonly struct EnvelopeSeries(double[] time, double[] amplitude, double[] phase)
{
	public double[] Time { get; } = time;

	public double[] Amplitude { get; } = amplitude;

	public double[] Phase { get; } = phase;
}
