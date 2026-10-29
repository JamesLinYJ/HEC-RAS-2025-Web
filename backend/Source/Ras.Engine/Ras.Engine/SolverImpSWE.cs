using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Engine.Engine;
using Ras.Engine.Structures.Weirs;
using Utility.Progress;

namespace Ras.Engine;

public class SolverImpSWE : SolverImp, ISolver
{
	internal float[] faceHbarInv;

	public Vector2[] CellAdvVarExp;

	public float[] CellAdvVarImp;

	public float[] FaceAdvVarImp;

	public Vector2[] CellDiffVarExp;

	private double[] _faceBTermWithDeferredCorrection;

	private Geospatial.Vectors.Vector[] _bcVelocityMap;

	public float[] FaceVelMagFrict
	{
		get
		{
			if (Convergence.OuterConv.Iterations > 1)
			{
				return FaceVelMagAvg;
			}
			return _oldState.FaceVelMag;
		}
	}

	public double[] FaceVelNormAvg { get; private set; }

	public float[] FaceVelTangAvgA { get; private set; }

	public float[] FaceVelTangAvgB { get; private set; }

	public float[] FaceVelMagAvg { get; private set; }

	internal double[] FaceInvDiagA { get; set; }

	internal double[] FaceInvDiagB { get; set; }

	internal double[] FaceRHS { get; set; }

	internal double[] FaceBTerm { get; set; }

	private double[] FaceBTermForCurrentIteration => _faceBTermWithDeferredCorrection ?? FaceBTerm;

	public SolverImpSWE(Mesh mesh, MeshPropertyTables props, MeshWaterState initialState, SolverControl control, SolverConvergence convergence, ComputeDriver driver)
		: base(mesh, props, initialState, control, convergence ?? new SolverConvergence(), driver)
	{
	}

	public override ReadOnlySpan<double> GetFaceVelocity()
	{
		_waterState.ComputeFaceVelocity();
		return _waterState.FaceVelocity;
	}

	private void UpdateBCVelocityMap(double absTime)
	{
		Array.Fill(_bcVelocityMap, new Geospatial.Vectors.Vector(double.NaN, double.NaN));
		foreach (BoundaryCondition spatiallyVariableStageVelocityBoundaryCondition in SpatiallyVariableStageVelocityBoundaryConditions)
		{
			double[,] faceMultipleValuesAtTime = spatiallyVariableStageVelocityBoundaryCondition.FaceMultipleValuesAtTime(absTime);
			for (int i = 0; i < spatiallyVariableStageVelocityBoundaryCondition.Faces.Length; i++)
			{
				int faceIndex = spatiallyVariableStageVelocityBoundaryCondition.Faces[i];
				_bcVelocityMap[faceIndex] = new Geospatial.Vectors.Vector(faceMultipleValuesAtTime[i, 1], faceMultipleValuesAtTime[i, 2]);
			}
		}
	}

	public override void Prepare(ProgressReporter pr = null)
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		base.Prepare(pr);
		Allocate();
		switch (Control.faceTangentialVelocityMethod)
		{
		case SolverControl.FaceTangentialVelocityMethod.CellBased:
			ComputeFaceVelCompCellbased(_waterState);
			break;
		default:
			ComputeFaceVelCompWSL(_waterState);
			break;
		case SolverControl.FaceTangentialVelocityMethod.Momentum:
			break;
		}
		Array.Copy(_waterState.FaceVelMag, _oldState.FaceVelMag, _mesh.FaceCount);
		if (Control.Turbulence.IsActive)
		{
			CellDiffVarExp = new Vector2[_mesh.CellCount];
			PrepareTurbulenceState();
			ComputeTurbulentMixing(_waterState, PartGrid.FaceIndices, PartGrid.CellIndices, null, null);
		}
		if (Control.SecondaryFlow.Enabled)
		{
			_waterState.secondaryFlow = new SecondaryFlowState(_mesh);
			_oldState.secondaryFlow = _waterState.secondaryFlow;
			_secondaryFlow = new SecondaryFlowSolver(_mesh, Control, _waterState.secondaryFlow);
			_secondaryFlow.Prepare(this);
		}
	}

	private void Allocate()
	{
		int faceCount = _mesh.FaceCount;
		int cellCount = _mesh.CellCount;
		FaceInvDiagA = new double[faceCount];
		FaceInvDiagB = new double[faceCount];
		FaceVelNormAvg = new double[faceCount];
		FaceVelTangAvgA = new float[faceCount];
		FaceVelTangAvgB = new float[faceCount];
		FaceVelMagAvg = new float[faceCount];
		faceHbarInv = new float[faceCount];
		FaceRHS = new double[faceCount];
		FaceBTerm = new double[faceCount];
		if (HasNonOrthogonalCorrection)
		{
			_faceBTermWithDeferredCorrection = new double[faceCount];
		}
		if (SpatiallyVariableStageVelocityBoundaryConditions.Count > 0)
		{
			_bcVelocityMap = new Geospatial.Vectors.Vector[faceCount];
			Array.Fill(_bcVelocityMap, new Geospatial.Vectors.Vector(double.NaN, double.NaN));
		}
		CellAdvVarExp = new Vector2[cellCount];
		CellAdvVarImp = new float[cellCount];
		FaceAdvVarImp = new float[faceCount];
	}

	public override void PreparePropertyTables()
	{
		base.PreparePropertyTables();
		switch (Control.bedFrictionMethod)
		{
		case SolverControl.BedFrictionMethod.None:
			_propertyTables.SetConstBedFrictCoeff(0.0);
			break;
		case SolverControl.BedFrictionMethod.Constant:
			_propertyTables.SetConstBedFrictCoeff(Control.nonlinFrictCoeff);
			break;
		case SolverControl.BedFrictionMethod.Variable:
			_propertyTables.ComputeFaceBedFrictVariableTable(_mesh, grav, nSItoUnits, Control.dzMinFriction);
			break;
		}
	}

	protected override bool PreSolveOuter(double absTime, double dt, out double EOPTime)
	{
		base.PreSolveOuter(absTime, dt, out EOPTime);
		if (SpatiallyVariableStageVelocityBoundaryConditions.Count > 0)
		{
			UpdateBCVelocityMap(absTime);
		}
		if (Control.UsePartialGrid && PartGrid.CellCount == 0)
		{
			return true;
		}
		MeshWaterState oldState = _oldState;
		if (HasNonOrthogonalCorrection)
		{
			UpdateFaceGradientData(oldState);
		}
		bool advectionMethodControlTerm = Control.advectionMethod != SolverControl.AdvectionMethod.None;
		bool isActive = Control.Turbulence.IsActive;
		if ((advectionMethodControlTerm | isActive) || Control.faceTangentialVelocityMethod == SolverControl.FaceTangentialVelocityMethod.CellBased)
		{
			ComputeCellVelocity(oldState);
		}
		if ((advectionMethodControlTerm | isActive) || BoundaryConditions.HasWind)
		{
			UpdateCellHydraulicDepth(oldState);
			ComputeFaceDepths(oldState);
		}
		if (advectionMethodControlTerm)
		{
			ComputeCellAdvVar(oldState);
		}
		if (Control.Turbulence.IsActive)
		{
			ComputeTurbulentMixing(oldState, PartGrid.FaceIndices, PartGrid.CellIndices, CellDiffVarExp, null);
		}
		ComputeFaceBTerm(oldState, dt);
		if (HydraulicStructures.HasWeirs)
		{
			OverrideWeirFaceBTerm(oldState, dt);
		}
		return true;
	}

	private void UpdateFaceBTermWithDeferredCorrection(MeshWaterState ws, double dt)
	{
		Array.Copy(FaceBTerm, _faceBTermWithDeferredCorrection, FaceBTerm.Length);
		double implicitFactor = Control.ImplicitFactor;
		double currentFactor = grav * dt * implicitFactor;
		threadFor.Run(_mesh.FaceCount, (int start, int stop) =>
		{
			Span<double> faceBTermWithDeferredCorrection = _faceBTermWithDeferredCorrection.AsSpan();
			for (int i = start; i <= stop; i++)
			{
				if (FaceUsesNonOrthogonalCorrection(i))
				{
					faceBTermWithDeferredCorrection[i] -= currentFactor * ComputeFaceWaterSurfaceCorrection(i, ws);
				}
			}
		});
	}

	public void ComputeCellVelocity(MeshWaterState ws)
	{
		int cellCount = PartGrid.CellCount;
		List<int> cellIndices = PartGrid.CellIndices;
		Geospatial.Vectors.Vector[] cellVel = ws.CellVelocity;
		switch (Control.cellVelocityMethod)
		{
		case SolverControl.CellVelocityMethod.Perot:
			threadFor.Run(cellCount, (int start, int stop) =>
			{
				_hmesh.CellComputes.PerotVelocity(ws.FaceVelocity, cellVel, cellIndices, start, stop);
			});
			break;
		case SolverControl.CellVelocityMethod.WLS:
			threadFor.Run(cellCount, (int start, int stop) =>
			{
				_hmesh.CellComputes.VelocityWLS(ws.FaceArea, ws.FaceVelocity, cellVel, LocalCoordinates.FaceNormal, cellIndices, start, stop);
			});
			if (HydraulicStructures.HasWeirs)
			{
				OverrideStructFaceCellVelocity(ws);
			}
			break;
		}
	}

	private void ComputeFaceBTerm(MeshWaterState wsOld, double dt)
	{
		int faceCount = PartGrid.FaceCount;
		int loopSize = SolverImp.GetLoopSize(4000, faceCount);
		bool semiImplicit = Control.ImplicitFactor < 1.0;
		double dtThetaCmpGrav = dt * (1.0 - Control.ImplicitFactor) * grav;
		bool useAdvection = Control.advectionMethod != SolverControl.AdvectionMethod.None;
		double thetaAdv = ((Control.advectionMethod == SolverControl.AdvectionMethod.ExplicitUpwind) ? 0.0 : Control.AdvectionImplicitFactor);
		double dtfCoriolis = dt * Control.fCoriolis;
		threadFor.Run(faceCount, ComputeFaceBTermReange, loopSize);
		if (Control.Turbulence.IsActive)
		{
			ComputeFaceBTermTurbulence(wsOld, dt, faceCount);
		}
		if (_secondaryFlow == null)
		{
			return;
		}
		double[] faceMomentumDispersion = _secondaryFlow.State.FaceMomentumDispersion;
		foreach (int faceIndex in PartGrid.FaceIndices)
		{
			FaceBTerm[faceIndex] += dt * faceMomentumDispersion[faceIndex];
		}
		void ComputeFaceBTermReange(int start, int stop)
		{
			ComputeFaceBTermUnsteadyStartStop(wsOld, start, stop);
			if (useAdvection)
			{
				ComputeFaceBTermAdvectionStartStop(wsOld, dt, thetaAdv, start, stop);
			}
			if (semiImplicit)
			{
				ComputeFaceBTermExplicitSfcGradStartStop(wsOld, dtThetaCmpGrav, start, stop);
			}
			if (dtfCoriolis > 0.0)
			{
				ComputeFaceBTermCoriolisStartStop(wsOld, dtfCoriolis, start, stop);
			}
			if (BoundaryConditions.HasAirPressure)
			{
				ComputeFaceBTermAirPressStartStop(wsOld, dt, start, stop);
			}
			if (BoundaryConditions.HasWind)
			{
				ComputeFaceBTermWindStartStop(wsOld, dt, start, stop);
			}
		}
	}

	internal void ComputeFaceBTermUnsteadyStartStop(MeshWaterState wsOld, int start, int stop)
	{
		ReadOnlySpan<double> faceVelocity = wsOld.FaceVelocity.AsSpan();
		Span<double> faceBTerm = FaceBTerm.AsSpan();
		if (PartGrid.Enabled)
		{
			ReadOnlySpan<int> faceIndices = CollectionsMarshal.AsSpan(PartGrid.FaceIndices);
			for (int i = start; i <= stop; i++)
			{
				int index = faceIndices[i];
				faceBTerm[index] = faceVelocity[index];
			}
		}
		else
		{
			int length = stop - start + 1;
			faceVelocity.Slice(start, length).CopyTo(faceBTerm.Slice(start, length));
		}
	}

	internal void ComputeFaceBTermExplicitSfcGradStartStop(MeshWaterState wsOld, double dtThetaCmpGrav, int start, int stop)
	{
		double[] faceInverseDistances = FaceInverseDistances;
		Span<double> faceBTerm = FaceBTerm.AsSpan();
		ReadOnlySpan<int> faceIndices = CollectionsMarshal.AsSpan(PartGrid.FaceIndices);
		for (int i = start; i <= stop; i++)
		{
			int faceIndex = faceIndices[i];
			double faceNormalGradient = ComputeFaceNormalGradient(wsOld, faceIndex, faceInverseDistances);
			if (FaceUsesNonOrthogonalCorrection(faceIndex))
			{
				faceNormalGradient += ComputeFaceWaterSurfaceCorrection(faceIndex, wsOld);
			}
			faceBTerm[faceIndex] -= dtThetaCmpGrav * faceNormalGradient;
		}
	}

	internal void ComputeFaceBTermAdvectionStartStop(MeshWaterState wsOld, double dt, double theta, int start, int stop)
	{
		ReadOnlySpan<int> faceIndices = CollectionsMarshal.AsSpan(PartGrid.FaceIndices);
		ReadOnlySpan<int> faceCellAs = _meshConnectivity.FaceCellA;
		ReadOnlySpan<int> faceCellBs = _meshConnectivity.FaceCellB;
		ReadOnlySpan<double> faceAreas = wsOld.FaceArea;
		ReadOnlySpan<double> faceVelocities = wsOld.FaceVelocity;
		ReadOnlySpan<FaceFlag> faceFlags = FaceFlags;
		ReadOnlySpan<float> faceHbarInvSpan = faceHbarInv;
		ReadOnlySpan<Vector2> cellExplicitVariables = CellAdvVarExp;
		ReadOnlySpan<float> cellImplicitVariables = CellAdvVarImp;
		Geospatial.Vectors.Vector[] faceNormal = _hmesh.FaceNormal;
		Geospatial.Vectors.Vector[,] faceNormal2 = LocalCoordinates.FaceNormal;
		ComputeControl.CellFlag[] cellFlags = CellFlags;
		FaceAB[] faceAlphaCellAInv = _hmesh.FaceAlphaCellAInv;
		Span<double> faceBTerm = FaceBTerm;
		Span<float> faceAdvVarImp = FaceAdvVarImp;
		for (int i = start; i <= stop; i++)
		{
			int faceIndex = faceIndices[i];
			if (faceAreas[faceIndex] <= 0.0 || faceFlags[faceIndex] == 0)
			{
				faceAdvVarImp[faceIndex] = 0f;
				continue;
			}
			Solver.ComputeFaceAdvectionFromCellAccumulations(faceIndex, faceCellAs[faceIndex], faceCellBs[faceIndex], faceHbarInvSpan[faceIndex], faceVelocities[faceIndex], theta, cellExplicitVariables, cellImplicitVariables, faceNormal, faceNormal2, cellFlags, faceAlphaCellAInv, out var explicitAdvection, out var implicitAdvection);
			faceBTerm[faceIndex] -= dt * explicitAdvection;
			faceAdvVarImp[faceIndex] = (float)implicitAdvection;
		}
	}

	private void ComputeFaceBTermAdvectionStartStopExp(MeshWaterState wsOld, double dt, int start, int stop)
	{
		ReadOnlySpan<int> faceCellA = _meshConnectivity.FaceCellA.AsSpan();
		ReadOnlySpan<int> faceCellB = _meshConnectivity.FaceCellB.AsSpan();
		ReadOnlySpan<float> faceHbarInvSp = faceHbarInv.AsSpan();
		Span<double> faceBTerm = FaceBTerm.AsSpan();
		ReadOnlySpan<int> faceIndices = CollectionsMarshal.AsSpan(PartGrid.FaceIndices);
		for (int i = start; i <= stop; i++)
		{
			int faceIndex = faceIndices[i];
			int cA = faceCellA[faceIndex];
			int cB = faceCellB[faceIndex];
			ComputeAdvectionFace(faceIndex, cA, cB, wsOld, faceHbarInvSp, out var advection);
			faceBTerm[faceIndex] -= dt * advection;
		}
	}

	public unsafe void ComputeAdvectionFace(int fIdx, int cA, int cB, MeshWaterState ws, ReadOnlySpan<float> faceHbarInvSp, out double advection)
	{
		advection = 0.0;
		_ = Control.dzMinAdvection;
		if (cB < 0)
		{
			return;
		}
		int[] cellNeighborOffsets = _meshConnectivity.CellNeighborOffsets;
		int[] cellNeighborFaces = _meshConnectivity.CellNeighborFaces;
		int[] cellNeighborCells = _meshConnectivity.CellNeighborCells;
		int[] cellFaceOrientations = _meshConnectivity.CellFaceOrientations;
		int ordinal = 0;
		int cA2 = cA;
		while (ordinal < 2)
		{
			double computed = 0.0;
			Geospatial.Vectors.Vector vector = LocalCoordinates.FaceNormal[fIdx, ordinal];
			int cellNeighborOffsetStart = cellNeighborOffsets[cA2];
			int cellNeighborOffsetEnd = cellNeighborOffsets[cA2 + 1];
			for (int i = cellNeighborOffsetStart; i < cellNeighborOffsetEnd; i++)
			{
				int cellNeighborFaceIndex = cellNeighborFaces[i];
				int cellNeighborCellIndex = cellNeighborCells[i];
				if (cellNeighborCellIndex >= 0)
				{
					int cellFaceOrientation = cellFaceOrientations[i];
					double cellFaceOrientationFaceVelocityTerm = (double)cellFaceOrientation * ws.FaceVelocity[cellNeighborFaceIndex];
					if (cellFaceOrientationFaceVelocityTerm < 0.0)
					{
						Geospatial.Vectors.Vector input = ws.CellVelocity[cellNeighborCellIndex];
						int fromSide = 1 - (1 - cellFaceOrientation >> 1);
						Geospatial.Vectors.Vector vector2 = LocalCoordinates.RotateThroughFace(input, cellNeighborFaceIndex, fromSide);
						double dotProduct = vector2.X * vector.X + vector2.Y * vector.Y;
						computed += ws.FaceArea[cellNeighborFaceIndex] * cellFaceOrientationFaceVelocityTerm * (dotProduct - ws.FaceVelocity[fIdx]);
					}
				}
			}
			if (HydraulicStructures.HasSourceSinks && HydraulicStructures.SourceSinks.TryGetValue(cA2, out (double, double, Geospatial.Vectors.Vector) valueTuple) && valueTuple.Item1 > 0.0)
			{
				double dotProduct2 = valueTuple.Item3.X * vector.X + valueTuple.Item3.Y * vector.Y;
				computed -= dotProduct2 - valueTuple.Item1 * ws.FaceVelocity[fIdx];
			}
			advection += _hmesh.FaceAlphaCellAInv[fIdx].LR[ordinal] * computed;
			ordinal++;
			cA2 = cB;
		}
		advection *= faceHbarInvSp[fIdx];
	}

	internal void ComputeFaceBTermCoriolisStartStop(MeshWaterState wsOld, double dtfCoriolis, int start, int stop)
	{
		ReadOnlySpan<double> faceAreaSp = wsOld.FaceArea.AsSpan();
		ReadOnlySpan<double> xSp = wsOld.FaceVelTangA.AsSpan();
		ReadOnlySpan<FaceFlag> faceFlagsSp = FaceFlags.AsSpan();
		Span<double> faceBTermSp = FaceBTerm.AsSpan();
		if (PartGrid.Enabled)
		{
			ReadOnlySpan<int> faceIndices = CollectionsMarshal.AsSpan(PartGrid.FaceIndices);
			for (int i = start; i <= stop; i++)
			{
				int index = faceIndices[i];
				if (faceAreaSp[index] > 0.0 && faceFlagsSp[index] != 0)
				{
					faceBTermSp[index] += dtfCoriolis * xSp[index];
				}
			}
		}
		else
		{
			AddScaledWhereWetAndFlagged(dtfCoriolis, xSp, faceAreaSp, faceFlagsSp, faceBTermSp, start, stop);
		}
	}

	private void ComputeFaceBTermAirPressStartStop(MeshWaterState wsOld, double dt, int start, int stop)
	{
		ReadOnlySpan<double> faceAreaSp = wsOld.FaceArea.AsSpan();
		ReadOnlySpan<double> xSp = BoundaryConditions.AirPressure.FaceGradAirPressure.AsSpan();
		ReadOnlySpan<FaceFlag> faceFlagsSp = FaceFlags.AsSpan();
		Span<double> faceBTermSp = FaceBTerm.AsSpan();
		double dtOverWaterDensity = dt / WaterDensity;
		if (PartGrid.Enabled)
		{
			ReadOnlySpan<int> faceIndices = CollectionsMarshal.AsSpan(PartGrid.FaceIndices);
			for (int i = start; i <= stop; i++)
			{
				int index = faceIndices[i];
				if (faceAreaSp[index] > 0.0 && faceFlagsSp[index] != 0)
				{
					faceBTermSp[index] -= dtOverWaterDensity * xSp[index];
				}
			}
		}
		else
		{
			AddScaledWhereWetAndFlagged(0.0 - dtOverWaterDensity, xSp, faceAreaSp, faceFlagsSp, faceBTermSp, start, stop);
		}
	}

	private void ComputeFaceBTermWindStartStop(MeshWaterState wsOld, double dt, int start, int stop)
	{
		ReadOnlySpan<double> faceAreaSp = wsOld.FaceArea.AsSpan();
		ReadOnlySpan<float> xSp = BoundaryConditions.Wind.FaceWindStressN.AsSpan();
		ReadOnlySpan<float> ySp = faceHbarInv.AsSpan();
		ReadOnlySpan<FaceFlag> faceFlagsSp = FaceFlags.AsSpan();
		Span<double> faceBTermSp = FaceBTerm.AsSpan();
		double dtOverWaterDensity = dt / WaterDensity;
		if (PartGrid.Enabled)
		{
			ReadOnlySpan<int> faceIndices = CollectionsMarshal.AsSpan(PartGrid.FaceIndices);
			for (int i = start; i <= stop; i++)
			{
				int index = faceIndices[i];
				if (faceAreaSp[index] > 0.0 && faceFlagsSp[index] != 0)
				{
					faceBTermSp[index] += dtOverWaterDensity * (double)xSp[index] * (double)ySp[index];
				}
			}
		}
		else
		{
			AddScaledProductWhereWetAndFlagged(dtOverWaterDensity, xSp, ySp, faceAreaSp, faceFlagsSp, faceBTermSp, start, stop);
		}
	}

	private void ComputeFaceBTermTurbulence(MeshWaterState wsOld, double dt, int faceCount)
	{
		int loopSize = SolverImp.GetLoopSize(4000, faceCount);
		threadFor.Run(faceCount, (int start, int stop) =>
		{
			ComputeFaceBTermTurbulenceStartStop(wsOld, dt, start, stop);
		}, loopSize);
	}

	private void ComputeFaceBTermTurbulenceStartStop(MeshWaterState wsOld, double dt, int start, int stop)
	{
		FaceAB[] faceAlphaCellAInv = _hmesh.FaceAlphaCellAInv;
		Vector2[] faceNormalF = _hmesh.FaceNormalF;
		ReadOnlySpan<int> faceCellASp = _meshConnectivity.FaceCellA.AsSpan();
		ReadOnlySpan<int> faceCellBSp = _meshConnectivity.FaceCellB.AsSpan();
		ReadOnlySpan<Vector2> cellDiffVarExpSp = CellDiffVarExp.AsSpan();
		ReadOnlySpan<double> faceArea = wsOld.FaceArea.AsSpan();
		ReadOnlySpan<float> faceHbarInvSp = faceHbarInv.AsSpan();
		Span<double> faceBTerm = FaceBTerm.AsSpan();
		ReadOnlySpan<int> faceIndices = CollectionsMarshal.AsSpan(PartGrid.FaceIndices);
		for (int i = start; i <= stop; i++)
		{
			int faceIndex = faceIndices[i];
			if (faceArea[faceIndex] > 0.0)
			{
				ComputeDiffusionFace(faceIndex, faceHbarInvSp, out var diffusion, faceCellASp, faceCellBSp, faceNormalF, cellDiffVarExpSp, faceAlphaCellAInv);
				faceBTerm[faceIndex] += dt * (double)diffusion;
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void ComputeDiffusionFace(int fIdx, ReadOnlySpan<float> faceHbarInvSp, out float diffusion, ReadOnlySpan<int> faceCellASp, ReadOnlySpan<int> faceCellBSp, Vector2[] fNorms, ReadOnlySpan<Vector2> cellDiffVarExpSp, FaceAB[] faceAlphaCellAInv)
	{
		int cellA = faceCellASp[fIdx];
		int cellB = faceCellBSp[fIdx];
		diffusion = Solver.ComputeFaceTurbulentMixing(cellA, cellB, faceHbarInvSp[fIdx], fNorms[fIdx], faceAlphaCellAInv[fIdx], cellDiffVarExpSp);
	}

	public void OverrideWeirFaceBTerm(MeshWaterState ws, double dt)
	{
		Span<double> faceBTerm = FaceBTerm.AsSpan();
		Span<float> faceAdvVarImp = FaceAdvVarImp.AsSpan();
		double[] faceInverseDistances = FaceInverseDistances;
		double dtGravTerm = dt * grav * (1.0 - Control.ImplicitFactor);
		foreach (Weir weir in HydraulicStructures.Weirs)
		{
			for (int i = 0; i < weir.Faces.Length; i++)
			{
				int faceIndex = weir.Faces[i];
				double weirCrest = weir.WeirCrest[i];
				Face face = _mesh.Faces[faceIndex];
				int cellA = face.cellA;
				int cellB = face.cellB;
				double cellElevation = ws.CellElevation[cellA];
				double cellElevation2 = ws.CellElevation[cellB];
				double weirApproachVel = weir.GetWeirApproachVel(ws, _mesh, _hmesh, cellA, faceIndex);
				double weirApproachVel2 = weir.GetWeirApproachVel(ws, _mesh, _hmesh, cellB, faceIndex);
				double cellElevationWeirApproachVelTerm = cellElevation + weirApproachVel * weirApproachVel / (2.0 * grav);
				double cellElevation2WeirApproachVel2Term = cellElevation2 + weirApproachVel2 * weirApproachVel2 / (2.0 * grav);
				if (Math.Max(cellElevation, cellElevation2) <= weirCrest || ws.FaceArea[faceIndex] <= 0.0)
				{
					faceBTerm[faceIndex] = 0.0;
					faceAdvVarImp[faceIndex] = 0f;
					weir.IsFaceWet[i] = false;
					weir.SavedWeirVal[i] = 0.0;
					continue;
				}
				bool sign = cellElevationWeirApproachVelTerm > cellElevation2WeirApproachVel2Term;
				double cellElevation3 = (sign ? cellElevation : cellElevation2);
				double cellElevation4 = (sign ? cellElevation2 : cellElevation);
				double weirApproachVel3 = (sign ? weirApproachVel : weirApproachVel2);
				double signTerm = (sign ? 1.0 : (-1.0));
				weir.IsFaceWet[i] = true;
				double faceVelocity = ws.FaceVelocity[faceIndex];
				double faceVelocity2 = faceVelocity;
				if (Control.advectionMethod != SolverControl.AdvectionMethod.None)
				{
					faceAdvVarImp[faceIndex] = (float)(signTerm * 0.5 * faceVelocity * faceInverseDistances[faceIndex]);
					double weirApproachVel3FaceInverseDistancesTerm = (0.0 - signTerm) * 0.5 * weirApproachVel3 * weirApproachVel3 * faceInverseDistances[faceIndex];
					faceVelocity2 -= dt * weirApproachVel3FaceInverseDistancesTerm;
				}
				double kMinorLossWeirTerm = signTerm * 0.5 * weir.KMinorLoss * faceVelocity * faceVelocity * faceInverseDistances[faceIndex];
				faceVelocity2 -= dt * kMinorLossWeirTerm;
				double cellElevation3WeirCrestTerm = cellElevation3 - weirCrest + weirApproachVel3 * weirApproachVel3 / (2.0 * grav);
				double computed = 2.0 / 3.0 * cellElevation3WeirCrestTerm;
				double savedWeirVal = signTerm * Math.Max(0.0, weirCrest + computed - cellElevation4);
				weir.SavedWeirVal[i] = savedWeirVal;
				weir.WeirDepth[i] = Math.Max(computed, cellElevation4 - weirCrest);
				double faceNormalGradient = ComputeFaceNormalGradient(ws, faceIndex, faceInverseDistances);
				faceNormalGradient += savedWeirVal * faceInverseDistances[faceIndex];
				faceVelocity2 -= dtGravTerm * faceNormalGradient;
				faceBTerm[faceIndex] = faceVelocity2;
			}
		}
	}

	public void OverrideWeirFaceFrict(MeshWaterState ws)
	{
		Span<float> faceNonlinFrictCoeff = ws.FaceNonlinFrictCoeff.AsSpan();
		foreach (Weir weir in HydraulicStructures.Weirs)
		{
			for (int i = 0; i < weir.Faces.Length; i++)
			{
				int index = weir.Faces[i];
				faceNonlinFrictCoeff[index] = 0f;
			}
		}
	}

	protected override bool PreSolveInner(double EOPTime, double dt)
	{
		base.PreSolveInner(EOPTime, dt);
		Stopwatch stopwatch = Stopwatch.StartNew();
		Convergence.OuterConv.IncrementIterations();
		Convergence.ResetInner();
		MeshWaterState waterState = _waterState;
		MeshWaterState oldState = _oldState;
		if (HasNonOrthogonalCorrection)
		{
			UpdateFaceGradientData(waterState);
			UpdateFaceBTermWithDeferredCorrection(waterState, dt);
		}
		if (Convergence.OuterConv.Iterations > 1)
		{
			ComputeFaceVelocityAvg(waterState, oldState);
		}
		if (HydraulicStructures.HasWeirs)
		{
			OverrideWeirFaceFrict(waterState);
		}
		UpdateFaceMatrixCoeffRHS(waterState, oldState, dt);
		ComputeAssembly(EOPTime, dt);
		PreComputeNewton(waterState);
		ticks[20] += stopwatch.ElapsedTicks;
		return true;
	}

	protected override void PostSolveOuter(double dt)
	{
		base.PostSolveOuter(dt);
		UpdateCellHydraulicDepth(_waterState);
	}

	private void UpdateCellHydraulicDepth(MeshWaterState ws)
	{
		int cellCount = PartGrid.CellCount;
		int loopSize = SolverImp.GetLoopSize(3000, cellCount);
		threadFor.Run(cellCount, (int start, int stop) =>
		{
			UpdateCellHydraulicDepthStartStop(ws, start, stop);
		}, loopSize);
	}

	private void UpdateCellHydraulicDepthStartStop(MeshWaterState ws, int start, int stop)
	{
		Span<float> cellHydraulicDepth = ws.CellHydraulicDepth.AsSpan();
		int[] cellPriorIndexes = ws.CellPriorIndexes;
		ReadOnlySpan<double> cellElevation = ws.CellElevation.AsSpan();
		ReadOnlySpan<double> cellVolume = ws.CellVolume.AsSpan();
		ReadOnlySpan<int> cellIndices = PartGrid.CellIndices.AsSpan();
		for (int i = start; i <= stop; i++)
		{
			int cellIndex = cellIndices[i];
			double wsel = cellElevation[cellIndex];
			double volume = cellVolume[cellIndex];
			_propertyTables.GetCellHydraulicDepth(cellIndex, wsel, cellPriorIndexes, in volume, out var hydraulicDepth);
			cellHydraulicDepth[cellIndex] = (float)hydraulicDepth;
		}
	}

	private void ComputeFaceDepths(MeshWaterState wsOld)
	{
		int faceCount = PartGrid.FaceCount;
		int loopSize = SolverImp.GetLoopSize(4000, faceCount);
		threadFor.Run(faceCount, (int start, int stop) =>
		{
			ComputeFaceDepthsStartStop(wsOld, start, stop);
		}, loopSize);
	}

	internal void ComputeFaceDepthsStartStop(MeshWaterState wsOld, int start, int stop)
	{
		ReadOnlySpan<int> faceIndices = CollectionsMarshal.AsSpan(PartGrid.FaceIndices);
		ReadOnlySpan<float> cellHydraulicDepth = wsOld.CellHydraulicDepth.AsSpan();
		ReadOnlySpan<double> faceArea = wsOld.FaceArea.AsSpan();
		ReadOnlySpan<FaceAB> faceAlpha = _hmesh.FaceAlpha.AsSpan();
		Span<float> faceHbarInvSpan = faceHbarInv.AsSpan();
		ReadOnlySpan<int> faceCellA = _meshConnectivity.FaceCellA.AsSpan();
		ReadOnlySpan<int> faceCellB = _meshConnectivity.FaceCellB.AsSpan();
		for (int i = start; i <= stop; i++)
		{
			int fIdx = faceIndices[i];
			if (faceArea[fIdx] <= 0.0)
			{
				faceHbarInvSpan[fIdx] = 1f;
				continue;
			}
			int faceCellB2 = faceCellB[fIdx];
			double valueAFaceABTerm;
			if (faceCellB2 >= 0)
			{
				int index = faceCellA[fIdx];
				FaceAB faceAB = faceAlpha[fIdx];
				valueAFaceABTerm = faceAB.ValueA * (double)cellHydraulicDepth[index] + faceAB.ValueB * (double)cellHydraulicDepth[faceCellB2];
			}
			else
			{
				valueAFaceABTerm = _propertyTables.ComputeFaceHydraulicDepth(in fIdx, wsOld.FacePriorIndexes, in wsOld.FaceElevation[fIdx]);
			}
			faceHbarInvSpan[fIdx] = (float)(1.0 / Math.Max(valueAFaceABTerm, Control.dzMinAdvection));
		}
	}

	private void ComputeFaceVelocityAvg(MeshWaterState ws, MeshWaterState wsOld)
	{
		int faceCount = PartGrid.FaceCount;
		int loopSize = SolverImp.GetLoopSize(4000, faceCount);
		threadFor.Run(faceCount, (int start, int stop) =>
		{
			ComputeFaceVelocityAvgStartStop(ws, wsOld, start, stop);
		}, loopSize);
	}

	internal void ComputeFaceVelocityAvgStartStop(MeshWaterState ws, MeshWaterState wsOld, int start, int stop)
	{
		ReadOnlySpan<double> faceVelocitySp = ws.FaceVelocity.AsSpan();
		ReadOnlySpan<double> faceVelocityOldSp = wsOld.FaceVelocity.AsSpan();
		Span<double> faceVelNormAvgSp = FaceVelNormAvg.AsSpan();
		ReadOnlySpan<double> faceVelTangASp = ws.FaceVelTangA.AsSpan();
		ReadOnlySpan<double> faceVelTangBSp = ws.FaceVelTangB.AsSpan();
		ReadOnlySpan<double> faceVelTangOldASp = wsOld.FaceVelTangA.AsSpan();
		ReadOnlySpan<double> faceVelTangOldBSp = wsOld.FaceVelTangB.AsSpan();
		Span<float> faceVelTangAvgASp = FaceVelTangAvgA.AsSpan();
		Span<float> faceVelTangAvgBSp = FaceVelTangAvgB.AsSpan();
		Span<float> faceVelMagAvgSp = FaceVelMagAvg.AsSpan();
		if (PartGrid.Enabled)
		{
			ReadOnlySpan<int> faceIndices = CollectionsMarshal.AsSpan(PartGrid.FaceIndices);
			for (int i = start; i <= stop; i++)
			{
				ComputeFaceVelocityAvgFace(faceIndices[i], faceVelocitySp, faceVelocityOldSp, faceVelTangASp, faceVelTangBSp, faceVelTangOldASp, faceVelTangOldBSp, faceVelNormAvgSp, faceVelTangAvgASp, faceVelTangAvgBSp, faceVelMagAvgSp);
			}
		}
		else
		{
			ComputeFaceVelocityAvg(faceVelocitySp, faceVelocityOldSp, faceVelTangASp, faceVelTangBSp, faceVelTangOldASp, faceVelTangOldBSp, faceVelNormAvgSp, faceVelTangAvgASp, faceVelTangAvgBSp, faceVelMagAvgSp, start, stop);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void ComputeFaceVelocityAvgFace(int fIdx, ReadOnlySpan<double> faceVelocitySp, ReadOnlySpan<double> faceVelocityOldSp, ReadOnlySpan<double> faceVelTangASp, ReadOnlySpan<double> faceVelTangBSp, ReadOnlySpan<double> faceVelTangOldASp, ReadOnlySpan<double> faceVelTangOldBSp, Span<double> faceVelNormAvgSp, Span<float> faceVelTangAvgASp, Span<float> faceVelTangAvgBSp, Span<float> faceVelMagAvgSp)
	{
		double faceVelNormAvgSp2 = 0.5 * (faceVelocitySp[fIdx] + faceVelocityOldSp[fIdx]);
		double faceVelTangAvgASp2 = 0.5 * (faceVelTangASp[fIdx] + faceVelTangOldASp[fIdx]);
		double faceVelTangAvgBSp2 = 0.5 * (faceVelTangBSp[fIdx] + faceVelTangOldBSp[fIdx]);
		faceVelNormAvgSp[fIdx] = faceVelNormAvgSp2;
		faceVelTangAvgASp[fIdx] = (float)faceVelTangAvgASp2;
		faceVelTangAvgBSp[fIdx] = (float)faceVelTangAvgBSp2;
		faceVelMagAvgSp[fIdx] = (float)Math.Sqrt(faceVelNormAvgSp2 * faceVelNormAvgSp2 + faceVelTangAvgASp2 * faceVelTangAvgASp2);
	}

	private void UpdateFaceMatrixCoeffRHS(MeshWaterState ws, MeshWaterState wsOld, double dt)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		int faceCount = PartGrid.FaceCount;
		int loopSize = SolverImp.GetLoopSize(4000, faceCount);
		double theta = Control.ImplicitFactor;
		double dt2Theta2negGrav = (0.0 - dt) * dt * grav * theta * theta;
		threadFor.Run(faceCount, (int start, int stop) =>
		{
			UpdateFaceMatrixCoeffRHSStartStop(ws, wsOld, dt, dt2Theta2negGrav, theta, start, stop);
		}, loopSize);
		// A dry crest also blocks the implicit continuity matrix. Zeroing only
		// the final reported velocity allows the pressure solve to move water
		// through a closed weir without a corresponding face flux.
		foreach (Weir weir in HydraulicStructures.Weirs)
		{
			for (int i = 0; i < weir.Faces.Length; i++)
			{
				if (!weir.IsFaceWet[i])
				{
					FaceCoeff[weir.Faces[i]] = 0.0;
					FaceRHS[weir.Faces[i]] = 0.0;
				}
			}
		}
		ticks[21] += stopwatch.ElapsedTicks;
	}

	internal void UpdateFaceMatrixCoeffRHSStartStop(MeshWaterState ws, MeshWaterState wsOld, double dt, double dt2Theta2negGrav, double theta, int start, int stop)
	{
		ReadOnlySpan<double> faceInvDistSp = FaceInverseDistances.AsSpan();
		ReadOnlySpan<double> faceAreaSp = ws.FaceArea;
		ReadOnlySpan<float> faceNonlinFrictCoeffSp = ws.FaceNonlinFrictCoeff.AsSpan();
		ReadOnlySpan<float> faceVelMagFrictSp = FaceVelMagFrict.AsSpan();
		ReadOnlySpan<float> faceAdvVarImpSp = FaceAdvVarImp.AsSpan();
		ReadOnlySpan<double> faceBTermSp = FaceBTermForCurrentIteration.AsSpan();
		ReadOnlySpan<double> faceVelocity = wsOld.FaceVelocity.AsSpan();
		Span<double> faceInvDiagASp = FaceInvDiagA.AsSpan();
		Span<double> faceInvDiagBSp = FaceInvDiagB.AsSpan();
		Span<double> faceCoeffSp = FaceCoeff.AsSpan();
		Span<double> faceRHSSp = FaceRHS.AsSpan();
		float linFrictCoeff = (float)Control.linFrictCoeff;
		if (PartGrid.Enabled)
		{
			ReadOnlySpan<int> faceIndices = CollectionsMarshal.AsSpan(PartGrid.FaceIndices);
			for (int i = start; i <= stop; i++)
			{
				UpdateFaceMatrixCoeffRHSFace(faceIndices[i], dt, dt2Theta2negGrav, theta, faceAreaSp, linFrictCoeff, faceNonlinFrictCoeffSp, faceVelMagFrictSp, faceAdvVarImpSp, faceInvDistSp, faceBTermSp, faceVelocity, faceInvDiagASp, faceInvDiagBSp, faceCoeffSp, faceRHSSp);
			}
		}
		else
		{
			UpdateFaceMatrixCoeffRHS(dt, dt2Theta2negGrav, theta, linFrictCoeff, faceAreaSp, faceNonlinFrictCoeffSp, faceVelMagFrictSp, faceAdvVarImpSp, faceInvDistSp, faceBTermSp, faceVelocity, faceInvDiagASp, faceInvDiagBSp, faceCoeffSp, faceRHSSp, start, stop);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void UpdateFaceMatrixCoeffRHSFace(int fIdx, double dt, double dt2Theta2negGrav, double theta, ReadOnlySpan<double> faceAreaSp, float linFrictCoeff, ReadOnlySpan<float> faceNonlinFrictCoeffSp, ReadOnlySpan<float> faceVelMagFrictSp, ReadOnlySpan<float> faceAdvVarImpSp, ReadOnlySpan<double> faceInvDistSp, ReadOnlySpan<double> faceBTermSp, ReadOnlySpan<double> oldFaceVel, Span<double> faceInvDiagASp, Span<double> faceInvDiagBSp, Span<double> faceCoeffSp, Span<double> faceRHSSp)
	{
		if (faceAreaSp[fIdx] > 0.0)
		{
			float linFrictCoeffFaceNonlinFrictCoeffSpTerm = linFrictCoeff + faceNonlinFrictCoeffSp[fIdx] * faceVelMagFrictSp[fIdx] + faceAdvVarImpSp[fIdx];
			double faceInvDiagASp2 = 1.0 / (1.0 + dt * (double)linFrictCoeffFaceNonlinFrictCoeffSpTerm);
			faceInvDiagASp[fIdx] = faceInvDiagASp2;
			faceCoeffSp[fIdx] = dt2Theta2negGrav * faceAreaSp[fIdx] * faceInvDiagASp2 * faceInvDistSp[fIdx];
			faceRHSSp[fIdx] = (0.0 - faceAreaSp[fIdx]) * dt * (theta * faceInvDiagASp2 * faceBTermSp[fIdx] + (1.0 - theta) * oldFaceVel[fIdx]);
		}
		else
		{
			faceInvDiagASp[fIdx] = (faceInvDiagBSp[fIdx] = 1.0);
			faceCoeffSp[fIdx] = 0.0;
			faceRHSSp[fIdx] = 0.0;
		}
	}

	public void ComputeCellAdvVar(MeshWaterState ws)
	{
		int cellCount = PartGrid.CellCount;
		threadFor.Run(cellCount, (int start, int stop) =>
		{
			ComputeCellAdvVarStartStop(ws, start, stop);
		});
	}

	internal void ComputeCellAdvVarStartStop(MeshWaterState ws, int start, int stop)
	{
		bool[] cellActivity = (PartGrid.Enabled ? PartGrid.CellActive : null);
		int[] faceToPartialFace = (PartGrid.Enabled ? PartGrid.FaceToPartialFace : null);
		ReadOnlySpan<Geospatial.Vectors.Vector> cellVelocity = ws.CellVelocity.AsSpan();
		ReadOnlySpan<double> faceVelocity = ws.FaceVelocity.AsSpan();
		ReadOnlySpan<double> faceArea = ws.FaceArea.AsSpan();
		ReadOnlySpan<int> neighborCells = _meshConnectivity.CellNeighborCells.AsSpan();
		ReadOnlySpan<int> neighborFaces = _meshConnectivity.CellNeighborFaces.AsSpan();
		ReadOnlySpan<int> faceOrientations = _meshConnectivity.CellFaceOrientations.AsSpan();
		int[] cellNeighborOffsets = _meshConnectivity.CellNeighborOffsets;
		Geospatial.Vectors.Vector[] faceNormal = _hmesh.FaceNormal;
		Geospatial.Vectors.Vector[,] faceNormal2 = LocalCoordinates.FaceNormal;
		ReadOnlySpan<int> cellIndices = CollectionsMarshal.AsSpan(PartGrid.CellIndices);
		for (int i = start; i <= stop; i++)
		{
			int cellIndex = cellIndices[i];
			double sourceFlow = 0.0;
			Geospatial.Vectors.Vector sourceMomentum = default;
			if (HydraulicStructures.HasSourceSinks && HydraulicStructures.SourceSinks.TryGetValue(cellIndex, out (double, double, Geospatial.Vectors.Vector) valueTuple) && valueTuple.Item1 > 0.0)
			{
				(sourceFlow, _, sourceMomentum) = valueTuple;
			}
			Solver.ComputeCellAccumulation(cellIndex, cellNeighborOffsets, neighborFaces, neighborCells, faceOrientations, faceVelocity, faceArea, cellVelocity, faceNormal, faceNormal2, CellFlags, LocalCoordinates, cellActivity, faceToPartialFace, _bcVelocityMap, sourceFlow, sourceMomentum, out var explicitVariable, out var implicitVariable);
			CellAdvVarExp[cellIndex] = explicitVariable;
			CellAdvVarImp[cellIndex] = implicitVariable;
		}
	}

	public void ComputeCellDiffVar(MeshWaterState ws)
	{
		ComputeCellTurbulentMixing(ws, PartGrid.CellIndices, CellDiffVarExp, null);
	}

	internal void ComputeCellDiffVarStartStop(MeshWaterState ws, int start, int stop)
	{
		ComputeCellTurbulentMixingStartStop(ws, PartGrid.CellIndices, CellDiffVarExp, null, start, stop);
	}

	protected override double ComputeFaceCoeff(MeshWaterState ws, int fIdx, double dt)
	{
		double faceNonlinFrictCoeffWsTerm = (double)ws.FaceNonlinFrictCoeff[fIdx] * (double)ws.FaceVelMag[fIdx] + (double)FaceAdvVarImp[fIdx];
		double dtTerm = 1.0 / (1.0 + dt * faceNonlinFrictCoeffWsTerm);
		double faceArea = ws.FaceArea[fIdx];
		double dtGravTerm = (0.0 - dt) * dt * grav * Control.ImplicitFactor * Control.ImplicitFactor;
		double faceInverseDistance = FaceInverseDistances[fIdx];
		return dtGravTerm * faceArea * dtTerm * faceInverseDistance;
	}

	protected override void ComputeAssemblyRow(int cIdx)
	{
		double faceCoeffSum = 0.0;
		int[] rowPointers = MatrixNewton.RowPointers;
		CellRHS[cIdx] = _oldState.CellVolume[cIdx];
		int rowPointerStart = rowPointers[cIdx];
		int rowPointerEnd = rowPointers[cIdx + 1];
		Span<double> doubleSpan = MatrixNewton.Values;
		Span<int> stencilFaces = StencilFaces.AsSpan();
		Span<int> stencilSigns = StencilSigns.AsSpan();
		for (int i = rowPointerStart; i < rowPointerEnd; i++)
		{
			int stencilFaceIndex = stencilFaces[i];
			if (stencilFaceIndex >= 0)
			{
				double faceCoeff = FaceCoeff[stencilFaceIndex];
				doubleSpan[i] = faceCoeff;
				faceCoeffSum -= faceCoeff;
				CellRHS[cIdx] += (double)stencilSigns[i] * FaceRHS[stencilFaceIndex];
			}
		}
		MatrixDiagonal[cIdx] = faceCoeffSum;
	}

	protected override void OverrideAssemblyExternalStageBC(MeshWaterState ws, double dt, int fIdx)
	{
		int faceCellA = _meshConnectivity.FaceCellA[fIdx];
		double faceCoeff = ComputeFaceCoeff(ws, fIdx, dt);
		CellRHS[faceCellA] += FaceRHS[fIdx] - faceCoeff * ws.PinnedFaceStage[fIdx];
		MatrixDiagonal[faceCellA] -= faceCoeff;
	}

	protected override void OverrideAssemblyWeir(MeshWaterState ws, double dt, Weir weir)
	{
		ReadOnlySpan<double> faceArea = ws.FaceArea.AsSpan();
		ReadOnlySpan<double> faceInvDiagA = FaceInvDiagA.AsSpan();
		double[] faceInverseDistances = FaceInverseDistances;
		double dtGravTerm = dt * dt * grav * Control.ImplicitFactor * Control.ImplicitFactor;
		for (int i = 0; i < weir.Faces.Length; i++)
		{
			if (!weir.IsFaceWet[i]) continue;
			int faceIndex = weir.Faces[i];
			int faceCellA = _meshConnectivity.FaceCellA[faceIndex];
			int faceCellB = _meshConnectivity.FaceCellB[faceIndex];
			double savedWeirVal = weir.SavedWeirVal[i];
			double faceAreaFaceIndexTerm = dtGravTerm * faceArea[faceIndex] * savedWeirVal * faceInverseDistances[faceIndex] * faceInvDiagA[faceIndex];
			CellRHS[faceCellA] += faceAreaFaceIndexTerm;
			CellRHS[faceCellB] -= faceAreaFaceIndexTerm;
		}
	}

	protected override void OverwriteWeirFaceDepths(MeshWaterState ws)
	{
		foreach (Weir weir in HydraulicStructures.Weirs)
		{
			for (int i = 0; i < weir.Faces.Length; i++)
			{
				int fIdx = weir.Faces[i];
				if (weir.IsFaceWet[i])
				{
					double wsel = weir.WeirCrest[i] + weir.WeirDepth[i];
					_propertyTables.UpdateFaceONE(in fIdx, ws.FacePriorIndexes, ws.FaceElevation, ws.FaceArea, ws.FaceHydraulicRadius, ws.FaceHydraulicDepth, ws.FaceMannings, ws.FaceNonlinFrictCoeff, ws.FaceShearFluxCoeff, ws.FaceConveyance, in wsel);
				}
			}
		}
	}

	protected override void ComputeFaceFlowsAndVelocities(double dt)
	{
		MeshWaterState ws = _waterState;
		int faceCount = PartGrid.FaceCount;
		int loopSize = SolverImp.GetLoopSize(5000, faceCount);
		double gravDtTheta = grav * dt * Control.ImplicitFactor;
		double[] faceDistInv = FaceInverseDistances;
		threadFor.Run(faceCount, (int start, int stop) =>
		{
			ComputeFaceFlowAndNormalVelocityStartStop(ws, gravDtTheta, faceDistInv, start, stop);
		}, loopSize);
		if (HydraulicStructures.HasWeirs)
		{
			ComputeWeirFaceFlowsAndVelocities(ws, faceDistInv, gravDtTheta);
		}
		switch (Control.faceTangentialVelocityMethod)
		{
		case SolverControl.FaceTangentialVelocityMethod.CellBased:
			ComputeFaceVelCompCellbased(ws);
			break;
		default:
			ComputeFaceVelCompWSL(ws);
			break;
		case SolverControl.FaceTangentialVelocityMethod.Momentum:
			break;
		}
		Check(ws, _propertyTables);
	}

	internal void ComputeFaceFlowAndNormalVelocityStartStop(MeshWaterState ws, double gravDtTheta, double[] faceDistInv, int start, int stop)
	{
		ReadOnlySpan<double> faceAreaSp = ws.FaceArea.AsSpan();
		ReadOnlySpan<double> faceInvDiagASp = FaceInvDiagA.AsSpan();
		ReadOnlySpan<double> faceBTermSp = FaceBTermForCurrentIteration.AsSpan();
		ReadOnlySpan<FaceFlag> faceFlagsSp = FaceFlags.AsSpan();
		Span<double> faceFlowSp = ws.FaceFlow.AsSpan();
		Span<double> faceVelSp = ws.FaceVelocity.AsSpan();
		ReadOnlySpan<int> faceIndices = CollectionsMarshal.AsSpan(PartGrid.FaceIndices);
		for (int i = start; i <= stop; i++)
		{
			int fIdx = faceIndices[i];
			ComputeFaceFlowAndNormalVelocityOne(ws, faceDistInv, faceFlagsSp, faceFlowSp, faceVelSp, faceAreaSp, faceBTermSp, faceInvDiagASp, gravDtTheta, fIdx);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ComputeFaceFlowAndNormalVelocityOne(MeshWaterState ws, double[] faceDistInv, ReadOnlySpan<FaceFlag> faceFlagsSp, Span<double> faceFlowSp, Span<double> faceVelSp, ReadOnlySpan<double> faceAreaSp, ReadOnlySpan<double> faceBTermSp, ReadOnlySpan<double> faceInvDiagASp, double gravThetaDt, int fIdx)
	{
		if (faceFlagsSp[fIdx] == FaceFlag.FORCE_FLOW)
		{
			if (faceAreaSp[fIdx] > 0.0)
			{
				faceVelSp[fIdx] = faceFlowSp[fIdx] / faceAreaSp[fIdx];
			}
		}
		else if (faceAreaSp[fIdx] > 0.0 && (faceFlagsSp[fIdx] & FaceFlag.COMPUTE_FLOW) != 0)
		{
			double faceNormalGradient = ComputeFaceNormalGradient(ws, fIdx, faceDistInv);
			double faceBTermSpFIdxTerm = faceBTermSp[fIdx] - gravThetaDt * faceNormalGradient;
			double faceVelSp2 = faceInvDiagASp[fIdx] * faceBTermSpFIdxTerm;
			faceVelSp[fIdx] = faceVelSp2;
			faceFlowSp[fIdx] = faceVelSp2 * faceAreaSp[fIdx];
		}
		else
		{
			faceFlowSp[fIdx] = 0.0;
			faceVelSp[fIdx] = 0.0;
		}
	}

	private void ComputeWeirFaceFlowsAndVelocities(MeshWaterState ws, double[] faceDistInv, double gravThetaDt)
	{
		ReadOnlySpan<double> faceInvDiagA = FaceInvDiagA.AsSpan();
		ReadOnlySpan<double> faceBTerm = FaceBTerm.AsSpan();
		Span<double> faceFlow = ws.FaceFlow.AsSpan();
		Span<double> faceVelocity = ws.FaceVelocity.AsSpan();
		ReadOnlySpan<double> faceArea = ws.FaceArea.AsSpan();
		foreach (Weir weir in HydraulicStructures.Weirs)
		{
			for (int i = 0; i < weir.Faces.Length; i++)
			{
				int faceIndex = weir.Faces[i];
				if (!weir.IsFaceWet[i])
				{
					faceFlow[faceIndex] = 0.0;
					faceVelocity[faceIndex] = 0.0;
					continue;
				}
				double faceNormalGradient = ComputeFaceNormalGradient(ws, faceIndex, faceDistInv);
				double savedWeirVal = weir.SavedWeirVal[i];
				faceNormalGradient += savedWeirVal * faceDistInv[faceIndex];
				double faceBTermFaceIndexTerm = faceBTerm[faceIndex] - gravThetaDt * faceNormalGradient;
				double faceVelocity2 = faceInvDiagA[faceIndex] * faceBTermFaceIndexTerm;
				faceVelocity[faceIndex] = faceVelocity2;
				faceFlow[faceIndex] = faceVelocity2 * faceArea[faceIndex];
			}
		}
	}

	public bool Check(MeshWaterState ws, MeshPropertyTables props)
	{
		int faceCount = _mesh.FaceCount;
		bool enabled = PartGrid.Enabled;
		Span<int> faceToPartialFace = PartGrid.FaceToPartialFace.AsSpan();
		for (int i = 0; i < faceCount; i++)
		{
			if (ws.FaceVelocity[i] != 0.0 && ws.FaceArea[i] == 0.0)
			{
				return false;
			}
			if (enabled && faceToPartialFace[i] < 0 && (ws.FaceVelocity[i] != 0.0 || ws.FaceArea[i] > 0.0))
			{
				return false;
			}
		}
		return true;
	}

	private void ComputeFaceVelCompWSL(MeshWaterState ws)
	{
		int faceCount = PartGrid.FaceCount;
		int loopSize = SolverImp.GetLoopSize(5000, faceCount);
		threadFor.Run(faceCount, (int start, int stop) =>
		{
			ComputeFaceVelCompWSLStartStop(ws, start, stop);
		}, loopSize);
	}

	internal void ComputeFaceVelCompWSLStartStop(MeshWaterState ws, int start, int stop)
	{
		Span<double> faceVelNormSp = ws.FaceVelocity.AsSpan();
		Span<double> faceVelTangASp = ws.FaceVelTangA.AsSpan();
		Span<double> faceVelTangBSp = ws.FaceVelTangB.AsSpan();
		Span<float> faceVelMagSp = ws.FaceVelMag.AsSpan();
		ReadOnlySpan<double> faceAreaSp = ws.FaceArea.AsSpan();
		ReadOnlySpan<FaceFlag> faceFlagsSp = FaceFlags.AsSpan();
		FaceFlag forceFlow = FaceFlag.FORCE_FLOW;
		ReadOnlySpan<int> faceIndices = CollectionsMarshal.AsSpan(PartGrid.FaceIndices);
		for (int i = start; i <= stop; i++)
		{
			int fIdx = faceIndices[i];
			ComputeFaceVelCompWSLOne(ws, fIdx, forceFlow, faceVelNormSp, faceVelTangASp, faceVelTangBSp, faceVelMagSp, faceAreaSp, faceFlagsSp);
		}
	}

	private void ComputeFaceVelCompWSLOne(MeshWaterState ws, int fIdx, FaceFlag forceFlow, Span<double> faceVelNormSp, Span<double> faceVelTangASp, Span<double> faceVelTangBSp, Span<float> faceVelMagSp, ReadOnlySpan<double> faceAreaSp, ReadOnlySpan<FaceFlag> faceFlagsSp)
	{
		if (faceFlagsSp[fIdx] == forceFlow)
		{
			faceVelTangASp[fIdx] = 0.0;
			faceVelTangBSp[fIdx] = 0.0;
			faceVelMagSp[fIdx] = (float)Math.Abs(faceVelNormSp[fIdx]);
		}
		else if (faceAreaSp[fIdx] > 0.0)
		{
			float faceVelNormSp2 = (float)faceVelNormSp[fIdx];
			float faceVelTangBSp2 = (float)_hmesh.FaceComputes.Tangential(fIdx, faceVelNormSp);
			faceVelTangASp[fIdx] = (faceVelTangBSp[fIdx] = faceVelTangBSp2);
			faceVelMagSp[fIdx] = MathF.Sqrt(faceVelNormSp2 * faceVelNormSp2 + faceVelTangBSp2 * faceVelTangBSp2);
		}
		else
		{
			faceVelTangASp[fIdx] = HydraulicMeshComputer.FaceComputes.TangentialSideA(fIdx, faceVelNormSp);
			faceVelTangBSp[fIdx] = HydraulicMeshComputer.FaceComputes.TangentialSideB(fIdx, faceVelNormSp);
			faceVelMagSp[fIdx] = 0f;
		}
	}

	private void ComputeFaceVelCompCellbased(MeshWaterState ws)
	{
		int faceCount = PartGrid.FaceCount;
		int loopSize = SolverImp.GetLoopSize(5000, faceCount);
		threadFor.Run(faceCount, (int start, int stop) =>
		{
			ComputeFaceVelCompCellbasedStartStop(ws, start, stop);
		}, loopSize);
	}

	private void ComputeFaceVelCompCellbasedStartStop(MeshWaterState ws, int start, int stop)
	{
		ReadOnlySpan<double> faceVelNormSp = ws.FaceVelocity.AsSpan();
		Span<double> faceVelTangASp = ws.FaceVelTangA.AsSpan();
		Span<double> faceVelTangBSp = ws.FaceVelTangB.AsSpan();
		Span<float> faceVelMagSp = ws.FaceVelMag.AsSpan();
		ReadOnlySpan<double> faceAreaSp = ws.FaceArea.AsSpan();
		Geospatial.Vectors.Vector[] cellVelocity = ws.CellVelocity;
		ReadOnlySpan<FaceFlag> faceFlagsSp = FaceFlags.AsSpan();
		ReadOnlySpan<ComputeControl.CellFlag> cellFlagsSp = CellFlags.AsSpan();
		FaceFlag forceFlow = FaceFlag.FORCE_FLOW;
		ComputeControl.CellFlag cellFlagNone = ComputeControl.CellFlag.None;
		ReadOnlySpan<int> faceCellASp = _meshConnectivity.FaceCellA.AsSpan();
		ReadOnlySpan<int> faceCellBSp = _meshConnectivity.FaceCellB.AsSpan();
		ReadOnlySpan<int> faceIndices = CollectionsMarshal.AsSpan(PartGrid.FaceIndices);
		for (int i = start; i <= stop; i++)
		{
			int fIdx = faceIndices[i];
			ComputeFaceVelCompCellbasedOne(fIdx, forceFlow, cellFlagNone, faceCellASp, faceCellBSp, cellVelocity, faceVelNormSp, faceVelTangASp, faceVelTangBSp, faceVelMagSp, faceAreaSp, faceFlagsSp, cellFlagsSp);
		}
	}

	private void ComputeFaceVelCompCellbasedOne(int fIdx, FaceFlag forceFlow, ComputeControl.CellFlag cellFlagNone, ReadOnlySpan<int> faceCellASp, ReadOnlySpan<int> faceCellBSp, Geospatial.Vectors.Vector[] cellVelocity, ReadOnlySpan<double> faceVelNormSp, Span<double> faceVelTangASp, Span<double> faceVelTangBSp, Span<float> faceVelMagSp, ReadOnlySpan<double> faceAreaSp, ReadOnlySpan<FaceFlag> faceFlagsSp, ReadOnlySpan<ComputeControl.CellFlag> cellFlagsSp)
	{
		if (faceFlagsSp[fIdx] == forceFlow)
		{
			faceVelTangASp[fIdx] = 0.0;
			faceVelTangBSp[fIdx] = 0.0;
			faceVelMagSp[fIdx] = (float)Math.Abs(faceVelNormSp[fIdx]);
			return;
		}
		int faceCellASp2 = faceCellASp[fIdx];
		int faceCellBSp2 = faceCellBSp[fIdx];
		Geospatial.Vectors.Vector vector = ((cellFlagsSp[faceCellASp2] != cellFlagNone) ? LocalCoordinates.RotateToFace(cellVelocity[faceCellASp2], fIdx, 0) : cellVelocity[faceCellASp2]);
		Geospatial.Vectors.Vector vector2;
		if (faceCellBSp2 < 0)
		{
			vector2 = Geospatial.Vectors.Vector.Zero;
		}
		else
		{
			vector2 = ((cellFlagsSp[faceCellBSp2] != cellFlagNone) ? LocalCoordinates.RotateToFace(cellVelocity[faceCellBSp2], fIdx, 1) : cellVelocity[faceCellBSp2]);
		}
		if (faceAreaSp[fIdx] > 0.0)
		{
			double faceVelNormSp2 = faceVelNormSp[fIdx];
			double faceVelTangBSp2 = 0.5 * (vector.Y + vector2.Y);
			faceVelTangASp[fIdx] = (faceVelTangBSp[fIdx] = faceVelTangBSp2);
			faceVelMagSp[fIdx] = (float)Math.Sqrt(faceVelNormSp2 * faceVelNormSp2 + faceVelTangBSp2 * faceVelTangBSp2);
		}
		else
		{
			faceVelTangASp[fIdx] = vector.Y;
			faceVelTangBSp[fIdx] = vector2.Y;
			faceVelMagSp[fIdx] = 0f;
		}
	}

	protected override void ResetRemovedFaces()
	{
		base.ResetRemovedFaces();
		ReadOnlySpan<int> faceIndicesRemoved = CollectionsMarshal.AsSpan(PartGrid.FaceIndicesRemoved);
		int length = faceIndicesRemoved.Length;
		MeshWaterState waterState = _waterState;
		Span<float> faceVelMag = waterState.FaceVelMag.AsSpan();
		Span<double> faceVelTangA = waterState.FaceVelTangA.AsSpan();
		Span<double> faceVelTangB = waterState.FaceVelTangB.AsSpan();
		Span<double> faceRHS = FaceRHS.AsSpan();
		for (int i = 0; i < length; i++)
		{
			int index = faceIndicesRemoved[i];
			faceVelMag[index] = 0f;
			faceVelTangA[index] = 0.0;
			faceVelTangB[index] = 0.0;
			faceRHS[index] = 0.0;
		}
	}

	protected override void ApplyFlowBoundaryConditionEGSlope(BoundaryCondition bc, double Qtot, MeshWaterState ws)
	{
		double minConveyance = Math.Abs(Qtot) / Math.Sqrt(bc.EGSlope);
		ApplyFlowBoundaryConditionByConveyance(bc, Qtot, ws, FaceConveyanceFrictCoeff, minConveyance, setVelocity: true);
	}

	protected override Func<MeshWaterState, int, double> GetFaceConveyanceFunc()
	{
		return FaceConveyanceFrictCoeff;
	}

	private double FaceConveyanceFrictCoeff(MeshWaterState ws, int fIdx)
	{
		double computed = 0.001;
		_propertyTables.GetFaceHydraulicRadiusAtWaterSurface(fIdx, ws.FaceElevation[fIdx], out var hydRad, out var _);
		if (hydRad <= 0.0)
		{
			return 0.0;
		}
		double gravHydRadTerm = Math.Max(grav * computed * computed * Math.Pow(hydRad, -1.3333333333333333), ws.FaceNonlinFrictCoeff[fIdx]);
		return ws.FaceArea[fIdx] * Math.Sqrt(grav / gravHydRadTerm);
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal static void UpdateFaceMatrixCoeffRHS(double dt, double dt2Theta2negGrav, double theta, float linFrictCoeff, ReadOnlySpan<double> faceAreaSp, ReadOnlySpan<float> faceNonlinFrictCoeffSp, ReadOnlySpan<float> faceVelMagFrictSp, ReadOnlySpan<float> faceAdvVarImpSp, ReadOnlySpan<double> faceInvDistSp, ReadOnlySpan<double> faceBTermSp, ReadOnlySpan<double> oldFaceVelSp, Span<double> faceInvDiagASp, Span<double> faceInvDiagBSp, Span<double> faceCoeffSp, Span<double> faceRHSSp, int start, int stop)
	{
		int count3 = stop + 1;
		int count = Vector<double>.Count;
		int count2 = Vector<float>.Count;
		Vector<float> vector = new Vector<float>(linFrictCoeff);
		Vector<double> dtV = new Vector<double>(dt);
		Vector<double> dt2Theta2negGravV = new Vector<double>(dt2Theta2negGrav);
		Vector<double> thetaV = new Vector<double>(theta);
		Vector<double> thetaCmpV = new Vector<double>(1.0 - theta);
		int i;
		for (i = start; i + count2 <= count3; i += count2)
		{
			System.Numerics.Vector.Widen(vector + new Vector<float>(faceNonlinFrictCoeffSp.Slice(i, count2)) * new Vector<float>(faceVelMagFrictSp.Slice(i, count2)) + new Vector<float>(faceAdvVarImpSp.Slice(i, count2)), out var low, out var high);
			CoeffRhsBlock(i, low, dtV, dt2Theta2negGravV, thetaV, thetaCmpV, faceAreaSp, faceInvDistSp, faceBTermSp, oldFaceVelSp, faceInvDiagASp, faceInvDiagBSp, faceCoeffSp, faceRHSSp);
			CoeffRhsBlock(i + count, high, dtV, dt2Theta2negGravV, thetaV, thetaCmpV, faceAreaSp, faceInvDistSp, faceBTermSp, oldFaceVelSp, faceInvDiagASp, faceInvDiagBSp, faceCoeffSp, faceRHSSp);
		}
		for (; i < count3; i++)
		{
			UpdateFaceMatrixCoeffRHSFace(i, dt, dt2Theta2negGrav, theta, faceAreaSp, linFrictCoeff, faceNonlinFrictCoeffSp, faceVelMagFrictSp, faceAdvVarImpSp, faceInvDistSp, faceBTermSp, oldFaceVelSp, faceInvDiagASp, faceInvDiagBSp, faceCoeffSp, faceRHSSp);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void CoeffRhsBlock(int i, Vector<double> cfD, Vector<double> dtV, Vector<double> dt2Theta2negGravV, Vector<double> thetaV, Vector<double> thetaCmpV, ReadOnlySpan<double> faceAreaSp, ReadOnlySpan<double> faceInvDistSp, ReadOnlySpan<double> faceBTermSp, ReadOnlySpan<double> oldFaceVelSp, Span<double> faceInvDiagASp, Span<double> faceInvDiagBSp, Span<double> faceCoeffSp, Span<double> faceRHSSp)
	{
		int count = Vector<double>.Count;
		Vector<double> vector = new Vector<double>(faceAreaSp.Slice(i, count));
		Vector<long> condition = System.Numerics.Vector.GreaterThan(vector, Vector<double>.Zero);
		Vector<double> vector2 = Vector<double>.One / (Vector<double>.One + dtV * cfD);
		Vector<double> left = dt2Theta2negGravV * vector * vector2 * new Vector<double>(faceInvDistSp.Slice(i, count));
		Vector<double> left2 = -vector * dtV * (thetaV * vector2 * new Vector<double>(faceBTermSp.Slice(i, count)) + thetaCmpV * new Vector<double>(oldFaceVelSp.Slice(i, count)));
		Vector<double> left3 = new Vector<double>((ReadOnlySpan<double>)faceInvDiagBSp.Slice(i, count));
		System.Numerics.Vector.ConditionalSelect(condition, vector2, Vector<double>.One).CopyTo(faceInvDiagASp.Slice(i, count));
		System.Numerics.Vector.ConditionalSelect(condition, left3, Vector<double>.One).CopyTo(faceInvDiagBSp.Slice(i, count));
		System.Numerics.Vector.ConditionalSelect(condition, left, Vector<double>.Zero).CopyTo(faceCoeffSp.Slice(i, count));
		System.Numerics.Vector.ConditionalSelect(condition, left2, Vector<double>.Zero).CopyTo(faceRHSSp.Slice(i, count));
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal static void ComputeFaceVelocityAvg(ReadOnlySpan<double> faceVelocitySp, ReadOnlySpan<double> faceVelocityOldSp, ReadOnlySpan<double> faceVelTangASp, ReadOnlySpan<double> faceVelTangBSp, ReadOnlySpan<double> faceVelTangOldASp, ReadOnlySpan<double> faceVelTangOldBSp, Span<double> faceVelNormAvgSp, Span<float> faceVelTangAvgASp, Span<float> faceVelTangAvgBSp, Span<float> faceVelMagAvgSp, int start, int stop)
	{
		int count3 = stop + 1;
		int count = Vector<double>.Count;
		int count2 = Vector<float>.Count;
		Vector<double> vector = new Vector<double>(0.5);
		int i;
		for (i = start; i + count2 <= count3; i += count2)
		{
			Vector<double> vector2 = vector * (new Vector<double>(faceVelocitySp.Slice(i, count)) + new Vector<double>(faceVelocityOldSp.Slice(i, count)));
			Vector<double> vector3 = vector * (new Vector<double>(faceVelocitySp.Slice(i + count, count)) + new Vector<double>(faceVelocityOldSp.Slice(i + count, count)));
			Vector<double> vector4 = vector * (new Vector<double>(faceVelTangASp.Slice(i, count)) + new Vector<double>(faceVelTangOldASp.Slice(i, count)));
			Vector<double> vector5 = vector * (new Vector<double>(faceVelTangASp.Slice(i + count, count)) + new Vector<double>(faceVelTangOldASp.Slice(i + count, count)));
			Vector<double> low = vector * (new Vector<double>(faceVelTangBSp.Slice(i, count)) + new Vector<double>(faceVelTangOldBSp.Slice(i, count)));
			Vector<double> high = vector * (new Vector<double>(faceVelTangBSp.Slice(i + count, count)) + new Vector<double>(faceVelTangOldBSp.Slice(i + count, count)));
			vector2.CopyTo(faceVelNormAvgSp.Slice(i, count));
			vector3.CopyTo(faceVelNormAvgSp.Slice(i + count, count));
			System.Numerics.Vector.Narrow(vector4, vector5).CopyTo(faceVelTangAvgASp.Slice(i, count2));
			System.Numerics.Vector.Narrow(low, high).CopyTo(faceVelTangAvgBSp.Slice(i, count2));
			System.Numerics.Vector.Narrow(System.Numerics.Vector.SquareRoot(vector2 * vector2 + vector4 * vector4), System.Numerics.Vector.SquareRoot(vector3 * vector3 + vector5 * vector5)).CopyTo(faceVelMagAvgSp.Slice(i, count2));
		}
		for (; i < count3; i++)
		{
			ComputeFaceVelocityAvgFace(i, faceVelocitySp, faceVelocityOldSp, faceVelTangASp, faceVelTangBSp, faceVelTangOldASp, faceVelTangOldBSp, faceVelNormAvgSp, faceVelTangAvgASp, faceVelTangAvgBSp, faceVelMagAvgSp);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal static void DivideNarrow(ReadOnlySpan<double> numSp, ReadOnlySpan<double> denSp, Span<float> resultSp, int start, int stop)
	{
		int count3 = stop + 1;
		int count = Vector<double>.Count;
		int count2 = Vector<float>.Count;
		int i;
		for (i = start; i + count2 <= count3; i += count2)
		{
			Vector<double> low = new Vector<double>(numSp.Slice(i, count)) / new Vector<double>(denSp.Slice(i, count));
			Vector<double> high = new Vector<double>(numSp.Slice(i + count, count)) / new Vector<double>(denSp.Slice(i + count, count));
			System.Numerics.Vector.Narrow(low, high).CopyTo(resultSp.Slice(i, count2));
		}
		for (; i < count3; i++)
		{
			resultSp[i] = (float)(numSp[i] / denSp[i]);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal static void AddScaledWhereWetAndFlagged(double k, ReadOnlySpan<double> xSp, ReadOnlySpan<double> faceAreaSp, ReadOnlySpan<FaceFlag> faceFlagsSp, Span<double> faceBTermSp, int start, int stop)
	{
		int count2 = stop + 1;
		int count = Vector<double>.Count;
		Vector<double> vector = new Vector<double>(k);
		Span<long> flagLanes = stackalloc long[Vector<double>.Count];
		int i;
		for (i = start; i + count <= count2; i += count)
		{
			Vector<long> condition = SolverImp.WetAndFlaggedMask(faceAreaSp, faceFlagsSp, flagLanes, i);
			Vector<double> vector2 = new Vector<double>((ReadOnlySpan<double>)faceBTermSp.Slice(i, count));
			Vector<double> left = vector2 + vector * new Vector<double>(xSp.Slice(i, count));
			System.Numerics.Vector.ConditionalSelect(condition, left, vector2).CopyTo(faceBTermSp.Slice(i, count));
		}
		for (; i < count2; i++)
		{
			if (faceAreaSp[i] > 0.0 && faceFlagsSp[i] != 0)
			{
				faceBTermSp[i] += k * xSp[i];
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal static void AddScaledProductWhereWetAndFlagged(double k, ReadOnlySpan<float> xSp, ReadOnlySpan<float> ySp, ReadOnlySpan<double> faceAreaSp, ReadOnlySpan<FaceFlag> faceFlagsSp, Span<double> faceBTermSp, int start, int stop)
	{
		int count3 = stop + 1;
		int count = Vector<double>.Count;
		int count2 = Vector<float>.Count;
		Vector<double> vector = new Vector<double>(k);
		Span<long> flagLanes = stackalloc long[Vector<double>.Count];
		int i;
		for (i = start; i + count2 <= count3; i += count2)
		{
			System.Numerics.Vector.Widen(new Vector<float>(xSp.Slice(i, count2)), out var low, out var high);
			System.Numerics.Vector.Widen(new Vector<float>(ySp.Slice(i, count2)), out var low2, out var high2);
			Vector<long> condition = SolverImp.WetAndFlaggedMask(faceAreaSp, faceFlagsSp, flagLanes, i);
			Vector<double> vector2 = new Vector<double>((ReadOnlySpan<double>)faceBTermSp.Slice(i, count));
			System.Numerics.Vector.ConditionalSelect(condition, vector2 + vector * low * low2, vector2).CopyTo(faceBTermSp.Slice(i, count));
			Vector<long> condition2 = SolverImp.WetAndFlaggedMask(faceAreaSp, faceFlagsSp, flagLanes, i + count);
			Vector<double> vector3 = new Vector<double>((ReadOnlySpan<double>)faceBTermSp.Slice(i + count, count));
			System.Numerics.Vector.ConditionalSelect(condition2, vector3 + vector * high * high2, vector3).CopyTo(faceBTermSp.Slice(i + count, count));
		}
		for (; i < count3; i++)
		{
			if (faceAreaSp[i] > 0.0 && faceFlagsSp[i] != 0)
			{
				faceBTermSp[i] += k * (double)xSp[i] * (double)ySp[i];
			}
		}
	}
}
