namespace Geospatial.IO.MetImport;

public readonly struct GriddedSourceUnits
{
	public string RawUnits { get; init; }

	public string RawDataType { get; init; }

	public string DefaultPrecipDataType { get; init; }

	public string DefaultLength { get; init; }
}
