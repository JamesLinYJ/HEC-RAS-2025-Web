using System;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Ras.Layers.Payloads;

namespace Ras.Synthetics;

public class FloodBoreParams : BasicRectangleParams
{
	public double Velocity = 1.0;

	public double DtBc = 1.0;

	public double Duration = 4000.0;

	public static FloodBoreParams Default()
	{
		return new FloodBoreParams
		{
			CellsTall = 3,
			CellsWide = 100,
			CellSize = 10.0,
			NValue = 0.03f,
			Slope = 0.0,
			Velocity = 1.0,
			DtBc = 1.0,
			Duration = 4000.0,
			SolveDt = 0.5,
			SolveDuration = 4000.0
		};
	}

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		return (Point pt, double t) =>
		{
			double x = pt.X;
			double velocityTerm = Math.Max(0.0, Velocity * t - x);
			return (float)Math.Pow(2.3333333333333335 * Math.Pow((double)NValue * Velocity, 2.0) * velocityTerm, 1755.0 / 4095.0);
		};
	}

	public override Func<Point, double, Vector> AnalyticalVelocityFunction()
	{
		return (Point pt, double t) => new Vector(Velocity, 0.0);
	}

	public override BoundaryConditionLine GetUpstreamBC(BoundaryCondition bc, Polyline pl)
	{
		int count = (int)(Duration / DtBc) + 1;
		double[] doubleValues = new double[count];
		double[] doubleValuesArray = new double[count];
		for (int i = 0; i < count; i++)
		{
			doubleValues[i] = (double)i * DtBc;
			double nValueVelocityTerm = Math.Pow(2.3333333333333335 * Math.Pow((double)NValue * Velocity, 2.0) * (Velocity * doubleValues[i]), 1755.0 / 4095.0);
			doubleValuesArray[i] = nValueVelocityTerm * CellSize * (double)CellsTall * Velocity;
		}
		doubleValuesArray[count - 1] = doubleValuesArray[count - 2];
		doubleValues[count - 1] = 10000000000.0;
		return new BoundaryConditionLine(bc, pl)
		{
			Name = "Upstream",
			Payloads = { (PayloadBase)new TimeSeriesPayload("Flow", IrregularRelativeTimeSeriesDefinition.FromSeconds(doubleValues), doubleValuesArray) }
		};
	}
}
