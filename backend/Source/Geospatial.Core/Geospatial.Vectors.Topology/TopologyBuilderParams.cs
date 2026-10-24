namespace Geospatial.Vectors.Topology;

public class TopologyBuilderParams
{
	public static bool DefaultAllowPartialRecomplete = true;

	public bool AllowFloatingArcs { get; set; }

	public bool AllowFloatingNodes { get; set; }

	public bool AllowMultipleControlPointsPerRegion { get; set; }

	public bool AllowZeroControlPointsInRegion { get; set; }

	public bool AllowArcToStartAndEndOnSameNode { get; set; }

	public bool MustBeEnclosed { get; set; }

	public bool AllowPartialRecomplete { get; set; }

	public bool DifferentialValidatePartialRecomplete { get; set; }

	public static TopologyBuilderParams Default()
	{
		return new TopologyBuilderParams
		{
			AllowFloatingArcs = true,
			AllowFloatingNodes = false,
			AllowMultipleControlPointsPerRegion = false,
			AllowZeroControlPointsInRegion = true,
			AllowArcToStartAndEndOnSameNode = true,
			MustBeEnclosed = true,
			AllowPartialRecomplete = DefaultAllowPartialRecomplete
		};
	}
}
