using Geospatial.GDALAssist;
using Geospatial.Rasters;
using Geospatial.Vectors;

namespace Geospatial.IO;

public class RasterFileConverterConfig
{
	public const string NativeDriver = "Native";

	public const string GDALDriver = "GDAL";

	public string SuggestedDriver;

	public bool OverwriteDestinationFile;

	public Extent? SourceClip;

	public RasterDefinition? OverrideExportRaster;

	public Projection OverrideSourceProjection;

	public Projection DestinationProjection;

	public bool GenerateOverviews;

	public static RasterFileConverterConfig Default()
	{
		return new RasterFileConverterConfig
		{
			GenerateOverviews = true
		};
	}

	protected RasterFileConverterConfig()
	{
	}
}
