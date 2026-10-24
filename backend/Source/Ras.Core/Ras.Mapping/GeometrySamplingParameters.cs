using Utility;

namespace Ras.Mapping;

public class GeometrySamplingParameters
{
	public static readonly GeometrySamplingParameters Default = new GeometrySamplingParameters();

	public static readonly GeometrySamplingParameters ForceHorizontalMode = new GeometrySamplingParameters
	{
		ForceHorizontalRendering = true
	};

	public static readonly GeometrySamplingParameters StoringToDisk = new GeometrySamplingParameters
	{
		CellPixelRenderingCutoff = 0f
	};

	public ParallelizationIntent ParallelizationIntent { get; init; } = ParallelizationIntent.Parallel;

	public int ParallelizeAcrossCellsIfMoreThan { get; init; } = 250;

	public int ParallelizePixelsWithinCellsIfMoreThan { get; init; } = 1000;

	public float CellPixelRenderingCutoff { get; init; } = 5f;

	public bool ForceHorizontalRendering { get; init; }
}
