using System;
using System.Linq;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;

namespace Ras.Engine.Structures.Weirs;

public class Weir : HydraulicStructure
{
	public WeirPhysProps PhysProps { get; set; } = new WeirPhysProps();

	public WeirState State { get; set; } = new WeirState();

	public WeirFlowState FlowState { get; set; } = new WeirFlowState();

	public double[] FaceFlows => FlowState.Q;

	public override bool TreatFlowAsInternalBoundary => false;

	public double[] WeirCrest { get; set; }

	public double KMinorLoss { get; set; }

	public double[] SavedWeirVal { get; set; }

	public double[] WeirDepth { get; set; }

	public bool[] IsFaceWet { get; set; }

	public override HSType StructureType => HSType.Weir;

	public Weir(string name, Polyline cL)
		: base(name, cL)
	{
	}

	public override void DistributeStructure()
	{
	}

	public override void Prepare(SolverControl solverControl, Mesh mesh, MeshPropertyTables propTables)
	{
		if (TreatFlowAsInternalBoundary)
		{
			PhysProps.Length = CenterLine.Length;
		}
		else
		{
			double faceSimpleLengthSum = 0.0;
			for (int i = 0; i < Faces.Length; i++)
			{
				faceSimpleLengthSum += mesh.FaceSimpleLength(Faces[i]);
			}
			PhysProps.Length = faceSimpleLengthSum;
		}
		PhysProps.SetUnitSys(UnitSys);
		FlowState.AllocateFaceFlows(Faces.Length);
		WeirCrest = new double[Faces.Length];
		for (int j = 0; j < Faces.Length; j++)
		{
			propTables.GetFaceTables(Faces[j], out var _, out var elevs, out var _, out var _);
			WeirCrest[j] = elevs.Span[0];
		}
		SavedWeirVal = new double[Faces.Length];
		if (solverControl.equationSet == SolverControl.EquationSet.SWE && solverControl.timeStepping == SolverControl.TimeStepping.Implicit)
		{
			WeirDepth = new double[Faces.Length];
			IsFaceWet = new bool[Faces.Length];
		}
		if (solverControl.cellVelocityMethod == SolverControl.CellVelocityMethod.Perot)
		{
			throw new SolverException("Weir hydraulic structures are not compatible with Perot cell velocity method.");
		}
		KMinorLoss = ComputeMinorLossCoefficient(PhysProps.WeirCoef, solverControl);
	}

	public override void Apply(double absTime, double dt, HydraulicStructureCollection hsCollection, MeshWaterState waterState, Mesh mesh, MeshPropertyTables propTables, HydraulicMeshComputer hmesh)
	{
		bool condition = false;
		double sumFlowStateTerm;
		if (TreatFlowAsInternalBoundary)
		{
			for (int i = 0; i < Faces.Length; i++)
			{
				double wSEUS = waterState.CellElevation[UpCells[i]];
				double wSEDS = waterState.CellElevation[DnCells[i]];
				double length = mesh.FaceSimpleLength(Faces[i]);
				double weirApproachVel = GetWeirApproachVel(waterState, mesh, hmesh, UpCells[i], Faces[i]);
				double weirApproachVel2 = GetWeirApproachVel(waterState, mesh, hmesh, DnCells[i], Faces[i]);
				propTables.GetFaceTables(Faces[i], out var areas, out var elevs, out var wettedPerim, out var _);
				FlowState.WSEUS = wSEUS;
				FlowState.WSEDS = wSEDS;
				FlowState.VelUS = weirApproachVel;
				FlowState.VelDS = weirApproachVel2;
				PhysProps.Length = length;
				WeirCompute.ComputeWeirFlow(PhysProps, FlowState, i, areas, elevs, wettedPerim);
			}
			sumFlowStateTerm = FlowState.Q.Sum();
		}
		else
		{
			sumFlowStateTerm = GetWeirTotalFaceFlow(waterState);
		}
		if (condition)
		{
			if (absTime == 0.0)
			{
				StructureTSLogger.RotateLog();
			}
			int scaledLength = Faces.Length / 2;
			StructureTSLogger.Log($"{absTime}, {waterState.CellElevation[UpCells[scaledLength]]}, {waterState.CellElevation[DnCells[scaledLength]]}, {sumFlowStateTerm}");
		}
	}

	public override void OverwriteFaceFlows(MeshWaterState ws)
	{
		for (int i = 0; i < Faces.Length; i++)
		{
			int faceIndex = Faces[i];
			double faceArea = ws.FaceArea[faceIndex];
			if (faceArea > 0.0)
			{
				ws.FaceFlow[faceIndex] = FaceFlows[i] * (double)FaceSign[i];
				ws.FaceVelocity[faceIndex] = ws.FaceFlow[faceIndex] / faceArea;
			}
			else
			{
				ws.FaceFlow[faceIndex] = 0.0;
				ws.FaceVelocity[faceIndex] = 0.0;
			}
		}
	}

	public double GetWeirTotalFaceFlow(MeshWaterState ws)
	{
		double weirTotalFaceFlow = 0.0;
		for (int i = 0; i < Faces.Length; i++)
		{
			int faceIndex = Faces[i];
			weirTotalFaceFlow += ws.FaceFlow[faceIndex] * (double)FaceSign[i];
		}
		return weirTotalFaceFlow;
	}

	public double GetWeirApproachVel(MeshWaterState waterState, Mesh mesh, HydraulicMeshComputer hmesh, int cIdx, int fIdx)
	{
		Vector other = waterState.CellVelocity[cIdx];
		Vector vector = hmesh.FaceNormal[fIdx];
		return Math.Max((double)((mesh.Faces[fIdx].cellB - 2 * cIdx + mesh.Faces[fIdx].cellA) / (mesh.Faces[fIdx].cellB - mesh.Faces[fIdx].cellA)) * vector.Dot(other), 0.0);
	}

	public static double ComputeMinorLossCoefficient(double Cw, SolverControl solverControl)
	{
		Math.Sqrt(2.0 * solverControl.computeControl.grav / 3.0);
		if (Cw < 0.01)
		{
			return 0.0;
		}
		double x = 2.0 / (3.0 * Cw) * Math.Sqrt(2.0 * solverControl.computeControl.grav / 3.0);
		return 3.0 * (Math.Pow(x, 2.0 / 3.0) - 1.0);
	}
}
