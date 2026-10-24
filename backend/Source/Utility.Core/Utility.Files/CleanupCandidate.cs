using System;
using System.Collections.Generic;

namespace Utility.Files;

public class CleanupCandidate
{
	public string FilePath { get; init; }

	public long SizeBytes { get; init; }

	public DateTime Timestamp { get; init; }

	public string Category { get; init; }

	public string ParentName { get; init; }

	public string Reason { get; init; }

	public List<string> AuxiliaryFiles { get; init; } = new List<string>();

	public long TotalRecoverableBytes { get; init; }

	public string PrettySize => FileHelpers.PrettyByteCount(TotalRecoverableBytes);
}
