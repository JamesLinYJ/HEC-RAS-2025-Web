using System;

namespace Ras.Synthetics.Solvers;

public sealed class CrossSectionTrapezoidal : ICrossSection
{
	public double BottomWidth;

	public double SideSlopeHV;

	public double Area(double depth)
	{
		if (depth <= 0.0)
		{
			return 0.0;
		}
		return (BottomWidth + SideSlopeHV * depth) * depth;
	}

	public double WettedPerimeter(double depth)
	{
		if (depth <= 0.0)
		{
			return 0.0;
		}
		return BottomWidth + 2.0 * depth * Math.Sqrt(1.0 + SideSlopeHV * SideSlopeHV);
	}

	public double TopWidth(double depth)
	{
		if (depth <= 0.0)
		{
			return 0.0;
		}
		return BottomWidth + 2.0 * SideSlopeHV * depth;
	}

	public void HydraulicProperties(double depth, out double area, out double wettedPerimeter, out double topWidth)
	{
		if (depth <= 0.0)
		{
			area = 0.0;
			wettedPerimeter = 0.0;
			topWidth = 0.0;
		}
		else
		{
			double sideSlopeHVTimesDepth = SideSlopeHV * depth;
			area = (BottomWidth + sideSlopeHVTimesDepth) * depth;
			wettedPerimeter = BottomWidth + 2.0 * depth * Math.Sqrt(1.0 + SideSlopeHV * SideSlopeHV);
			topWidth = BottomWidth + 2.0 * sideSlopeHVTimesDepth;
		}
	}
}
