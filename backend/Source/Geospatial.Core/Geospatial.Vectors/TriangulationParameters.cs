namespace Geospatial.Vectors;

public class TriangulationParameters
{
	public const double DefaultNearPointTolerance = 0.001;

	public double? Tolerance { get; set; }

	public Vector? NormalizedOffset { get; set; }

	public Extent? EnforcedMBR { get; set; }

	public ConstrainedEdgeMode ConstrainedEdgeMode { get; set; } = ConstrainedEdgeMode.UseLastConstraining;
}
