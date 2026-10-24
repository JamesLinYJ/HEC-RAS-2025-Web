using System;
using Geospatial.Vectors;
using Ras.Engine;

namespace Ras.Synthetics;

public sealed class AdvectionDecaySourceParams : UniformTransportParams
{
	public const double Length = 100.0;

	public const int CellCountX = 200;

	public const int CellCountY = 3;

	public const double AdvectionVelocity = 1.0;

	public const double DecayRate = 0.02;

	public const double SourceDensity = 0.005;

	public const double InflowConcentration = 1.0;

	public override int CellsX => 200;

	public override int CellsY => 3;

	public override double DomainLengthX => 100.0;

	public override double DomainLengthY => 1.5;

	public override double VelocityX => 1.0;

	public override double SolveDt { get; set; } = 100000000.0;

	public override double SolveDuration { get; set; } = 100000000.0;

	public double EquilibriumConcentration => 0.25;

	public static AdvectionDecaySourceParams Default()
	{
		return new AdvectionDecaySourceParams();
	}

	public Func<Point, double> InitialConcentrationFunction()
	{
		return (Point _) => 1.0;
	}

	public Func<Point, double, double> AnalyticalConcentrationFunction()
	{
		return (Point point, double _) => AnalyticalConcentration(point.X);
	}

	public double AnalyticalConcentration(double x)
	{
		return EquilibriumConcentration + (1.0 - EquilibriumConcentration) * Math.Exp(-0.02 * x / VelocityX);
	}

	public double[] CreateInitialConcentration(Mesh mesh)
	{
		double[] initialConcentration = new double[mesh.CellCount];
		Array.Fill(initialConcentration, 1.0);
		return initialConcentration;
	}

	public void CreateReactionTerms(Mesh mesh, TransportFlowField flow, out double[] sourceMassRate, out double[] sinkCoefficient)
	{
		sourceMassRate = new double[mesh.CellCount];
		sinkCoefficient = new double[mesh.CellCount];
		for (int i = 0; i < mesh.CellCount; i++)
		{
			double cellVolumeNew = flow.CellVolumeNew[i];
			sourceMassRate[i] = 0.005 * cellVolumeNew;
			sinkCoefficient[i] = -0.02 * cellVolumeNew;
		}
	}
}
