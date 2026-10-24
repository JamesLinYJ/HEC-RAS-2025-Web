using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Testing.Core.Regression;

public class StatsArtifact
{
	[JsonPropertyName("wireVersion")]
	public int WireVersion { get; set; } = 2;

	[JsonPropertyName("harnessVersion")]
	public string HarnessVersion { get; set; }

	[JsonPropertyName("rasBaseVersion")]
	public string RasBaseVersion { get; set; }

	[JsonPropertyName("statistics")]
	public List<TestStatistic> Statistics { get; set; } = new List<TestStatistic>();
}
