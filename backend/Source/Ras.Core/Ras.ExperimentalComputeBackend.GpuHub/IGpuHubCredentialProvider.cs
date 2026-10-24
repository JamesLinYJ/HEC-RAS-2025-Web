using System.Threading;
using System.Threading.Tasks;
using Utility.Communication;

namespace Ras.ExperimentalComputeBackend.GpuHub;

public interface IGpuHubCredentialProvider
{
	ValueTask<bool> HasTokenAsync(CancellationToken ct = default(CancellationToken));

	ValueTask<string> GetBearerTokenAsync(CancellationToken ct = default(CancellationToken));

	ValueTask<string> GetBaseUrlAsync(CancellationToken ct = default(CancellationToken));

	S3Client TryCreateS3Client();
}
