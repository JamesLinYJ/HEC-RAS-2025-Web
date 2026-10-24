using System;
using Geospatial.Vectors;
using Ras.Engine;

namespace Ras.Synthetics;

public sealed class FirstOrderDecayParams : UniformTransportParams
{
	public const double Length = 100.0;

	public const int CellCount = 20;

	public const double InitialConcentration = 5.0;

	public const double DecayRate = 0.05;

	public override int CellsX => 20;

	public override int CellsY => 20;

	public override double DomainLengthX => 100.0;

	public override double DomainLengthY => 100.0;

	public override double SolveDt { get; set; } = 0.5;

	public override double SolveDuration { get; set; } = 50.0;

	public static FirstOrderDecayParams Default()
	{
		return new FirstOrderDecayParams();
	}

	public Func<Point, double> InitialConcentrationFunction()
	{
		return (Point _) => 5.0;
	}

	public Func<Point, double, double> AnalyticalConcentrationFunction()
	{
		return (Point _, double time) => AnalyticalConcentration(time);
	}

	public double AnalyticalConcentration(double time)
	{
		return 5.0 * Math.Exp(-0.05 * time);
	}

	public double[] CreateInitialConcentration(Mesh mesh)
	{
		double[] initialConcentration = new double[mesh.CellCount];
		Array.Fill(initialConcentration, 5.0);
		return initialConcentration;
	}

	public double[] CreateSinkCoefficient(Mesh mesh, TransportFlowField flow)
	{
		double[] sinkCoefficient = new double[mesh.CellCount];
		for (int i = 0; i < mesh.CellCount; i++)
		{
			sinkCoefficient[i] = -0.05 * flow.CellVolumeNew[i];
		}
		return sinkCoefficient;
	}
}
