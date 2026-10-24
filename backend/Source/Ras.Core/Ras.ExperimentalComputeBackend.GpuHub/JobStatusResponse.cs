using System.Text.Json.Serialization;

namespace Ras.ExperimentalComputeBackend.GpuHub;

public class JobStatusResponse
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("status")]
	public string Status { get; set; }

	[JsonPropertyName("progress")]
	public int Progress { get; set; }

	[JsonPropertyName("startedAt")]
	public long StartedAt { get; set; }

	[JsonPropertyName("completedAt")]
	public long CompletedAt { get; set; }

	[JsonPropertyName("errorMessage")]
	public string ErrorMessage { get; set; }
}
