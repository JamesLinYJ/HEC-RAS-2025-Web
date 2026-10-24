using System.Text.Json.Serialization;

namespace Ras.ExperimentalComputeBackend.GpuHub;

public class CreateJobResponse
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("status")]
	public string Status { get; set; }
}
