namespace Geospatial.Rendering.Charting;

public interface ISeriesRequest
{
	VectorSymbology Symbology { get; }

	string Name { get; }

	string XAxisLabel { get; }

	string XAxisUnits { get; }

	string YAxisLabel { get; }

	string YAxisUnits { get; }

	ChartType ChartType { get; }
}
