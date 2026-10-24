using Geospatial.Vectors;

namespace Geospatial.IO;

public class ShapefileHeader
{
	public const int ExpectedLength = 100;

	public const int FileCode = 9994;

	public const int Version = 1000;

	public string Filename { get; set; }

	public int ShpTypeCode { get; set; }

	public ShapeType ShapeType => ShapeTypeHelper.GetShapeType(ShpTypeCode);

	public ShapeOordinates ShapeOordinates => ShapeTypeHelper.GetOordinates(ShpTypeCode);

	public Extent CachedExtent { get; set; } = Extent.NullExtent;

	public long FileLength { get; set; }
}
