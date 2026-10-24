using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using Ras.Engine;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Ras.Synthetics.SyntheticParams;

namespace Ras.Synthetics;

public class SemiCircleParams : BaseParams
{
	public bool UseGPU;

	public int LtsLevels = 3;

	public bool UseFBF = true;

	public bool UseAdv = true;

	private Mesh _mesh;

	private const double InsideRad = 1000.0;

	private const double OutsideRad = 1200.0;

	public override Extent SystemExtent => CreateMesh().Extent;

	public static SemiCircleParams Default()
	{
		return new SemiCircleParams
		{
			NValue = 0.03f,
			SolveDt = 10.0,
			SolveDuration = 50000.0,
			ReportFrequency = 1
		};
	}

	public override IResample<float> TerrainResampler()
	{
		return null;
	}

	public override float TerrainElevation(Point pt)
	{
		return (float)(Vector.ComputeLength(pt.X, pt.Y) * 0.005);
	}

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		return null;
	}

	public override Func<Point, float> InitialStageFunction()
	{
		return null;
	}

	public override Mesh CreateMesh()
	{
		if (_mesh == null)
		{
			_mesh = MakeMesh();
		}
		return _mesh;
	}

	public override List<BoundaryConditionLine> GetBCLines(Ras.Layers.BoundaryCondition bc)
	{
		Polyline polyline = new Polyline();
		polyline.Add(1000.0, -1.0);
		polyline.Add(1200.0, -1.0);
		Polyline polyline2 = new Polyline();
		polyline2.Add(-1000.0, -1.0);
		polyline2.Add(-1200.0, -1.0);
		BoundaryConditionLine constantFlow = BoundaryConditionLine.ConstantFlow(bc, polyline, 12000.0);
		BoundaryConditionLine normalDepth = BoundaryConditionLine.NormalDepth(bc, polyline2, 0.002);
		return new List<BoundaryConditionLine> { constantFlow, normalDepth };
	}

	public override void InitPlanOptions(Plan pl)
	{
		pl.SolverType = (UseGPU ? SolverType.GPU : SolverType.CPU);
		pl.LtsLevels = (SolverControl.LtsLevelOptions)LtsLevels;
		pl.UseFBF = UseFBF;
		pl.AdvectionMethod = SolverControl.AdvectionMethod.ExplicitUpwind;
	}

	public static Topology MakeTopology()
	{
		List<Point> points = new List<Point>();
		double computed = 0.0;
		double pi = Math.PI;
		Point pt = new Point(1200.0 * Math.Cos(computed), 1200.0 * Math.Sin(computed));
		Point pt2 = new Point(1200.0 * Math.Cos(pi), 1200.0 * Math.Sin(pi));
		Point pt3 = new Point(1000.0 * Math.Cos(pi), 1000.0 * Math.Sin(pi));
		Point pt4 = new Point(1000.0 * Math.Cos(computed), 1000.0 * Math.Sin(computed));
		for (int i = 1; i < 100; i++)
		{
			double piTerm = computed + (double)i / 100.0 * (pi - computed);
			points.Add(new Point(1200.0 * Math.Cos(piTerm), 1200.0 * Math.Sin(piTerm)));
		}
		List<Point> pointsList = new List<Point>();
		for (int j = 1; j < 100; j++)
		{
			double piTerm2 = pi + (double)j / 100.0 * (computed - pi);
			pointsList.Add(new Point(1000.0 * Math.Cos(piTerm2), 1000.0 * Math.Sin(piTerm2)));
		}
		int count = 8;
		int count2 = 80;
		Topology topology = new Topology();
		topology.AddNode(pt);
		topology.AddNode(pt2);
		topology.AddNode(pt3);
		topology.AddNode(pt4);
		topology.AddArc(0, 1, points);
		topology.AddArc(1, 2, null);
		topology.AddArc(2, 3, pointsList);
		topology.AddArc(3, 0, null);
		topology.AddRegionControlPoint(0.5 * (points.First() + pointsList.Last()));
		topology.EnsureMeshMetadataColumns();
		topology.SetControlPointData(0, PatchType.Quad, SizeFunction.Constant, float.NaN);
		topology.SetArcData(0, LinearSizeFunction.CountWithBias, float.NaN, count2, 1f);
		topology.SetArcData(1, LinearSizeFunction.CountWithBias, float.NaN, count, 1f);
		topology.SetArcData(2, LinearSizeFunction.CountWithBias, float.NaN, count2, 1f);
		topology.SetArcData(3, LinearSizeFunction.CountWithBias, float.NaN, count, 1f);
		return topology;
	}

	public static Mesh MakeMesh()
	{
		Topology topology = MakeTopology();
		topology.Complete();
		topology.BuildConceptualMesh(out var _, out var finalMesh);
		return finalMesh;
	}
}
