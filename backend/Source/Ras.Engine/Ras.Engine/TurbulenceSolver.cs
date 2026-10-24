using System;
using System.Collections.Generic;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Engine;

public class TurbulenceSolver
{
	private static readonly ILogger _logger = LogHelper.GetLogger("TurbulenceSolver");

	public const int KIndex = 0;

	public const int EpsIndex = 1;

	private const double MinDepth = 0.0001;

	private readonly Mesh _mesh;

	private readonly HydraulicMeshComputer _hmesh;

	private readonly SolverControl _control;

	private readonly TurbulenceState _turb;

	private MeshWaterState _waterState;

	private TransportSolver _transport;

	private TransportFlowField _flow;

	private TransportBoundaryCondition _inflowBC;

	private ThreadFor _threadFor = new ThreadFor(1);

	private List<int> _allCells;

	private double[] _cellUStar;

	private double[] _srcK;

	private double[] _sinkK;

	private double[] _srcEps;

	private double[] _sinkEps;

	private double[] _faceDiffK;

	private double[] _faceDiffEps;

	private TurbulenceParams Params => _control.Turbulence;

	private bool IsKEpsilon => Params.Model == TurbulenceModel.KEpsilon;

	public int ConstituentCount
	{
		get
		{
			if (!IsKEpsilon)
			{
				return 1;
			}
			return 2;
		}
	}

	public TransportSolver Transport => _transport;

	public TurbulenceSolver(Mesh mesh, SolverControl control, TurbulenceState turb)
	{
		_mesh = mesh;
		_hmesh = mesh.MetadataCache;
		_control = control;
		_turb = turb;
	}

	public void Prepare(Solver host)
	{
		PrepareCore(host.WaterState, TransportFlowField.Alias(host.OldState, host.WaterState, null, null, host.CellWaterSources.EnableTransportTracking()), CollectOpenBoundaryFaces(host.BoundaryConditions), host.Reporter, restoreExistingState: false);
	}

	internal void PrepareFromState(Solver host)
	{
		PrepareCore(host.WaterState, TransportFlowField.Alias(host.OldState, host.WaterState, null, null, host.CellWaterSources.EnableTransportTracking()), CollectOpenBoundaryFaces(host.BoundaryConditions), host.Reporter, restoreExistingState: true);
	}

	public void Prepare(MeshWaterState waterState, TransportFlowField flow, ICollection<int> openBoundaryFaces, ProgressReporter reporter = null)
	{
		PrepareCore(waterState, flow, openBoundaryFaces, reporter, restoreExistingState: false);
	}

	private void PrepareCore(MeshWaterState waterState, TransportFlowField flow, ICollection<int> openBoundaryFaces, ProgressReporter reporter, bool restoreExistingState)
	{
		_waterState = waterState;
		_flow = flow;
		if (restoreExistingState)
		{
			ValidateAndFloorRestoredState();
		}
		int cellCount = _mesh.CellCount;
		int faceCount = _mesh.FaceCount;
		_cellUStar = new double[cellCount];
		_srcK = new double[cellCount];
		_sinkK = new double[cellCount];
		_faceDiffK = new double[faceCount];
		if (IsKEpsilon)
		{
			_srcEps = new double[cellCount];
			_sinkEps = new double[cellCount];
			_faceDiffEps = new double[faceCount];
		}
		_allCells = new List<int>(cellCount);
		for (int i = 0; i < cellCount; i++)
		{
			_allCells.Add(i);
		}
		_threadFor?.Dispose();
		_threadFor = new ThreadFor((_control.numCores <= 0) ? 1 : _control.numCores);
		TransportBoundaryConditionCollection bcs = new TransportBoundaryConditionCollection();
		BuildBoundaryConditions(openBoundaryFaces, bcs);
		TransportControl transportControl = new TransportControl(ConstituentCount)
		{
			ThetaImplicit = 1.0,
			advectionScheme = AdvectionScheme.FirstOrderUpwind,
			faceNormalGradientMethod = FaceNormalGradientMethod.OverRelaxation,
			DeriveVolumeChangeFromFlows = true,
			numCores = _control.numCores,
			MatrixSolverPrimary = _control.MatrixSolverPrimary,
			ReuseFactorizationAcrossConstituents = false,
			ExplicitTrapezoidalSink = false
		};
		transportControl.SetConstituentRange(0, Params.MinTurbulentEnergy, 1E+20);
		transportControl.ConstituentRangeClampTolerances[0] = Params.MinTurbulentEnergy + 1E-12;
		if (IsKEpsilon)
		{
			transportControl.SetConstituentRange(1, Params.MinDissipation, 1E+20);
			transportControl.ConstituentRangeClampTolerances[1] = Params.MinDissipation + 1E-12;
		}
		_transport = (Params.UseExplicitTransport ? ((TransportSolver)new TransportSolverExp(_mesh, transportControl, _flow, ConstituentCount, bcs)) : ((TransportSolver)new TransportSolverImp(_mesh, transportControl, _flow, ConstituentCount, bcs)));
		_transport.SetCellSourceMassRate(0, _srcK);
		_transport.SetCellSinkCoefficient(0, _sinkK);
		_transport.SetFaceDiffusivity(0, _faceDiffK);
		if (IsKEpsilon)
		{
			_transport.SetCellSourceMassRate(1, _srcEps);
			_transport.SetCellSinkCoefficient(1, _sinkEps);
			_transport.SetFaceDiffusivity(1, _faceDiffEps);
		}
		_transport.Prepare(reporter);
		if (restoreExistingState)
		{
			RestoreTransportedState();
		}
		else
		{
			SeedEquilibriumState();
		}
	}

	private static HashSet<int> CollectOpenBoundaryFaces(BoundaryConditionCollection hydBcs)
	{
		HashSet<int> openBoundaryFaces = new HashSet<int>();
		if (hydBcs == null)
		{
			return openBoundaryFaces;
		}
		AddFaces(openBoundaryFaces, hydBcs.FlowBoundaryConditions);
		AddFaces(openBoundaryFaces, hydBcs.StageBoundaryConditions);
		AddFaces(openBoundaryFaces, hydBcs.RatingCurveBoundaryConditions);
		AddFaces(openBoundaryFaces, hydBcs.NormalDepthBoundaryConditions);
		AddFaces(openBoundaryFaces, hydBcs.VariableFlowBoundaryConditions);
		AddFaces(openBoundaryFaces, hydBcs.VariableStageBoundaryConditions);
		AddFaces(openBoundaryFaces, hydBcs.SpatiallyVariableStageVelocityBoundaryConditions);
		return openBoundaryFaces;
	}

	private void BuildBoundaryConditions(ICollection<int> openFaces, TransportBoundaryConditionCollection bcs)
	{
		if (openFaces == null)
		{
			openFaces = Array.Empty<int>();
		}
		HashSet<int> openFacesSet = (openFaces as HashSet<int>) ?? new HashSet<int>(openFaces);
		List<int> noFluxWallFaces = new List<int>();
		for (int i = 0; i < _mesh.FaceCount; i++)
		{
			if (_mesh.Faces[i].cellB < 0 && !openFacesSet.Contains(i))
			{
				noFluxWallFaces.Add(i);
			}
		}
		if (noFluxWallFaces.Count > 0)
		{
			bcs.Add(new TransportBoundaryCondition(TransportBCType.NoFluxWall, noFluxWallFaces.ToArray()));
		}
		if (openFacesSet.Count > 0)
		{
			int[] equilibriumInflowIds = new int[openFacesSet.Count];
			openFacesSet.CopyTo(equilibriumInflowIds);
			Array.Sort(equilibriumInflowIds);
			_inflowBC = new TransportBoundaryCondition(TransportBCType.EquilibriumInflow, equilibriumInflowIds);
			_inflowBC.AllocateFaceConcentrations(ConstituentCount);
			bcs.Add(_inflowBC);
		}
	}

	private static void AddFaces(HashSet<int> target, List<BoundaryCondition> bcs)
	{
		foreach (BoundaryCondition bc in bcs)
		{
			if (bc.Faces != null)
			{
				int[] faces = bc.Faces;
				foreach (int faceIndex in faces)
				{
					target.Add(faceIndex);
				}
			}
		}
	}

	private void SeedEquilibriumState()
	{
		ComputeCellHydraulics();
		TurbulenceParams turbulenceParams = Params;
		int cellCount = _mesh.CellCount;
		double[] cellK = new double[cellCount];
		double[] cellEps = new double[cellCount];
		ReadOnlySpan<float> cellHydraulicDepths = _waterState.CellHydraulicDepth;
		for (int i = 0; i < cellCount; i++)
		{
			double cellHydraulicDepth = cellHydraulicDepths[i];
			if (cellHydraulicDepth < 0.0001)
			{
				cellK[i] = turbulenceParams.MinTurbulentEnergy;
				cellEps[i] = turbulenceParams.MinDissipation;
			}
			else
			{
				cellK[i] = Math.Max(turbulenceParams.EquilibriumK(_cellUStar[i]), turbulenceParams.MinTurbulentEnergy);
				cellEps[i] = Math.Max(turbulenceParams.EquilibriumEps(_cellUStar[i], cellHydraulicDepth), turbulenceParams.MinDissipation);
			}
		}
		_transport.SetCellConcentration(0, cellK);
		if (IsKEpsilon)
		{
			_transport.SetCellConcentration(1, cellEps);
		}
		Array.Copy(cellK, _turb.cellK, cellCount);
		Array.Copy(cellEps, _turb.cellEps, cellCount);
		UpdateEddyViscosity();
	}

	private void ValidateAndFloorRestoredState()
	{
		TurbulenceParams turbulenceParams = Params;
		ValidateAndFloor(_turb.cellK, turbulenceParams.MinTurbulentEnergy, "cellK");
		if (IsKEpsilon)
		{
			ValidateAndFloor(_turb.cellEps, turbulenceParams.MinDissipation, "cellEps");
		}
		else
		{
			Array.Fill(_turb.cellEps, turbulenceParams.MinDissipation);
		}
	}

	private static void ValidateAndFloor(double[] values, double minimum, string fieldName)
	{
		for (int i = 0; i < values.Length; i++)
		{
			double computed = values[i];
			if (!double.IsFinite(computed))
			{
				throw new SolverException($"Existing turbulence field {fieldName} is non-finite at cell {i}.");
			}
			if (computed < minimum)
			{
				values[i] = minimum;
			}
		}
	}

	private void RestoreTransportedState()
	{
		_transport.SetCellConcentration(0, _turb.cellK);
		if (IsKEpsilon)
		{
			_transport.SetCellConcentration(1, _turb.cellEps);
		}
		UpdateEddyViscosity();
	}

	public ITransport.Status SolveStep(double absTime, double dt, out int failureCell)
	{
		failureCell = -1;
		ComputeCellHydraulics();
		_turb.ComputeCellVelGradients(_waterState, _allCells, _threadFor);
		int count = Math.Max(Params.OuterIterations, 1);
		ITransport.Status status = ITransport.Status.OK;
		for (int i = 0; i < count; i++)
		{
			UpdateEddyViscosity();
			UpdateFaceDiffusivities();
			UpdateSourceTerms();
			UpdateInflowBoundaryValues();
			status = ((i != 0) ? _transport.ResolveStep(absTime, dt, out failureCell) : _transport.SolveStep(absTime, dt, out failureCell));
			if (status != ITransport.Status.OK)
			{
				_logger.LogWarning("Turbulence transport failed at cell {Cell} with status {Status}", failureCell, status);
				return status;
			}
			ApplyFloors();
		}
		UpdateEddyViscosity();
		return status;
	}

	private void ComputeCellHydraulics()
	{
		MeshWaterState waterState = _waterState;
		_ = waterState.CellVolume;
		_ = _hmesh.CellAreas;
		_ = waterState.FaceArea;
		_ = waterState.FaceShearFluxCoeff;
		_ = waterState.FaceVelMag;
		_threadFor.Run(_mesh.CellCount, (int start, int stop) =>
		{
			ComputeCellHydraulicsStartStop(start, stop);
		});
	}

	private void ComputeCellHydraulicsStartStop(int start, int stop)
	{
		MeshWaterState waterState = _waterState;
		double[] faceArea = waterState.FaceArea;
		float[] faceShearFluxCoeff = waterState.FaceShearFluxCoeff;
		float[] faceVelMag = waterState.FaceVelMag;
		Span<double> cellUStar = _cellUStar;
		ReadOnlySpan<float> cellHydraulicDepths = _waterState.CellHydraulicDepth;
		for (int i = start; i <= stop; i++)
		{
			double cellHydraulicDepth = cellHydraulicDepths[i];
			if (cellHydraulicDepth < 0.0001 || faceShearFluxCoeff == null || faceVelMag == null)
			{
				cellUStar[i] = 0.0;
				continue;
			}
			double computed = 0.0;
			double faceAreaSum = 0.0;
			ReadOnlySpan<int> faces = _mesh.Cells[i].Faces.Span;
			for (int j = 0; j < faces.Length; j++)
			{
				int faceIndex = faces[j];
				double faceArea2 = faceArea[faceIndex];
				if (!(faceArea2 <= 0.0))
				{
					computed += faceArea2 * (double)faceShearFluxCoeff[faceIndex] * (double)faceVelMag[faceIndex];
					faceAreaSum += faceArea2;
				}
			}
			cellUStar[i] = ((faceAreaSum > 0.0) ? (computed / (faceAreaSum * cellHydraulicDepth)) : 0.0);
		}
	}

	private void UpdateEddyViscosity()
	{
		TurbulenceParams p = Params;
		bool kEps = IsKEpsilon;
		double[] cellK = _turb.cellK;
		CopyConcentration(0, cellK);
		if (kEps)
		{
			double[] cellEps = _turb.cellEps;
			CopyConcentration(1, cellEps);
		}
		_threadFor.Run(_mesh.CellCount, (int start, int stop) =>
		{
			UpdateEddyViscosityStartStop(p, kEps, start, stop);
		});
	}

	private void UpdateEddyViscosityStartStop(TurbulenceParams p, bool kEps, int start, int stop)
	{
		double[] cellK = _turb.cellK;
		double[] cellEps = _turb.cellEps;
		double[] cellLength = _turb.cellLength;
		double[] cellNut = _turb.cellNut;
		ReadOnlySpan<float> cellHydraulicDepths = _waterState.CellHydraulicDepth;
		for (int i = start; i <= stop; i++)
		{
			double cellHydraulicDepth = cellHydraulicDepths[i];
			if (cellHydraulicDepth < 0.0001)
			{
				if (cellLength != null)
				{
					cellLength[i] = 0.0;
				}
				cellNut[i] = 0.0;
				continue;
			}
			double cellKMinTurbulentEnergyTerm = Math.Max(cellK[i], p.MinTurbulentEnergy);
			double cMuTerm;
			if (kEps)
			{
				double cellEpsMinDissipationTerm = Math.Max(cellEps[i], p.MinDissipation);
				double cdMaxLengthScaleRatioTerm = p.CD * cellKMinTurbulentEnergyTerm * Math.Sqrt(cellKMinTurbulentEnergyTerm) / (p.MaxLengthScaleRatio * cellHydraulicDepth);
				cMuTerm = p.CMu * cellKMinTurbulentEnergyTerm * cellKMinTurbulentEnergyTerm / Math.Max(cellEpsMinDissipationTerm, cdMaxLengthScaleRatioTerm);
			}
			else
			{
				double cellLengthAlphaLengthTerm = (cellLength[i] = p.AlphaLength * cellHydraulicDepth);
				double computed = Math.Sqrt(cellKMinTurbulentEnergyTerm);
				cellEps[i] = p.CD * cellKMinTurbulentEnergyTerm * computed / cellLengthAlphaLengthTerm;
				cMuTerm = p.CMu / p.CD * computed * cellLengthAlphaLengthTerm;
			}
			cellNut[i] = Math.Min(cMuTerm, p.MaxEddyViscosity);
		}
	}

	private void CopyConcentration(int constituent, double[] destination)
	{
		_transport.GetCellConcentration(constituent).CopyTo(destination);
	}

	private void UpdateFaceDiffusivities()
	{
		TurbulenceParams turbulenceParams = Params;
		double invSigmaK = 1.0 / turbulenceParams.SigmaK;
		double invSigmaEps = 1.0 / turbulenceParams.SigmaEps;
		bool kEps = IsKEpsilon;
		_threadFor.Run(_mesh.FaceCount, (int start, int stop) =>
		{
			UpdateFaceDiffusivitiesStartStop(invSigmaK, invSigmaEps, kEps, start, stop);
		});
	}

	private void UpdateFaceDiffusivitiesStartStop(double invSigmaK, double invSigmaEps, bool kEps, int start, int stop)
	{
		double[] cellNut = _turb.cellNut;
		FaceAB[] faceAlpha = _hmesh.FaceAlpha;
		double[] faceDiffK = _faceDiffK;
		double[] faceDiffEps = _faceDiffEps;
		for (int i = start; i <= stop; i++)
		{
			Face face = _mesh.Faces[i];
			int cellA = face.cellA;
			int cellB = face.cellB;
			double cellNutCellATerm;
			if (cellB < 0)
			{
				cellNutCellATerm = cellNut[cellA];
			}
			else
			{
				FaceAB faceAB = faceAlpha[i];
				cellNutCellATerm = faceAB.ValueA * cellNut[cellA] + faceAB.ValueB * cellNut[cellB];
			}
			faceDiffK[i] = cellNutCellATerm * invSigmaK;
			if (kEps)
			{
				faceDiffEps[i] = cellNutCellATerm * invSigmaEps;
			}
		}
	}

	private void UpdateSourceTerms()
	{
		TurbulenceParams p = Params;
		bool kEps = IsKEpsilon;
		_threadFor.Run(_mesh.CellCount, (int start, int stop) =>
		{
			UpdateSourceTermsStartStop(p, kEps, start, stop);
		});
	}

	private void UpdateSourceTermsStartStop(TurbulenceParams p, bool kEps, int start, int stop)
	{
		double bedProductionK = p.BedProductionK;
		double bedProductionEps = p.BedProductionEps;
		double c1Eps = p.C1Eps;
		double c2Eps = p.C2Eps;
		double[] cellK = _turb.cellK;
		double[] cellEps = _turb.cellEps;
		double[] cellNut = _turb.cellNut;
		double[] cellVolume = _waterState.CellVolume;
		double[] cellAreas = _hmesh.CellAreas;
		float[] cellStrainRate = _turb.cellStrainRate;
		ReadOnlySpan<float> cellHydraulicDepths = _waterState.CellHydraulicDepth;
		for (int i = start; i <= stop; i++)
		{
			double cellHydraulicDepth = cellHydraulicDepths[i];
			if (cellHydraulicDepth < 0.0001)
			{
				_srcK[i] = 0.0;
				_sinkK[i] = 0.0;
				if (kEps)
				{
					_srcEps[i] = 0.0;
					_sinkEps[i] = 0.0;
				}
				continue;
			}
			double cellVolume2 = cellVolume[i];
			double cellArea = cellAreas[i];
			double cellUStar = _cellUStar[i];
			double cellUStarPolynomial = cellUStar * cellUStar * cellUStar;
			double cellKMinTurbulentEnergyTerm = Math.Max(cellK[i], p.MinTurbulentEnergy);
			double cellStrainRate2 = cellStrainRate[i];
			double cellNutCellStrainRate2Term = cellNut[i] * cellStrainRate2 * cellStrainRate2;
			_srcK[i] = cellVolume2 * cellNutCellStrainRate2Term + cellArea * bedProductionK * cellUStarPolynomial;
			if (kEps)
			{
				double cellEpsMinDissipationTerm = Math.Max(cellEps[i], p.MinDissipation) / cellKMinTurbulentEnergyTerm;
				_sinkK[i] = (0.0 - cellVolume2) * cellEpsMinDissipationTerm;
				_srcEps[i] = cellVolume2 * c1Eps * cellEpsMinDissipationTerm * cellNutCellStrainRate2Term + cellArea * bedProductionEps * cellUStarPolynomial * cellUStar / cellHydraulicDepth;
				_sinkEps[i] = (0.0 - cellVolume2) * c2Eps * cellEpsMinDissipationTerm;
			}
			else
			{
				_sinkK[i] = (0.0 - cellVolume2) * p.CD * Math.Sqrt(cellKMinTurbulentEnergyTerm) / (p.AlphaLength * cellHydraulicDepth);
			}
		}
	}

	private void UpdateInflowBoundaryValues()
	{
		if (_inflowBC == null)
		{
			return;
		}
		TurbulenceParams turbulenceParams = Params;
		bool isKEpsilon = IsKEpsilon;
		int[] faces = _inflowBC.Faces;
		double[] faceConcentrationByConstituent = _inflowBC.FaceConcentrationByConstituent[0];
		double[] faceConcentrationByConstituentArray = (isKEpsilon ? _inflowBC.FaceConcentrationByConstituent[1] : null);
		float[] faceShearFluxCoeff = _waterState.FaceShearFluxCoeff;
		float[] faceVelMag = _waterState.FaceVelMag;
		float[] cellHydraulicDepth = _waterState.CellHydraulicDepth;
		for (int i = 0; i < faces.Length; i++)
		{
			int faceIndex = faces[i];
			int cellA = _mesh.Faces[faceIndex].cellA;
			double cellHydraulicDepth2 = cellHydraulicDepth[cellA];
			if (cellHydraulicDepth2 < 0.0001 || faceShearFluxCoeff == null || faceVelMag == null)
			{
				faceConcentrationByConstituent[i] = turbulenceParams.MinTurbulentEnergy;
				if (faceConcentrationByConstituentArray != null)
				{
					faceConcentrationByConstituentArray[i] = turbulenceParams.MinDissipation;
				}
				continue;
			}
			double uStar = (double)(faceShearFluxCoeff[faceIndex] * faceVelMag[faceIndex]) / cellHydraulicDepth2;
			faceConcentrationByConstituent[i] = Math.Max(turbulenceParams.EquilibriumK(uStar), turbulenceParams.MinTurbulentEnergy);
			if (faceConcentrationByConstituentArray != null)
			{
				faceConcentrationByConstituentArray[i] = Math.Max(turbulenceParams.EquilibriumEps(uStar, cellHydraulicDepth2), turbulenceParams.MinDissipation);
			}
		}
	}

	private void ApplyFloors()
	{
		_transport.ClampCellConcentration(0, Params.MinTurbulentEnergy);
		if (IsKEpsilon)
		{
			_transport.ClampCellConcentration(1, Params.MinDissipation);
		}
	}

	public void Cleanup()
	{
		_transport?.Cleanup();
		_threadFor?.Dispose();
		_threadFor = new ThreadFor(1);
	}
}
