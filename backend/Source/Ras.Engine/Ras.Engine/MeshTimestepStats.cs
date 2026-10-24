namespace Ras.Engine;

public class MeshTimestepStats
{
	public double Time { get; init; }

	public double TimeStep { get; init; }

	public double MaxTimeStep { get; init; }

	public long Step { get; init; }

	public int WetCellCount { get; init; }

	public int Retries { get; init; }

	public int Iterations { get; init; }

	public int LimitingCell { get; init; }

	public int LimitingFace { get; init; }

	public float ActiveCells { get; set; }

	public int AdvectionLimitCount { get; init; }

	public int UserLimitCount { get; init; }

	public int CelerityLimitCount { get; init; }

	public int IncreaseLimitCount { get; init; }

	public int RetryElevationCount { get; init; }

	public int RetryNanCount { get; init; }

	public int RetryNegVolumeCount { get; init; }
}
