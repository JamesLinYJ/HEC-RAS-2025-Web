namespace Geospatial.Rendering.Charting;

public class XYSeriesRequest : SeriesRequest
{
	public double[] XData { get; }

	public double[] YData { get; }

	public XYSeriesRequest(string name, string xLabel, string xUnits, double[] xData, string yLabel, string yUnits, double[] yData)
		: base(name, xLabel, xUnits, yLabel, yUnits)
	{
		XData = xData;
		YData = yData;
	}
}
