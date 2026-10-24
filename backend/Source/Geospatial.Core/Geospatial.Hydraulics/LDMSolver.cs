using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Utility.Matrices;
using Utility.SimdOps;

namespace Geospatial.Hydraulics;

public sealed class LDMSolver
{
	private static readonly int VectorWidth = Vector<float>.Count;

	private readonly FaceHydraulics _hydraulics;

	private readonly float[] _leftNeighborCoupling;

	private readonly float[] _centerCoupling;

	private readonly float[] _rightNeighborCoupling;

	private readonly float[] _leftSelfCoupling;

	private readonly float[] _rightSelfCoupling;

	private readonly float[] _drivingSource;

	private readonly float[] _leftJacobian;

	private readonly float[] _centerJacobian;

	private readonly float[] _rightJacobian;

	private readonly float[] _faceSolidDepthOverWidth;

	private readonly float[] _lowerDiagonal;

	private readonly float[] _centerDiagonal;

	private readonly float[] _upperDiagonal;

	private readonly float[] _rightHandSide;

	private readonly float[] _velocityCorrection;

	private readonly float[] _lateralMixing;

	private readonly float[] _cellVelocity;

	private readonly float[] _previousCellVelocity;

	public int MaximumCellCount { get; }

	public LDMSolver(int maximumCellCount)
	{
		if (maximumCellCount < 1)
		{
			throw new ArgumentOutOfRangeException("maximumCellCount", "Cell capacity must be positive.");
		}
		MaximumCellCount = maximumCellCount;
		_hydraulics = new FaceHydraulics(maximumCellCount);
		int count = maximumCellCount + 1;
		_faceSolidDepthOverWidth = new float[count];
		_leftNeighborCoupling = new float[maximumCellCount];
		_centerCoupling = new float[maximumCellCount];
		_rightNeighborCoupling = new float[maximumCellCount];
		_leftSelfCoupling = new float[maximumCellCount];
		_rightSelfCoupling = new float[maximumCellCount];
		_drivingSource = new float[maximumCellCount];
		_leftJacobian = new float[maximumCellCount];
		_centerJacobian = new float[maximumCellCount];
		_rightJacobian = new float[maximumCellCount];
		_lowerDiagonal = new float[maximumCellCount];
		_centerDiagonal = new float[maximumCellCount];
		_upperDiagonal = new float[maximumCellCount];
		_rightHandSide = new float[maximumCellCount];
		_velocityCorrection = new float[maximumCellCount];
		_lateralMixing = new float[maximumCellCount];
		_cellVelocity = new float[maximumCellCount];
		_previousCellVelocity = new float[maximumCellCount];
	}

	public LDMResult Solve(FaceGeometry geometry, LDMControl control)
	{
		ValidateInputs(geometry, control, validateStageGeneration: true);
		float[] wseValues = CurveStageGenerator.GenerateAdaptive(geometry, control.WseIncrement, control.MaximumWseCount);
		return SolveCore(geometry, control, wseValues, allowEarlyTermination: true);
	}

	internal LDMResult SolveAdaptiveGeneratedStages(FaceGeometry geometry, LDMControl control, float[] wseValues)
	{
		ValidateInputs(geometry, control, validateStageGeneration: false);
		ValidateWseValues(wseValues);
		return SolveCore(geometry, control, wseValues, allowEarlyTermination: true);
	}

	public LDMResult Solve(FaceGeometry geometry, LDMControl control, float[] wseValues)
	{
		ValidateInputs(geometry, control, validateStageGeneration: false);
		ValidateWseValues(wseValues);
		return SolveCore(geometry, control, wseValues, allowEarlyTermination: false);
	}

	internal bool TryComputeCellConveyances(ReadOnlySpan<float> cellWidths, ReadOnlySpan<float> cellAreas, ReadOnlySpan<float> cellWettedPerimeters, ReadOnlySpan<float> cellMannings, ReadOnlySpan<float> faceBedElevations, float waterSurfaceElevation, LDMControl control, Span<float> cellConveyances, out LDMStageDiagnostics diagnostics)
	{
		ValidateControl(control, validateStageGeneration: false);
		ValidateHydraulicCells(cellWidths, cellAreas, cellWettedPerimeters, cellMannings, faceBedElevations, waterSurfaceElevation, cellConveyances, MaximumCellCount);
		int length = cellWidths.Length;
		cellConveyances.Slice(0, length).Clear();
		FaceHydraulicInputs inputs = CreateHydraulicInputs(control);
		_hydraulics.Preprocess(cellWidths, cellAreas, cellWettedPerimeters, cellMannings, faceBedElevations, inputs, waterSurfaceElevation);
		if (_hydraulics.AllDry)
		{
			diagnostics = new LDMStageDiagnostics(converged: true, 0, 0f, 0f);
			return true;
		}
		ComputeNormalDepthVelocity(length, control);
		Presolve(length, control);
		bool iterate = Iterate(length, control, out var iterations, out var error);
		float slopeControlTerm = 1f / MathF.Sqrt(control.Slope);
		float cellConveyances2Sum = 0f;
		bool isFinite = float.IsFinite(error);
		for (int i = 0; i < length; i++)
		{
			if (_hydraulics.cellWet[i])
			{
				float cellConveyances2 = _hydraulics.cellArea[i] * _cellVelocity[i] * slopeControlTerm;
				if (!float.IsFinite(cellConveyances2) || cellConveyances2 < 0f)
				{
					isFinite = false;
					break;
				}
				cellConveyances[i] = cellConveyances2;
				cellConveyances2Sum += cellConveyances2;
				if (!float.IsFinite(cellConveyances2Sum))
				{
					isFinite = false;
					break;
				}
			}
		}
		bool iterateWithIsFinite = iterate & isFinite;
		diagnostics = new LDMStageDiagnostics(iterateWithIsFinite, iterations, error, isFinite ? cellConveyances2Sum : float.NaN);
		if (!iterateWithIsFinite)
		{
			cellConveyances.Slice(0, length).Clear();
		}
		return iterateWithIsFinite;
	}

	internal LDMResult SolveHydraulicCells(ReadOnlySpan<float> cellWidths, ReadOnlySpan<float> cellAreas, ReadOnlySpan<float> cellWettedPerimeters, ReadOnlySpan<float> cellMannings, ReadOnlySpan<float> faceBedElevations, float waterSurfaceElevation, LDMControl control, string name = "")
	{
		ValidateControl(control, validateStageGeneration: false);
		float[] floatValues = new float[cellWidths.Length];
		ValidateHydraulicCells(cellWidths, cellAreas, cellWettedPerimeters, cellMannings, faceBedElevations, waterSurfaceElevation, floatValues, MaximumCellCount);
		FaceGeometry geometry = CreateHydraulicCellGeometry(cellWidths, cellMannings, faceBedElevations, name);
		LDMResult lDMResult = new LDMResult(geometry, control, new float[1] { waterSurfaceElevation });
		FaceHydraulicInputs inputs = CreateHydraulicInputs(control);
		_hydraulics.Preprocess(cellWidths, cellAreas, cellWettedPerimeters, cellMannings, faceBedElevations, inputs, waterSurfaceElevation);
		if (_hydraulics.AllDry)
		{
			lDMResult.SetDry(0);
			return lDMResult;
		}
		WriteBulkTotals(0, lDMResult);
		ComputeStage(geometry, control, 0, lDMResult);
		return lDMResult;
	}

	public static LDMResult[] SolveAll(FaceGeometry[] geometries, LDMControl control, CancellationToken cancellationToken = default(CancellationToken))
	{
		return SolveAllCore(geometries, control, null, cancellationToken);
	}

	public static LDMResult[] SolveAll(FaceGeometry[] geometries, LDMControl control, float[][] wseValues, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(wseValues, "wseValues");
		return SolveAllCore(geometries, control, wseValues, cancellationToken);
	}

	private LDMResult SolveCore(FaceGeometry geometry, LDMControl control, float[] wseValues, bool allowEarlyTermination)
	{
		LDMResult lDMResult = new LDMResult(geometry, control, wseValues);
		FaceHydraulicInputs inputs = CreateHydraulicInputs(control);
		float maximumBed = MaximumBedElevation(geometry);
		for (int i = 0; i < wseValues.Length; i++)
		{
			_hydraulics.Preprocess(geometry, inputs, wseValues[i]);
			if (_hydraulics.AllDry)
			{
				lDMResult.SetDry(i);
				continue;
			}
			WriteBulkTotals(i, lDMResult);
			ComputeStage(geometry, control, i, lDMResult);
			if (TryComputeConveyanceError(geometry, lDMResult, i, maximumBed, out var error))
			{
				lDMResult.FinalConveyanceError = error;
				if (allowEarlyTermination && control.ConveyanceRelativeTolerance != 0f && !(error >= control.ConveyanceRelativeTolerance))
				{
					lDMResult.SolvedWseCount = i + 1;
					return lDMResult;
				}
			}
		}
		return lDMResult;
	}

	private void ComputeStage(FaceGeometry geometry, LDMControl control, int wseIndex, LDMResult result)
	{
		int minWetCell = _hydraulics.minWetCell;
		int maxWetCell = _hydraulics.maxWetCell;
		int length = maxWetCell - minWetCell + 1;
		ComputeNormalDepthVelocity(geometry.CellCount, control);
		Presolve(geometry.CellCount, control);
		bool converged = Iterate(geometry.CellCount, control, out var iterations, out var error);
		float flow = SimdOps.Dot(_hydraulics.cellArea.AsSpan(minWetCell, length), _cellVelocity.AsSpan(minWetCell, length));
		result.Conveyance[wseIndex] = flow / MathF.Sqrt(control.Slope);
		if (result.HasDispersion)
		{
			result.Dispersion[wseIndex] = HydraulicCoefficients.ComputeChikwenduDispersion(_hydraulics.cellArea, _hydraulics.cellWettedWidth, _hydraulics.cellDepth, _hydraulics.cellDragVeg, _cellVelocity, control.EffectiveLongitudinalMixingCoefficient, control.EffectiveLateralMixingCoefficient, minWetCell, maxWetCell);
		}
		if (result.HasAlpha)
		{
			result.Alpha[wseIndex] = HydraulicCoefficients.VelocityWeightingCoefficient(_hydraulics.cellArea, _cellVelocity, flow, minWetCell, maxWetCell);
		}
		if (result.HasBeta)
		{
			result.Beta[wseIndex] = HydraulicCoefficients.MomentumCoefficient(_hydraulics.cellArea, _cellVelocity, flow, minWetCell, maxWetCell);
		}
		if (result.HasVelocityDistribution)
		{
			result.Flow[wseIndex] = flow;
			result.CellVelocity[wseIndex] = new float[geometry.CellCount];
			Array.Copy(_cellVelocity, result.CellVelocity[wseIndex], geometry.CellCount);
		}
		result.Iterations[wseIndex] = iterations;
		result.FinalVelocityError[wseIndex] = error;
		result.Converged[wseIndex] = converged;
	}

	private void ComputeNormalDepthVelocity(int cellCount, LDMControl control)
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

	private void WriteBulkTotals(int wseIndex, LDMResult result)
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

	private bool TryComputeConveyanceError(FaceGeometry geometry, LDMResult result, int wseIndex, float maximumBed, out float error)
	{
		error = float.NaN;
		if (wseIndex <= 1 || result.Wse[wseIndex] <= maximumBed)
		{
			return false;
		}
		if (!result.Converged[wseIndex] || !result.Converged[wseIndex - 1])
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

	private void ValidateInputs(FaceGeometry geometry, LDMControl control, bool validateStageGeneration)
	{
		ValidateControl(control, validateStageGeneration);
		ValidateGeometry(geometry, MaximumCellCount);
	}

	private static LDMResult[] SolveAllCore(FaceGeometry[] geometries, LDMControl control, float[][] wseValues, CancellationToken cancellationToken)
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
			return Array.Empty<LDMResult>();
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
		LDMResult[] results = new LDMResult[geometries.Length];
		ParallelOptions parallelOptions = new ParallelOptions
		{
			CancellationToken = cancellationToken
		};
		Parallel.For(0, geometries.Length, parallelOptions, () => new LDMSolver(maximumCellCount), (int geometryIndex, ParallelLoopState _, LDMSolver solver) =>
		{
			results[geometryIndex] = (hasExplicitWse ? solver.Solve(geometries[geometryIndex], control, wseValues[geometryIndex]) : solver.Solve(geometries[geometryIndex], control));
			return solver;
		}, (LDMSolver _) =>
		{
		});
		return results;
	}

	private static void ValidateControl(LDMControl control, bool validateStageGeneration)
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
		ValidatePositive(control.VelocityTolerance, "VelocityTolerance");
		ValidateRelaxation(control.PicardRelaxationFactor, "PicardRelaxationFactor");
		ValidateRelaxation(control.NewtonRelaxationFactor, "NewtonRelaxationFactor");
		if (control.MaximumIterations < 1)
		{
			throw new ArgumentOutOfRangeException("MaximumIterations", "Maximum iterations must be positive.");
		}
		if (!float.IsFinite(control.NewtonJacobianFactor) || control.NewtonJacobianFactor < 0f || control.NewtonJacobianFactor > 1f)
		{
			throw new ArgumentOutOfRangeException("NewtonJacobianFactor", "Newton Jacobian factor must be finite and between zero and one.");
		}
		ValidateNonnegative(control.MixingCoefficient, "MixingCoefficient");
		ValidateOptionalNonnegative(control.LongitudinalMixingCoefficient, "LongitudinalMixingCoefficient");
		ValidateOptionalNonnegative(control.LateralMixingCoefficient, "LateralMixingCoefficient");
		if (control.ComputeDispersion && control.EffectiveLateralMixingCoefficient <= 0f)
		{
			throw new ArgumentOutOfRangeException("LateralMixingCoefficient", "Effective lateral mixing must be positive when dispersion is computed.");
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

	private static void ValidateHydraulicCells(ReadOnlySpan<float> cellWidths, ReadOnlySpan<float> cellAreas, ReadOnlySpan<float> cellWettedPerimeters, ReadOnlySpan<float> cellMannings, ReadOnlySpan<float> faceBedElevations, float waterSurfaceElevation, Span<float> cellConveyances, int maximumCellCount)
	{
		int length = cellWidths.Length;
		if (length < 1)
		{
			throw new ArgumentException("At least one hydraulic cell is required.", "cellWidths");
		}
		if (length > maximumCellCount)
		{
			throw new ArgumentException($"Hydraulic input has {length} cells, exceeding solver capacity {maximumCellCount}.", "cellWidths");
		}
		if (cellAreas.Length != length || cellWettedPerimeters.Length != length || cellMannings.Length != length)
		{
			throw new ArgumentException("Hydraulic cell input spans must have matching lengths.", "cellAreas");
		}
		if (cellConveyances.Length < length)
		{
			throw new ArgumentException("The output span must have one slot per hydraulic cell.", "cellConveyances");
		}
		if (faceBedElevations.Length != length + 1)
		{
			throw new ArgumentException("The node-bed span must have one more entry than the cells.", "faceBedElevations");
		}
		if (!float.IsFinite(waterSurfaceElevation))
		{
			throw new ArgumentOutOfRangeException("waterSurfaceElevation", "Water-surface elevation must be finite.");
		}
		for (int i = 0; i < faceBedElevations.Length; i++)
		{
			float faceBedElevation = faceBedElevations[i];
			if (!float.IsFinite(faceBedElevation) || faceBedElevation == -9999f)
			{
				throw new ArgumentException("Node-bed elevations must be finite terrain values.", "faceBedElevations");
			}
		}
		for (int j = 0; j < length; j++)
		{
			if (!float.IsFinite(cellWidths[j]) || cellWidths[j] <= 0f)
			{
				throw new ArgumentException("Hydraulic cell widths must be finite and positive.", "cellWidths");
			}
			if (!float.IsFinite(cellAreas[j]) || cellAreas[j] < 0f)
			{
				throw new ArgumentException("Hydraulic cell areas must be finite and nonnegative.", "cellAreas");
			}
			if (!float.IsFinite(cellWettedPerimeters[j]) || cellWettedPerimeters[j] < 0f || (cellAreas[j] > 1E-06f && cellWettedPerimeters[j] <= 0f))
			{
				throw new ArgumentException("Wet hydraulic cells require a finite, positive wetted perimeter.", "cellWettedPerimeters");
			}
			if (!float.IsFinite(cellMannings[j]) || cellMannings[j] <= 0f)
			{
				throw new ArgumentException("Hydraulic cell Manning values must be finite and positive.", "cellMannings");
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

	private static void ValidateRelaxation(float computed, string parameterName)
	{
		if (!float.IsFinite(computed) || computed <= 0f || computed > 1f)
		{
			throw new ArgumentOutOfRangeException(parameterName, "Relaxation factors must be greater than zero through one.");
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

	private static void ValidateOptionalNonnegative(float computed, string parameterName)
	{
		if (!float.IsNaN(computed))
		{
			ValidateNonnegative(computed, parameterName);
		}
	}

	private static FaceHydraulicInputs CreateHydraulicInputs(LDMControl control)
	{
		return new FaceHydraulicInputs(control.Gravity, control.ManningUnitFactor, control.VegetationStemDensity, control.VegetationDragCoefficient, control.VegetationStemDiameter);
	}

	private static FaceGeometry CreateHydraulicCellGeometry(ReadOnlySpan<float> cellWidths, ReadOnlySpan<float> cellMannings, ReadOnlySpan<float> faceBedElevations, string name)
	{
		float[] floatValues = new float[cellWidths.Length + 1];
		for (int i = 0; i < cellWidths.Length; i++)
		{
			floatValues[i + 1] = floatValues[i] + cellWidths[i];
			if (!float.IsFinite(floatValues[i + 1]))
			{
				throw new ArgumentException("The cumulative hydraulic-cell width must be finite.", "cellWidths");
			}
		}
		return new FaceGeometry(floatValues, faceBedElevations.ToArray(), cellMannings.ToArray())
		{
			Name = name
		};
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

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	private void Presolve(int cellCount, LDMControl control)
	{
		float effectiveLateralMixingCoefficient = control.EffectiveLateralMixingCoefficient;
		float gravity = control.Gravity;
		float slope = control.Slope;
		float vegetationDragCoefficient = control.VegetationDragCoefficient;
		float newtonJacobianFactor = control.NewtonJacobianFactor;
		int vectorWidth = VectorWidth;
		int minWetCell = _hydraulics.minWetCell;
		int maxWetCell = _hydraulics.maxWetCell;
		float[] lateralMixing = _lateralMixing;
		FaceHydraulics.ClearTails(lateralMixing, minWetCell, maxWetCell, cellCount);
		Vector<float> vector = new Vector<float>(0.5f * effectiveLateralMixingCoefficient);
		int i = minWetCell;
		int count;
		for (count = maxWetCell + 1; i + vectorWidth <= count; i += vectorWidth)
		{
			Vector<float> vector2 = Vector.SquareRoot(new Vector<float>(_hydraulics.cellDragVeg, i));
			(vector * vector2 * new Vector<float>(_hydraulics.cellDepth, i) * new Vector<float>(_hydraulics.cellSolid, i)).CopyTo(lateralMixing, i);
		}
		for (; i < count; i++)
		{
			lateralMixing[i] = 0.5f * effectiveLateralMixingCoefficient * MathF.Sqrt(_hydraulics.cellDragVeg[i]) * _hydraulics.cellDepth[i] * _hydraulics.cellSolid[i];
		}
		float[] faceSolidDepthOverWidth = _faceSolidDepthOverWidth;
		int wetLast = Math.Min(maxWetCell + 1, cellCount);
		FaceHydraulics.ClearTails(faceSolidDepthOverWidth, minWetCell, wetLast, cellCount + 1);
		int j = minWetCell;
		int count2;
		for (count2 = Math.Min(maxWetCell + 2, cellCount + 1); j + vectorWidth <= count2; j += vectorWidth)
		{
			(new Vector<float>(_hydraulics.faceSolid, j) * new Vector<float>(_hydraulics.faceDepth, j) / new Vector<float>(_hydraulics.faceWidth, j)).CopyTo(faceSolidDepthOverWidth, j);
		}
		for (; j < count2; j++)
		{
			faceSolidDepthOverWidth[j] = _hydraulics.faceSolid[j] * _hydraulics.faceDepth[j] / _hydraulics.faceWidth[j];
		}
		int k = Math.Max(1, minWetCell);
		int count3;
		for (count3 = maxWetCell + 1; k + vectorWidth <= count3; k += vectorWidth)
		{
			Vector<float> vector3 = -new Vector<float>(faceSolidDepthOverWidth, k);
			(vector3 * new Vector<float>(lateralMixing, k - 1)).CopyTo(_leftNeighborCoupling, k);
			(vector3 * new Vector<float>(lateralMixing, k)).CopyTo(_leftSelfCoupling, k);
		}
		for (; k < count3; k++)
		{
			_leftNeighborCoupling[k] = (0f - lateralMixing[k - 1]) * faceSolidDepthOverWidth[k];
			_leftSelfCoupling[k] = (0f - lateralMixing[k]) * faceSolidDepthOverWidth[k];
		}
		if (minWetCell == 0)
		{
			_leftNeighborCoupling[0] = 0f;
			_leftSelfCoupling[0] = 0f;
		}
		int l = minWetCell;
		int count4;
		for (count4 = Math.Min(maxWetCell + 1, cellCount - 1); l + vectorWidth <= count4; l += vectorWidth)
		{
			Vector<float> vector4 = -new Vector<float>(faceSolidDepthOverWidth, l + 1);
			(vector4 * new Vector<float>(lateralMixing, l + 1)).CopyTo(_rightNeighborCoupling, l);
			(vector4 * new Vector<float>(lateralMixing, l)).CopyTo(_rightSelfCoupling, l);
		}
		for (; l < count4; l++)
		{
			_rightNeighborCoupling[l] = (0f - lateralMixing[l + 1]) * faceSolidDepthOverWidth[l + 1];
			_rightSelfCoupling[l] = (0f - lateralMixing[l]) * faceSolidDepthOverWidth[l + 1];
		}
		if (maxWetCell == cellCount - 1)
		{
			_rightNeighborCoupling[cellCount - 1] = 0f;
			_rightSelfCoupling[cellCount - 1] = 0f;
		}
		float gravityTimesSlope = gravity * slope;
		float scaledVegetationDragCoefficient = 0.5f * vegetationDragCoefficient;
		for (int m = minWetCell; m <= maxWetCell; m++)
		{
			if (!_hydraulics.cellWet[m])
			{
				_leftNeighborCoupling[m] = 0f;
				_centerCoupling[m] = 0f;
				_rightNeighborCoupling[m] = 0f;
				_leftSelfCoupling[m] = 0f;
				_rightSelfCoupling[m] = 0f;
				_leftJacobian[m] = 0f;
				_centerJacobian[m] = 0f;
				_rightJacobian[m] = 0f;
				_drivingSource[m] = 0f;
			}
			else
			{
				_centerCoupling[m] = _hydraulics.cellSolid[m] * _hydraulics.cellDragVeg[m] * _hydraulics.cellPerim[m] + scaledVegetationDragCoefficient * _hydraulics.cellAreaVeg[m] * _hydraulics.cellWidth[m] - _leftSelfCoupling[m] - _rightSelfCoupling[m];
				_drivingSource[m] = gravityTimesSlope * _hydraulics.cellSolid[m] * _hydraulics.cellDepth[m] * _hydraulics.cellWidth[m];
				_leftJacobian[m] = newtonJacobianFactor * (_leftNeighborCoupling[m] + _leftSelfCoupling[m]);
				_centerJacobian[m] = newtonJacobianFactor * (_centerCoupling[m] - _leftNeighborCoupling[m] - _rightNeighborCoupling[m]);
				_rightJacobian[m] = newtonJacobianFactor * (_rightNeighborCoupling[m] + _rightSelfCoupling[m]);
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	private bool Iterate(int cellCount, LDMControl control, out int iterations, out float error)
	{
		if (_hydraulics.minWetCell < 0)
		{
			iterations = 0;
			error = 0f;
			return true;
		}
		int minWetCell = _hydraulics.minWetCell;
		int length = _hydraulics.maxWetCell - minWetCell + 1;
		float velocityTolerance = control.VelocityTolerance;
		int maximumIterations = control.MaximumIterations;
		float error2 = 1E+20f;
		for (int i = 1; i <= maximumIterations; i++)
		{
			Array.Copy(_cellVelocity, minWetCell, _previousCellVelocity, minWetCell, length);
			if (i <= 2 || error2 > 100f * velocityTolerance)
			{
				PicardStep(cellCount, control);
			}
			else
			{
				NewtonStep(cellCount, control);
			}
			float maxVal = SimdOps.Max(_cellVelocity.AsSpan(minWetCell, length));
			error2 = SimdOps.NormalizedRmse(_cellVelocity.AsSpan(minWetCell, length), _previousCellVelocity.AsSpan(minWetCell, length), _hydraulics.cellWet.AsSpan(minWetCell, length), maxVal);
			if (error2 < velocityTolerance)
			{
				iterations = i;
				error = error2;
				return true;
			}
		}
		iterations = maximumIterations;
		error = error2;
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	private void PicardStep(int cellCount, LDMControl control)
	{
		int minWetCell = _hydraulics.minWetCell;
		int length = _hydraulics.maxWetCell - minWetCell + 1;
		Assemble(cellCount);
		Array.Copy(_drivingSource, minWetCell, _rightHandSide, minWetCell, length);
		Span<float> minWetCellSpan = _lowerDiagonal.AsSpan(minWetCell, length);
		Span<float> b = _centerDiagonal.AsSpan(minWetCell, length);
		Span<float> minWetCell2 = _upperDiagonal.AsSpan(minWetCell, length);
		Span<float> d = _rightHandSide.AsSpan(minWetCell, length);
		Span<float> minWetCell3 = _cellVelocity.AsSpan(minWetCell, length);
		Span<float> minWetCell4 = _previousCellVelocity.AsSpan(minWetCell, length);
		TDMA.SolveInPlace(minWetCellSpan, b, minWetCell2, d, minWetCell3);
		SimdOps.Relax(minWetCell3, minWetCell4, control.PicardRelaxationFactor, minWetCell3);
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	private void NewtonStep(int cellCount, LDMControl control)
	{
		int vectorWidth = VectorWidth;
		int minWetCell = _hydraulics.minWetCell;
		int maxWetCell = _hydraulics.maxWetCell;
		int maxWetCellMinWetCellTerm = maxWetCell - minWetCell + 1;
		Assemble(cellCount);
		TDMA.MatMul(_lowerDiagonal, _centerDiagonal, _upperDiagonal, _previousCellVelocity, _rightHandSide, minWetCell, maxWetCellMinWetCellTerm);
		int i = minWetCell;
		int count;
		for (count = maxWetCell + 1; i + vectorWidth <= count; i += vectorWidth)
		{
			(new Vector<float>(_drivingSource, i) - new Vector<float>(_rightHandSide, i)).CopyTo(_rightHandSide, i);
		}
		for (; i < count; i++)
		{
			_rightHandSide[i] = _drivingSource[i] - _rightHandSide[i];
		}
		for (int j = minWetCell; j <= maxWetCell; j++)
		{
			if (!_hydraulics.cellWet[j])
			{
				_rightHandSide[j] = 0f;
			}
		}
		int k = minWetCell;
		int count2;
		for (count2 = maxWetCell + 1; k + vectorWidth <= count2; k += vectorWidth)
		{
			(new Vector<float>(_centerDiagonal, k) + new Vector<float>(_centerJacobian, k) * new Vector<float>(_previousCellVelocity, k)).CopyTo(_centerDiagonal, k);
		}
		for (; k < count2; k++)
		{
			_centerDiagonal[k] += _centerJacobian[k] * _previousCellVelocity[k];
		}
		int l = Math.Max(1, minWetCell);
		int count3;
		for (count3 = maxWetCell + 1; l + vectorWidth <= count3; l += vectorWidth)
		{
			(new Vector<float>(_centerDiagonal, l) + new Vector<float>(_leftJacobian, l) * new Vector<float>(_previousCellVelocity, l - 1)).CopyTo(_centerDiagonal, l);
		}
		for (; l < count3; l++)
		{
			_centerDiagonal[l] += _leftJacobian[l] * _previousCellVelocity[l - 1];
		}
		int m = minWetCell;
		int count4;
		for (count4 = Math.Min(maxWetCell + 1, cellCount - 1); m + vectorWidth <= count4; m += vectorWidth)
		{
			(new Vector<float>(_centerDiagonal, m) + new Vector<float>(_rightJacobian, m) * new Vector<float>(_previousCellVelocity, m + 1)).CopyTo(_centerDiagonal, m);
		}
		for (; m < count4; m++)
		{
			_centerDiagonal[m] += _rightJacobian[m] * _previousCellVelocity[m + 1];
		}
		TDMA.SolveInPlace(_lowerDiagonal.AsSpan(minWetCell, maxWetCellMinWetCellTerm), _centerDiagonal.AsSpan(minWetCell, maxWetCellMinWetCellTerm), _upperDiagonal.AsSpan(minWetCell, maxWetCellMinWetCellTerm), _rightHandSide.AsSpan(minWetCell, maxWetCellMinWetCellTerm), _velocityCorrection.AsSpan(minWetCell, maxWetCellMinWetCellTerm));
		SimdOps.AddScaled(_previousCellVelocity.AsSpan(minWetCell, maxWetCellMinWetCellTerm), control.NewtonRelaxationFactor, _velocityCorrection.AsSpan(minWetCell, maxWetCellMinWetCellTerm), _cellVelocity.AsSpan(minWetCell, maxWetCellMinWetCellTerm));
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	private void Assemble(int cellCount)
	{
		int vectorWidth = VectorWidth;
		int minWetCell = _hydraulics.minWetCell;
		int maxWetCell = _hydraulics.maxWetCell;
		_lowerDiagonal[minWetCell] = 0f;
		int i = Math.Max(1, minWetCell + 1);
		int count;
		for (count = maxWetCell + 1; i + vectorWidth <= count; i += vectorWidth)
		{
			(new Vector<float>(_leftNeighborCoupling, i) * new Vector<float>(_cellVelocity, i - 1) + new Vector<float>(_leftSelfCoupling, i) * new Vector<float>(_cellVelocity, i)).CopyTo(_lowerDiagonal, i);
		}
		for (; i < count; i++)
		{
			_lowerDiagonal[i] = _leftNeighborCoupling[i] * _cellVelocity[i - 1] + _leftSelfCoupling[i] * _cellVelocity[i];
		}
		_upperDiagonal[maxWetCell] = 0f;
		int j = minWetCell;
		int maxWetCell2;
		for (maxWetCell2 = maxWetCell; j + vectorWidth <= maxWetCell2; j += vectorWidth)
		{
			(new Vector<float>(_rightNeighborCoupling, j) * new Vector<float>(_cellVelocity, j + 1) + new Vector<float>(_rightSelfCoupling, j) * new Vector<float>(_cellVelocity, j)).CopyTo(_upperDiagonal, j);
		}
		for (; j < maxWetCell2; j++)
		{
			_upperDiagonal[j] = _rightNeighborCoupling[j] * _cellVelocity[j + 1] + _rightSelfCoupling[j] * _cellVelocity[j];
		}
		int k = minWetCell;
		int count2;
		for (count2 = maxWetCell + 1; k + vectorWidth <= count2; k += vectorWidth)
		{
			(new Vector<float>(_centerCoupling, k) * new Vector<float>(_cellVelocity, k)).CopyTo(_centerDiagonal, k);
		}
		for (; k < count2; k++)
		{
			_centerDiagonal[k] = _centerCoupling[k] * _cellVelocity[k];
		}
		int l = Math.Max(1, minWetCell);
		int count3;
		for (count3 = maxWetCell + 1; l + vectorWidth <= count3; l += vectorWidth)
		{
			(new Vector<float>(_centerDiagonal, l) - new Vector<float>(_leftNeighborCoupling, l) * new Vector<float>(_cellVelocity, l - 1)).CopyTo(_centerDiagonal, l);
		}
		for (; l < count3; l++)
		{
			_centerDiagonal[l] -= _leftNeighborCoupling[l] * _cellVelocity[l - 1];
		}
		int m = minWetCell;
		int count4;
		for (count4 = Math.Min(maxWetCell + 1, cellCount - 1); m + vectorWidth <= count4; m += vectorWidth)
		{
			(new Vector<float>(_centerDiagonal, m) - new Vector<float>(_rightNeighborCoupling, m) * new Vector<float>(_cellVelocity, m + 1)).CopyTo(_centerDiagonal, m);
		}
		for (; m < count4; m++)
		{
			_centerDiagonal[m] -= _rightNeighborCoupling[m] * _cellVelocity[m + 1];
		}
		for (int n = minWetCell; n <= maxWetCell; n++)
		{
			if (!_hydraulics.cellWet[n])
			{
				_lowerDiagonal[n] = 0f;
				_upperDiagonal[n] = 0f;
				_centerDiagonal[n] = 1f;
			}
		}
	}
}
