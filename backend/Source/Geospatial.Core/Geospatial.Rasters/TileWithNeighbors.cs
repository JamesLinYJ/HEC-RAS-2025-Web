using System;

namespace Geospatial.Rasters;

public class TileWithNeighbors<T> where T : unmanaged, IEquatable<T>
{
	public RasterData<T> Center { get; set; }

	public RasterData<T> Left { get; set; }

	public RasterData<T> Right { get; set; }

	public RasterData<T> Top { get; set; }

	public RasterData<T> Bottom { get; set; }

	public RasterData<T> UpperLeft { get; set; }

	public RasterData<T> UpperRight { get; set; }

	public RasterData<T> LowerLeft { get; set; }

	public RasterData<T> LowerRight { get; set; }
}
