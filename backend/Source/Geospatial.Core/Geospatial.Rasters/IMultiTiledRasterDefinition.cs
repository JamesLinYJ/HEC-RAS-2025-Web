namespace Geospatial.Rasters;

public interface IMultiTiledRasterDefinition
{
	int Count { get; }

	ITiledRasterDefinition GetTileDefinition(int idx);
}
