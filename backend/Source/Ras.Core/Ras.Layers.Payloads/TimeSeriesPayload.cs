using System;
using System.Diagnostics;
using Geospatial.PairedData;
using Utility.Hashing;
using Utility.Logging;

namespace Ras.Layers.Payloads;

[DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
public class TimeSeriesPayload : PayloadBase, ITimeSeriesPayload
{
	[HashContributor]
	public TimeSeriesDefinitionBase TimeSeriesDefinition { get; set; }

	[HashContributor]
	public Memory<double> Values { get; set; }

	public int Count => Values.Length;

	public TimeSeriesPayload(string variable, TimeSeriesDefinitionBase tsd, Memory<double> values)
		: base(variable)
	{
		TimeSeriesDefinition = tsd;
		Values = values;
	}

	public OperationResult CoversTimeWindow(DateTime startTime, TimeSpan duration, DateTime bcStartTime)
	{
		return TimeSeriesDefinition.CoversTimeWindow(startTime, duration, Count, bcStartTime);
	}

	private string GetDebuggerDisplay()
	{
		return (Name ?? "(No Name)") + " " + Variable + ": " + Values.Length + " Values";
	}
}
