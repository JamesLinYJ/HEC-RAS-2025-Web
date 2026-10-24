using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Ras.Engine;
using Ras.Engine.Structures;
using Ras.Engine.Structures.Weirs;
using Utility.Units;

namespace Ras.Synthetics;

public class SteadyWeirFlowParams : SteadyStructFlowParams
{
	public double MinWeirElev = 2.0;

	public double MaxWeirElev = 2.0;

	public double WeirCoeff = 3.09;

	public double WeirSubmergenceAcoef = 7.25;

	public double WeirSubmergenceBcoef = 0.5;

	public WeirCrestShape WeirCrestShape = WeirCrestShape.BroadCrested;

	public bool Lts;

	public override UnitSystem Units => UnitSystem.USCustomary;

	public override float TerrainElevation(Point pt)
	{
		double cellsWideCellSizeTerm = (double)CellsWide * CellSize / 2.0;
		double computed = 0.0;
		double cellsTallTimesCellSize = (double)CellsTall * CellSize;
		double ptTerm = Math.Abs(pt.X - cellsWideCellSizeTerm);
		double minWeirElevPtTerm = MinWeirElev + (pt.Y - computed) / (cellsTallTimesCellSize - computed) * (MaxWeirElev - MinWeirElev);
		return (float)Math.Clamp(minWeirElevPtTerm * (1.0 - ptTerm), 0.0, minWeirElevPtTerm);
	}

	public override void UpdateInitialState(MeshWaterState state, Mesh mesh, MeshPropertyTables props)
	{
		if (InitStage >= MinWeirElev)
		{
			state.SetConstantElevation(InitStage);
			return;
		}
		Extent ext = SystemExtent.Extend(0.1);
		double centerLine = (double)CellsWide * CellSize / 2.0;
		IResample<float> initialStageFromCellCenters = new FunctionalResampler<float>(ext, -9999f, (Point p) => (!(p.X < centerLine)) ? ((float)InitStage) : ((float)MinWeirElev));
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
		double x = (double)CellsWide * CellSize / 2.0;
		Point point = new Point(x, (double)CellsTall * CellSize);
		Point point2 = new Point(x, 0.0);
		Polyline polyline = new Polyline(new List<Point> { point, point2 });
		Weir weir = StructureFactories.GenerateWeir(polyline);
		List<(int, bool)> source = mesh.FacesAlongPolyline(polyline);
		weir.Faces = source.Select(((int, bool) t) => t.Item1).ToArray();
		weir.PhysProps.WeirCoef = WeirCoeff;
		weir.PhysProps.Shape = WeirCrestShape;
		weir.PhysProps.SubmergenceAcoef = WeirSubmergenceAcoef;
		weir.PhysProps.SubmergenceBcoef = WeirSubmergenceBcoef;
		structures.AddHydraulicStructure(weir);
	}

	public override Func<double, double, double> AnalyticalRatingCurveFunction()
	{
		return (double head, double submergence) => Math.Pow(1.0 - Math.Pow(submergence, WeirSubmergenceAcoef), WeirSubmergenceBcoef) * WeirCoeff * Math.Pow(head, 1.5);
	}
}
