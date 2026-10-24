using System;
using System.Collections.Generic;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Engine;
using Ras.Engine.Structures;
using Ras.Engine.Structures.Culverts;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Ras.Layers.Payloads;
using Utility.Units;

namespace Ras.Synthetics;

public class QuasiperiodicCulvertFlowParams : BasicRectangleParams
{
	public double DtBc = 300.0;

	public double Duration = 86400.0;

	public double InitWSE = 2.0;

	public double LeftStageAmp = 2.0;

	public double RightStageAmp = 2.0;

	public double LeftStagePeriod = 1440.0;

	public double RightStagePeriod = 1200.0;

	public double LeftStageOffset = 2.0;

	public double RightStageOffset = 2.0;

	public double CulvertInvert = 1.5;

	public bool Lts;

	public override UnitSystem Units => UnitSystem.USCustomary;

	public override float TerrainElevation(Point pt)
	{
		double cellsWideCellSizeTerm = (double)CellsWide * CellSize / 2.0;
		double ptTerm = Math.Abs(pt.X - cellsWideCellSizeTerm);
		double computed = 10.0 + 10.0 * (0.0 - ptTerm);
		double min = 0.5 - 0.01 * ptTerm;
		return (float)Math.Clamp(computed, min, 10.0);
	}

	public override void UpdateInitialState(MeshWaterState state, Mesh mesh, MeshPropertyTables props)
	{
		state.SetConstantElevation(InitWSE);
	}

	public override void InitSolverControl(SolverControl sc)
	{
		base.InitSolverControl(sc);
		sc.computeControl.variableDt = true;
		sc.numCores = 1;
		sc.parallelMethod = SolverControl.ParallelMethod.THREAD_FOR;
		sc.stepType = (Lts ? SolverControl.StepType.FWDBACK_LTS : SolverControl.StepType.FWDBACK);
		if (Lts)
		{
			sc.LtsLevels = SolverControl.LtsLevelOptions.LtsLevels4;
			sc.maxLocalIterations = 3;
		}
	}

	public override List<BoundaryConditionLine> GetBCLines(Ras.Layers.BoundaryCondition bc)
	{
		Extent extent = CreateMesh().Extent;
		Polyline pl = Polyline.FromSegment(extent.LeftWall.Scale(0.01, 0.99));
		Polyline pl2 = Polyline.FromSegment(extent.RightWall.Scale(0.01, 0.99));
		return new List<BoundaryConditionLine>
		{
			BuildPeriodicStageLine(bc, pl, "Upstream", LeftStageAmp, LeftStagePeriod, LeftStageOffset),
			BuildPeriodicStageLine(bc, pl2, "Downstream", RightStageAmp, RightStagePeriod, RightStageOffset)
		};
	}

	private BoundaryConditionLine BuildPeriodicStageLine(Ras.Layers.BoundaryCondition bc, Polyline pl, string name, double amp, double period, double offset)
	{
		int count = (int)(SolveDuration / DtBc) + 1;
		double[] doubleValues = new double[count];
		double[] doubleValuesArray = new double[count];
		for (int i = 0; i < count; i++)
		{
			doubleValuesArray[i] = amp * Math.Sin(Math.PI * 2.0 * (doubleValues[i] = (double)i * DtBc) / period) + offset;
		}
		return new BoundaryConditionLine(bc, pl)
		{
			Name = name,
			Payloads = { (PayloadBase)new TimeSeriesPayload("Stage", IrregularRelativeTimeSeriesDefinition.FromSeconds(doubleValues), doubleValuesArray) }
		};
	}

	public override void AddStructures(HydraulicStructureCollection structures, Mesh mesh)
	{
		double cellsWideCellSizeTerm = (double)CellsWide * CellSize / 2.0;
		double y = (double)CellsTall * CellSize / 2.0;
		Point point = new Point(cellsWideCellSizeTerm - 6.0, y);
		Point point2 = new Point(cellsWideCellSizeTerm + 6.0, y);
		Culvert structure = StructureFactories.GenerateCulvert(new Polyline(new List<Point> { point, point2 }), CulvertInvert, CulvertInvert, CulvertShape.Circular);
		structures.AddHydraulicStructure(structure);
	}
}
