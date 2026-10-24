namespace RasMapperLib.Terrain;

public struct RasterRow
{
	public int Index;

	public float Fraction;

	public RasterRow(int index, float fraction)
	{
		this = default;
		Index = index;
		Fraction = fraction;
	}
}
