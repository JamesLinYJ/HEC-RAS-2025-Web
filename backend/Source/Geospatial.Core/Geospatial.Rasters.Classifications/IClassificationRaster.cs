namespace Geospatial.Rasters.Classifications;

public interface IClassificationRaster : IClassificationResampler, IResample<int>, ISurface<int>, IRaster<int>, IRasterDefinition
{
}
