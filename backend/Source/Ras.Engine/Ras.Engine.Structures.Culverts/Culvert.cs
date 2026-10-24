using System;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Ras.Engine.Structures.Culverts;

public class Culvert : HydraulicStructure
{
	protected static readonly ILogger _logger = LogHelper.GetLogger("Culvert");

	private double _currentFlow;

	public static readonly bool LogCulvertFlow = false;

	public CulvertRatingTable CulvertRatingTable;

	public CulvertRatingTable CulvertRatingTableReverse;

	private double _prevAppliedFlow;

	public Vector Axis { get; set; }

	public CulvertPhysProps PhysProps { get; set; } = new CulvertPhysProps();

	public CulvertSolverControl SolverControl { get; set; }

	public bool UseRatingTable => true;

	public override bool TreatFlowAsInternalBoundary => false;

	public override HSType StructureType => HSType.Culvert;

	public override double CellWaterTransferFlow => _currentFlow;

	public Culvert(string name, Polyline cL)
		: base(name, cL)
	{
	}

	public override void DistributeStructure()
	{
	}

	public override void Prepare(SolverControl solverControl, Mesh mesh, MeshPropertyTables meshPropertyTables)
	{
		SolverControl = new CulvertSolverControl(UnitSys);
		_prevAppliedFlow = 0.0;
		Point pA = CenterLine.Points[0];
		Point pB = CenterLine.Points[CenterLine.Points.Count - 1];
		Axis = new Vector(pA, pB);
		PhysProps.Length = CenterLine.Length;
		if (UseRatingTable)
		{
			FlowOrientedCulvert flowOrientedCulvert = new FlowOrientedCulvert(PhysProps, reversed: false);
			CulvertRatingTable = new CulvertRatingTable(Name, flowOrientedCulvert, SolverControl);
			flowOrientedCulvert.Reversed = true;
			CulvertRatingTableReverse = new CulvertRatingTable(Name, flowOrientedCulvert, SolverControl);
		}
	}

	public override void Apply(double absTime, double dt, HydraulicStructureCollection hsCollection, MeshWaterState waterState, Mesh mesh, MeshPropertyTables propTables, HydraulicMeshComputer hmesh)
	{
		(double flow, double vel, double gradUp, double gradDn) flowVelDerivs = GetFlowVelDerivs(waterState, dt);
		double flow = flowVelDerivs.flow;
		double vel = flowVelDerivs.vel;
		double gradUp = flowVelDerivs.gradUp;
		double gradDn = flowVelDerivs.gradDn;
		_currentFlow = flow;
		UpdateSourceSink(hsCollection, UpCells[0], flow, vel, gradUp, isUpstream: true);
		UpdateSourceSink(hsCollection, DnCells[0], flow, vel, gradDn, isUpstream: false);
		if (LogCulvertFlow)
		{
			if (absTime == 0.0)
			{
				StructureTSLogger.RotateLog();
			}
			double cellElevation = waterState.CellElevation[UpCells[0]];
			double cellElevation2 = waterState.CellElevation[DnCells[0]];
			StructureTSLogger.Log($"{absTime}, {cellElevation}, {cellElevation2}, {flow}");
		}
	}

	public override bool TryGetCellWaterTransferCells(out int upstreamCell, out int downstreamCell)
	{
		if (UpCells == null || DnCells == null || UpCells.Length == 0 || DnCells.Length == 0)
		{
			upstreamCell = -1;
			downstreamCell = -1;
			return false;
		}
		upstreamCell = UpCells[0];
		downstreamCell = DnCells[0];
		return true;
	}

	public (double flow, double vel, double gradUp, double gradDn) GetFlowVelDerivs(MeshWaterState waterState, double dt)
	{
		double upstreamWse = waterState.CellElevation[UpCells[0]];
		double downstreamWse = waterState.CellElevation[DnCells[0]];
		double culvVelocityHead = GetCulvVelocityHead(waterState, UpCells[0]);
		double culvVelocityHead2 = GetCulvVelocityHead(waterState, DnCells[0]);
		CulvertBoundaryConditions culvertBoundaryConditions = new CulvertBoundaryConditions(upstreamWse, downstreamWse, culvVelocityHead, culvVelocityHead2);
		double computed;
		double computed2;
		double dFlow_dDown;
		double dFlow_dUp;
		if (UseRatingTable)
		{
			(double usVal, double dsVal, bool isReverse) eGorWSELForRatingTable = culvertBoundaryConditions.GetEGorWSELForRatingTable();
			var (localValue, localValue2, _) = eGorWSELForRatingTable;
			if (!eGorWSELForRatingTable.isReverse)
			{
				(computed, computed2, dFlow_dDown, dFlow_dUp) = CulvertRatingTable.GetFlowVelDerivs(localValue, localValue2);
			}
			else
			{
				(double flow, double vel, double dFlow_dUp, double dFlow_dDown) flowVelDerivs = CulvertRatingTableReverse.GetFlowVelDerivs(localValue2, localValue);
				double flow2 = flowVelDerivs.flow;
				double vel2 = flowVelDerivs.vel;
				double dFlow_dUp2 = flowVelDerivs.dFlow_dUp;
				double dFlow_dDown2 = flowVelDerivs.dFlow_dDown;
				double flow2Offset = 0.0 - flow2;
				double vel2Offset = 0.0 - vel2;
				dFlow_dUp = dFlow_dUp2;
				dFlow_dDown = dFlow_dDown2;
				computed2 = vel2Offset;
				computed = flow2Offset;
			}
		}
		else
		{
			CulvertFlowSolverResult culvertFlowSolverResult = CulvertFlowSolver.Solve(PhysProps, culvertBoundaryConditions, SolverControl);
			if (!culvertFlowSolverResult.Convergence.IsConverged)
			{
				_logger.LogWarning("Culvert {Name} flow solver did not converge for upstream val {UpVal} and downstream val {DnVal}. EG Error: {EgError}", Name, culvertBoundaryConditions.UpstreamEnergyGrade, culvertBoundaryConditions.DownstreamEnergyGrade, culvertFlowSolverResult.Convergence.EnergyGradeError);
			}
			computed = culvertFlowSolverResult.Solution.Qtotal;
			double depth = culvertFlowSolverResult.Solution.WSEOutlet - PhysProps.InvertDS;
			CulvertXSection culvertXSection = new CulvertXSection();
			culvertXSection.Depth = depth;
			culvertXSection.Qtotal = culvertFlowSolverResult.Solution.Qtotal;
			culvertXSection.Mann = PhysProps.Mann;
			culvertXSection.Compute(PhysProps, SolverControl);
			computed2 = culvertXSection.Vel;
			dFlow_dDown = 0.0;
			dFlow_dUp = 0.0;
		}
		double cellArea = waterState.CellArea[UpCells[0]];
		double cellArea2 = waterState.CellArea[DnCells[0]];
		if (dt > 0.0 && cellArea > 0.0 && cellArea2 > 0.0)
		{
			double dtDFlow_dDownTerm = Math.Max(1.0, 1.0 + dt * (dFlow_dDown / cellArea - dFlow_dUp / cellArea2));
			double prevAppliedFlow = _prevAppliedFlow + (computed - _prevAppliedFlow) / dtDFlow_dDownTerm;
			if (Math.Abs(computed) > 1E-09)
			{
				computed2 *= prevAppliedFlow / computed;
			}
			computed = (_prevAppliedFlow = prevAppliedFlow);
		}
		return (flow: computed, vel: computed2, gradUp: dFlow_dDown, gradDn: dFlow_dUp);
	}

	private void UpdateSourceSink(HydraulicStructureCollection hsCollection, int cIdx, double flow, double vel, double grad, bool isUpstream)
	{
		(double flow, double grad, Vector momentum) tuple = hsCollection.SourceSinks[cIdx];
		double flow2 = tuple.flow;
		double grad2 = tuple.grad;
		Vector momentum = tuple.momentum;
		int isUpstreamTerm = ((!isUpstream) ? 1 : (-1));
		Vector vector = momentum + isUpstreamTerm * Axis.UnitVector() * vel * flow;
		hsCollection.SourceSinks[cIdx] = (flow2 + (double)isUpstreamTerm * flow, grad2 + grad, vector);
	}

	private double GetCulvVelocityHead(MeshWaterState waterState, int cell)
	{
		Vector other = waterState.CellVelocity[cell];
		return Math.Pow(Math.Max(Axis.Dot(other) / Axis.Length, 0.0), 2.0) / (2.0 * SolverControl.Grav);
	}

	public CulvertSolution Compute(double upstreamVal, double downstreamVal)
	{
		CulvertBoundaryConditions bcs = new CulvertBoundaryConditions(upstreamVal, downstreamVal, 0.0, 0.0);
		CulvertFlowSolverResult culvertFlowSolverResult = CulvertFlowSolver.Solve(PhysProps, bcs, SolverControl);
		if (!culvertFlowSolverResult.Convergence.IsConverged)
		{
			_logger.LogWarning("Culvert {Name} flow solver did not converge for upstream val {UpVal} and downstream val {DnVal}. EG Error: {EgError}", Name, upstreamVal, downstreamVal, culvertFlowSolverResult.Convergence.EnergyGradeError);
		}
		return culvertFlowSolverResult.Solution;
	}

	public override double ComputeFlow(double upstreamVal, double downstreamVal)
	{
		return Compute(upstreamVal, downstreamVal).Qtotal;
	}

	public override (double flow, double vel) ComputeFlowAndVel(double upstreamVal, double downstreamVal)
	{
		CulvertSolution culvertSolution = Compute(upstreamVal, downstreamVal);
		double depth = culvertSolution.WSEOutlet - PhysProps.InvertDS;
		CulvertXSection culvertXSection = new CulvertXSection
		{
			Depth = depth,
			Qtotal = culvertSolution.Qtotal,
			Mann = PhysProps.Mann
		};
		culvertXSection.Compute(PhysProps, SolverControl);
		return (flow: culvertSolution.Qtotal, vel: culvertXSection.Vel);
	}
}
