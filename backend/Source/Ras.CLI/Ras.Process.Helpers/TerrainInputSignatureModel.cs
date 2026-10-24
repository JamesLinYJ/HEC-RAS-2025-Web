using System;

namespace Ras.Process.Helpers;

public sealed class TerrainInputSignatureModel
{
	public int Index { get; set; }

	public string Name { get; set; }

	public string TypeName { get; set; }

	public string File { get; set; }

	public long? FileSize { get; set; }

	public DateTime? LastModifiedUtc { get; set; }

	public string Hash { get; set; }

	public int? DataHash { get; set; }
}
