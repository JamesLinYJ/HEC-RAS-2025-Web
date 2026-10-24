using System;
using Geospatial.Rasters;

namespace Geospatial.Rendering.TiledRenderingSystem;

public struct CachedTileIndex
{
	public int PyramidLevel { get; private init; }

	public Pixel TileIndex { get; private init; }

	public int GlobalVersion { get; private init; }

	public CachedTileIndex(int pyramidLevel, Pixel tileIndex, int globalVersion)
	{
		PyramidLevel = pyramidLevel;
		TileIndex = tileIndex;
		GlobalVersion = globalVersion;
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(PyramidLevel, TileIndex);
	}
}
