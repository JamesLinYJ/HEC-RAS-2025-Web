using Ras.Layers.Payloads;
using Utility.ORM;

namespace Ras.Layers.BoundaryConditions;

public class SpatiallyVariableTimeSeriesPayloadEntity
{
	[ComplexMetadata]
	public TimeSeriesDefinitionEntity TimeSeriesDefinition { get; set; } = new TimeSeriesDefinitionEntity();

	[Metadata("Start (Time)")]
	public int TimeRow { get; set; }

	[Metadata("Count (Time)")]
	public int TimeCount { get; set; }

	[Metadata("Start (Station)")]
	public int StationRow { get; set; }

	[Metadata("Count (Station)")]
	public int StationCount { get; set; }

	[Metadata("Start (Value)")]
	public int ValueRow { get; set; }

	[Metadata("Count (Value)")]
	public int ValueCount { get; set; }

	public static SpatiallyVariableTimeSeriesPayloadEntity From(SpatiallyVariableTimeSeriesPayload payload)
	{
		return new SpatiallyVariableTimeSeriesPayloadEntity
		{
			TimeSeriesDefinition = TimeSeriesDefinitionEntity.From(payload.TimeSeriesDefinition)
		};
	}
}
