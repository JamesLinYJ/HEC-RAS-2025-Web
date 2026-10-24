using Geospatial.PairedData;
using Utility.ORM;

namespace Ras.Layers.BoundaryConditions;

public class TimeSeriesPayloadEntity
{
	[ComplexMetadata]
	public TimeSeriesDefinitionEntity TimeSeriesDefinition { get; set; } = new TimeSeriesDefinitionEntity();

	[Metadata("Start (Time)")]
	public int TimeRow { get; set; }

	[Metadata("Start (Value)")]
	public int ValueRow { get; set; }

	[Metadata("Count")]
	public int RowCount { get; set; }

	public static TimeSeriesPayloadEntity From(TimeSeriesDefinitionBase tsd)
	{
		return new TimeSeriesPayloadEntity
		{
			TimeSeriesDefinition = TimeSeriesDefinitionEntity.From(tsd)
		};
	}
}
