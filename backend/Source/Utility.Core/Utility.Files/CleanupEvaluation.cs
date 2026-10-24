using System;

namespace Utility.Files;

public class CleanupEvaluation
{
	public string FilePath { get; init; }

	public long SizeBytes { get; init; }

	public DateTime Timestamp { get; init; }

	public string Category { get; init; }

	public string ParentName { get; init; }

	public bool Retained { get; init; }

	public string Reason { get; init; }
}
