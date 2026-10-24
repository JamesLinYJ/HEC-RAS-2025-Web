using System;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using Utility.Logging;

namespace Ras.Synthetics;

public sealed class SquareAdvectionParams : UniformTransportParams
{
	public const double Length = 100.0;

	public const int CellCountX = 100;

	public const int CellCountY = 3;

	public const double AdvectionVelocity = 1.0;

	public const double PulseDuration = 10.0;

	public override int CellsX => 100;

	public override int CellsY => 3;

	public override double DomainLengthX => 100.0;

	public override double DomainLengthY => 3.0;

	public override double VelocityX => 1.0;

	public override double SolveDt { get; set; } = 0.5;

	public override double SolveDuration { get; set; } = 50.0;

	public static SquareAdvectionParams Default()
	{
		return new SquareAdvectionParams();
	}

	public Func<Point, double> InitialConcentrationFunction()
	{
		return (Point _) => 0.0;
	}

	public double BoundaryConcentration(double time)
	{
		if (!(time >= 0.0) || !(time < 10.0))
		{
			return 0.0;
		}
		return 1.0;
	}

	public Func<Point, double, double> AnalyticalConcentrationFunction()
	{
		return AnalyticalConcentration;
	}

	public Mesh CreateDistortedTriangleMesh()
	{
		Topology topo = Topology.FromExtent(SystemExtent);
		topo.EnsureMeshMetadataColumns();
		topo.SetControlPointData(0, PatchType.TrianglePaver, SizeFunction.Constant, 1f);
		topo.SetControlPointRecipeString(0, "Distort()");
		topo.SetControlPointTINResolution(0, 1f);
		for (int i = 0; i < 4; i++)
		{
			topo.SetArcData(i, LinearSizeFunction.Unset, float.NaN, 8, 1f);
		}
		OperationResult operationResult = topo.BuildConceptualMesh(out var _, out var finalMesh);
		if (!operationResult.Result || finalMesh == null)
		{
			throw new InvalidOperationException("Distorted triangular mesh generation failed: " + operationResult.GetConcatenatedMessages(";"));
		}
		return finalMesh;
	}

	private double AnalyticalConcentration(Point point, double time)
	{
		double velocityXTimesTime = VelocityX * time;
		double velocityXTimeTerm = VelocityX * Math.Max(0.0, time - 10.0);
		if (!(point.X >= velocityXTimeTerm) || !(point.X < velocityXTimesTime))
		{
			return 0.0;
		}
		return 1.0;
	}
}
