using Ras.Updates;
using Utility.Progress;

namespace Ras.Helpers;

public class PullProjectParams
{
	public required string Server { get; set; }

	public required string Bucket { get; set; }

	public string S3Key { get; set; } = "";

	public string S3Secret { get; set; } = "";

	public required string Directory { get; set; }

	public ExampleProjectManifest Manifest { get; set; }

	public string ManifestKey { get; set; }

	public bool DryRun { get; set; }

	public bool ForceOverwriteExisting { get; set; }

	public string CacheDirectory { get; set; }

	public bool ConsoleOutput { get; set; }

	public ProgressReporter Reporter { get; set; }
}
