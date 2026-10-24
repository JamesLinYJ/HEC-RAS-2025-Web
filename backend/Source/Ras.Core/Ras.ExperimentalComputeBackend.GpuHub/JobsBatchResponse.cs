using System.Text.Json.Serialization;

namespace Ras.ExperimentalComputeBackend.GpuHub;

public class JobsBatchResponse
{
	[JsonPropertyName("jobs")]
	public JobStatusResponse[] Jobs { get; set; }
}
