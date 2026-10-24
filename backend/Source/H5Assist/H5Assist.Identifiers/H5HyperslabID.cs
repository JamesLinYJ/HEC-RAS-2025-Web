using System;
using System.Collections.Generic;
using HDF.PInvoke;

namespace H5Assist.Identifiers;

internal class H5HyperslabID : IDisposable
{
	public long SpaceId;

	public ulong[] Dimensions;

	public static H5HyperslabID Open(long spaceId, ulong[] hyperslabDims, ulong[] offset)
	{
		H5HyperslabID result = new H5HyperslabID
		{
			Dimensions = hyperslabDims,
			SpaceId = H5S.create_simple(hyperslabDims.Length, hyperslabDims, null)
		};
		H5S.select_hyperslab(spaceId, H5S.seloper_t.SET, offset, null, hyperslabDims, null);
		return result;
	}

	public static H5HyperslabID CreateComplexRows(IList<int> startRows, IList<int> numRows, ulong[] size, long spaceId)
	{
		int count = 2;
		ulong size2 = size[1];
		ulong[] dimensions = new ulong[count];
		ulong[] ulongs = new ulong[count];
		ulong[] ulongsArray = new ulong[count];
		dimensions[1] = size2;
		for (int i = 0; i < startRows.Count; i++)
		{
			int startRow = startRows[i];
			int numRow = numRows[i];
			dimensions[0] += (ulong)numRow;
			ulongs[0] = (ulong)startRow;
			ulongs[1] = 0uL;
			ulongsArray[0] = (ulong)numRow;
			ulongsArray[1] = size2;
			H5S.seloper_t op = ((i != 0) ? H5S.seloper_t.OR : H5S.seloper_t.SET);
			H5S.select_hyperslab(spaceId, op, ulongs, null, ulongsArray, null);
		}
		long spaceId2 = H5S.create_simple(count, dimensions, null);
		return new H5HyperslabID
		{
			Dimensions = dimensions,
			SpaceId = spaceId2
		};
	}

	public static H5HyperslabID CreateComplexColumns(IList<int> startColumns, IList<int> numColumns, ulong[] size, long spaceId)
	{
		int count = 2;
		ulong size2 = size[0];
		ulong[] dimensions = new ulong[count];
		ulong[] ulongs = new ulong[count];
		ulong[] ulongsArray = new ulong[count];
		dimensions[0] = size2;
		for (int i = 0; i < startColumns.Count; i++)
		{
			int startColumn = startColumns[i];
			int numColumn = numColumns[i];
			dimensions[1] += (ulong)numColumn;
			ulongs[0] = 0uL;
			ulongs[1] = (ulong)startColumn;
			ulongsArray[0] = size2;
			ulongsArray[1] = (ulong)numColumn;
			H5S.seloper_t op = ((i != 0) ? H5S.seloper_t.OR : H5S.seloper_t.SET);
			H5S.select_hyperslab(spaceId, op, ulongs, null, ulongsArray, null);
		}
		long spaceId2 = H5S.create_simple(count, dimensions, null);
		return new H5HyperslabID
		{
			Dimensions = dimensions,
			SpaceId = spaceId2
		};
	}

	public static H5HyperslabID CreateComplexElements(IList<int> startElements, IList<int> numElements, ulong[] size, long spaceId)
	{
		int count = 1;
		ulong[] ulongs = new ulong[count];
		ulong[] ulongsArray = new ulong[count];
		ulong[] ulongs2 = new ulong[count];
		for (int i = 0; i < startElements.Count; i++)
		{
			int startElement = startElements[i];
			int numElement = numElements[i];
			ulongs[0] += (ulong)numElement;
			ulongsArray[0] = (ulong)startElement;
			ulongs2[0] = (ulong)numElement;
			H5S.seloper_t op = ((i != 0) ? H5S.seloper_t.OR : H5S.seloper_t.SET);
			H5S.select_hyperslab(spaceId, op, ulongsArray, null, ulongs2, null);
		}
		H5S.create_simple(count, ulongs, null);
		throw new NotImplementedException();
	}

	public static H5HyperslabID AppendRows(H5DatasetID dataset, ulong[] hyperslabDims)
	{
		ulong[] ulongs = new ulong[hyperslabDims.Length];
		H5S.get_simple_extent_dims(dataset.SpaceId, ulongs, null);
		RowAppendDims(ulongs, hyperslabDims, out var offset, out var newDims);
		return CreateHyperslab(dataset, hyperslabDims, newDims, offset);
	}

	public static H5HyperslabID AppendColumns(H5DatasetID dataset, ulong[] hyperslabDims)
	{
		ulong[] ulongs = new ulong[hyperslabDims.Length];
		H5S.get_simple_extent_dims(dataset.SpaceId, ulongs, null);
		ColAppendDims(ulongs, hyperslabDims, out var offset, out var newDims);
		return CreateHyperslab(dataset, hyperslabDims, newDims, offset);
	}

	public static H5HyperslabID AppendElements(H5DatasetID dataset, ulong[] hyperslabDims)
	{
		ulong[] ulongs = new ulong[hyperslabDims.Length];
		H5S.get_simple_extent_dims(dataset.SpaceId, ulongs, null);
		ElementAppendDims(ulongs, hyperslabDims, out var offset, out var newDims);
		return CreateHyperslab(dataset, hyperslabDims, newDims, offset);
	}

	public static H5HyperslabID InsertRows(H5DatasetID dataset, ulong[] hyperslabDims, int dstInsertionRow)
	{
		ulong[] ulongs = new ulong[hyperslabDims.Length];
		H5S.get_simple_extent_dims(dataset.SpaceId, ulongs, null);
		RowInsertDims(ulongs, hyperslabDims, (ulong)dstInsertionRow, out var offset, out var newDims);
		return CreateHyperslab(dataset, hyperslabDims, newDims, offset);
	}

	public static H5HyperslabID Insert1D(H5DatasetID dataset, ulong[] hyperslabDims, long startIdx)
	{
		if (hyperslabDims == null || hyperslabDims.Length != 1)
		{
			throw new ArgumentException("HyperslabDims need 1 value.");
		}
		ulong[] ulongs = new ulong[hyperslabDims.Length];
		H5S.get_simple_extent_dims(dataset.SpaceId, ulongs, null);
		InsertDims1D(ulongs, hyperslabDims, (ulong)startIdx, out var offset, out var newDims);
		return CreateHyperslab(dataset, hyperslabDims, newDims, offset);
	}

	public static H5HyperslabID Insert2D(H5DatasetID dataset, ulong[] hyperslabDims, int startRow, int startCol)
	{
		if (hyperslabDims == null || hyperslabDims.Length != 2)
		{
			throw new ArgumentException("HyperslabDims need 2 values.");
		}
		ulong[] ulongs = new ulong[hyperslabDims.Length];
		H5S.get_simple_extent_dims(dataset.SpaceId, ulongs, null);
		InsertDims2D(ulongs, hyperslabDims, (ulong)startRow, (ulong)startCol, out var offset, out var newDims);
		return CreateHyperslab(dataset, hyperslabDims, newDims, offset);
	}

	private static H5HyperslabID CreateHyperslab(H5DatasetID dataset, ulong[] hyperslabDims, ulong[] newSpaceDims, ulong[] insertionOffset)
	{
		H5HyperslabID result = new H5HyperslabID
		{
			Dimensions = hyperslabDims,
			SpaceId = H5S.create_simple(hyperslabDims.Length, hyperslabDims, null)
		};
		H5D.set_extent(dataset.DatasetId, newSpaceDims);
		H5S.close(dataset.SpaceId);
		H5S.select_hyperslab(dataset.SpaceId = H5D.get_space(dataset.DatasetId), H5S.seloper_t.SET, insertionOffset, null, hyperslabDims, null);
		return result;
	}

	private static void ElementAppendDims(ulong[] prevDims, ulong[] hyperslabDims, out ulong[] offset, out ulong[] newDims)
	{
		offset = new ulong[1];
		offset[0] = prevDims[0];
		newDims = new ulong[1];
		newDims[0] = prevDims[0] + hyperslabDims[0];
	}

	private static void RowAppendDims(ulong[] prevDims, ulong[] hyperslabDims, out ulong[] offset, out ulong[] newDims)
	{
		offset = new ulong[2];
		offset[0] = prevDims[0];
		offset[1] = 0uL;
		newDims = new ulong[2];
		newDims[0] = prevDims[0] + hyperslabDims[0];
		newDims[1] = hyperslabDims[1];
	}

	private static void ColAppendDims(ulong[] prevDims, ulong[] hyperslabDims, out ulong[] offset, out ulong[] newDims)
	{
		offset = new ulong[2];
		offset[0] = 0uL;
		offset[1] = prevDims[1];
		newDims = new ulong[2];
		newDims[0] = hyperslabDims[0];
		newDims[1] = prevDims[1] + hyperslabDims[1];
	}

	private static void RowInsertDims(ulong[] prevDims, ulong[] hyperslabDims, ulong startRow, out ulong[] offset, out ulong[] newDims)
	{
		InsertDims2D(prevDims, hyperslabDims, startRow, 0uL, out offset, out newDims);
	}

	private static void ColumnInsertDims(ulong[] prevDims, ulong[] hyperslabDims, ulong startCol, out ulong[] offset, out ulong[] newDims)
	{
		InsertDims2D(prevDims, hyperslabDims, 0uL, startCol, out offset, out newDims);
	}

	private static void InsertDims1D(ulong[] prevDims, ulong[] hyperslabDims, ulong startIdx, out ulong[] offset, out ulong[] newDims)
	{
		offset = new ulong[1];
		offset[0] = startIdx;
		newDims = new ulong[1];
		newDims[0] = Math.Max(prevDims[0], offset[0] + hyperslabDims[0]);
	}

	private static void InsertDims2D(ulong[] prevDims, ulong[] hyperslabDims, ulong startRow, ulong startCol, out ulong[] offset, out ulong[] newDims)
	{
		offset = new ulong[2];
		offset[0] = startRow;
		offset[1] = startCol;
		newDims = new ulong[2];
		newDims[0] = Math.Max(prevDims[0], offset[0] + hyperslabDims[0]);
		newDims[1] = Math.Max(prevDims[1], offset[1] + hyperslabDims[1]);
	}

	private static void InsertDims3D(ulong[] prevDims, ulong[] hyperslabDims, ulong startX, ulong startY, ulong startZ, out ulong[] offset, out ulong[] newDims)
	{
		offset = new ulong[2];
		offset[0] = startZ;
		offset[1] = startY;
		offset[2] = startX;
		newDims = new ulong[2];
		newDims[0] = Math.Max(prevDims[0], offset[0] + hyperslabDims[0]);
		newDims[1] = Math.Max(prevDims[1], offset[1] + hyperslabDims[1]);
		newDims[1] = Math.Max(prevDims[1], offset[1] + hyperslabDims[1]);
		throw new NotImplementedException();
	}

	public void Dispose()
	{
		H5S.close(SpaceId);
	}
}
