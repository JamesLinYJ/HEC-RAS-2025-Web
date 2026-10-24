using System;
using Utility.Progress;

namespace Ras.Helpers;

public class PushProjectParams
{
	public required string Server { get; set; }

	public required string Bucket { get; set; }

	public string S3Key { get; set; } = "";

	public string S3Secret { get; set; } = "";

	public required string Directory { get; set; }

	public bool SkipGlobalManifest { get; set; }

	public string GlobalManifestKey { get; set; } = "globalmanifest.json";

	public DateTime PublishDate { get; set; }

	public bool DryRun { get; set; }

	public string JsonOutputPath { get; set; }

	public bool ForceOverwriteExisting { get; set; }

	public string MinimumVersion { get; set; }

	public string MaximumVersion { get; set; }

	public ProgressReporter Reporter { get; set; }

	public bool ConsoleOutput { get; set; }
}
