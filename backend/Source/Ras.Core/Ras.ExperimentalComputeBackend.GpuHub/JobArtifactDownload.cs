using System.Text.Json.Serialization;

namespace Ras.ExperimentalComputeBackend.GpuHub;

public class JobArtifactDownload
{
	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("url")]
	public string Url { get; set; }

	[JsonPropertyName("artifactType")]
	public string ArtifactType { get; set; }

	[JsonPropertyName("size")]
	public long Size { get; set; }
}
