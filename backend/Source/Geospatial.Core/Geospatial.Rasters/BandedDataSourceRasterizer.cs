using System;
using System.Collections.Generic;
using Geospatial.Vectors;
using Utility.Memory;

namespace Geospatial.Rasters;

internal class BandedDataSourceRasterizer<T> : IMultiBandRaster<T>, IMultiBandResampler<T>, ISurface<T>, IRasterDefinition, ICacheEviction where T : unmanaged, IEquatable<T>
{
	private readonly IMultiBandRasterProvider<T> _dataSource;

	public int ProfileCount => _dataSource.ProfileCount;

	public Extent ValidExtent => _dataSource.RasterDefinition.Extent;

	public T? NoDataValue => _dataSource.NoDataValue;

	public RasterDefinition RasterDefinition => _dataSource.RasterDefinition;

	public BandedDataSourceRasterizer(IMultiBandRasterProvider<T> dataSource)
	{
		_dataSource = dataSource;
	}

	public void Resample(ResampleArgs args, int profileIndex, Memory<T> outData, ResampleCache cache = null)
	{
		args.EnsureBuffer(outData, NoDataValue);
		if (args.Raster.IsRegistered(RasterDefinition) && RasterDefinition.Extent.ContainsOrEquals(args.Raster.Extent))
		{
			PixelRange pixelRange = RasterDefinition.GetPixelRange(args.Raster.Extent, PixelRangeOptions.CenterPixelCovered);
			GetRange(pixelRange, profileIndex, outData);
			return;
		}
		PixelRange pixelRange2 = RasterDefinition.GetPixelRange(args.Raster.Extent);
		if (pixelRange2.IsNull())
		{
			return;
		}
		RasterDefinition subRaster = RasterDefinition.GetSubRaster(pixelRange2);
		Memory<T> mem;
		using (SharedMemoryPool<T>.RentNoFill(pixelRange2.CellCount, out mem))
		{
			GetRange(pixelRange2, profileIndex, mem);
			RasterOperations.Resample(subRaster, mem, args, outData, NoDataValue.GetValueOrDefault());
		}
	}

	public void Resample(ResampleArgs args, int bandStart, int bandCount, Memory<T>[] result)
	{
		this.Resample_NaiiveLoop(args, bandStart, bandCount, result);
	}

	public void GetRange(PixelRange pr, int bandIndex, Memory<T> data)
	{
		if (data.Length != pr.CellCount)
		{
			throw new ArgumentException();
		}
		BlockSize blockSize = _dataSource.BlockSize;
		RasterDefinition rasterDefinition = _dataSource.RasterDefinition;
		if (!rasterDefinition.InRange(pr))
		{
			throw new ArgumentOutOfRangeException("pr");
		}
		rasterDefinition.GetBlockDimensions(blockSize, out var blocksWide, out var _);
		PixelRange blockRange = rasterDefinition.GetBlockRange(blockSize, pr);
		RasterDefinition subRaster = rasterDefinition.GetSubRaster(pr);
		Memory<T> mem;
		using (SharedMemoryPool<T>.Rent(blockSize.PixelCount, out mem))
		{
			for (int i = blockRange.MinRow; i <= blockRange.MaxRow; i++)
			{
				for (int j = blockRange.MinCol; j <= blockRange.MaxCol; j++)
				{
					int blocksWideTerm = i * blocksWide + j;
					RasterDefinition blockRaster = _dataSource.RasterDefinition.GetBlockRaster(blockSize, blocksWideTerm);
					_dataSource.ReadBlock(bandIndex, blocksWideTerm, mem);
					RasterOperations.CopyTo(blockRaster, mem, subRaster, data);
				}
			}
		}
	}

	public void SamplePoints(Memory<Point> points, int bandStart, int bandCount, Memory<T>[] result)
	{
		if (_dataSource.VerticallyChunkedBlockSize.IsNull())
		{
			SamplePointsByProfile(points, bandStart, bandCount, result);
			return;
		}
		if (bandStart != 0 || bandCount != ProfileCount)
		{
			throw new NotImplementedException();
		}
		SamplePointsByVerticalBlock(points, result);
	}

	private void SamplePointsByProfile(Memory<Point> points, int bandStart, int bandCount, Memory<T>[] outData)
	{
		T valueOrDefault = NoDataValue.GetValueOrDefault();
		Dictionary<int, List<int>> blockPointMap = _dataSource.RasterDefinition.GetBlockPointMap(_dataSource.BlockSize, points);
		Span<Point> pointsSpan = points.Span;
		T[] values = new T[_dataSource.BlockSize.PixelCount];
		for (int i = bandStart; i < bandStart + bandCount; i++)
		{
			Span<T> outDataSpan = outData[i - bandStart].Span;
			foreach (KeyValuePair<int, List<int>> blockPointMap2 in blockPointMap)
			{
				int key = blockPointMap2.Key;
				List<int> blockPointMap2Term = blockPointMap2.Value;
				if (key == -1)
				{
					foreach (int ordinal in blockPointMap2Term)
					{
						outDataSpan[ordinal] = valueOrDefault;
					}
					continue;
				}
				_dataSource.ReadBlock(i, key, values);
				RasterDefinition blockRaster = RasterDefinition.GetBlockRaster(_dataSource.BlockSize, key);
				foreach (int ordinal2 in blockPointMap2Term)
				{
					int cellIndexBounded = blockRaster.CellIndexBounded(pointsSpan[ordinal2]);
					if (cellIndexBounded == -1)
					{
						throw new Exception("Point/Block raster error.");
					}
					outDataSpan[ordinal2] = values[cellIndexBounded];
				}
			}
		}
	}

	private void SamplePointsByVerticalBlock(Memory<Point> points, Memory<T>[] outData)
	{
		int width = _dataSource.VerticallyChunkedBlockSize.Width;
		Dictionary<int, List<int>> intListByInt = new Dictionary<int, List<int>>();
		RasterDefinition rasterDefinition = RasterDefinition;
		Span<Point> pointsSpan = points.Span;
		for (int i = 0; i < pointsSpan.Length; i++)
		{
			Point pt = pointsSpan[i];
			int key = rasterDefinition.CellIndex(pt) / width;
			if (intListByInt.TryGetValue(key, out var localValue))
			{
				localValue.Add(i);
				continue;
			}
			localValue = new List<int>();
			localValue.Add(i);
			intListByInt[key] = localValue;
		}
		T[] values = new T[_dataSource.VerticallyChunkedBlockSize.PixelCount];
		foreach (KeyValuePair<int, List<int>> localValue2 in intListByInt)
		{
			int key2 = localValue2.Key;
			_dataSource.ReadVerticalBlock(key2, values);
			foreach (int ordinal in localValue2.Value)
			{
				Point pt2 = pointsSpan[ordinal];
				int cellIndexRasterDefinitionTerm = rasterDefinition.CellIndex(pt2) - key2 * width;
				for (int j = 0; j < ProfileCount; j++)
				{
					outData[j].Span[ordinal] = values[j * width + cellIndexRasterDefinitionTerm];
				}
			}
		}
	}

	public void SamplePoints(Memory<Point> points, int profileIndex, Memory<T> result)
	{
		T valueOrDefault = NoDataValue.GetValueOrDefault();
		Span<T> buffer = result.Span;
		Span<Point> pointsSpan = points.Span;
		Dictionary<int, List<int>> blockPointMap = _dataSource.RasterDefinition.GetBlockPointMap(_dataSource.BlockSize, points);
		T[] values = new T[_dataSource.BlockSize.PixelCount];
		foreach (KeyValuePair<int, List<int>> blockPointMap2 in blockPointMap)
		{
			int key = blockPointMap2.Key;
			List<int> blockPointMap2Term = blockPointMap2.Value;
			if (key == -1)
			{
				foreach (int ordinal in blockPointMap2Term)
				{
					buffer[ordinal] = valueOrDefault;
				}
				continue;
			}
			_dataSource.ReadBlock(profileIndex, key, values);
			RasterDefinition blockRaster = RasterDefinition.GetBlockRaster(_dataSource.BlockSize, key);
			foreach (int ordinal2 in blockPointMap2Term)
			{
				int cellIndexBounded = blockRaster.CellIndexBounded(pointsSpan[ordinal2]);
				if (cellIndexBounded == -1)
				{
					throw new Exception("Point/Block raster error.");
				}
				buffer[ordinal2] = values[cellIndexBounded];
			}
		}
	}

	public void EvictCache()
	{
		(_dataSource as ICacheEviction)?.EvictCache();
	}

	public IDisposable TryPauseCache()
	{
		return (_dataSource as ICacheEviction)?.TryPauseCache() ?? NoOpDisposable.Instance;
	}
}
