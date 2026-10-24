using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Utility.Logging;
using Utility.ORM;

namespace Ras.Layers;

public class TaskSummary
{
	[JsonPropertyName("Software Version")]
	[Metadata("Software Version")]
	public string SoftwareVersion { get; set; }

	[JsonIgnore]
	public IReadOnlyList<MessageLogEntry> LogEntries { get; set; }

	[JsonPropertyName("Success")]
	[Metadata("Success", LegacyKeyNames = new string[] { "Computed Successfully" })]
	public bool Success { get; set; }

	[JsonPropertyName("Failure Reason")]
	[Metadata("Failure Reason")]
	public string FailureReason { get; set; }

	[JsonPropertyName("Start Time")]
	[Metadata("Start Time")]
	public DateTime StartTime { get; set; }

	[JsonPropertyName("Total Duration")]
	[Metadata("Total Duration")]
	public TimeSpan TotalDuration { get; set; }

	public DateTime EndTime => StartTime + TotalDuration;
}
