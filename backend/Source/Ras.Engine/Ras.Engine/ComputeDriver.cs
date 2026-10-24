using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Microsoft.Extensions.Logging;
using Ras.Engine.Structures;
using Ras.Engine.Structures.Culverts;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Engine;

public class ComputeDriver
{
	private readonly Mesh _globalMesh;

	private readonly MeshPropertyTables _propertyTables;

	public MeshTags MeshTags;

	private List<ComputePartition> _computePartitions;

	private readonly int[] _cellPartition;

	private readonly int[] _facePartition;

	public BoundaryConditionCollection BoundaryConditions;

	public HydraulicStructureCollection HydraulicStructures;

	private ProgressReporter _reporter = ProgressReporter.None();

	private static readonly ILogger _logger = LogHelper.GetLogger("ComputeDriver");

	public Solver.StepLimitation globalStepLimitation;

	public int globalLimitingCell;

	public int globalLimitingFace;

	public int globalRetryCount;

	public int[] globalStepLimitationCount;

	public int[] cellLimitCount;

	public int globalIterationCount;

	public double globalActiveCells;

	public long StepCount;

	public MeshPropertyTables PropertyTables => _propertyTables;

	public ComputeControl computeControl { get; private set; }

	public bool IdentityMapping
	{
		get
		{
			if (_computePartitions.Count == 1)
			{
				return _computePartitions[0].IdentityMapping;
			}
			return false;
		}
	}

	public ProgressReporter Reporter
	{
		get
		{
			return _reporter;
		}
		set
		{
			if (_reporter == value)
			{
				return;
			}
			if (_reporter == null)
			{
				throw new ArgumentNullException("Reporter");
			}
			_reporter = value;
			if (_computePartitions == null)
			{
				return;
			}
			foreach (ComputePartition computePartition in _computePartitions)
			{
				computePartition.solver.Reporter = _reporter;
			}
		}
	}

	public Action<MeshTimestep> TimestepDone { get; set; }

	public Action<MeshTimestep> ComputeStepDone { get; set; }

	public long TotalOuterIterationCount
	{
		get
		{
			long totalIterationsSum = 0L;
			foreach (ComputePartition computePartition in _computePartitions)
			{
				if (computePartition.solver is SolverImp solverImp)
				{
					totalIterationsSum += solverImp.Convergence.OuterConv.TotalIterations;
				}
			}
			return totalIterationsSum;
		}
	}

	public long TotalInnerIterationCount
	{
		get
		{
			long totalIterationsSum = 0L;
			foreach (ComputePartition computePartition in _computePartitions)
			{
				if (computePartition.solver is SolverImp solverImp)
				{
					totalIterationsSum += solverImp.Convergence.InnerConv.TotalIterations;
				}
			}
			return totalIterationsSum;
		}
	}

	public long TotalIterationCount => TotalOuterIterationCount + TotalInnerIterationCount;

	public Mesh GlobalMesh => _globalMesh;

	public long TMicros { get; private set; }

	public long EndMicros
	{
		get
		{
			return computeControl.EndMicros;
		}
		private set
		{
			computeControl.EndMicros = value;
		}
	}

	public double Time => MicrosToSeconds(TMicros);

	public long DtMicros { get; private set; }

	public long DtMicrosLast { get; private set; }

	public long DtMaxMicros { get; private set; }

	public double Timestep => MicrosToSeconds(DtMicros);

	public double EndTime
	{
		get
		{
			return MicrosToSeconds(EndMicros);
		}
		set
		{
			EndMicros = SecondsToMicros(value);
		}
	}

	public MeshTimestep ConstructMeshTimestep()
	{
		MeshWaterState meshWaterState = ConstructGlobalState();
		MeshTimestep meshTimestep = new MeshTimestep(meshWaterState, this);
		HydraulicStructureCollection hydraulicStructures = HydraulicStructures;
		if (hydraulicStructures != null && hydraulicStructures.Culverts?.Count > 0)
		{
			meshTimestep.CulvertData = ExtractCulvertTimestepData(meshWaterState);
		}
		return meshTimestep;
	}

	public MeshWaterState ConstructGlobalState()
	{
		if (IdentityMapping)
		{
			MeshWaterState meshWaterState = _computePartitions[0].solver.MaybeGetMeshWaterState();
			if (meshWaterState != null)
			{
				return meshWaterState;
			}
		}
		MeshWaterState meshWaterState2 = MeshWaterState.Factory<DriverWaterStateConfig>(_globalMesh, _propertyTables);
		meshWaterState2.dtMicros = DtMicros;
		meshWaterState2.tMicros = TMicros;
		for (int i = 0; i < _computePartitions.Count; i++)
		{
			ComputePartition computePartition = _computePartitions[i];
			CompositeCellScalar(i, computePartition.solver.GetCellWaterSurface(), meshWaterState2.CellElevation);
			CompositeCellScalar(i, computePartition.solver.GetCellVolume(), meshWaterState2.CellVolume);
			CompositeFaceScalar(i, computePartition.solver.GetFaceVelocity(), meshWaterState2.FaceVelocity);
			CompositeFaceScalar(i, computePartition.solver.GetFaceArea(), meshWaterState2.FaceArea);
		}
		return meshWaterState2;
	}

	public double[] TryGetCellDtMax(MeshWaterState mws)
	{
		if (_computePartitions.Count == 1 && _computePartitions[0].IdentityMapping && _computePartitions[0].solver is Solver solver)
		{
			return solver.CellDtMax;
		}
		return null;
	}

	public void CompositeCellScalar(int partIdx, ReadOnlySpan<double> partData, double[] globalData)
	{
		ComputePartition computePartition = _computePartitions[partIdx];
		for (int i = 0; i < computePartition.CellMapping.Length; i++)
		{
			int cellMapping = computePartition.CellMapping[i];
			if (_cellPartition[cellMapping] == partIdx)
			{
				globalData[cellMapping] = partData[i];
			}
		}
	}

	public void CompositeFaceScalar(int partIdx, ReadOnlySpan<double> partData, double[] globalData)
	{
		ComputePartition computePartition = _computePartitions[partIdx];
		for (int i = 0; i < computePartition.FaceMapping.Length; i++)
		{
			int faceMappingComputePartitionTerm = computePartition.FaceMapping[i].Item1;
			if (_facePartition[faceMappingComputePartitionTerm] == partIdx)
			{
				globalData[faceMappingComputePartitionTerm] = (double)computePartition.FaceMapping[i].Item2 * partData[i];
			}
		}
	}

	public void CompositeFaceScalar(int partIdx, ReadOnlySpan<float> partData, float[] globalData)
	{
		ComputePartition computePartition = _computePartitions[partIdx];
		for (int i = 0; i < computePartition.FaceMapping.Length; i++)
		{
			int faceMappingComputePartitionTerm = computePartition.FaceMapping[i].Item1;
			if (_facePartition[faceMappingComputePartitionTerm] == partIdx)
			{
				globalData[faceMappingComputePartitionTerm] = (float)computePartition.FaceMapping[i].Item2 * partData[i];
			}
		}
	}

	private CulvertTimestepData ExtractCulvertTimestepData(MeshWaterState waterState)
	{
		List<Culvert> culverts = HydraulicStructures.Culverts;
		int count = culverts.Count;
		CulvertTimestepData culvertTimestepData = new CulvertTimestepData
		{
			Flow = new float[count],
			OutsideDepth = new float[count * 2],
			OutletVelocity = new float[count]
		};
		for (int i = 0; i < count; i++)
		{
			Culvert culvert = culverts[i];
			double cellElevation = waterState.CellElevation[culvert.UpCells[0]];
			double cellElevation2 = waterState.CellElevation[culvert.DnCells[0]];
			double outsideDepth = cellElevation - (double)PropertyTables.CellMinElevation(culvert.UpCells[0]);
			double outsideDepth2 = cellElevation2 - (double)PropertyTables.CellMinElevation(culvert.DnCells[0]);
			culvertTimestepData.OutsideDepth[i * 2] = (float)outsideDepth;
			culvertTimestepData.OutsideDepth[i * 2 + 1] = (float)outsideDepth2;
			var (flow, outletVelocity, _, _) = culvert.GetFlowVelDerivs(waterState, 0.0);
			culvertTimestepData.Flow[i] = (float)flow;
			culvertTimestepData.OutletVelocity[i] = (float)outletVelocity;
		}
		return culvertTimestepData;
	}

	public static long SecondsToMicros(double secs)
	{
		secs *= 1000000.0;
		return (long)secs;
	}

	public static double MicrosToSeconds(long micros)
	{
		return (double)micros / 1000000.0;
	}

	public ComputeDriver(Mesh globalMesh, MeshPropertyTables propertyTables)
		: this(globalMesh, propertyTables, new ComputeControl())
	{
	}

	public ComputeDriver(Mesh globalMesh, MeshPropertyTables propertyTables, ComputeControl control)
	{
		computeControl = control ?? new ComputeControl();
		_globalMesh = globalMesh;
		_propertyTables = propertyTables;
		_computePartitions = new List<ComputePartition>();
		BoundaryConditions = new BoundaryConditionCollection(_globalMesh);
		HydraulicStructures = new HydraulicStructureCollection();
		globalStepLimitationCount = new int[1 + Enum.GetValues(typeof(Solver.StepLimitation)).Cast<int>().Max()];
		if (computeControl.accumulateStepLimitation)
		{
			cellLimitCount = new int[_globalMesh.CellCount];
		}
		else
		{
			cellLimitCount = null;
		}
	}

	public ComputeDriver(Mesh globalMesh, MeshPropertyTables propertyTables, MeshWaterState initialState, ComputeControl control, List<SolverControl> solverControls, int[] cellSolverMapping)
		: this(globalMesh, propertyTables, control)
	{
		_cellPartition = cellSolverMapping.ToArray();
		_facePartition = PartitionFacesFromCells();
		List<int>[] intLists = new List<int>[solverControls.Count];
		for (int i = 0; i < solverControls.Count; i++)
		{
			intLists[i] = new List<int>();
		}
		for (int j = 0; j < globalMesh.CellCount; j++)
		{
			intLists[_cellPartition[j]].Add(j);
		}
		for (int k = 0; k < solverControls.Count; k++)
		{
			SolverControl solverControl = solverControls[k];
			List<int> cellIndexes = intLists[k];
			solverControl.AddGhosts(cellIndexes, globalMesh);
			_computePartitions.Add(new ComputePartition(solverControl, cellIndexes, globalMesh, propertyTables, initialState, this));
		}
	}

	public ComputeDriver(Mesh globalMesh, MeshPropertyTables propertyTables, MeshWaterState initialState, ComputeControl control, SolverControl solverControl)
		: this(globalMesh, propertyTables, control)
	{
		_computePartitions.Add(new ComputePartition(solverControl, null, _globalMesh, propertyTables, initialState, this));
	}

	private int[] PartitionFacesFromCells()
	{
		int[] intValues = new int[_globalMesh.FaceCount];
		for (int i = 0; i < _globalMesh.FaceCount; i++)
		{
			Face face = _globalMesh.Faces[i];
			intValues[i] = _cellPartition[face.cellA];
		}
		return intValues;
	}

	public Tuple<int, int> MapFaceToLocal(int globalFace, out int partIdx)
	{
		partIdx = _facePartition[globalFace];
		return _computePartitions[partIdx].MapFaceToLocal(globalFace);
	}

	public int MapCellToLocal(int globalCell, out int partIdx)
	{
		partIdx = _cellPartition[globalCell];
		return _computePartitions[partIdx].MapCellToLocal(globalCell);
	}

	public static ComputeDriver SingleSolverFactory(ISolver solver, MeshPropertyTables propertyTables, ComputeControl computeControl)
	{
		ComputeDriver computeDriver = new ComputeDriver(solver.GetMesh(), propertyTables, computeControl);
		computeDriver._computePartitions.Add(new ComputePartition(solver, computeDriver._globalMesh));
		return computeDriver;
	}

	public ISolver GetSolver(int partIdx)
	{
		return _computePartitions[partIdx].solver;
	}

	public void DistributeBoundaryConditions()
	{
		if (_computePartitions.Count == 1 && _computePartitions[0].IdentityMapping)
		{
			BoundaryConditionCollection boundaryConditions = _computePartitions[0].solver.GetBoundaryConditions();
			if (!boundaryConditions.Equals(BoundaryConditions))
			{
				boundaryConditions.FlowBoundaryConditions.AddRange(BoundaryConditions.FlowBoundaryConditions);
				boundaryConditions.InternalFlowBoundaryConditions.AddRange(BoundaryConditions.InternalFlowBoundaryConditions);
				boundaryConditions.StageBoundaryConditions.AddRange(BoundaryConditions.StageBoundaryConditions);
				boundaryConditions.RatingCurveBoundaryConditions.AddRange(BoundaryConditions.RatingCurveBoundaryConditions);
				boundaryConditions.VariableFlowBoundaryConditions.AddRange(BoundaryConditions.VariableFlowBoundaryConditions);
				boundaryConditions.VariableStageBoundaryConditions.AddRange(BoundaryConditions.VariableStageBoundaryConditions);
				boundaryConditions.SpatiallyVariableStageVelocityBoundaryConditions.AddRange(BoundaryConditions.SpatiallyVariableStageVelocityBoundaryConditions);
				boundaryConditions.NormalDepthBoundaryConditions.AddRange(BoundaryConditions.NormalDepthBoundaryConditions);
				boundaryConditions.Precipitation = BoundaryConditions.Precipitation;
				boundaryConditions.Evapotranspiration = BoundaryConditions.Evapotranspiration;
				boundaryConditions.AirPressure = BoundaryConditions.AirPressure;
				boundaryConditions.Wind = BoundaryConditions.Wind;
			}
			return;
		}
		foreach (BoundaryCondition flowBoundaryCondition in BoundaryConditions.FlowBoundaryConditions)
		{
			DistributeBoundaryCondition(flowBoundaryCondition, BoundaryCondition.BCType.Flow);
		}
		foreach (BoundaryCondition internalFlowBoundaryCondition in BoundaryConditions.InternalFlowBoundaryConditions)
		{
			DistributeBoundaryCondition(internalFlowBoundaryCondition, BoundaryCondition.BCType.InternalFlow);
		}
		foreach (BoundaryCondition stageBoundaryCondition in BoundaryConditions.StageBoundaryConditions)
		{
			DistributeBoundaryCondition(stageBoundaryCondition, BoundaryCondition.BCType.Stage);
		}
		foreach (BoundaryCondition ratingCurveBoundaryCondition in BoundaryConditions.RatingCurveBoundaryConditions)
		{
			DistributeBoundaryCondition(ratingCurveBoundaryCondition, BoundaryCondition.BCType.RatingCurve);
		}
		foreach (BoundaryCondition variableFlowBoundaryCondition in BoundaryConditions.VariableFlowBoundaryConditions)
		{
			DistributeBoundaryCondition(variableFlowBoundaryCondition, BoundaryCondition.BCType.VariableFlow);
		}
		foreach (BoundaryCondition variableStageBoundaryCondition in BoundaryConditions.VariableStageBoundaryConditions)
		{
			DistributeBoundaryCondition(variableStageBoundaryCondition, BoundaryCondition.BCType.VariableStage);
		}
		foreach (BoundaryCondition spatiallyVariableStageVelocityBoundaryCondition in BoundaryConditions.SpatiallyVariableStageVelocityBoundaryConditions)
		{
			DistributeBoundaryCondition(spatiallyVariableStageVelocityBoundaryCondition, BoundaryCondition.BCType.SpatiallyVariableStageVelocity);
		}
		foreach (BoundaryCondition normalDepthBoundaryCondition in BoundaryConditions.NormalDepthBoundaryConditions)
		{
			DistributeBoundaryCondition(normalDepthBoundaryCondition, BoundaryCondition.BCType.NormalDepth);
		}
		DistributePrecipitation(BoundaryConditions.Precipitation);
		DistributeEvapotranspiration(BoundaryConditions.Evapotranspiration);
		DistributeAirPressure(BoundaryConditions.AirPressure);
		DistributeWind(BoundaryConditions.Wind);
	}

	private void DistributeBoundaryCondition(BoundaryCondition bc, BoundaryCondition.BCType bcType)
	{
		int unity = -1;
		BoundaryCondition boundaryCondition = bc.ShallowCopy();
		boundaryCondition.Faces = new int[bc.Faces.Length];
		if (bc.Faces != null && bc.Faces.Length != 0)
		{
			for (int i = 0; i < bc.Faces.Length; i++)
			{
				int faceIndex = bc.Faces[i];
				Tuple<int, int> tuple = MapFaceToLocal(faceIndex, out var partIdx);
				if (unity < 0)
				{
					unity = partIdx;
				}
				else if (unity != partIdx)
				{
					Point point = GlobalMesh.FaceCentroidPoint(faceIndex);
					throw new SolverException($"Boundary condition near {point.X} {point.Y} is split across solvers");
				}
				boundaryCondition.Faces[i] = tuple.Item1;
				if ((bcType == BoundaryCondition.BCType.Flow || bcType == BoundaryCondition.BCType.VariableFlow) && tuple.Item2 < 0)
				{
					throw new SolverException("Flipping flow boundary conditions not allowed [yet]");
				}
			}
		}
		else if (bc.Cells != null && bc.Cells.Length != 0)
		{
			for (int j = 0; j < bc.Cells.Length; j++)
			{
				int cellIndex = bc.Cells[j];
				MapCellToLocal(cellIndex, out var partIdx2);
				if (unity < 0)
				{
					unity = partIdx2;
				}
				else if (unity != partIdx2)
				{
					Point point2 = GlobalMesh.CellCenters[cellIndex];
					throw new SolverException($"Boundary condition near {point2.X} {point2.Y} is split across solvers");
				}
			}
		}
		_computePartitions[unity].BoundaryConditions.AddBoundaryCondition(boundaryCondition, bcType);
	}

	private void DistributePrecipitation(SpatiotemporalBoundaryCondition precip)
	{
		if (precip == null)
		{
			foreach (ComputePartition computePartition in _computePartitions)
			{
				computePartition.solver.GetBoundaryConditions().Precipitation = null;
			}
			return;
		}
		throw new SolverException("Precipitation spread across multiple solvers is not implemented.");
	}

	private void DistributeEvapotranspiration(SpatiotemporalBoundaryCondition evap)
	{
		if (evap == null)
		{
			foreach (ComputePartition computePartition in _computePartitions)
			{
				computePartition.solver.GetBoundaryConditions().Evapotranspiration = null;
			}
			return;
		}
		throw new SolverException("Evapotranspiration spread across multiple solvers is not implemented.");
	}

	private void DistributeAirPressure(SpatiotemporalBoundaryCondition airPres)
	{
		if (airPres == null)
		{
			foreach (ComputePartition computePartition in _computePartitions)
			{
				computePartition.solver.GetBoundaryConditions().AirPressure = null;
			}
			return;
		}
		throw new SolverException("Atmospheric Pressure spread across multiple solvers is not implemented.");
	}

	private void DistributeWind(SpatiotemporalBoundaryCondition wind)
	{
		if (wind == null)
		{
			foreach (ComputePartition computePartition in _computePartitions)
			{
				computePartition.solver.GetBoundaryConditions().Wind = null;
			}
			return;
		}
		throw new SolverException("Wind spread across multiple solvers is not implemented.");
	}

	public void ApplyBoundaryConditions(double absTime, double dt)
	{
		foreach (ComputePartition computePartition in _computePartitions)
		{
			computePartition.solver.ApplyBoundaryConditions(absTime, dt);
		}
	}

	public void DistributeHydraulicStructures()
	{
		if (_computePartitions.Count == 1 && _computePartitions[0].IdentityMapping)
		{
			HydraulicStructureCollection hydraulicStructures = _computePartitions[0].solver.GetHydraulicStructures();
			if ((hydraulicStructures != null || HydraulicStructures != null) && !object.Equals(hydraulicStructures, HydraulicStructures))
			{
				hydraulicStructures.AllHydraulicStructures.AddRange(HydraulicStructures.AllHydraulicStructures);
				hydraulicStructures.Weirs.AddRange(HydraulicStructures.Weirs);
				hydraulicStructures.Gates.AddRange(HydraulicStructures.Gates);
				hydraulicStructures.Culverts.AddRange(HydraulicStructures.Culverts);
				hydraulicStructures.Pumps.AddRange(HydraulicStructures.Pumps);
			}
			return;
		}
		foreach (HydraulicStructure allHydraulicStructure in HydraulicStructures.AllHydraulicStructures)
		{
			DistributeHydraulicStructure(allHydraulicStructure);
		}
	}

	private void DistributeHydraulicStructure(HydraulicStructure hs)
	{
		int unity = -1;
		HydraulicStructure hydraulicStructure = hs.ShallowCopy();
		hydraulicStructure.Faces = new int[hs.Faces.Length];
		for (int i = 0; i < hs.Faces.Length; i++)
		{
			int faceIndex = hs.Faces[i];
			Tuple<int, int> tuple = MapFaceToLocal(faceIndex, out var partIdx);
			if (unity < 0)
			{
				unity = partIdx;
			}
			else if (unity != partIdx)
			{
				Point point = GlobalMesh.FaceCentroidPoint(faceIndex);
				throw new SolverException($"Hydraulic structure near {point.X} {point.Y} is split across solvers");
			}
			hydraulicStructure.Faces[i] = tuple.Item1;
		}
		hydraulicStructure.UpCells = new int[hs.UpCells.Length];
		for (int j = 0; j < hs.UpCells.Length; j++)
		{
			int upCellIndex = hs.UpCells[j];
			int upCells = MapCellToLocal(upCellIndex, out var partIdx2);
			if (unity < 0)
			{
				unity = partIdx2;
			}
			else if (unity != partIdx2)
			{
				Point point2 = GlobalMesh.CellCenters[upCellIndex];
				throw new SolverException($"Hydraulic structure near {point2.X} {point2.Y} is split across solvers");
			}
			hydraulicStructure.UpCells[j] = upCells;
		}
		hydraulicStructure.DnCells = new int[hs.DnCells.Length];
		for (int k = 0; k < hs.DnCells.Length; k++)
		{
			int dnCellIndex = hs.DnCells[k];
			int dnCells = MapCellToLocal(dnCellIndex, out var partIdx3);
			if (unity < 0)
			{
				unity = partIdx3;
			}
			else if (unity != partIdx3)
			{
				Point point3 = GlobalMesh.CellCenters[dnCellIndex];
				throw new SolverException($"Hydraulic structure near {point3.X} {point3.Y} is split across solvers");
			}
			hydraulicStructure.DnCells[k] = dnCells;
		}
		_computePartitions[unity].HydraulicStructures.AddHydraulicStructure(hydraulicStructure);
	}

	public void ApplyHydraulicStructures(double absTime, double dt)
	{
		foreach (ComputePartition computePartition in _computePartitions)
		{
			computePartition.solver.ApplyHydraulicStructures(absTime, dt);
		}
	}

	public OperationResult Solve(double duration)
	{
		return Solve(SecondsToMicros(duration));
	}

	public OperationResult Solve(long durationMicros)
	{
		EndMicros = TMicros + durationMicros;
		return Solve();
	}

	public OperationResult Solve()
	{
		Reporter.ReportMessage("Initializing Solver (CPU)...");
		try
		{
			Prepare(Reporter);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error during preparation for solve.");
			Reporter.ReportMessage("Solve failed during preparation");
			return OperationResult.Fail("Compute", ex);
		}
		if (Reporter.CancelRequested)
		{
			return OperationResult.Fail(GeneralMessageCodes.GetUserOperationCancelled("Compute"));
		}
		Reporter.ReportMessage("Computing (CPU)...");
		Reporter.ReportProgress(0);
		OperationResult result;
		try
		{
			result = SolveSteps();
		}
		catch (Exception ex2)
		{
			_logger.LogError(ex2, "Error during solve.");
			Reporter.ReportMessage("Solve failed");
			result = OperationResult.Fail("Compute", ex2);
		}
		Cleanup();
		return result;
	}

	public void Prepare(ProgressReporter pr = null)
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		globalRetryCount = 0;
		globalIterationCount = 0;
		for (int i = 0; i < globalStepLimitationCount.Length; i++)
		{
			globalStepLimitationCount[i] = 0;
		}
		DtMicrosLast = 0L;
		foreach (ComputePartition computePartition2 in _computePartitions)
		{
			MeshWaterState meshWaterState = computePartition2.solver.MaybeGetMeshWaterState();
			if (meshWaterState != null && meshWaterState.dtMicros > DtMicrosLast)
			{
				DtMicrosLast = meshWaterState.dtMicros;
			}
		}
		DtMicros = 0L;
		DistributeBoundaryConditions();
		DistributeHydraulicStructures();
		float scaledCount = 1f / (float)_computePartitions.Count;
		for (int j = 0; j < _computePartitions.Count; j++)
		{
			ComputePartition computePartition = _computePartitions[j];
			ProgressReporter progressReporter = pr.SubTask("Partition " + j, (float)j * scaledCount, scaledCount);
			computePartition.UpdateMeshFlags(computePartition.solver.GetCellFlags(), computeControl.CellFlagMap, MeshTags);
			computePartition.solver.Prepare(progressReporter);
			if (progressReporter.CancelRequested)
			{
				break;
			}
		}
	}

	public OperationResult SolveSteps(double duration)
	{
		return SolveSteps(SecondsToMicros(duration));
	}

	public OperationResult SolveSteps(long durationMicros)
	{
		EndMicros = TMicros + durationMicros;
		return SolveSteps();
	}

	public OperationResult SolveSteps()
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		DtMicros = computeControl.DtMicros;
		OperationResult operationResult = new OperationResult(success: true);
		ReportState(operationResult.Result, userTimestep: true);
		new Stopwatch();
		while (TMicros < EndMicros)
		{
			long totalIterationCount = TotalIterationCount;
			Reporter.ReportProgressFraction((float)TMicros / (float)EndMicros);
			if (Reporter.CancelRequested)
			{
				break;
			}
			long dtMicrosPrevious = 0L;
			CheckpointState();
			ComputeNextTimestep(operationResult, out var nextDtMicros, out var nextTUserMicros, out var nextDtMaxMicros);
			DtMicros = nextDtMicros;
			DtMaxMicros = nextDtMaxMicros;
			if (operationResult.Result)
			{
				int i;
				for (i = 0; i < computeControl.maxIterations; i++)
				{
					globalStepLimitationCount[(int)globalStepLimitation]++;
					double dt = MicrosToSeconds(DtMicros);
					ApplyBoundaryConditions(Time, dt);
					ApplyHydraulicStructures(Time, dt);
					ISolver.Status status = SolveStep(TMicros, DtMicros, dtMicrosPrevious);
					if (status == ISolver.Status.OK)
					{
						break;
					}
					dtMicrosPrevious = DtMicros;
					DtMicros /= 2L;
					if (DtMicros < computeControl.dtMinMicros)
					{
						_logger.LogTrace("TIMESTEP: repeats forced time step below dtMin={dtMin}", MicrosToSeconds(computeControl.dtMinMicros));
						operationResult.Result = false;
						operationResult.AddMessage(EngineMessageCodes.GetSolverCourantViolation(globalLimitingCell, MicrosToSeconds(DtMicros)));
						break;
					}
					globalStepLimitation = status switch
					{
						ISolver.Status.ELEVATION_CHANGE => Solver.StepLimitation.LIM_RETRY_ELEVATION_CHANGE, 
						ISolver.Status.NAN_STATE => Solver.StepLimitation.LIM_RETRY_NAN_STATE, 
						ISolver.Status.VOLUME_NEGATIVE => Solver.StepLimitation.LIM_RETRY_NEGATIVE_VOLUME, 
						_ => Solver.StepLimitation.LIM_RETRY_ERROR, 
					};
					RevertState();
					globalRetryCount++;
				}
				if (i >= computeControl.maxIterations)
				{
					_logger.LogTrace("Too many retries for a step");
					operationResult.Result = false;
					operationResult.AddMessage(EngineMessageCodes.GetExceededMaxRetries(computeControl.maxIterations));
				}
				foreach (ComputePartition computePartition in _computePartitions)
				{
					computePartition.solver.CommitState();
				}
				DtMicrosLast = DtMicros;
				TMicros += DtMicros;
				StepCount++;
				if (cellLimitCount != null && globalLimitingCell >= 0)
				{
					cellLimitCount[globalLimitingCell]++;
				}
				long totalIterationCountDifference = TotalIterationCount - totalIterationCount;
				globalIterationCount = (int)((totalIterationCountDifference > int.MaxValue) ? int.MaxValue : totalIterationCountDifference);
				int cellCountSum = 0;
				int cellCount = _globalMesh.CellCount;
				foreach (ComputePartition computePartition2 in _computePartitions)
				{
					if (computePartition2.solver.Control.timeStepping == SolverControl.TimeStepping.Implicit)
					{
						SolverImp solverImp = (SolverImp)computePartition2.solver;
						cellCountSum += solverImp.PartGrid.CellCount;
					}
					else
					{
						Solver solver = (Solver)computePartition2.solver;
						cellCountSum += solver.Mesh.CellCount;
					}
				}
				globalActiveCells = (double)cellCountSum / (double)cellCount * 100.0;
			}
			ReportState(operationResult, TMicros == nextTUserMicros);
			if (!operationResult.Result)
			{
				_logger.LogTrace("Abort SolveSteps.");
				break;
			}
		}
		if (Reporter.CancelRequested)
		{
			_logger.LogTrace("Run cancelled at {Now}", DateTime.Now);
			operationResult.Result = false;
			operationResult.AddMessage(EngineMessageCodes.GetUserCancelled());
			Reporter.ReportMessage(EngineMessageCodes.GetUserCancelled());
		}
		else
		{
			Reporter.ReportProgress(100);
			Reporter.ReportMessage(EngineMessageCodes.GetCompleted(stopwatch.Elapsed));
		}
		_logger.LogTrace("Exit SolveSteps, {Time}", Time);
		_logger.LogTrace("Runtime for SolveSteps: {solveWatch.Elapsed}", stopwatch.Elapsed);
		return operationResult;
	}

	private void ReportState(OperationResult opResult, bool userTimestep)
	{
		MeshTimestep meshTimestep = null;
		if (ComputeStepDone != null)
		{
			if (meshTimestep == null)
			{
				meshTimestep = ConstructMeshTimestep();
			}
			ComputeStepDone(meshTimestep);
		}
		if (TimestepDone != null && (userTimestep || !opResult.Result))
		{
			if (meshTimestep == null)
			{
				meshTimestep = ConstructMeshTimestep();
			}
			TimestepDone?.Invoke(meshTimestep);
		}
		foreach (ComputePartition computePartition in _computePartitions)
		{
			computePartition.solver.ReportState(opResult, userTimestep);
		}
	}

	public void ComputeNextTimestep(OperationResult opResult, out long nextDtMicros, out long nextTUserMicros, out long nextDtMaxMicros)
	{
		long dtMicrosSum = TMicros % computeControl.DtMicros;
		if (dtMicrosSum < 0)
		{
			dtMicrosSum += computeControl.DtMicros;
		}
		nextTUserMicros = TMicros + (computeControl.DtMicros - dtMicrosSum);
		nextTUserMicros = Math.Min(nextTUserMicros, EndMicros);
		long nextDtMicros2 = (nextDtMaxMicros = SecondsToMicros(Math.Min(ComputeMaxGlobalTimestep(out var limitingCell, out var limitingFace), 8640000.0)));
		if (computeControl.variableDt)
		{
			nextDtMicros = nextDtMicros2;
			if (DtMicrosLast > 0)
			{
				long nextDtMicros3 = (long)((1.0 + computeControl.dtMaxIncrease) * (double)DtMicrosLast);
				if (nextDtMicros3 < nextDtMicros)
				{
					nextDtMicros = nextDtMicros3;
					globalStepLimitation = Solver.StepLimitation.LIM_INCREASE;
				}
			}
			long nextTUserMicrosAboveTMicros = nextTUserMicros - TMicros;
			if (nextTUserMicrosAboveTMicros < computeControl.nLookahead * nextDtMicros)
			{
				int nextDtMicrosTerm = (int)((nextTUserMicrosAboveTMicros - 1) / nextDtMicros) + 1;
				long nextDtMicros4 = nextTUserMicrosAboveTMicros / nextDtMicrosTerm;
				nextDtMicros = nextDtMicros4;
				if (nextDtMicros == computeControl.DtMicros)
				{
					globalStepLimitation = Solver.StepLimitation.LIM_USER;
					globalLimitingCell = -1;
				}
			}
			if (globalStepLimitation != Solver.StepLimitation.LIM_USER && globalStepLimitation != Solver.StepLimitation.LIM_INCREASE)
			{
				globalLimitingCell = limitingCell;
				globalLimitingFace = limitingFace;
			}
		}
		else
		{
			nextDtMaxMicros = computeControl.DtMicros;
			MicrosToSeconds(computeControl.DtMicros);
			nextDtMicros = computeControl.DtMicros;
			long nextDtMicros5 = nextTUserMicros - TMicros;
			if (nextDtMicros > nextDtMicros5)
			{
				nextDtMicros = nextDtMicros5;
			}
			globalStepLimitation = Solver.StepLimitation.LIM_USER;
		}
		if (nextDtMicros < computeControl.dtMinMicros)
		{
			_logger.LogTrace("TIMESTEP: is below dtMin={dtMin}", MicrosToSeconds(computeControl.dtMinMicros));
			_logger.LogTrace("  limitingCell: {limitingCell} stepLimitation: {stepLimitation}", globalLimitingCell, globalStepLimitation);
			if (globalLimitingCell >= 0)
			{
				_logger.LogTrace("  cell location: {x} {y}", _globalMesh.CellCenters[globalLimitingCell].X, _globalMesh.CellCenters[globalLimitingCell].Y);
			}
			opResult.Result = false;
			opResult.AddMessage(EngineMessageCodes.GetSolverCourantViolation(globalLimitingCell, MicrosToSeconds(nextDtMicros)));
		}
	}

	public ISolver.Status SolveStep(long tMicros, long dtMicros, long dtMicrosPrevious)
	{
		ISolver.Status result = ISolver.Status.OK;
		foreach (ComputePartition computePartition in _computePartitions)
		{
			ISolver.Status status = computePartition.solver.SolveStep(tMicros, dtMicros, dtMicrosPrevious, out var failureCell);
			if (status != ISolver.Status.OK)
			{
				result = status;
				if (failureCell >= 0)
				{
					globalLimitingCell = computePartition.MapCellToGlobal(failureCell);
					cellLimitCount[globalLimitingCell]++;
				}
			}
		}
		return result;
	}

	public void CheckpointState()
	{
		foreach (ComputePartition computePartition in _computePartitions)
		{
			computePartition.solver.CheckpointState();
		}
	}

	public void RevertState()
	{
		foreach (ComputePartition computePartition in _computePartitions)
		{
			computePartition.solver.RevertState();
		}
	}

	public double ComputeMaxGlobalTimestep(out int limitingCell, out int limitingFace)
	{
		globalStepLimitation = Solver.StepLimitation.LIM_NONE;
		limitingCell = -1;
		limitingFace = -1;
		double maxValue = double.MaxValue;
		int ordinal = -1;
		int limitingCell2Term = -1;
		int limitingFace2Term = -1;
		for (int i = 0; i < _computePartitions.Count; i++)
		{
			double maximumTimestep = _computePartitions[i].solver.MaximumTimestep(out var stepLimitation, out var limitingCell2, out var limitingFace2);
			if (maximumTimestep < maxValue)
			{
				maxValue = maximumTimestep;
				globalStepLimitation = stepLimitation;
				ordinal = i;
				limitingCell2Term = limitingCell2;
				limitingFace2Term = limitingFace2;
			}
		}
		if (ordinal >= 0)
		{
			if (limitingCell2Term >= 0)
			{
				limitingCell = _computePartitions[ordinal].MapCellToGlobal(limitingCell2Term);
			}
			if (limitingFace2Term >= 0)
			{
				(limitingFace, _) = _computePartitions[ordinal].MapFaceToGlobal(limitingFace2Term);
			}
		}
		return maxValue;
	}

	public void Cleanup()
	{
		foreach (ComputePartition computePartition in _computePartitions)
		{
			computePartition.solver.Cleanup();
		}
	}
}
