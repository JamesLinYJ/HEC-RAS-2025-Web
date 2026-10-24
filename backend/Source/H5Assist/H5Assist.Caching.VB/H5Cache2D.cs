using H5Assist.Chunking.VB;
using H5Assist.VB;

namespace H5Assist.Caching.VB;

public abstract class H5Cache2D<T> : H5Cache<T, Chunk2D<T>> where T : struct
{
	public H5Cache2D(H5Assist.VB.H5Reader h5r)
		: base(h5r)
	{
	}

	public H5Cache2D(string filename)
		: base(filename)
	{
	}

	public abstract T[] Item(int dsetIndex, int itemIndex);

	protected override int ComputeBytesPerChunk(long[] chunkDims)
	{
		return (int)(chunkDims[0] * chunkDims[1]) * SizeOfT;
	}

	protected override int GetNumDimensionsExpected()
	{
		return 2;
	}
}
