using System;

namespace Geospatial.Rasters;

public struct FilePyramidTileIndex : IEquatable<FilePyramidTileIndex>
{
	public int FileIndex;

	public int PyramidLevel;

	public int TileIndex;

	public FilePyramidTileIndex(int fileIdx, int pLevel, int tileIdx)
	{
		FileIndex = fileIdx;
		PyramidLevel = pLevel;
		TileIndex = tileIdx;
	}

	public FilePyramidTileIndex(int fileIdx, PyramidTileIndex pti)
	{
		FileIndex = fileIdx;
		PyramidLevel = pti.PyramidLevel;
		TileIndex = pti.TileIndex;
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(FileIndex, PyramidLevel, TileIndex);
	}

	public bool Equals(FilePyramidTileIndex other)
	{
		if (FileIndex == other.FileIndex && PyramidLevel == other.PyramidLevel)
		{
			return TileIndex == other.TileIndex;
		}
		return false;
	}
}
