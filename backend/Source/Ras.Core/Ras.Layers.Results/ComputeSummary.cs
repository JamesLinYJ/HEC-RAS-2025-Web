using System;
using System.Text.Json.Serialization;
using Utility.ORM;

namespace Ras.Layers.Results;

public class ComputeSummary : TaskSummary
{
	[JsonPropertyName("Compute Log")]
	[Metadata("Compute Log")]
	public string ComputeLog { get; set; }

	[JsonPropertyName("Solver")]
	[Metadata("Solver")]
	public string Solver { get; set; }

	[JsonPropertyName("Output Duration")]
	[Metadata("Output Duration")]
	public TimeSpan OutputDuration { get; set; }

	[JsonPropertyName("Solver Init Duration")]
	[Metadata("Solver Init Duration")]
	public TimeSpan SolverInitDuration { get; set; }

	[Metadata("Global SolveStep Retry Count")]
	public int GlobalRetryCount { get; set; }
}
