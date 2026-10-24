using Geospatial.Vectors;
using Geospatial.Vectors.Rasterizers;
using Utility.Extensions;

namespace Ras.Mapping;

public static class MetadataParser
{
	public static MeshElementType ParseLocation(string stringValue)
	{
		if (stringValue.RoughlyEquals("cell") || stringValue.RoughlyEquals("cells"))
		{
			return MeshElementType.Cell;
		}
		if (stringValue.RoughlyEquals("face") || stringValue.RoughlyEquals("faces"))
		{
			return MeshElementType.Face;
		}
		return MeshElementType.Unknown;
	}

	public static Coverage ParseCoverage(string stringValue)
	{
		if (stringValue.RoughlyEquals("average"))
		{
			return Coverage.Average;
		}
		if (stringValue.RoughlyEquals("wet"))
		{
			return Coverage.Wet;
		}
		if (stringValue.RoughlyEquals("dry"))
		{
			return Coverage.Dry;
		}
		return Coverage.Unknown;
	}

	public static Orientation ParseOrientation(string stringValue)
	{
		if (stringValue.RoughlyEquals("scalar"))
		{
			return Orientation.Scalar;
		}
		if (stringValue.RoughlyEquals("normal"))
		{
			return Orientation.Normal;
		}
		return Orientation.Unknown;
	}
}
