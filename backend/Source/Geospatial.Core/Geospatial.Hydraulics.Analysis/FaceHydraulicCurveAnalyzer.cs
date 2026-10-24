using System;
using System.Diagnostics;
using Geospatial.Rasters;
using Geospatial.Vectors;

namespace Geospatial.Hydraulics.Analysis;

public static class FaceHydraulicCurveAnalyzer
{
	public static FaceHydraulicCurveAnalysis Analyze(Mesh mesh, IResample<float> elevations, IResample<float> nValues, int faceIndex, FaceProfileOptions profileOptions, LDMControl control, LDMSolver solver)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		bool tryBuildFromFace = FaceProfileExtractor.TryBuildFromFace(mesh, elevations, nValues, faceIndex, profileOptions, out var geom);
		stopwatch.Stop();
		double totalMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
		if (!tryBuildFromFace)
		{
			return new FaceHydraulicCurveAnalysis
			{
				Status = FaceHydraulicCurveStatus.ProfileExtractionFailed,
				Detail = "Terrain profile could not be extracted.",
				ProfileMilliseconds = totalMilliseconds
			};
		}
		if (geom.CellCount > solver.MaximumCellCount)
		{
			return new FaceHydraulicCurveAnalysis
			{
				Status = FaceHydraulicCurveStatus.CellLimitExceeded,
				Detail = $"{geom.CellCount} refined cells exceed the LDM limit of {solver.MaximumCellCount}.",
				Geometry = geom,
				ProfileMilliseconds = totalMilliseconds
			};
		}
		stopwatch.Restart();
		float[] generateAdaptive = CurveStageGenerator.GenerateAdaptive(geom, control.WseIncrement, control.MaximumWseCount);
		stopwatch.Stop();
		double totalMilliseconds2 = stopwatch.Elapsed.TotalMilliseconds;
		stopwatch.Restart();
		LDMResult lDMResult = solver.SolveAdaptiveGeneratedStages(geom, control, generateAdaptive);
		stopwatch.Stop();
		float faceBedElevation = geom.FaceBedElevations[0];
		for (int i = 1; i <= geom.CellCount; i++)
		{
			faceBedElevation = MathF.Max(faceBedElevation, geom.FaceBedElevations[i]);
		}
		int maximumBedIndex = Array.IndexOf(lDMResult.Wse, faceBedElevation, 0, lDMResult.SolvedWseCount);
		string detail = Validate(lDMResult, maximumBedIndex, lDMResult.HasBeta);
		return new FaceHydraulicCurveAnalysis
		{
			Status = ((detail.Length != 0) ? FaceHydraulicCurveStatus.InvalidCurve : FaceHydraulicCurveStatus.Solved),
			Detail = detail,
			Geometry = geom,
			Curve = lDMResult,
			GeneratedStageCount = generateAdaptive.Length,
			AvoidedStageCount = Math.Max(0, generateAdaptive.Length - lDMResult.SolvedWseCount),
			MaximumBedElevation = faceBedElevation,
			MaximumBedIndex = maximumBedIndex,
			ProfileMilliseconds = totalMilliseconds,
			StageGenerationMilliseconds = totalMilliseconds2,
			SolveMilliseconds = stopwatch.Elapsed.TotalMilliseconds
		};
	}

	private static string Validate(LDMResult curve, int maximumBedIndex, bool validateBeta)
	{
		if (curve.SolvedWseCount < 2)
		{
			return "LDM generated fewer than two source rows.";
		}
		if (maximumBedIndex < 0)
		{
			return "Adaptive stages did not preserve the maximum bed elevation.";
		}
		for (int i = 0; i < curve.SolvedWseCount; i++)
		{
			if (!float.IsFinite(curve.Wse[i]) || (i > 0 && curve.Wse[i] <= curve.Wse[i - 1]))
			{
				return "LDM elevations are nonfinite or not strictly increasing.";
			}
			if (!float.IsFinite(curve.Conveyance[i]) || !float.IsFinite(curve.Area[i]) || (validateBeta && curve.Area[i] != 0f && (!float.IsFinite(curve.Beta[i]) || curve.Beta[i] <= 0f)))
			{
				return $"LDM hydraulic values are invalid at stage {curve.Wse[i]:G9}.";
			}
		}
		return "";
	}
}
