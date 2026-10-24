using System;
using System.Threading;
using System.Threading.Tasks;

namespace Geospatial.Hydraulics;

public sealed class SCMSolver
{
	private readonly FaceHydraulics _hydraulics;

	public int MaximumCellCount { get; }

	public SCMSolver(int maximumCellCount)
	{
		if (maximumCellCount < 1)
		{
			throw new ArgumentOutOfRangeException("maximumCellCount", "Cell capacity must be positive.");
		}
		MaximumCellCount = maximumCellCount;
		_hydraulics = new FaceHydraulics(maximumCellCount);
	}

	public SCMResult Solve(FaceGeometry geometry, SCMControl control)
	{
		ValidateInputs(geometry, control, validateStageGeneration: true);
		float[] wseValues = CurveStageGenerator.GenerateAdaptive(geometry, control.WseIncrement, control.MaximumWseCount);
		return SolveCore(geometry, control, wseValues, allowEarlyTermination: true);
	}

	public SCMResult Solve(FaceGeometry geometry, SCMControl control, float[] wseValues)
	{
		ValidateInputs(geometry, control, validateStageGeneration: false);
		ValidateWseValues(wseValues);
		return SolveCore(geometry, control, wseValues, allowEarlyTermination: false);
	}

	public static SCMResult[] SolveAll(FaceGeometry[] geometries, SCMControl control, CancellationToken cancellationToken = default(CancellationToken))
	{
		return SolveAllCore(geometries, control, null, cancellationToken);
	}

	public static SCMResult[] SolveAll(FaceGeometry[] geometries, SCMControl control, float[][] wseValues, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(wseValues, "wseValues");
		return SolveAllCore(geometries, control, wseValues, cancellationToken);
	}

	private SCMResult SolveCore(FaceGeometry geometry, SCMControl control, float[] wseValues, bool allowEarlyTermination)
	{
		SCMResult sCMResult = new SCMResult(geometry, control, wseValues);
		FaceHydraulicInputs inputs = CreateHydraulicInputs(control);
		float maximumBed = MaximumBedElevation(geometry);
		for (int i = 0; i < wseValues.Length; i++)
		{
			_hydraulics.Preprocess(geometry, inputs, wseValues[i]);
			if (_hydraulics.AllDry)
			{
				sCMResult.SetDry(i);
				continue;
			}
			WriteBulkTotals(i, sCMResult);
			ComputeStage(control, i, sCMResult);
			if (TryComputeConveyanceError(geometry, sCMResult, i, maximumBed, out var error))
			{
				sCMResult.FinalConveyanceError = error;
				if (allowEarlyTermination && control.ConveyanceRelativeTolerance != 0f && !(error >= control.ConveyanceRelativeTolerance))
				{
					sCMResult.SolvedWseCount = i + 1;
					return sCMResult;
				}
			}
		}
		return sCMResult;
	}

	private void ComputeStage(SCMControl control, int wseIndex, SCMResult result)
	{
		float computed = 0f;
		float cellPerimSum = 0f;
		for (int i = _hydraulics.minWetCell; i <= _hydraulics.maxWetCell; i++)
		{
			if (_hydraulics.cellWet[i])
			{
				float cellPerim = _hydraulics.cellPerim[i];
				float x = _hydraulics.cellManningComp[i];
				computed += cellPerim * MathF.Pow(x, 1.5f);
				cellPerimSum += cellPerim;
			}
		}
		if (cellPerimSum <= 0f || computed <= 0f)
		{
			result.Conveyance[wseIndex] = 0f;
			return;
		}
		float cellPerimSumTerm = MathF.Pow(computed / cellPerimSum, 2f / 3f);
		float areaOverWettedPerimeter = result.Area[wseIndex] / result.WettedPerimeter[wseIndex];
		result.Conveyance[wseIndex] = control.ManningUnitFactor * result.Area[wseIndex] * MathF.Cbrt(areaOverWettedPerimeter * areaOverWettedPerimeter) / cellPerimSumTerm;
	}

	private void WriteBulkTotals(int wseIndex, SCMResult result)
	{
		float cellAreaSum = 0f;
		float cellPerimSum = 0f;
		float cellWettedWidthSum = 0f;
		for (int i = _hydraulics.minWetCell; i <= _hydraulics.maxWetCell; i++)
		{
			if (_hydraulics.cellWet[i])
			{
				cellAreaSum += _hydraulics.cellArea[i];
				cellPerimSum += _hydraulics.cellPerim[i];
				cellWettedWidthSum += _hydraulics.cellWettedWidth[i];
			}
		}
		result.Area[wseIndex] = cellAreaSum;
		result.WettedPerimeter[wseIndex] = cellPerimSum;
		result.WettedWidth[wseIndex] = cellWettedWidthSum;
	}

	private bool TryComputeConveyanceError(FaceGeometry geometry, SCMResult result, int wseIndex, float maximumBed, out float error)
	{
		error = float.NaN;
		if (wseIndex <= 1 || result.Wse[wseIndex] <= maximumBed)
		{
			return false;
		}
		if (!AllCellsWet(geometry.CellCount))
		{
			return false;
		}
		float conveyance = result.Conveyance[wseIndex];
		float area = result.Area[wseIndex - 1];
		float wettedPerimeter = result.WettedPerimeter[wseIndex - 1];
		float conveyance2 = result.Conveyance[wseIndex - 1];
		if (conveyance <= 0f || conveyance2 <= 0f || wettedPerimeter <= 0f)
		{
			return false;
		}
		float areaOverWettedPerimeter = area / wettedPerimeter;
		float areaConveyance2Term = area * MathF.Cbrt(areaOverWettedPerimeter * areaOverWettedPerimeter) / conveyance2;
		float areaOverWettedPerimeter2 = result.Area[wseIndex] / wettedPerimeter;
		float areaWseIndexTerm = result.Area[wseIndex] * MathF.Cbrt(areaOverWettedPerimeter2 * areaOverWettedPerimeter2) / areaConveyance2Term;
		error = MathF.Abs(areaWseIndexTerm - conveyance) / conveyance;
		return float.IsFinite(error);
	}

	private bool AllCellsWet(int cellCount)
	{
		if (_hydraulics.minWetCell != 0 || _hydraulics.maxWetCell != cellCount - 1)
		{
			return false;
		}
		for (int i = 0; i < cellCount; i++)
		{
			if (!_hydraulics.cellWet[i])
			{
				return false;
			}
		}
		return true;
	}

	private void ValidateInputs(FaceGeometry geometry, SCMControl control, bool validateStageGeneration)
	{
		ValidateControl(control, validateStageGeneration);
		ValidateGeometry(geometry, MaximumCellCount);
	}

	private static SCMResult[] SolveAllCore(FaceGeometry[] geometries, SCMControl control, float[][] wseValues, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(geometries, "geometries");
		bool hasExplicitWse = wseValues != null;
		ValidateControl(control, !hasExplicitWse);
		if (hasExplicitWse && wseValues.Length != geometries.Length)
		{
			throw new ArgumentException("One WSE array is required for each geometry.", "wseValues");
		}
		if (geometries.Length == 0)
		{
			return Array.Empty<SCMResult>();
		}
		int maximumCellCount = 0;
		for (int i = 0; i < geometries.Length; i++)
		{
			ValidateGeometry(geometries[i], int.MaxValue);
			maximumCellCount = Math.Max(maximumCellCount, geometries[i].CellCount);
			if (hasExplicitWse)
			{
				ValidateWseValues(wseValues[i]);
			}
		}
		SCMResult[] results = new SCMResult[geometries.Length];
		ParallelOptions parallelOptions = new ParallelOptions
		{
			CancellationToken = cancellationToken
		};
		Parallel.For(0, geometries.Length, parallelOptions, () => new SCMSolver(maximumCellCount), (int geometryIndex, ParallelLoopState _, SCMSolver solver) =>
		{
			results[geometryIndex] = (hasExplicitWse ? solver.Solve(geometries[geometryIndex], control, wseValues[geometryIndex]) : solver.Solve(geometries[geometryIndex], control));
			return solver;
		}, (SCMSolver _) =>
		{
		});
		return results;
	}

	private static void ValidateControl(SCMControl control, bool validateStageGeneration)
	{
		ArgumentNullException.ThrowIfNull(control, "control");
		ValidatePositive(control.Gravity, "Gravity");
		ValidateNonnegative(control.VegetationStemDensity, "VegetationStemDensity");
		ValidateNonnegative(control.VegetationDragCoefficient, "VegetationDragCoefficient");
		ValidateNonnegative(control.VegetationStemDiameter, "VegetationStemDiameter");
		ValidateNonnegative(control.ConveyanceRelativeTolerance, "ConveyanceRelativeTolerance");
		float vegetationStemDensityControlTerm = control.VegetationStemDensity * (float)Math.PI * control.VegetationStemDiameter * control.VegetationStemDiameter * 0.25f;
		if (!float.IsFinite(vegetationStemDensityControlTerm) || vegetationStemDensityControlTerm >= 1f)
		{
			throw new ArgumentOutOfRangeException("VegetationStemDensity", "Vegetation stems must leave a positive open-area fraction.");
		}
		if (validateStageGeneration)
		{
			ValidateStageGeneration(control.WseIncrement, control.MaximumWseCount);
		}
	}

	private static void ValidateGeometry(FaceGeometry geometry, int maximumCellCount)
	{
		ArgumentNullException.ThrowIfNull(geometry, "geometry");
		if (geometry.CellCount < 1)
		{
			throw new ArgumentException("Geometry must contain at least one cell.", "geometry");
		}
		if (geometry.CellCount > maximumCellCount)
		{
			throw new ArgumentException($"Geometry has {geometry.CellCount} cells, exceeding solver capacity {maximumCellCount}.", "geometry");
		}
		int count = geometry.CellCount + 1;
		if (geometry.FaceStations == null || geometry.FaceStations.Length < count)
		{
			throw new ArgumentException("Geometry does not contain enough face stations.", "geometry");
		}
		if (geometry.FaceBedElevations == null || geometry.FaceBedElevations.Length < count)
		{
			throw new ArgumentException("Geometry does not contain enough face bed elevations.", "geometry");
		}
		if (geometry.CellMannings == null || geometry.CellMannings.Length < geometry.CellCount)
		{
			throw new ArgumentException("Geometry does not contain enough cell Manning values.", "geometry");
		}
		for (int i = 0; i < count; i++)
		{
			if (!float.IsFinite(geometry.FaceStations[i]))
			{
				throw new ArgumentException("Face stations must be finite.", "geometry");
			}
			if (!float.IsFinite(geometry.FaceBedElevations[i]))
			{
				throw new ArgumentException("Face bed elevations must be finite.", "geometry");
			}
			if (i > 0 && geometry.FaceStations[i] <= geometry.FaceStations[i - 1])
			{
				throw new ArgumentException("Face stations must be strictly ascending.", "geometry");
			}
		}
		for (int j = 0; j < geometry.CellCount; j++)
		{
			if (!float.IsFinite(geometry.CellMannings[j]) || geometry.CellMannings[j] <= 0f)
			{
				throw new ArgumentException("Cell Manning values must be finite and positive.", "geometry");
			}
		}
	}

	private static void ValidateWseValues(float[] wseValues)
	{
		ArgumentNullException.ThrowIfNull(wseValues, "wseValues");
		if (wseValues.Length == 0)
		{
			throw new ArgumentException("At least one WSE stage is required.", "wseValues");
		}
		for (int i = 0; i < wseValues.Length; i++)
		{
			if (!float.IsFinite(wseValues[i]))
			{
				throw new ArgumentException("WSE stages must be finite.", "wseValues");
			}
			if (i > 0 && wseValues[i] <= wseValues[i - 1])
			{
				throw new ArgumentException("WSE stages must be strictly ascending.", "wseValues");
			}
		}
	}

	private static void ValidateStageGeneration(float wseIncrement, int maximumWseCount)
	{
		ValidatePositive(wseIncrement, "wseIncrement");
		if (maximumWseCount < 1)
		{
			throw new ArgumentOutOfRangeException("maximumWseCount", "Maximum WSE count must be positive.");
		}
	}

	private static void ValidatePositive(float computed, string parameterName)
	{
		if (!float.IsFinite(computed) || computed <= 0f)
		{
			throw new ArgumentOutOfRangeException(parameterName, "Value must be finite and positive.");
		}
	}

	private static void ValidateNonnegative(float computed, string parameterName)
	{
		if (!float.IsFinite(computed) || computed < 0f)
		{
			throw new ArgumentOutOfRangeException(parameterName, "Value must be finite and nonnegative.");
		}
	}

	private static FaceHydraulicInputs CreateHydraulicInputs(SCMControl control)
	{
		return new FaceHydraulicInputs(control.Gravity, control.ManningUnitFactor, control.VegetationStemDensity, control.VegetationDragCoefficient, control.VegetationStemDiameter);
	}

	private static float MaximumBedElevation(FaceGeometry geometry)
	{
		float faceBedElevation = geometry.FaceBedElevations[0];
		for (int i = 1; i <= geometry.CellCount; i++)
		{
			faceBedElevation = MathF.Max(faceBedElevation, geometry.FaceBedElevations[i]);
		}
		return faceBedElevation;
	}
}
