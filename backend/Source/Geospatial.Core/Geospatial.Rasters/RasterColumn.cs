namespace Geospatial.Rasters;

public struct RasterColumn(int index, float fraction)
{
	public int Index = index;

	public float Fraction = fraction;
}
