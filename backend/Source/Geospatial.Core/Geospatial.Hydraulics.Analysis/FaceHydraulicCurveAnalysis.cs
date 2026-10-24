namespace Geospatial.Hydraulics.Analysis;

public sealed class FaceHydraulicCurveAnalysis
{
	public FaceHydraulicCurveStatus Status { get; init; }

	public string Detail { get; init; } = "";

	public FaceGeometry Geometry { get; init; }

	public LDMResult Curve { get; init; }

	public int GeneratedStageCount { get; init; }

	public int AvoidedStageCount { get; init; }

	public float MaximumBedElevation { get; init; }

	public int MaximumBedIndex { get; init; } = -1;

	public double ProfileMilliseconds { get; init; }

	public double StageGenerationMilliseconds { get; init; }

	public double SolveMilliseconds { get; init; }

	public bool Succeeded => Status == FaceHydraulicCurveStatus.Solved;
}
