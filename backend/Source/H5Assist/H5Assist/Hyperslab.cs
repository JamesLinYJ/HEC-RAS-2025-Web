namespace H5Assist;

internal static class Hyperslab
{
	public static void GetRowDims(int startRow, int numRows, ulong[] size, out ulong[] hyperslabDims, out ulong[] offset)
	{
		Get2DDims(startRow, numRows, 0, (int)size[1], out hyperslabDims, out offset);
	}

	public static void GetColDims(int startCol, int numCols, ulong[] size, out ulong[] hyperslabDims, out ulong[] offset)
	{
		Get2DDims(0, (int)size[0], startCol, numCols, out hyperslabDims, out offset);
	}

	public static void Get2DDims(int startRow, int numRows, int startCol, int numCols, out ulong[] hyperslabDims, out ulong[] offset)
	{
		hyperslabDims = new ulong[2];
		hyperslabDims[0] = (ulong)numRows;
		hyperslabDims[1] = (ulong)numCols;
		offset = new ulong[2];
		offset[0] = (ulong)startRow;
		offset[1] = (ulong)startCol;
	}

	public static void Get1DDims(int startElement, int numElements, out ulong[] hyperslabDims, out ulong[] offset)
	{
		hyperslabDims = new ulong[1];
		hyperslabDims[0] = (ulong)numElements;
		offset = new ulong[1];
		offset[0] = (ulong)startElement;
	}
}
