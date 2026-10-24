using System;
using Geospatial.Vectors;
using Ras.Engine;

namespace Ras.Synthetics;

public sealed class TransportMmsParams : UniformTransportParams
{
	public const double Length = Math.PI;

	public const double Diffusivity = 0.05;

	public const double WaveNumberX = 1.0;

	public const double WaveNumberY = 1.0;

	public override int CellsX => 64;

	public override int CellsY => 64;

	public override double DomainLengthX => Math.PI;

	public override double DomainLengthY => Math.PI;

	public override double VelocityX => 1.0;

	public override double VelocityY => 0.5;

	public override double SolveDt { get; set; } = 100000000.0;

	public override double SolveDuration { get; set; } = 100000000.0;

	public static TransportMmsParams Default()
	{
		return new TransportMmsParams();
	}

	public Func<Point, double> InitialConcentrationFunction()
	{
		return (Point _) => 0.0;
	}

	public Func<Point, double, double> AnalyticalConcentrationFunction()
	{
		return (Point point, double _) => ExactConcentration(point);
	}

	public double ExactConcentration(Point point)
	{
		return Math.Sin(1.0 * point.X) * Math.Sin(1.0 * point.Y);
	}

	public double SourceDensity(Point point)
	{
		double pointTerm = Math.Cos(1.0 * point.X);
		double pointTerm2 = Math.Sin(1.0 * point.X);
		double pointTerm3 = Math.Cos(1.0 * point.Y);
		double pointTerm4 = Math.Sin(1.0 * point.Y);
		double computed = 1.0 * pointTerm * pointTerm4;
		double computed2 = 1.0 * pointTerm2 * pointTerm3;
		double computed3 = pointTerm2 * pointTerm4;
		return VelocityX * computed + VelocityY * computed2 + 0.1 * computed3;
	}

	public double[] CreateSourceMassRate(Mesh mesh, TransportFlowField flow)
	{
		double[] sourceMassRate = new double[mesh.CellCount];
		for (int i = 0; i < mesh.CellCount; i++)
		{
			sourceMassRate[i] = SourceDensity(mesh.CellCenters[i]) * flow.CellVolumeNew[i];
		}
		return sourceMassRate;
	}
}
