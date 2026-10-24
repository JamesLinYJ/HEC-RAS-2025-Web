using Geospatial.Vectors;

namespace Ras.Synthetics;

public class SlopeChannelParams : InOutPlanarParams
{
	public double LatSlope;

	public override float TerrainElevation(Point pt)
	{
		return (float)(Slope * (SystemExtent.MaxX - pt.X) + LatSlope * pt.Y);
	}
}
