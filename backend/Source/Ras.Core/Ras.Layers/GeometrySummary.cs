using System;
using System.Text.Json.Serialization;
using Utility.ORM;

namespace Ras.Layers;

public class GeometrySummary : TaskSummary
{
	[JsonPropertyName("Prepare Log")]
	[Metadata("Prepare Log")]
	public string PrepareLog { get; set; }

	[JsonPropertyName("Property Table Duration")]
	[Metadata("Property Table Duration")]
	public TimeSpan PropertyTableDuration { get; set; }
}
