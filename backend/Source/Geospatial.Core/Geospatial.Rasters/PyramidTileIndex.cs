using System;

namespace Geospatial.Rasters;

public struct PyramidTileIndex(int pLevel, int tileIdx) : IEquatable<PyramidTileIndex>
{
	public int PyramidLevel = pLevel;

	public int TileIndex = tileIdx;

	public override int GetHashCode()
	{
		return HashCode.Combine(PyramidLevel, TileIndex);
	}

	public bool Equals(PyramidTileIndex other)
	{
		if (PyramidLevel == other.PyramidLevel)
		{
			return TileIndex == other.TileIndex;
		}
		return false;
	}
}
