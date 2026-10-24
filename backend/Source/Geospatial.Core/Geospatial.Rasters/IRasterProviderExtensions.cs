using System;

namespace Geospatial.Rasters;

public static class IRasterProviderExtensions
{
	public static int BlocksWide<T>(this IRasterProvider<T> tp) where T : unmanaged, IEquatable<T>
	{
		return tp.RasterDefinition.GetBlocksWide(tp.BlockSize);
	}

	public static int BlocksTall<T>(this IRasterProvider<T> tp) where T : unmanaged, IEquatable<T>
	{
		return tp.RasterDefinition.GetBlocksTall(tp.BlockSize);
	}

	public static int BlockCount<T>(this IRasterProvider<T> tp) where T : unmanaged, IEquatable<T>
	{
		tp.RasterDefinition.GetBlockDimensions(tp.BlockSize, out var blocksWide, out var blocksTall);
		return blocksWide * blocksTall;
	}

	public static bool ReadBlock<T>(this IRasterProvider<T> tp, Pixel tileIdx, Memory<T> data) where T : unmanaged, IEquatable<T>
	{
		int blocksWide = tp.BlocksWide();
		return tp.ReadBlock(tileIdx.Y * blocksWide + tileIdx.X, data);
	}

	public static void ReadAll<T>(this IRasterProvider<T> src, Memory<T> data) where T : unmanaged, IEquatable<T>
	{
		if (data.Length != src.RasterDefinition.CellCount)
		{
			throw new ArgumentException();
		}
		src.RasterDefinition.GetBlockDimensions(src.BlockSize, out var blocksWide, out var blocksTall);
		int count = blocksWide * blocksTall;
		T[] values = new T[src.BlockSize.PixelCount];
		for (int i = 0; i < count; i++)
		{
			RasterDefinition blockRaster = src.RasterDefinition.GetBlockRaster(src.BlockSize, i);
			src.ReadBlock(i, values);
			RasterOperations.CopyTo(blockRaster, values, src.RasterDefinition, data);
		}
	}

	public static T[] ReadAll<T>(this IRasterProvider<T> src) where T : unmanaged, IEquatable<T>
	{
		T[] values = new T[src.RasterDefinition.CellCount];
		src.ReadAll(values);
		return values;
	}
}
