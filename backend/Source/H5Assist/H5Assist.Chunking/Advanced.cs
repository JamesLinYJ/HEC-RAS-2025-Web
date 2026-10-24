using System;
using System.IO;
using HDF.PInvoke;

namespace H5Assist.Chunking;

public static class Advanced
{
	public static int read_chunk(long dset_id, long dxpl_id, ulong[] offset, ref uint filter_mask, nint buf)
	{
		if (offset == null || offset.Length == 0) throw new ArgumentException("Chunk offsets are required.", nameof(offset));
		int result = H5D.read_chunk(dset_id, dxpl_id, ref offset[0], ref filter_mask, buf);
		if (result < 0) throw new IOException("HDF could not read the requested data chunk.");
		return result;
	}

	public static int write_chunk(long dset_id, long dxpl_id, uint filter_mask, ulong[] offset, nint data_size, nint buf)
	{
		if (offset == null || offset.Length == 0) throw new ArgumentException("Chunk offsets are required.", nameof(offset));
		int result = H5D.write_chunk(dset_id, dxpl_id, filter_mask, ref offset[0], data_size, buf);
		if (result < 0) throw new IOException("HDF could not write the requested data chunk.");
		return result;
	}
}
