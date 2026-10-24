using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Testing.Core.Regression;

public class TestStatistic
{
	[JsonInclude]
	[JsonPropertyName("wireVersion")]
	public int WireVersion { get; private set; } = 2;

	[JsonInclude]
	[JsonPropertyName("metric")]
	public string Metric { get; private set; }

	[JsonInclude]
	[JsonPropertyName("kind")]
	[JsonConverter(typeof(JsonStringEnumConverter))]
	public StatisticType Kind { get; private set; }

	[JsonInclude]
	[JsonPropertyName("ordinateRole")]
	[JsonConverter(typeof(JsonStringEnumConverter))]
	public OrdinateRole OrdinateRole { get; private set; }

	[JsonInclude]
	[JsonPropertyName("units")]
	[JsonConverter(typeof(JsonStringEnumConverter))]
	public StatisticUnits Units { get; private set; }

	[JsonInclude]
	[JsonPropertyName("dimensions")]
	public Dictionary<string, string> Dimensions { get; private set; } = new Dictionary<string, string>();

	[JsonInclude]
	[JsonPropertyName("point")]
	public StatisticPoint Point { get; private set; }

	[JsonInclude]
	[JsonPropertyName("measurements")]
	public List<StatisticValueGroup> Measurements { get; private set; } = new List<StatisticValueGroup>();

	[JsonConstructor]
	private TestStatistic()
	{
	}

	[SetsRequiredMembers]
	public TestStatistic(string metric, StatisticType kind, StatisticUnits units, OrdinateRole ordinateRole = OrdinateRole.None, StatisticPoint point = null)
	{
		Metric = metric;
		Kind = kind;
		Units = units;
		OrdinateRole = ordinateRole;
		Point = point;
	}

	public void AddMeasurement(double? a, double? b = null)
	{
		Measurements.Add(new StatisticValueGroup
		{
			A = a,
			B = b
		});
	}

	public TestStatistic WithDimension(string name, string dimensions)
	{
		Dimensions[name] = dimensions;
		return this;
	}

	public TestStatistic WithPlan(string plan)
	{
		return WithDimension("plan", plan);
	}
}
