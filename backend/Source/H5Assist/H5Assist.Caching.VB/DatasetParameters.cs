namespace H5Assist.Caching.VB;

public class DatasetParameters
{
	public string Name;

	public int ActiveThreadCount;

	public bool EvictionActive;

	public long[] Dimensions;

	public long[] ChunkDimensions;

	public int BytesPerChunk;

	public bool Pinned;
}
