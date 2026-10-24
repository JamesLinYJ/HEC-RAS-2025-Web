using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Ras.ExperimentalComputeBackend.GpuHub;

public class CreateJobRequest
{
	[JsonPropertyName("projectId")]
	public int ProjectId { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("parameters")]
	public Dictionary<string, object> Parameters { get; set; }

	[JsonPropertyName("experimentId")]
	public int? ExperimentId { get; set; }

	[JsonPropertyName("gpuCount")]
	public int? GpuCount { get; set; }

	[JsonPropertyName("cpuCores")]
	public int? CpuCores { get; set; }

	[JsonPropertyName("ramMb")]
	public int? RamMb { get; set; }
}
