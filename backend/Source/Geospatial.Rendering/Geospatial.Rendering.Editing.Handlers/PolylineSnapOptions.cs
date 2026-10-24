namespace Geospatial.Rendering.Editing.Handlers;

public readonly struct PolylineSnapOptions
{
	public double SearchRadius { get; init; }

	public double SampleSpacing { get; init; }

	public PolylineSnapSearchMode SearchMode { get; init; }

	public PolylineSnapElevationMode ElevationMode { get; init; }

	public int PointsPerRing { get; init; }

	public int Iterations { get; init; }

	public static PolylineSnapOptions Defaults => new PolylineSnapOptions
	{
		SearchRadius = 50.0,
		SampleSpacing = 3.0,
		SearchMode = PolylineSnapSearchMode.Perpendicular,
		ElevationMode = PolylineSnapElevationMode.Highest,
		PointsPerRing = 16,
		Iterations = 1
	};
}
