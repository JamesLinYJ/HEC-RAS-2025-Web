using Geospatial.IO.Rasters;
using Geospatial.Rasters;
using Geospatial.Vectors;

namespace Geospatial.IO;

public class BandedRasterWriterConfig
{
	public MultiBandRasterStorageConfiguration StorageConfiguration;

	public bool CreateOverviews;

	public Extent? SourceClip;

	public RasterDefinition? OverrideExportRaster;

	public static BandedRasterWriterConfig Default()
	{
		BandedRasterWriterConfig bandedRasterWriterConfig = new BandedRasterWriterConfig();
		bandedRasterWriterConfig.InitDefault();
		return bandedRasterWriterConfig;
	}

	protected virtual void InitDefault()
	{
		StorageConfiguration = MultiBandRasterStorageConfiguration.Tiled;
		SourceClip = null;
		OverrideExportRaster = null;
	}
}
