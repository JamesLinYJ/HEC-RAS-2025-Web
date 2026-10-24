using System;
using System.Collections.Generic;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Ras.Layers.Payloads;

namespace Ras.Synthetics.SyntheticParams;

public class DisconnectedDepressionParams : BasicRectangleParams
{
	public const double InitialStage = 9.7;

	public const double PeakStage = 10.35;

	public const double FinalUpstreamPondStage = 10.25;

	private static readonly double[] BedX = new double[8] { 0.0, 100.0, 200.0, 300.0, 400.0, 500.0, 600.0, 700.0 };

	private static readonly double[] BedZ = new double[8] { 9.0, 9.45, 10.05, 9.55, 10.25, 9.75, 10.45, 10.5 };

	public static DisconnectedDepressionParams Default()
	{
		return new DisconnectedDepressionParams
		{
			CellsWide = 70,
			CellsTall = 10,
			CellSize = 10.0,
			NValue = 0.03f,
			SolveDt = 5.0,
			SolveDuration = 72000.0
		};
	}

	public override float TerrainElevation(Point pt)
	{
		double ptBedXTerm = Math.Clamp(pt.X, BedX[0], BedX[^1]);
		int i;
		for (i = 0; i < BedX.Length - 2 && ptBedXTerm > BedX[i + 1]; i++)
		{
		}
		double interpolationFraction = (ptBedXTerm - BedX[i]) / (BedX[i + 1] - BedX[i]);
		return (float)(BedZ[i] + interpolationFraction * (BedZ[i + 1] - BedZ[i]));
	}

	public override Func<Point, float> InitialStageFunction()
	{
		return (Point _) => 9.7f;
	}

	public override Func<Point, float> InitialVelocityXFunction()
	{
		return (Point _) => 0f;
	}

	public override Func<Point, float> InitialVelocityYFunction()
	{
		return (Point _) => 0f;
	}

	public override BoundaryConditionLine GetUpstreamBC(BoundaryCondition boundaryCondition, Polyline polyline)
	{
		double[] coefficients = new double[5] { 0.0, 3600.0, 39600.0, 43200.0, 0.0 };
		coefficients[4] = SolveDuration;
		double[] lookupTable = coefficients;
		double[] lookupTableArray = new double[5] { 9.7, 10.35, 10.35, 9.7, 9.7 };
		return new BoundaryConditionLine(boundaryCondition, polyline)
		{
			Name = "Transient stage",
			Payloads = { (PayloadBase)new TimeSeriesPayload("Stage", IrregularRelativeTimeSeriesDefinition.FromSeconds(lookupTable), lookupTableArray) }
		};
	}

	public override IReadOnlyList<NamedResultPoint> GetResultPoints()
	{
		return new _003C_003Ez__ReadOnlyArray<NamedResultPoint>(new NamedResultPoint[2]
		{
			new NamedResultPoint(new Point(300.0, SystemExtent.Center.Y), "Upstream depression"),
			new NamedResultPoint(new Point(500.0, SystemExtent.Center.Y), "Downstream depression")
		});
	}
}
