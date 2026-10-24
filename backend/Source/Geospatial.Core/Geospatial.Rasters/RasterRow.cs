namespace Geospatial.Rasters;

public struct RasterRow(int index, float fraction)
{
	public int Index = index;

	public float Fraction = fraction;
}
