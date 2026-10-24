using System;
using Geospatial.PairedData;
using Utility.ORM;

namespace Ras.Layers.BoundaryConditions;

public class TimeSeriesDefinitionEntity
{
	[Metadata("TS Type")]
	public TimeSeriesType TimeSeriesType { get; set; }

	[Metadata("Irregular Time Expressed As")]
	public ExpressedTimeType ExpressedTimeType { get; set; }

	[Metadata("RTS Interval")]
	public TimeSpan RTSInterval { get; set; }

	[Metadata("Fixed Start")]
	public DateTime FixedStartTime { get; set; }

	public static TimeSeriesDefinitionEntity From(TimeSeriesDefinitionBase tsd)
	{
		TimeSeriesDefinitionEntity timeSeriesDefinitionEntity = new TimeSeriesDefinitionEntity();
		if (tsd is RegularTimeSeriesDefinition regularTimeSeriesDefinition)
		{
			timeSeriesDefinitionEntity.TimeSeriesType = TimeSeriesType.Regular;
			timeSeriesDefinitionEntity.FixedStartTime = regularTimeSeriesDefinition.FixedStartTime;
			timeSeriesDefinitionEntity.RTSInterval = regularTimeSeriesDefinition.Interval;
		}
		else if (tsd is IrregularAbsoluteTimeSeriesDefinition)
		{
			timeSeriesDefinitionEntity.TimeSeriesType = TimeSeriesType.Irregular;
			timeSeriesDefinitionEntity.ExpressedTimeType = ExpressedTimeType.Absolute;
		}
		else
		{
			if (!(tsd is IrregularRelativeTimeSeriesDefinition))
			{
				throw new NotSupportedException("TSD type not recognized.");
			}
			timeSeriesDefinitionEntity.TimeSeriesType = TimeSeriesType.Irregular;
			timeSeriesDefinitionEntity.ExpressedTimeType = ExpressedTimeType.Relative;
		}
		return timeSeriesDefinitionEntity;
	}
}
