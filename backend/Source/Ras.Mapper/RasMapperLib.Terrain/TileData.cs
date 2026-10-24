namespace RasMapperLib.Terrain;

public class TileData
{
	public float[] tileValues;

	public float[] perimeterValues;

	public byte[] mask;

	public long TimeLastUsed;

	public TileData()
	{
		tileValues = null;
		perimeterValues = null;
		mask = null;
	}
}
