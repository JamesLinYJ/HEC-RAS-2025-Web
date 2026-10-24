using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;
using Ras.Engine.Structures;
using Ras.Engine.Structures.Weirs;

namespace Ras.Synthetics;

public class QuasiperiodicWeirFlowParams : QuasiperiodicCulvertFlowParams
{
	public double WeirElev = 2.0;

	public double WeirCoeff = 3.09;

	public double WeirSubmergenceAcoef = 7.25;

	public double WeirSubmergenceBcoef = 0.5;

	public WeirCrestShape WeirCrestShape = WeirCrestShape.BroadCrested;

	public override float TerrainElevation(Point pt)
	{
		double cellsWideCellSizeTerm = (double)CellsWide * CellSize / 2.0;
		double ptTerm = Math.Abs(pt.X - cellsWideCellSizeTerm);
		double crestTerm = WeirElev + WeirElev * (0.0 - ptTerm);
		double min = 0.5 - 0.01 * ptTerm;
		return (float)Math.Clamp(crestTerm, min, WeirElev);
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
}
