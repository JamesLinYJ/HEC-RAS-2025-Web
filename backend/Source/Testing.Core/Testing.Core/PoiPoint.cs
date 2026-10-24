namespace Testing.Core;

public readonly struct PoiPoint(double x, double y, string name)
{
	public double X { get; } = x;

	public double Y { get; } = y;

	public string Name { get; } = name ?? "";
}
