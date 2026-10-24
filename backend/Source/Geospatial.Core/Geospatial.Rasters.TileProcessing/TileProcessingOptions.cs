using Geospatial.Vectors;
using Utility;

namespace Geospatial.Rasters.TileProcessing;

public sealed class TileProcessingOptions
{
	public Extent? Region { get; set; }

	public int NeighborhoodRadius { get; set; }

	public int MaxResidentTiles { get; set; } = 1024;

	public ITileStateBackingStore BackingStore { get; set; }

	public int MaxResolvePasses { get; set; } = 32;

	public ParallelizationIntent AccumulateIntent { get; set; }

	public int MaxPixelTilesInMemory { get; set; } = 256;
}
