namespace Geospatial.IO.TileMapService;

public struct GoogleCacheTile
{
	public string Tablename;

	public string TimeName;

	public int ZoomLevel;

	public int Column;

	public int Row;

	public byte[] ImageData;
}
