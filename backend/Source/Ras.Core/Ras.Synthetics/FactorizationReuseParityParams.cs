using System;
using Geospatial.Vectors;

namespace Ras.Synthetics;

public sealed class FactorizationReuseParityParams : UniformTransportParams
{
	public const double Length = 100.0;

	public const int CellCount = 60;

	public const int ConstituentCount = 4;

	public const double Diffusivity = 0.5;

	public const double InitialSigma = 8.0;

	public override int CellsX => 60;

	public override int CellsY => 60;

	public override double DomainLengthX => 100.0;

	public override double DomainLengthY => 100.0;

	public override double VelocityX => 1.0;

	public override double VelocityY => 0.3;

	public override double SolveDt { get; set; } = 1.0;

	public override double SolveDuration { get; set; } = 15.0;

	public static FactorizationReuseParityParams Default()
	{
		return new FactorizationReuseParityParams();
	}

	public double InitialConcentration(Point point, int constituent)
	{
		double constituentTerm = 100.0 * (0.2 + 0.12 * (double)constituent);
		double computed = 50.0;
		double pointTerm = point.X - constituentTerm;
		double pointTerm2 = point.Y - computed;
		double computed2 = 128.0;
		return (double)(constituent + 1) * Math.Exp((0.0 - (pointTerm * pointTerm + pointTerm2 * pointTerm2)) / computed2);
	}
}
