using Geospatial.Vectors;

namespace Geospatial.Rasters;

public interface ISurfaceCoverage
{
	bool IsIndependent { get; set; }

	bool? FullyCovers(Extent ext);
}
