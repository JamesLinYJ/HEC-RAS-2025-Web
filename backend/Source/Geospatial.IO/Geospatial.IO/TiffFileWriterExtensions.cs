using Geospatial.Rasters;
using Geospatial.Vectors;
using TiffBinaryReader;

namespace Geospatial.IO;

public static class TiffFileWriterExtensions
{
	public static void SetLocation(this TagMetadata file, RasterDefinition rdef)
	{
		file.SetLocation(rdef.Left, rdef.Top, rdef.CellSize);
	}

	public static void SetLocation(this TagMetadata file, Point upperLeft, double cellSize)
	{
		file.SetLocation(upperLeft.X, upperLeft.Y, cellSize);
	}
}
