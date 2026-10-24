using System;
using System.Collections.Generic;
using Geospatial.Vectors;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Ras.Synthetics.SyntheticParams;

namespace Ras.Synthetics;

public class SpatiallyVariableStageVelocityLongCoarseParams : SpatiallyVariableStageVelocityLongBaseParams
{
	private static readonly double[] LateralStageOffsets = new double[5] { 0.0, 0.1, 0.3, 0.1, 0.0 };

	private const double BaselineVelocityX = 0.5;

	private const double PulseStartTime = 120.0;

	private const double PulseDuration = 60.0;

	private const double PulseStageAmplitude = 0.5;

	private const double PulseVelocityAmplitude = 0.3;

	public static SpatiallyVariableStageVelocityLongCoarseParams Create()
	{
		return new SpatiallyVariableStageVelocityLongCoarseParams();
	}

	public override Mesh CreateMesh()
	{
		return CreateRectangularMesh(0.0, 0.0, 100.0);
	}

	public override List<BoundaryConditionLine> GetBCLines(BoundaryCondition bc)
	{
		Extent extent = CreateMesh().Extent;
		if (_westValues == null)
		{
			SetWestStageVelocitySeries(SpatiallyVariableStageVelocityLongBaseParams.StationYs, GenerateSignalSeries());
		}
		BoundaryConditionLine westBc = BuildWestBc(bc, new Point(extent.MinX, 1.0), new Point(extent.MinX, 99.0), _westValues);
		Polyline pl = new Polyline(new Point[2]
		{
			new Point(extent.MaxX, 1.0),
			new Point(extent.MaxX, 99.0)
		});
		BoundaryConditionLine boundaryConditionLine = BoundaryConditionLine.ConstantFlow(bc, pl, EastFlow);
		boundaryConditionLine.Name = "East";
		return new List<BoundaryConditionLine> { westBc, boundaryConditionLine };
	}

	public override IReadOnlyList<NamedResultPoint> GetResultPoints()
	{
		return SpatiallyVariableStageVelocityLongBaseParams.CreateResultPoints(50.0, 1950.0, 0.0, 100.0, 1000.0, includeNorthEdge: true);
	}

	private List<Memory<double>> GenerateSignalSeries()
	{
		int count = (int)Math.Round(SolveDuration / SolveDt) + 1;
		int stationYLength = SpatiallyVariableStageVelocityLongBaseParams.StationYs.Length;
		List<Memory<double>> doubleSpanList = new List<Memory<double>>(count);
		for (int i = 0; i < count; i++)
		{
			double solveDtTerm = (double)i * SolveDt;
			double rampFactor = BaseParams.RampFactor(solveDtTerm, RampSeconds);
			double pulseShape = PulseShape(solveDtTerm);
			double[] doubleValues = new double[stationYLength * 3];
			for (int j = 0; j < stationYLength; j++)
			{
				double pulseShapeSum = LateralStageOffsets[j] * rampFactor;
				double pulseShapeSum2 = 0.5 * rampFactor;
				if (j == 2)
				{
					pulseShapeSum += 0.5 * pulseShape;
					pulseShapeSum2 += 0.3 * pulseShape;
				}
				doubleValues[j] = InitialStage + pulseShapeSum;
				doubleValues[stationYLength + j] = pulseShapeSum2;
				doubleValues[2 * stationYLength + j] = 0.0;
			}
			doubleSpanList.Add(doubleValues);
		}
		return doubleSpanList;
	}

	private static double PulseShape(double t)
	{
		if (t <= 120.0 || t >= 180.0)
		{
			return 0.0;
		}
		double computed = (t - 120.0) / 60.0;
		if (!(computed <= 0.5))
		{
			return (1.0 - computed) * 2.0;
		}
		return computed * 2.0;
	}
}
