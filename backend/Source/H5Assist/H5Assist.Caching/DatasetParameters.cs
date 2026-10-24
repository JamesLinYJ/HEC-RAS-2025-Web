namespace H5Assist.Caching;

public struct DatasetParameters
{
	public string Name;

	public long[] Dimensions;

	public long[] ChunkDimensions;

	public long ElementsPerChunk
	{
		get
		{
			long unity = 1L;
			for (int i = 0; i < ChunkDimensions.Length; i++)
			{
				unity *= ChunkDimensions[i];
			}
			return unity;
		}
	}
}
