namespace Geospatial.Vectors.MeshTopology;

public class FunctionMinimizer
{
	public enum StopCondition
	{
		StopError,
		StopXTolerance,
		StopFTolerance,
		StopMaxIterations
	}

	public StopCondition stopCondition { get; protected set; }
}
