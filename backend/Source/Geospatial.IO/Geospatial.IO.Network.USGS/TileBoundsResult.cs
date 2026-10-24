using Geospatial.Vectors;

namespace Geospatial.IO.Network.USGS;

public readonly struct TileBoundsResult(Extent bounds, long sizeInBytes, bool success)
{
	public Extent Bounds { get; } = bounds;

	public long SizeInBytes { get; } = sizeInBytes;

	public bool Success { get; } = success;

	public static TileBoundsResult Failed => new TileBoundsResult(default, 0L, success: false);

	public static TileBoundsResult FailedWithSize(long size)
	{
		return new TileBoundsResult(default, size, success: false);
	}
}
