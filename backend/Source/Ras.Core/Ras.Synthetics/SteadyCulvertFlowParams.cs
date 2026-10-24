using System;
using System.Collections.Generic;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Engine;
using Ras.Engine.Structures;
using Ras.Engine.Structures.Culverts;

namespace Ras.Synthetics;

public class SteadyCulvertFlowParams : SteadyStructFlowParams
{
	public double CulvertUSInvert = 1.5;

	public double CulvertDSInvert = 1.5;

	public CulvertShape Shape = CulvertShape.Circular;

	public double CulvertLength = 12.0;

	public bool Lts;

	public override float TerrainElevation(Point pt)
	{
		double cellsWideCellSizeTerm = (double)CellsWide * CellSize / 2.0;
		double ptTerm = Math.Abs(pt.X - cellsWideCellSizeTerm);
		return (float)Math.Clamp(10.0 + 10.0 * (0.0 - ptTerm), 0.0, 10.0);
	}

	public override void UpdateInitialState(MeshWaterState state, Mesh mesh, MeshPropertyTables props)
	{
		if (InitStage >= CulvertUSInvert)
		{
			state.SetConstantElevation(InitStage);
			return;
		}
		Extent ext = SystemExtent.Extend(0.1);
		double centerLine = (double)CellsWide * CellSize / 2.0;
		IResample<float> initialStageFromCellCenters = new FunctionalResampler<float>(ext, -9999f, (Point p) => (!(p.X < centerLine)) ? ((float)InitStage) : ((float)CulvertUSInvert));
		state.SetInitialStageFromCellCenters(initialStageFromCellCenters);
	}

	public override void InitSolverControl(SolverControl sc)
	{
		base.InitSolverControl(sc);
		if (Lts)
		{
			sc.stepType = SolverControl.StepType.FWDBACKFWD_LTS;
			sc.LtsLevels = SolverControl.LtsLevelOptions.LtsLevels4;
			sc.maxLocalIterations = 3;
		}
	}

	public override void AddStructures(HydraulicStructureCollection structures, Mesh mesh)
	{
		double cellsWideCellSizeTerm = (double)CellsWide * CellSize / 2.0;
		double y = (double)CellsTall * CellSize / 2.0;
		Point point = new Point(cellsWideCellSizeTerm - CulvertLength / 2.0, y);
		Point point2 = new Point(cellsWideCellSizeTerm + CulvertLength / 2.0, y);
		Culvert structure = StructureFactories.GenerateCulvert(new Polyline(new List<Point> { point, point2 }), CulvertUSInvert, CulvertDSInvert, Shape);
		structures.AddHydraulicStructure(structure);
	}
}
