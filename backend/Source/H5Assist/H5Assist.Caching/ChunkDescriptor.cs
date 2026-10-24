using System;

namespace H5Assist.Caching;

public struct ChunkDescriptor(string dset, long chunkIdx0, long chunkIdx1) : IEquatable<ChunkDescriptor>
{
	public readonly string Dataset = dset;

	public readonly long ChunkIndex0 = chunkIdx0;

	public readonly long ChunkIndex1 = chunkIdx1;

	public bool Equals(ChunkDescriptor other)
	{
		if (Dataset == other.Dataset && ChunkIndex0 == other.ChunkIndex0)
		{
			return ChunkIndex1 == other.ChunkIndex1;
		}
		return false;
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(Dataset, ChunkIndex0, ChunkIndex1);
	}
}
