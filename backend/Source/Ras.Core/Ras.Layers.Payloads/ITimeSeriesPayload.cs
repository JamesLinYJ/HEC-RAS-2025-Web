using Geospatial.PairedData;

namespace Ras.Layers.Payloads;

public interface ITimeSeriesPayload
{
	TimeSeriesDefinitionBase TimeSeriesDefinition { get; }
}
