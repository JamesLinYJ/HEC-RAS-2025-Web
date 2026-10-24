using System.Collections.Generic;

namespace H5Assist.Differences;

public class DiffOptions
{
	public List<string> AllowedPaths { get; set; } = new List<string>();

	public long MinCompressedSizeBytes { get; set; } = 65536L;
}
