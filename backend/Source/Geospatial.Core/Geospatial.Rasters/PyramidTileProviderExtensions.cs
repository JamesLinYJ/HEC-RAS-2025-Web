using System;

namespace Geospatial.Rasters;

public static class PyramidTileProviderExtensions
{
	public static IRasterPyramid<T> AsRasterizer<T>(this IPyramidTileProvider<T> ftp) where T : unmanaged, IEquatable<T>
	{
		return new RasterPyramid<T>(ftp);
	}
}
