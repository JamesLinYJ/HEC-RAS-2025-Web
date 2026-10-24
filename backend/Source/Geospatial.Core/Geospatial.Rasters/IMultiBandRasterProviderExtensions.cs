using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;
using Utility.Memory;

namespace Geospatial.Rasters;

public static class IMultiBandRasterProviderExtensions
{
	private class MultiBandToSingleBandRasterProvider<T> : IRasterProvider<T>, IBlockedRasterDefinition, IRasterDefinition, ISurface<T> where T : unmanaged, IEquatable<T>
	{
		private readonly IMultiBandRasterProvider<T> _banded;

		private readonly int _pinnedProfileIndex;

		public BlockSize BlockSize => _banded.BlockSize;

		public RasterDefinition RasterDefinition => _banded.RasterDefinition;

		public Extent ValidExtent => _banded.ValidExtent;

		public T? NoDataValue => _banded.NoDataValue;

		public MultiBandToSingleBandRasterProvider(IMultiBandRasterProvider<T> banded, int profileIndex)
		{
			_banded = banded;
			_pinnedProfileIndex = profileIndex;
		}

		public bool ReadBlock(long blockIndex, Memory<T> block)
		{
			return _banded.ReadBlock(_pinnedProfileIndex, blockIndex, block);
		}
	}

	public static void ReadProfile<T>(this IMultiBandRasterProvider<T> src, int profileIndex, Memory<T> data) where T : unmanaged, IEquatable<T>
	{
		if (data.Length != src.RasterDefinition.CellCount)
		{
			throw new ArgumentException();
		}
		if (profileIndex >= src.ProfileCount || profileIndex < 0)
		{
			throw new ArgumentException();
		}
		src.RasterDefinition.GetBlockDimensions(src.BlockSize, out var blocksWide, out var blocksTall);
		int count = blocksWide * blocksTall;
		switch (count)
		{
		case 0:
			return;
		case 1:
			if (src.RasterDefinition.CellCount == src.BlockSize.PixelCount)
			{
				src.ReadBlock(profileIndex, 0L, data);
				return;
			}
			break;
		}
		Memory<T> mem;
		using (SharedMemoryPool<T>.RentNoFill(src.BlockSize.PixelCount, out mem))
		{
			Memory<T> memSpan = mem;
			for (int i = 0; i < count; i++)
			{
				RasterDefinition blockRaster = src.RasterDefinition.GetBlockRaster(src.BlockSize, i);
				src.ReadBlock(profileIndex, i, memSpan);
				RasterOperations.CopyTo(blockRaster, memSpan, src.RasterDefinition, data);
			}
		}
	}

	public static IRasterProvider<T> AsRasterProvider<T>(this IMultiBandRasterProvider<T> source, int pinnedProfileIndex) where T : unmanaged, IEquatable<T>
	{
		return new MultiBandToSingleBandRasterProvider<T>(source, pinnedProfileIndex);
	}

	public static T[] ReadProfile<T>(this IMultiBandRasterProvider<T> src, int profileIndex) where T : unmanaged, IEquatable<T>
	{
		T[] profile = new T[src.RasterDefinition.CellCount];
		src.ReadProfile(profileIndex, profile);
		return profile;
	}

	public static IMultiBandRaster<T> AsBandedRasterizer<T>(this IMultiBandRasterProvider<T> datasource, bool readSourceAndDisconnect = false) where T : unmanaged, IEquatable<T>
	{
		if (readSourceAndDisconnect)
		{
			int profileCount = datasource.ProfileCount;
			return new MultiBandRaster<T>(datasource.RasterDefinition, (from t in Enumerable.Range(0, profileCount)
				select datasource.ReadProfile(t)).ToArray(), datasource.NoDataValue);
		}
		return new BandedDataSourceRasterizer<T>(datasource);
	}

	public static ITimeSeriesRasterProvider<T> AsTimeSeriesProvider<T>(this IMultiBandRasterProvider<T> banded, IReadOnlyList<DateTime> times) where T : unmanaged, IEquatable<T>
	{
		return new VirtualTimeSeriesRasterProvider<T>(banded, times);
	}
}
