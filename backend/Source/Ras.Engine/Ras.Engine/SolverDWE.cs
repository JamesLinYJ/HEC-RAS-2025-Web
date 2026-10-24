using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Engine.Engine;
using Ras.Engine.Structures.Weirs;
using Utility.Progress;

namespace Ras.Engine;

public class SolverDWE : SolverImp, ISolver
{
	private double[] _faceCrossGradientCurrent;

	private double _deferredCorrectionDt;

	internal double[] FaceGTerm { get; set; }

	internal double[] FaceGradHN { get; set; }

	internal double[] FaceGradHT { get; set; }

	internal double[] FaceConvAvg { get; set; }

	private double FaceConvContAt(int fIdx)
	{
		if (Convergence.OuterConv.Iterations > 1)
		{
			return FaceConvAvg[fIdx];
		}
		return _oldState.FaceConveyance[fIdx];
	}

	public SolverDWE(Mesh mesh, MeshPropertyTables props, MeshWaterState initialState)
		: this(mesh, props, initialState, new SolverControl(), new SolverConvergence(), null)
	{
	}

	public SolverDWE(Mesh mesh, MeshPropertyTables props, MeshWaterState initialState, SolverControl control)
		: this(mesh, props, initialState, control, new SolverConvergence(), null)
	{
	}

	public SolverDWE(Mesh mesh, MeshPropertyTables props, MeshWaterState initialState, SolverControl control, SolverConvergence convergence, ComputeDriver driver)
		: base(mesh, props, initialState, control, convergence, driver)
	{
	}

	public override ReadOnlySpan<double> GetFaceVelocity()
	{
		_waterState.ComputeFaceVelocity();
		return _waterState.FaceVelocity;
	}

	public override void Prepare(ProgressReporter pr = null)
	{
		base.Prepare(pr);
		int faceCount = _mesh.FaceCount;
		FaceGTerm = new double[faceCount];
		FaceGradHN = new double[faceCount];
		FaceGradHT = new double[faceCount];
		FaceConvAvg = new double[faceCount];
		if (HasNonOrthogonalCorrection)
		{
			_faceCrossGradientCurrent = new double[faceCount];
		}
	}

	public override void PreparePropertyTables()
	{
		base.PreparePropertyTables();
		_propertyTables.ComputeFaceConveyanceTable(_mesh, computeControl.nSItoUnits);
	}

	protected override void ResetRemovedFaces()
	{
		base.ResetRemovedFaces();
		ReadOnlySpan<int> faceIndicesRemoved = CollectionsMarshal.AsSpan(PartGrid.FaceIndicesRemoved);
		int length = faceIndicesRemoved.Length;
		Span<double> faceGradHN = FaceGradHN.AsSpan();
		Span<double> faceGradHT = FaceGradHT.AsSpan();
		for (int i = 0; i < length; i++)
		{
			int faceIndicesRemoved2 = faceIndicesRemoved[i];
			faceGradHN[faceIndicesRemoved2] = 0.0;
			faceGradHT[faceIndicesRemoved2] = 0.0;
			if (HasNonOrthogonalCorrection)
			{
				_faceCrossGradientCurrent[faceIndicesRemoved2] = 0.0;
			}
		}
	}

	protected override bool PreSolveOuter(double absTime, double dt, out double EOPTime)
	{
		bool preSolveOuter = base.PreSolveOuter(absTime, dt, out EOPTime);
		if (preSolveOuter && HasNonOrthogonalCorrection)
		{
			UpdateFaceGradientData(_oldState);
		}
		return preSolveOuter;
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
			_deferredCorrectionDt = dt;
		}
		ComputeFaceNormalWaterSurfaceGradients(waterState);
		ComputeFaceTangGradAndFaceCoeff(waterState, dt);
		if (HydraulicStructures.HasWeirs)
		{
			OverrideStructFaceCellVelocity(waterState);
			OverrideFaceCoeffAtWeir(waterState, dt);
		}
		if (Convergence.OuterConv.Iterations > 1)
		{
			ComputeFaceConveyanceAvg(waterState, oldState);
		}
		ComputeAssembly(EOPTime, dt);
		PreComputeNewton(waterState);
		ticks[20] += stopwatch.ElapsedTicks;
		return true;
	}

	private void ComputeFaceNormalWaterSurfaceGradients(MeshWaterState ws)
	{
		int faceCount = PartGrid.FaceCount;
		int loopSize = SolverImp.GetLoopSize(4000, faceCount);
		double[] faceDistInv = FaceInverseDistances;
		threadFor.Run(faceCount, (int start, int stop) =>
		{
			ComputeFaceNormGradStartStop(ws, faceDistInv, start, stop);
		}, loopSize);
	}

	private void ComputeFaceNormGradStartStop(MeshWaterState ws, double[] faceDistInv, int start, int stop)
	{
		ReadOnlySpan<double> faceAreaSp = ws.FaceArea.AsSpan();
		ReadOnlySpan<FaceFlag> faceFlagsSp = FaceFlags.AsSpan();
		Span<double> faceGradHNSp = FaceGradHN.AsSpan();
		ReadOnlySpan<int> faceIndices = CollectionsMarshal.AsSpan(PartGrid.FaceIndices);
		for (int i = start; i <= stop; i++)
		{
			int fIdx = faceIndices[i];
			ComputeFaceNormGradOne(ws, faceFlagsSp, faceAreaSp, faceGradHNSp, faceDistInv, fIdx);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ComputeFaceNormGradOne(MeshWaterState ws, ReadOnlySpan<FaceFlag> faceFlagsSp, ReadOnlySpan<double> faceAreaSp, Span<double> faceGradHNSp, double[] faceDistInv, int fIdx)
	{
		if (faceAreaSp[fIdx] <= 0.0 || faceFlagsSp[fIdx] == 0)
		{
			faceGradHNSp[fIdx] = 0.0;
		}
		else
		{
			faceGradHNSp[fIdx] = ComputeFaceNormalGradient(ws, fIdx, faceDistInv);
		}
	}

	private void ComputeFaceTangGradAndFaceCoeff(MeshWaterState ws, double dt)
	{
		int faceCount = PartGrid.FaceCount;
		int loopSize = SolverImp.GetLoopSize(4000, faceCount);
		double[] faceInvDist = FaceInverseDistances;
		if (HasNonOrthogonalCorrection)
		{
			threadFor.Run(faceCount, (int start, int stop) =>
			{
				ComputeBaselineFaceTangentialGradientsStartStop(ws, start, stop);
			}, loopSize);
			threadFor.Run(faceCount, (int start, int stop) =>
			{
				ApplyNonOrthogonalFaceGradientsStartStop(ws, start, stop);
			}, loopSize);
		}
		threadFor.Run(faceCount, (int start, int stop) =>
		{
			ComputeFaceTangGradAndFaceCoeffStartStop(ws, dt, faceInvDist, start, stop);
		}, loopSize);
	}

	private void ComputeBaselineFaceTangentialGradientsStartStop(MeshWaterState ws, int start, int stop)
	{
		ReadOnlySpan<double> faceArea = ws.FaceArea.AsSpan();
		Span<double> faceGradHT = FaceGradHT.AsSpan();
		ReadOnlySpan<int> faceIndices = CollectionsMarshal.AsSpan(PartGrid.FaceIndices);
		if (PartGrid.Enabled)
		{
			for (int i = start; i <= stop; i++)
			{
				int faceIndex = faceIndices[i];
				faceGradHT[faceIndex] = ((faceArea[faceIndex] > 0.0) ? _hmesh.FaceComputes.Tangential(faceIndex, FaceGradHN) : 0.0);
			}
		}
		else
		{
			for (int j = start; j <= stop; j++)
			{
				faceGradHT[j] = ((faceArea[j] > 0.0) ? _hmesh.FaceComputes.Tangential(j, FaceGradHN) : 0.0);
			}
		}
	}

	private void ApplyNonOrthogonalFaceGradientsStartStop(MeshWaterState ws, int start, int stop)
	{
		ReadOnlySpan<double> faceAreaSp = ws.FaceArea.AsSpan();
		ReadOnlySpan<FaceFlag> faceFlagsSp = FaceFlags.AsSpan();
		Span<double> faceGradHNSp = FaceGradHN.AsSpan();
		Span<double> faceGradHTSp = FaceGradHT.AsSpan();
		Span<double> crossGradientSp = _faceCrossGradientCurrent.AsSpan();
		ReadOnlySpan<int> faceIndices = CollectionsMarshal.AsSpan(PartGrid.FaceIndices);
		if (PartGrid.Enabled)
		{
			for (int i = start; i <= stop; i++)
			{
				ApplyNonOrthogonalFaceGradient(ws, faceAreaSp, faceFlagsSp, faceGradHNSp, faceGradHTSp, crossGradientSp, faceIndices[i]);
			}
		}
		else
		{
			for (int j = start; j <= stop; j++)
			{
				ApplyNonOrthogonalFaceGradient(ws, faceAreaSp, faceFlagsSp, faceGradHNSp, faceGradHTSp, crossGradientSp, j);
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ApplyNonOrthogonalFaceGradient(MeshWaterState ws, ReadOnlySpan<double> faceAreaSp, ReadOnlySpan<FaceFlag> faceFlagsSp, Span<double> faceGradHNSp, Span<double> faceGradHTSp, Span<double> crossGradientSp, int fIdx)
	{
		if (faceAreaSp[fIdx] <= 0.0 || faceFlagsSp[fIdx] == 0 || !FaceUsesNonOrthogonalCorrection(fIdx))
		{
			crossGradientSp[fIdx] = 0.0;
			return;
		}
		double crossGradientSp2 = ComputeFaceWaterSurfaceCorrection(fIdx, ws);
		crossGradientSp[fIdx] = crossGradientSp2;
		faceGradHNSp[fIdx] += crossGradientSp2;
		if (CompactFaceValuesAreValid(fIdx, ws))
		{
			faceGradHTSp[fIdx] = ComputeFaceWaterSurfaceTangentialGradient(fIdx, ws);
		}
	}

	internal void ComputeFaceTangGradAndFaceCoeffStartStop(MeshWaterState ws, double dt, double[] faceInvDist, int start, int stop)
	{
		ReadOnlySpan<double> faceInvDistSp = faceInvDist.AsSpan();
		ReadOnlySpan<double> faceAreaSp = ws.FaceArea.AsSpan();
		ReadOnlySpan<double> faceConvSp = ws.FaceConveyance.AsSpan();
		ReadOnlySpan<FaceFlag> faceFlagsSp = FaceFlags.AsSpan();
		Span<double> faceGTermSp = FaceGTerm.AsSpan();
		Span<double> faceCoeffSp = FaceCoeff.AsSpan();
		Span<double> faceGradHT = FaceGradHT.AsSpan();
		double[] faceGradHN = FaceGradHN;
		if (PartGrid.Enabled)
		{
			ReadOnlySpan<int> faceIndices = CollectionsMarshal.AsSpan(PartGrid.FaceIndices);
			for (int i = start; i <= stop; i++)
			{
				int fIdx = faceIndices[i];
				ComputeFaceTangGradAndFaceCoeffOne(dt, faceAreaSp, faceConvSp, faceInvDistSp, faceGTermSp, faceCoeffSp, faceGradHT, faceGradHN, faceFlagsSp, fIdx);
			}
			return;
		}
		if (!HasNonOrthogonalCorrection)
		{
			for (int j = start; j <= stop; j++)
			{
				if (faceAreaSp[j] > 0.0)
				{
					faceGradHT[j] = _hmesh.FaceComputes.Tangential(j, faceGradHN);
				}
				else
				{
					faceGradHT[j] = 0.0;
				}
			}
		}
		ComputeGTermAndFaceCoeff(dt, Control.ClampGradientThreshold, faceGradHN, faceGradHT, faceConvSp, faceInvDistSp, faceAreaSp, faceFlagsSp, faceGTermSp, faceCoeffSp, start, stop);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void ComputeFaceTangGradAndFaceCoeffOne(double dt, ReadOnlySpan<double> faceAreaSp, ReadOnlySpan<double> faceConvSp, ReadOnlySpan<double> faceInvDistSp, Span<double> faceGTermSp, Span<double> faceCoeffSp, Span<double> faceGradHTSp, double[] faceGradHN, ReadOnlySpan<FaceFlag> faceFlagsSp, int fIdx)
	{
		if (faceAreaSp[fIdx] > 0.0)
		{
			double faceGradHTSp2 = (HasNonOrthogonalCorrection ? faceGradHTSp[fIdx] : _hmesh.FaceComputes.Tangential(fIdx, faceGradHN));
			faceGradHTSp[fIdx] = faceGradHTSp2;
			if (faceFlagsSp[fIdx] != 0)
			{
				faceGTermSp[fIdx] = ComputeFaceGTermONE(faceGradHN[fIdx], faceGradHTSp2);
				faceCoeffSp[fIdx] = (0.0 - dt) * faceConvSp[fIdx] * faceGTermSp[fIdx] * faceInvDistSp[fIdx];
			}
			else
			{
				faceGTermSp[fIdx] = 0.0;
				faceCoeffSp[fIdx] = 0.0;
			}
		}
		else
		{
			faceGradHTSp[fIdx] = 0.0;
			faceGTermSp[fIdx] = 0.0;
			faceCoeffSp[fIdx] = 0.0;
		}
	}

	internal double ComputeFaceGTermONE(double fNorm, double fTang)
	{
		return ComputeFaceGTermONE(fNorm, fTang, Control.ClampGradientThreshold);
	}

	protected override double ComputeFaceCoeff(MeshWaterState ws, int fIdx, double dt)
	{
		return (0.0 - dt) * Control.ImplicitFactor * FaceConvContAt(fIdx) * FaceGTerm[fIdx] * FaceInverseDistances[fIdx];
	}

	public void OverrideFaceCoeffAtWeir(MeshWaterState ws, double dt)
	{
		ReadOnlySpan<double> faceInverseDistances = FaceInverseDistances.AsSpan();
		ReadOnlySpan<double> faceArea = ws.FaceArea.AsSpan();
		Span<double> faceConveyance = ws.FaceConveyance.AsSpan();
		Span<double> faceGTerm = FaceGTerm.AsSpan();
		Span<double> faceCoeff = FaceCoeff.AsSpan();
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
				if (!(Math.Max(cellElevation, cellElevation2) <= weirCrest) && !(faceArea[faceIndex] <= 1E-10))
				{
					bool condition = cellElevationWeirApproachVelTerm > cellElevation2WeirApproachVel2Term;
					double cellElevation3 = (condition ? cellElevation : cellElevation2);
					double cellElevation4 = (condition ? cellElevation2 : cellElevation);
					double weirApproachVel3 = (condition ? weirApproachVel : weirApproachVel2);
					double cellElevation3WeirCrestTerm = cellElevation3 - weirCrest + weirApproachVel3 * weirApproachVel3 / (2.0 * grav);
					double computed = (cellElevation4 - weirCrest) / cellElevation3WeirCrestTerm;
					computed = Math.Clamp(computed, 0.0, 1.0);
					double submergenceAcoef = weir.PhysProps.SubmergenceAcoef;
					double submergenceBcoef = weir.PhysProps.SubmergenceBcoef;
					double submergenceAcoefSubmergenceBcoefTerm = Math.Pow(1.0 - Math.Pow(computed, submergenceAcoef), submergenceBcoef) * weir.PhysProps.WeirCoef * (double)_hmesh.FaceSimpleLengths[faceIndex] * Math.Sqrt(cellElevation3WeirCrestTerm) * cellElevation3WeirCrestTerm;
					double faceConveyance2 = faceGTerm[faceIndex] * submergenceAcoefSubmergenceBcoefTerm;
					faceConveyance[faceIndex] = faceConveyance2;
					faceCoeff[faceIndex] = (0.0 - dt) * faceConveyance2 * faceGTerm[faceIndex] * faceInverseDistances[faceIndex];
				}
			}
		}
	}

	protected override void ComputeAssemblyRow(int cIdx)
	{
		double faceCoeffSum = 0.0;
		int[] rowPointers = MatrixNewton.RowPointers;
		int rowPointerStart = rowPointers[cIdx];
		int rowPointerEnd = rowPointers[cIdx + 1];
		Span<double> doubleSpan = MatrixNewton.Values;
		ReadOnlySpan<double> cellElevation = _oldState.CellElevation.AsSpan();
		double computed = 0.0;
		double computed2 = 0.0;
		double implicitFactorOffsetOverImplicitFactor = (1.0 - Control.ImplicitFactor) / Control.ImplicitFactor;
		for (int i = rowPointerStart; i < rowPointerEnd; i++)
		{
			int stencilFaceIndex = StencilFaces[i];
			if (stencilFaceIndex >= 0)
			{
				double faceCoeff = FaceCoeff[stencilFaceIndex];
				doubleSpan[i] = faceCoeff;
				faceCoeffSum -= faceCoeff;
				int faceCellA = ((StencilSigns[i] == 1) ? _meshConnectivity.FaceCellA[stencilFaceIndex] : _meshConnectivity.FaceCellB[stencilFaceIndex]);
				if (faceCellA >= 0)
				{
					computed += faceCoeff * cellElevation[faceCellA];
				}
				if (FaceUsesNonOrthogonalCorrection(stencilFaceIndex))
				{
					double faceWaterSurfaceCorrection = ComputeFaceWaterSurfaceCorrection(stencilFaceIndex, _oldState);
					double deferredCorrectionDtFaceConveyanceTerm = _deferredCorrectionDt * _waterState.FaceConveyance[stencilFaceIndex] * FaceGTerm[stencilFaceIndex] * (_faceCrossGradientCurrent[stencilFaceIndex] + implicitFactorOffsetOverImplicitFactor * faceWaterSurfaceCorrection);
					computed2 += (double)StencilSigns[i] * deferredCorrectionDtFaceConveyanceTerm;
				}
			}
		}
		MatrixDiagonal[cIdx] = faceCoeffSum;
		CellRHS[cIdx] = _oldState.CellVolume[cIdx] - implicitFactorOffsetOverImplicitFactor * (faceCoeffSum * cellElevation[cIdx] + computed) + computed2;
	}

	private void ComputeFaceConveyanceAvg(MeshWaterState ws, MeshWaterState wsOld)
	{
		int faceCount = PartGrid.FaceCount;
		int loopSize = SolverImp.GetLoopSize(4000, faceCount);
		threadFor.Run(faceCount, (int start, int stop) =>
		{
			ComputeFaceConveyanceAvgStartStop(ws, wsOld, start, stop);
		}, loopSize);
	}

	internal void ComputeFaceConveyanceAvgStartStop(MeshWaterState ws, MeshWaterState wsOld, int start, int stop)
	{
		ReadOnlySpan<double> aSp = ws.FaceConveyance.AsSpan();
		ReadOnlySpan<double> bSp = wsOld.FaceConveyance.AsSpan();
		Span<double> resultSp = FaceConvAvg.AsSpan();
		if (PartGrid.Enabled)
		{
			ReadOnlySpan<int> faceIndices = CollectionsMarshal.AsSpan(PartGrid.FaceIndices);
			for (int i = start; i <= stop; i++)
			{
				int index = faceIndices[i];
				resultSp[index] = 0.5 * (aSp[index] + bSp[index]);
			}
		}
		else
		{
			SolverImp.AverageHalf(aSp, bSp, resultSp, start, stop);
		}
	}

	protected override void OverrideAssemblyExternalStageBC(MeshWaterState ws, double dt, int fIdx)
	{
		int faceCellA = _meshConnectivity.FaceCellA[fIdx];
		double faceCoeff = ComputeFaceCoeff(ws, fIdx, dt);
		double implicitFactorOffsetOverImplicitFactor = (1.0 - Control.ImplicitFactor) / Control.ImplicitFactor;
		double pinnedFaceStageOldStateTerm = _oldState.PinnedFaceStage[fIdx] - _oldState.CellElevation[faceCellA];
		CellRHS[faceCellA] -= faceCoeff * ws.PinnedFaceStage[fIdx];
		CellRHS[faceCellA] -= implicitFactorOffsetOverImplicitFactor * faceCoeff * pinnedFaceStageOldStateTerm;
		if (FaceUsesNonOrthogonalCorrection(fIdx))
		{
			double faceWaterSurfaceCorrection = ComputeFaceWaterSurfaceCorrection(fIdx, ws);
			double faceWaterSurfaceCorrection2 = ComputeFaceWaterSurfaceCorrection(fIdx, _oldState);
			CellRHS[faceCellA] += dt * ws.FaceConveyance[fIdx] * FaceGTerm[fIdx] * (faceWaterSurfaceCorrection + implicitFactorOffsetOverImplicitFactor * faceWaterSurfaceCorrection2);
		}
		MatrixDiagonal[faceCellA] -= faceCoeff;
	}

	protected override void ComputeFaceFlowsAndVelocities(double dt)
	{
		MeshWaterState ws = _waterState;
		MeshWaterState wsOld = _oldState;
		double[] faceDistInv = FaceInverseDistances;
		int faceCount = PartGrid.FaceCount;
		int loopSize = SolverImp.GetLoopSize(5000, faceCount);
		threadFor.Run(faceCount, (int start, int stop) =>
		{
			ComputeFaceFlowsAndVelocitiesStartStop(ws, wsOld, faceDistInv, start, stop);
		}, loopSize);
	}

	internal void ComputeFaceFlowsAndVelocitiesStartStop(MeshWaterState ws, MeshWaterState wsOld, double[] faceDistInv, int start, int stop)
	{
		ReadOnlySpan<FaceFlag> faceFlags = FaceFlags.AsSpan();
		ReadOnlySpan<double> faceGTerm = FaceGTerm.AsSpan();
		FaceFlag faceFlag = FaceFlag.FORCE_FLOW;
		ReadOnlySpan<double> faceArea = ws.FaceArea.AsSpan();
		ReadOnlySpan<double> faceConveyance = ws.FaceConveyance.AsSpan();
		Span<double> faceFlow = ws.FaceFlow.AsSpan();
		Span<double> faceVelocity = ws.FaceVelocity.AsSpan();
		ReadOnlySpan<int> faceIndices = CollectionsMarshal.AsSpan(PartGrid.FaceIndices);
		for (int i = start; i <= stop; i++)
		{
			int faceIndex = faceIndices[i];
			if (faceFlags[faceIndex] == faceFlag)
			{
				if (faceArea[faceIndex] > 0.0)
				{
					faceVelocity[faceIndex] = faceFlow[faceIndex] / faceArea[faceIndex];
				}
			}
			else if (faceArea[faceIndex] > 0.0)
			{
				double faceNormalGradient = ComputeFaceNormalGradient(wsOld, faceIndex, faceDistInv);
				double faceNormalGradient2 = ComputeFaceNormalGradient(ws, faceIndex, faceDistInv);
				if (FaceUsesNonOrthogonalCorrection(faceIndex))
				{
					faceNormalGradient += ComputeFaceWaterSurfaceCorrection(faceIndex, wsOld);
					faceNormalGradient2 += _faceCrossGradientCurrent[faceIndex];
				}
				double implicitFactorControlTerm = (1.0 - Control.ImplicitFactor) * faceNormalGradient + Control.ImplicitFactor * faceNormalGradient2;
				faceFlow[faceIndex] = (0.0 - faceConveyance[faceIndex]) * faceGTerm[faceIndex] * implicitFactorControlTerm;
				faceVelocity[faceIndex] = faceFlow[faceIndex] / faceArea[faceIndex];
			}
			else
			{
				faceFlow[faceIndex] = 0.0;
				faceVelocity[faceIndex] = 0.0;
			}
		}
	}

	protected override void ApplyFlowBoundaryConditionEGSlope(BoundaryCondition bc, double Qtot, MeshWaterState ws)
	{
		double minConveyance = Math.Abs(Qtot) / Math.Sqrt(bc.EGSlope);
		ApplyFlowBoundaryConditionByConveyance(bc, Qtot, ws, FaceConveyancePropTable, minConveyance, setVelocity: true);
	}

	protected override Func<MeshWaterState, int, double> GetFaceConveyanceFunc()
	{
		return FaceConveyancePropTable;
	}

	private static double FaceConveyancePropTable(MeshWaterState ws, int fIdx)
	{
		return ws.FaceConveyance[fIdx];
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	internal static void ComputeGTermAndFaceCoeff(double dt, double clampGradientThreshold, ReadOnlySpan<double> faceGradHNSp, ReadOnlySpan<double> faceGradHTSp, ReadOnlySpan<double> faceConvSp, ReadOnlySpan<double> faceInvDistSp, ReadOnlySpan<double> faceAreaSp, ReadOnlySpan<FaceFlag> faceFlagsSp, Span<double> faceGTermSp, Span<double> faceCoeffSp, int start, int stop)
	{
		int count2 = stop + 1;
		int count = Vector<double>.Count;
		Vector<double> vector = new Vector<double>(0.0 - dt);
		Vector<double> right = new Vector<double>(clampGradientThreshold);
		Span<long> flagLanes = stackalloc long[Vector<double>.Count];
		int i;
		for (i = start; i + count <= count2; i += count)
		{
			Vector<long> condition = SolverImp.WetAndFlaggedMask(faceAreaSp, faceFlagsSp, flagLanes, i);
			Vector<double> vector2 = new Vector<double>(faceGradHNSp.Slice(i, count));
			Vector<double> vector3 = new Vector<double>(faceGradHTSp.Slice(i, count));
			Vector<double> left = vector2 * vector2 + vector3 * vector3;
			left = System.Numerics.Vector.Max(left, right);
			Vector<double> vector4 = Vector<double>.One / System.Numerics.Vector.SquareRoot(System.Numerics.Vector.SquareRoot(left));
			Vector<double> left2 = vector * new Vector<double>(faceConvSp.Slice(i, count)) * vector4 * new Vector<double>(faceInvDistSp.Slice(i, count));
			System.Numerics.Vector.ConditionalSelect(condition, vector4, Vector<double>.Zero).CopyTo(faceGTermSp.Slice(i, count));
			System.Numerics.Vector.ConditionalSelect(condition, left2, Vector<double>.Zero).CopyTo(faceCoeffSp.Slice(i, count));
		}
		for (; i < count2; i++)
		{
			if (faceAreaSp[i] > 0.0)
			{
				if (faceFlagsSp[i] != 0)
				{
					double faceGTermSp2 = faceGradHNSp[i] * faceGradHNSp[i] + faceGradHTSp[i] * faceGradHTSp[i];
					faceGTermSp2 = Math.Max(faceGTermSp2, clampGradientThreshold);
					faceGTermSp2 = 1.0 / Math.Sqrt(Math.Sqrt(faceGTermSp2));
					faceGTermSp[i] = faceGTermSp2;
					faceCoeffSp[i] = (0.0 - dt) * faceConvSp[i] * faceGTermSp[i] * faceInvDistSp[i];
				}
				else
				{
					faceGTermSp[i] = 0.0;
					faceCoeffSp[i] = 0.0;
				}
			}
			else
			{
				faceGTermSp[i] = 0.0;
				faceCoeffSp[i] = 0.0;
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static double ComputeFaceGTermONE(double fNorm, double fTang, double clampGradientThreshold)
	{
		double clampGradientThresholdTerm = fNorm * fNorm + fTang * fTang;
		clampGradientThresholdTerm = Math.Max(clampGradientThresholdTerm, clampGradientThreshold);
		return 1.0 / Math.Sqrt(Math.Sqrt(clampGradientThresholdTerm));
	}
}
