namespace Geospatial.Vectors.MeshTopology;

public struct SizeParams(double target, double bias, int region)
{
	public double target = target;

	public double stretchRate = bias;

	public int region = region;
}
