using System.Text.Json.Serialization;

namespace Ras.ExperimentalComputeBackend.GpuHub;

public class ProjectResponse
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("slug")]
	public string Slug { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }
}
