namespace Geospatial.Hydraulics;

public sealed class FaceProfileOptions
{
	public int SampledPointsPerFace { get; set; } = 100;

	public NSampling NSampling { get; set; }

	public float DefaultManning { get; set; } = 0.03f;

	public double StationEpsilon { get; set; } = 0.0001;
}
