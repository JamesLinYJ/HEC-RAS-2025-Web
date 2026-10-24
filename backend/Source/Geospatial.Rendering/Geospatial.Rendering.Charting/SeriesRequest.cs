namespace Geospatial.Rendering.Charting;

public abstract class SeriesRequest : ISeriesRequest
{
	public string Name { get; }

	public string XAxisLabel { get; }

	public string XAxisUnits { get; }

	public string YAxisLabel { get; }

	public string YAxisUnits { get; }

	public VectorSymbology Symbology { get; set; } = VectorSymbology.DefaultSymbology();

	public ChartType ChartType { get; } = ChartType.Line;

	public SeriesRequest(string name, string xLabel, string xUnits, string yLabel, string yUnits)
	{
		Name = name;
		XAxisLabel = xLabel;
		XAxisUnits = xUnits;
		YAxisLabel = yLabel;
		YAxisUnits = yUnits;
	}
}
