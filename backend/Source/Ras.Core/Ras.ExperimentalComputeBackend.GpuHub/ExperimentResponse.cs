using System.Text.Json.Serialization;

namespace Ras.ExperimentalComputeBackend.GpuHub;

public class ExperimentResponse
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }
}
