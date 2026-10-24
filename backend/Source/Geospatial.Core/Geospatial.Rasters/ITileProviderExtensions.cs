using System;

namespace Geospatial.Rasters;

public static class ITileProviderExtensions
{
	public static IRaster<T> AsRasterizer<T>(this ITileProvider<T> tp) where T : unmanaged, IEquatable<T>
	{
		return new TiledRaster<T>(tp);
	}

	public static void ReadTile<T>(this ITileProvider<T> tr, int tileIdx, Memory<T> data) where T : unmanaged, IEquatable<T>
	{
		tr.ReadBlock(tr.TiledRasterDefinition.CellIndex2D(tileIdx), data);
	}

	public static T[] ReadTile<T>(this ITileProvider<T> prov, Pixel px) where T : unmanaged, IEquatable<T>
	{
		T[] tile = new T[prov.BlockSize.PixelCount];
		prov.ReadBlock(px, tile);
		return tile;
	}

	public static T[] ReadTile<T>(this ITileProvider<T> prov, int px) where T : unmanaged, IEquatable<T>
	{
		T[] tile = new T[prov.BlockSize.PixelCount];
		prov.ReadBlock(px, tile);
		return tile;
	}

	public static RasterData<T> ReadBlockAsRasterData<T>(this ITileProvider<T> prov, int idx1D) where T : unmanaged, IEquatable<T>
	{
		int tileSize = prov.TileSize;
		RasterDefinition cellRasterDefinition = prov.TiledRasterDefinition.GetCellRasterDefinition(idx1D, tileSize);
		T[] values = new T[tileSize * tileSize];
		prov.ReadBlock(idx1D, values);
		return new RasterData<T>(cellRasterDefinition, values)
		{
			NoDataValue = prov.NoDataValue
		};
	}
}
