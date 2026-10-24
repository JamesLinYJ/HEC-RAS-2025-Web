using System.Text.Json.Serialization;

namespace Ras.ExperimentalComputeBackend.GpuHub;

public class CreateExperimentRequest
{
	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("projectId")]
	public int ProjectId { get; set; }

	[JsonPropertyName("description")]
	public string Description { get; set; }
}
