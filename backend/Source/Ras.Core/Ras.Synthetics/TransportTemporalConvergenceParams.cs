using System;
using Geospatial.Vectors;
using Ras.Engine;

namespace Ras.Synthetics;

public sealed class TransportTemporalConvergenceParams : UniformTransportParams
{
	public const double Length = 200.0;

	public const int CellCountX = 200;

	public const int CellCountY = 3;

	public const double Diffusivity = 0.5;

	public const double InitialSigma = 8.0;

	public const double InitialCenterX = 50.0;

	public const double DecayRate = 0.3;

	public const double ReferenceTimeStep = 1.0 / 256.0;

	public override int CellsX => 200;

	public override int CellsY => 3;

	public override double DomainLengthX => 200.0;

	public override double DomainLengthY => 3.0;

	public override double VelocityX => 1.0;

	public override double SolveDt { get; set; } = 0.25;

	public override double SolveDuration { get; set; } = 25.0;

	public static TransportTemporalConvergenceParams Default()
	{
		return new TransportTemporalConvergenceParams();
	}

	public double[] RequestedTimeSteps()
	{
		return new double[3] { 0.25, 0.125, 0.0625 };
	}

	public Func<Point, double> InitialConcentrationFunction()
	{
		return InitialConcentration;
	}

	public double InitialConcentration(Point point)
	{
		double pointTerm = point.X - 50.0;
		return Math.Exp((0.0 - pointTerm) * pointTerm / 128.0);
	}

	public double[] CreateSinkCoefficient(Mesh mesh, TransportFlowField flow, double decayRate)
	{
		double[] sinkCoefficient = new double[mesh.CellCount];
		for (int i = 0; i < mesh.CellCount; i++)
		{
			sinkCoefficient[i] = (0.0 - decayRate) * flow.CellVolumeNew[i];
		}
		return sinkCoefficient;
	}
}
