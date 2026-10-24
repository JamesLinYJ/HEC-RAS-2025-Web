namespace RasMapperLib.Terrain;

public struct RasterColumn
{
	public int Index;

	public float Fraction;

	public RasterColumn(int index, float fraction)
	{
		this = default;
		Index = index;
		Fraction = fraction;
	}
}
