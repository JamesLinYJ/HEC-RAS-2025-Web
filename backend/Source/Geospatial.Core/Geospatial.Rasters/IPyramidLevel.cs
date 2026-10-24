namespace Geospatial.Rasters;

public interface IPyramidLevel
{
	int PyramidCount { get; }

	float PyramidSelectionRatio { get; set; }
}
