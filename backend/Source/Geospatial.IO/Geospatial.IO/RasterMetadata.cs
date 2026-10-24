namespace Geospatial.IO;

public struct RasterMetadata
{
	public static readonly RasterMetadata Empty;

	public string Projection { get; set; }

	public string Units { get; set; }

	public string DataType { get; set; }
}
