using Geospatial.Rasters;
using Geospatial.Vectors;

namespace Geospatial.Core.Rasters;

public class FlatAreaDetectorOptions
{
	public const double DefaultMaxSlope = 0.05;

	public double CellSize { get; set; } = 10.0;

	public double MaxSlope { get; set; } = 0.05;

	public InterpolationMode SampleInterpolation { get; set; } = InterpolationMode.Linear;

	public RasterCellNeighborSet Connectivity { get; set; } = RasterCellNeighborSet.EightCorners;

	public double SimplifyDistance { get; set; }

	public double MinimumRingArea { get; set; }

	public Extent ClipExtent { get; set; } = Extent.NullExtent;

	public bool ParallelFill { get; set; } = true;

	public bool ContourFilledBoundsOnly { get; set; } = true;

	public FlatAreaContourMode ContourMode { get; set; }

	public int ContourTileCells { get; set; }
}
