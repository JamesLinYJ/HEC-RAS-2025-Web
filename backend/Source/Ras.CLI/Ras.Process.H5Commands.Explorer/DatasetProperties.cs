using System;

namespace Ras.Process.H5Commands.Explorer;

public class DatasetProperties
{
	public long[] Dimensions { get; set; }

	public long[] ChunkDimensions { get; set; }

	public Type ElementType { get; set; }

	public long CompressedSize { get; set; }

	public long UncompressedSize { get; set; }

	public bool IsVariableLength { get; set; }
}
