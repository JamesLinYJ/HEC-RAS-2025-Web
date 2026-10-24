using Utility.Attributes;

namespace Ras.Layers.Meteorology;

public enum PrecipDataType
{
	[Alias("PerCum")]
	PeriodCumulative,
	[Alias("Cumulative")]
	Cumulative,
	[Alias("PerAvg")]
	PeriodAverage,
	[Alias("InstVal")]
	InstantaneousValue
}
