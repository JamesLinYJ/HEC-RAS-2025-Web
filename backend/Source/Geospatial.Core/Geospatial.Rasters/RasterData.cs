using System;
using System.Runtime.InteropServices;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

public class RasterData<T> : IRaster<T>, IResample<T>, ISurface<T>, IRasterDefinition where T : unmanaged, IEquatable<T>
{
	public readonly ReadOnlyMemory<T> Data;

	public readonly PixelRange ValidRegion;

	public T? NoDataValue { get; set; }

	public RasterDefinition RasterDefinition { get; }

	public Extent ValidExtent => RasterDefinition.Extent;

	public RasterData(RasterDefinition def, ReadOnlyMemory<T> data)
	{
		if (def.IsNull())
		{
			throw new ArgumentNullException("def");
		}
		if (def.CellCount != data.Length)
		{
			throw new ArgumentException("Data/RasterDefinition mismatch.");
		}
		RasterDefinition = def;
		Data = data;
	}

	public RasterData(RasterDefinition def, T[,] data)
	{
		if (def.IsNull())
		{
			throw new ArgumentNullException("def");
		}
		if (!def.IsCorrectSize(data))
		{
			throw new ArgumentException("Data/RasterDefinition mismatch.");
		}
		RasterDefinition = def;
		T[] values = new T[data.GetLength(0) * data.GetLength(1)];
		Buffer.BlockCopy(data, 0, values, 0, data.Length * Marshal.SizeOf<T>());
		Data = values;
	}

	public void Resample(ResampleArgs args, Memory<T> outData)
	{
		RasterOperations.Resample(RasterDefinition, Data, args, outData, NoDataValue.GetValueOrDefault());
	}

	public void GetRange(PixelRange range, Memory<T> outData)
	{
		RasterOperations.CopyRange(RasterDefinition, Data, range, outData);
	}

	public void SamplePoints(Memory<Point> points, Memory<T> result, SamplingParams sampleParams = default(SamplingParams))
	{
		T valueOrDefault = NoDataValue.GetValueOrDefault();
		Span<Point> pointsSpan = points.Span;
		Span<T> buffer = result.Span;
		ReadOnlySpan<T> bufferSpan = Data.Span;
		for (int i = 0; i < pointsSpan.Length; i++)
		{
			Point pt = pointsSpan[i];
			int cellIndexBounded = RasterDefinition.CellIndexBounded(pt);
			if (cellIndexBounded == -1)
			{
				buffer[i] = valueOrDefault;
			}
			else
			{
				buffer[i] = bufferSpan[cellIndexBounded];
			}
		}
	}

	public static void SamplePoints(RasterDefinition rasterDefinition, Memory<T> rasterData, T NDV, Memory<Point> points, Memory<T> result)
	{
		Span<Point> pointsSpan = points.Span;
		Span<T> buffer = result.Span;
		Span<T> rasterDataSpan = rasterData.Span;
		for (int i = 0; i < pointsSpan.Length; i++)
		{
			Point pt = pointsSpan[i];
			int cellIndexBounded = rasterDefinition.CellIndexBounded(pt);
			if (cellIndexBounded == -1)
			{
				buffer[i] = NDV;
			}
			else
			{
				buffer[i] = rasterDataSpan[cellIndexBounded];
			}
		}
	}

	public static void Fill(RasterDefinition rasterDefinition, Memory<T> rasterData, T localValue, PixelRange range)
	{
		Span<T> rasterDataSpan = rasterData.Span;
		if (range.IsFullRange(rasterDefinition))
		{
			rasterDataSpan.Fill(localValue);
			return;
		}
		int colCount = range.ColCount;
		for (int i = range.MinRow; i <= range.MaxRow; i++)
		{
			int start = i * rasterDefinition.Cols + range.MinCol;
			rasterDataSpan.Slice(start, colCount).Fill(localValue);
		}
	}

	public static void FillNegativeArea(RasterDefinition rasterDefinition, Memory<T> rasterData, T localValue, PixelRange range)
	{
		Span<T> rasterDataSpan = rasterData.Span;
		if (range.IsFullRange(rasterDefinition))
		{
			return;
		}
		if (range.MinRow > 0)
		{
			rasterDataSpan.Slice(0, rasterDefinition.Cols * range.MinRow).Fill(localValue);
		}
		int rows = rasterDefinition.Rows;
		int cols = rasterDefinition.Cols;
		if (range.MinCol > 0 || range.MaxCol < cols - 1)
		{
			for (int i = range.MinRow; i <= range.MaxRow; i++)
			{
				if (range.MinCol > 0)
				{
					int start = cols * i;
					rasterDataSpan.Slice(start, range.MinCol).Fill(localValue);
				}
				if (range.MaxCol < cols - 1)
				{
					int start2 = cols * i + range.MaxCol + 1;
					rasterDataSpan.Slice(start2, cols - range.MaxCol - 1).Fill(localValue);
				}
			}
		}
		if (range.MaxRow < rows - 1)
		{
			rasterDataSpan.Slice(cols * (range.MaxRow + 1)).Fill(localValue);
		}
	}

	public ReadOnlyMemory<T> GetTop()
	{
		return GetTop(Data, RasterDefinition.Cols);
	}

	public static ReadOnlyMemory<T> GetTop(ReadOnlyMemory<T> data, int tileWidth)
	{
		return data.Slice(0, tileWidth);
	}

	public ReadOnlyMemory<T> GetBottom()
	{
		return GetBottom(Data, RasterDefinition.Cols, RasterDefinition.Rows);
	}

	public static ReadOnlyMemory<T> GetBottom(ReadOnlyMemory<T> data, int tileWidth, int tileHeight)
	{
		return data.Slice((tileHeight - 1) * tileWidth, tileWidth);
	}

	public Memory<T> GetLeft()
	{
		return GetLeft(Data, RasterDefinition.Cols, RasterDefinition.Rows);
	}

	public static Memory<T> GetLeft(ReadOnlyMemory<T> data, int tileWidth, int tileHeight)
	{
		T[] left = new T[tileHeight];
		ReadOnlySpan<T> buffer = data.Span;
		for (int i = 0; i < tileHeight; i++)
		{
			left[i] = buffer[i * tileWidth];
		}
		return left;
	}

	public Memory<T> GetRight()
	{
		return GetRight(Data, RasterDefinition.Cols, RasterDefinition.Rows);
	}

	public static Memory<T> GetRight(ReadOnlyMemory<T> data, int tileWidth, int tileHeight)
	{
		T[] right = new T[tileHeight];
		ReadOnlySpan<T> buffer = data.Span;
		for (int i = 0; i < tileHeight; i++)
		{
			right[i] = buffer[(i + 1) * tileWidth - 1];
		}
		return right;
	}

	public static string DebugDisplay(RasterDefinition rdef, Memory<T> data, int nPx, T ndv)
	{
		RasterData<T> resample = new RasterData<T>(rdef, data)
		{
			NoDataValue = ndv
		};
		RasterDefinition rasterDef = RasterDefinition.ComputeBoundingRaster(rdef.Extent, nPx);
		return resample.AsRaster(rasterDef).DebugDisplayNoData();
	}
}
