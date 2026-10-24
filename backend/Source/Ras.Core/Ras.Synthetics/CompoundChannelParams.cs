using System;
using System.Collections.Generic;
using Geospatial.Vectors;

namespace Ras.Synthetics;

public class CompoundChannelParams : InOutPlanarParams
{
	public double ChannelWidth;

	public double FloodplainWidth;

	public double FloodplainHeight;

	public double ChannelManN;

	public double FloodplainManN;

	public new static CompoundChannelParams Default()
	{
		return new CompoundChannelParams
		{
			CellsTall = 25,
			CellsWide = 183,
			CellSize = 0.05,
			CellSizeX = 0.1,
			Slope = -0.00045,
			ChannelWidth = 0.711,
			FloodplainWidth = 0.508,
			FloodplainHeight = 0.0975,
			ChannelManN = 0.009,
			FloodplainManN = 0.012,
			UpstreamFlow = 0.027,
			UpstreamEGSlope = 0.01,
			DownstreamStage = 0.1128,
			RampSeconds = 100.0,
			SolveDt = 0.1,
			SolveDuration = 300.0
		};
	}

	public override float TerrainElevation(Point pt)
	{
		float simplePlanarTerrain = SimplePlanarTerrain(pt);
		float simplePlanarTerrain2 = 2f;
		double computed = 0.001;
		if (pt.Y < FloodplainWidth)
		{
			simplePlanarTerrain += (float)FloodplainHeight;
		}
		if (pt.Y < computed)
		{
			simplePlanarTerrain = simplePlanarTerrain2;
		}
		if (pt.Y > ChannelWidth + FloodplainWidth - computed)
		{
			simplePlanarTerrain = simplePlanarTerrain2;
		}
		return simplePlanarTerrain;
	}

	public override float SpatiallyVaryingNValue(Point pt)
	{
		if (pt.Y < FloodplainWidth)
		{
			return (float)FloodplainManN;
		}
		return (float)ChannelManN;
	}

	public override IReadOnlyList<MeasuredVelocitySample> GetMeasuredVelocitySamples()
	{
		double[] coefficients = new double[28]
		{
			122.366, 121.07, 119.814, 117.309, 114.768, 112.179, 107.152, 102.05, 91.864, 86.78,
			81.849, 79.202, 76.708, 74.119, 72.9, 71.491, 70.482, 69.266, 66.773, 64.102,
			61.597, 56.481, 51.459, 46.402, 41.38, 36.357, 31.209, 15.952
		};
		double[] coefficientsArray = new double[28]
		{
			4.456, 7.362, 7.895, 8.433, 9.181, 9.894, 10.18, 10.027, 10.751, 10.941,
			11.417, 12.369, 13.645, 15.625, 16.729, 19.204, 24.554, 26.774, 29.053, 30.929,
			32.271, 34.087, 35.132, 38.052, 38.004, 39.772, 40.4, 40.191
		};
		int cellsWideTerm = (int)Math.Ceiling(0.75 * (double)CellsWide);
		double x = CellSize * (double)cellsWideTerm;
		List<MeasuredVelocitySample> measuredVelocitySamples = new List<MeasuredVelocitySample>(coefficients.Length);
		for (int i = 0; i < coefficients.Length; i++)
		{
			double y = FloodplainWidth + ChannelWidth - coefficients[i] / 100.0;
			double dx = coefficientsArray[i] / 100.0;
			measuredVelocitySamples.Add(new MeasuredVelocitySample(new Point(x, y), -1.0, new Vector(dx, 0.0)));
		}
		return measuredVelocitySamples;
	}
}
