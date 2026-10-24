using System;
using System.Threading;
using System.Threading.Tasks;
using Utility.SimdOps;

namespace Geospatial.Hydraulics;

public sealed class DCMSolver
{
	private readonly FaceHydraulics _hydraulics;

	private readonly float[] _cellVelocity;

	public int MaximumCellCount { get; }

	public DCMSolver(int maximumCellCount)
	{
		if (maximumCellCount < 1)
		{
			throw new ArgumentOutOfRangeException("maximumCellCount", "Cell capacity must be positive.");
		}
		MaximumCellCount = maximumCellCount;
		_hydraulics = new FaceHydraulics(maximumCellCount);
		_cellVelocity = new float[maximumCellCount];
	}

	public DCMResult Solve(FaceGeometry geometry, DCMControl control)
	{
		ValidateInputs(geometry, control, validateStageGeneration: true);
		float[] wseValues = CurveStageGenerator.GenerateAdaptive(geometry, control.WseIncrement, control.MaximumWseCount);
		return SolveCore(geometry, control, wseValues, allowEarlyTermination: true);
	}

	public DCMResult Solve(FaceGeometry geometry, DCMControl control, float[] wseValues)
	{
		ValidateInputs(geometry, control, validateStageGeneration: false);
		ValidateWseValues(wseValues);
		return SolveCore(geometry, control, wseValues, allowEarlyTermination: false);
	}

	public static DCMResult[] SolveAll(FaceGeometry[] geometries, DCMControl control, CancellationToken cancellationToken = default(CancellationToken))
	{
		return SolveAllCore(geometries, control, null, cancellationToken);
	}

	public static DCMResult[] SolveAll(FaceGeometry[] geometries, DCMControl control, float[][] wseValues, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(wseValues, "wseValues");
		return SolveAllCore(geometries, control, wseValues, cancellationToken);
	}

	private DCMResult SolveCore(FaceGeometry geometry, DCMControl control, float[] wseValues, bool allowEarlyTermination)
	{
		DCMResult dCMResult = new DCMResult(geometry, control, wseValues);
		FaceHydraulicInputs inputs = CreateHydraulicInputs(control);
		float maximumBed = MaximumBedElevation(geometry);
		for (int i = 0; i < wseValues.Length; i++)
		{
			_hydraulics.Preprocess(geometry, inputs, wseValues[i]);
			if (_hydraulics.AllDry)
			{
				dCMResult.SetDry(i);
				continue;
			}
			WriteBulkTotals(i, dCMResult);
			ComputeStage(geometry, control, i, dCMResult);
			if (TryComputeConveyanceError(geometry, dCMResult, i, maximumBed, out var error))
			{
				dCMResult.FinalConveyanceError = error;
				if (allowEarlyTermination && control.ConveyanceRelativeTolerance != 0f && !(error >= control.ConveyanceRelativeTolerance))
				{
					dCMResult.SolvedWseCount = i + 1;
					return dCMResult;
				}
			}
		}
		return dCMResult;
	}

	private void ComputeStage(FaceGeometry geometry, DCMControl control, int wseIndex, DCMResult result)
	{
		ComputeNormalDepthVelocity(geometry.CellCount, control);
		int minWetCell = _hydraulics.minWetCell;
		int length = _hydraulics.maxWetCell - minWetCell + 1;
		float flow = SimdOps.Dot(_hydraulics.cellArea.AsSpan(minWetCell, length), _cellVelocity.AsSpan(minWetCell, length));
		result.Conveyance[wseIndex] = flow / MathF.Sqrt(control.Slope);
		if (result.HasVelocityDistribution)
		{
			result.Flow[wseIndex] = flow;
			result.CellVelocity[wseIndex] = new float[geometry.CellCount];
			Array.Copy(_cellVelocity, result.CellVelocity[wseIndex], geometry.CellCount);
		}
		if (result.HasAlpha)
		{
			result.Alpha[wseIndex] = HydraulicCoefficients.VelocityWeightingCoefficient(_hydraulics.cellArea, _cellVelocity, flow, minWetCell, _hydraulics.maxWetCell);
		}
		if (result.HasBeta)
		{
			result.Beta[wseIndex] = HydraulicCoefficients.MomentumCoefficient(_hydraulics.cellArea, _cellVelocity, flow, minWetCell, _hydraulics.maxWetCell);
		}
	}

	private void ComputeNormalDepthVelocity(int cellCount, DCMControl control)
	{
		int minWetCell = _hydraulics.minWetCell;
		int maxWetCell = _hydraulics.maxWetCell;
		FaceHydraulics.ClearTails(_cellVelocity, minWetCell, maxWetCell, cellCount);
		for (int i = minWetCell; i <= maxWetCell; i++)
		{
			if (!_hydraulics.cellWet[i])
			{
				_cellVelocity[i] = 0f;
				continue;
			}
			float gravityControlTerm = control.Gravity * _hydraulics.cellSolid[i] * _hydraulics.cellDepth[i] * control.Slope * _hydraulics.cellWidth[i];
			float cellSolidHydraulicsTerm = _hydraulics.cellSolid[i] * _hydraulics.cellDragVeg[i] * _hydraulics.cellPerim[i] + 0.5f * control.VegetationDragCoefficient * _hydraulics.cellAreaVeg[i] * _hydraulics.cellWidth[i];
			_cellVelocity[i] = ((cellSolidHydraulicsTerm > 0f) ? MathF.Sqrt(gravityControlTerm / cellSolidHydraulicsTerm) : 0f);
		}
	}

	private void WriteBulkTotals(int wseIndex, DCMResult result)
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

	private bool TryComputeConveyanceError(FaceGeometry geometry, DCMResult result, int wseIndex, float maximumBed, out float error)
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

	private void ValidateInputs(FaceGeometry geometry, DCMControl control, bool validateStageGeneration)
	{
		ValidateControl(control, validateStageGeneration);
		ValidateGeometry(geometry, MaximumCellCount);
	}

	private static DCMResult[] SolveAllCore(FaceGeometry[] geometries, DCMControl control, float[][] wseValues, CancellationToken cancellationToken)
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
			return Array.Empty<DCMResult>();
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
		DCMResult[] results = new DCMResult[geometries.Length];
		ParallelOptions parallelOptions = new ParallelOptions
		{
			CancellationToken = cancellationToken
		};
		Parallel.For(0, geometries.Length, parallelOptions, () => new DCMSolver(maximumCellCount), (int geometryIndex, ParallelLoopState _, DCMSolver solver) =>
		{
			results[geometryIndex] = (hasExplicitWse ? solver.Solve(geometries[geometryIndex], control, wseValues[geometryIndex]) : solver.Solve(geometries[geometryIndex], control));
			return solver;
		}, (DCMSolver _) =>
		{
		});
		return results;
	}

	private static void ValidateControl(DCMControl control, bool validateStageGeneration)
	{
		ArgumentNullException.ThrowIfNull(control, "control");
		ValidatePositive(control.Slope, "Slope");
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

	private static FaceHydraulicInputs CreateHydraulicInputs(DCMControl control)
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
