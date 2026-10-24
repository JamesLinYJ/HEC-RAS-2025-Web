using System;
using Geospatial.Vectors;

namespace Ras.Synthetics;

public sealed class AdvectionDiffusionGaussianParams : UniformTransportParams
{
	public const double Length = 200.0;

	public const int CellCountX = 200;

	public const int CellCountY = 3;

	public const double AdvectionVelocity = 1.0;

	public const double Diffusivity = 0.5;

	public const double InitialSigma = 8.0;

	public const double InitialCenterX = 50.0;

	public override int CellsX => 200;

	public override int CellsY => 3;

	public override double DomainLengthX => 200.0;

	public override double DomainLengthY => 3.0;

	public override double VelocityX => 1.0;

	public override double SolveDt { get; set; } = 0.5;

	public override double SolveDuration { get; set; } = 50.0;

	public double FinalCenterX => 50.0 + VelocityX * SolveDuration;

	public static AdvectionDiffusionGaussianParams Default()
	{
		return new AdvectionDiffusionGaussianParams();
	}

	public Func<Point, double> InitialConcentrationFunction()
	{
		return (Point point) => Gaussian(point.X, 0.0);
	}

	public Func<Point, double, double> AnalyticalConcentrationFunction()
	{
		return (Point point, double time) => Gaussian(point.X, time);
	}

	public double AnalyticalConcentration(double x, double time)
	{
		return Gaussian(x, time);
	}

	private double Gaussian(double x, double time)
	{
		double timeTerm = Math.Sqrt(64.0 + 1.0 * time);
		double velocityXTimeTerm = 50.0 + VelocityX * time;
		double computed = x - velocityXTimeTerm;
		return 8.0 / timeTerm * Math.Exp((0.0 - computed) * computed / (2.0 * timeTerm * timeTerm));
	}
}
