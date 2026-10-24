namespace Ras.Hydraulics.CrossSections;

public class FlowDistribution
{
	public float[] Stations { get; set; }

	public float[] Elevations { get; set; }

	public float[,] FlowNormalizedShearFactor { get; set; }

	public float[,] FlowNormalizedVelocityFactor { get; set; }
}
