using Utility.Units;

namespace Geospatial.Vectors.Rasterizers;

public class ArrivalTimeRenderOptions
{
	public float MinWsPlotTolerance { get; set; }

	public float DepthThreshold { get; set; }

	public Time ReportTimeUnits { get; set; } = Time.Hours;

	public double StartTimeDays { get; set; }
}
