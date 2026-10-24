namespace Ras.Layers.Payloads;

public readonly struct FaceStationSpan(double start, double mid, double end)
{
	public readonly double Start = start;

	public readonly double Mid = mid;

	public readonly double End = end;

	public double Length => End - Start;
}
