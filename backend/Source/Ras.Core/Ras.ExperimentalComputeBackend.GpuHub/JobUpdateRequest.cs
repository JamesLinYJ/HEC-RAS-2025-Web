using System.Text.Json.Serialization;

namespace Ras.ExperimentalComputeBackend.GpuHub;

public class JobUpdateRequest
{
	[JsonPropertyName("status")]
	public string Status { get; set; }
}
