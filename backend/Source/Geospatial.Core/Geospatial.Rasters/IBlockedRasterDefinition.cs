namespace Geospatial.Rasters;

public interface IBlockedRasterDefinition : IRasterDefinition
{
	BlockSize BlockSize { get; }
}
