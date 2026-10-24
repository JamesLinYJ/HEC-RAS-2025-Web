using System;
using Geospatial.Vectors;

namespace Ras.Synthetics.SyntheticParams;

public class TrapezoidalParams : InOutPlanarParams
{
	public double BottomWidth = 20.0;

	public double SideSlopeHV = 2.0;

	public double MaxOverbankRise = 10.0;

	public double CenterlineY = 20.0;

	public override float TerrainElevation(Point pt)
	{
		double slopeMaxXTerm = Slope * (SystemExtent.MaxX - pt.X);
		double ptCenterlineYTerm = Math.Abs(pt.Y - CenterlineY);
		double scaledBottomWidth = BottomWidth * 0.5;
		if (ptCenterlineYTerm < scaledBottomWidth)
		{
			return (float)slopeMaxXTerm;
		}
		return (float)(slopeMaxXTerm + Math.Min((ptCenterlineYTerm - scaledBottomWidth) / SideSlopeHV, MaxOverbankRise));
	}
}
