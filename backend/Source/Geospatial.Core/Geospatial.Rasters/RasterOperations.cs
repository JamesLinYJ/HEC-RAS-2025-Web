using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Geospatial.Rasters.Convolution;
using Geospatial.Transformers;
using Geospatial.Vectors;
using Utility;
using Utility.Memory;
using Utility.Progress;
using Utility.Units;

namespace Geospatial.Rasters;

public static class RasterOperations
{
	private enum RegisteredDataCopyResult
	{
		Success,
		NoOverlap,
		NotRegistered
	}

	private struct TileFillWork<T>
	{
		public int TileIndex;

		public Memory<T> TopWall;

		public Memory<T> LeftWall;

		public Memory<T> RightWall;

		public Memory<T> BottomWall;

		public TileFillWork<T> Merge(TileFillWork<T> newEl)
		{
			return new TileFillWork<T>
			{
				TileIndex = TileIndex,
				TopWall = (TopWall.IsEmpty ? newEl.TopWall : TopWall),
				LeftWall = (LeftWall.IsEmpty ? newEl.LeftWall : LeftWall),
				RightWall = (RightWall.IsEmpty ? newEl.RightWall : RightWall),
				BottomWall = (BottomWall.IsEmpty ? newEl.BottomWall : BottomWall)
			};
		}
	}

	public class AvailableWork<TKey, TWork> : IEnumerable<TWork>, IEnumerable
	{
		private readonly Func<TWork, TWork, TWork> _combine;

		private readonly Dictionary<TKey, TWork> _workMap = new Dictionary<TKey, TWork>();

		private readonly Queue<TKey> _queue = new Queue<TKey>();

		public AvailableWork(Func<TWork, TWork, TWork> combineWork)
		{
			_combine = combineWork;
		}

		public void AddWork(TKey key, TWork work)
		{
			if (_workMap.TryGetValue(key, out var localValue))
			{
				TWork workMap = _combine(work, localValue);
				_workMap[key] = workMap;
			}
			else
			{
				_workMap[key] = work;
				_queue.Enqueue(key);
			}
		}

		public IEnumerator<TWork> GetEnumerator()
		{
			throw new NotImplementedException();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}
	}

	[DebuggerDisplay("{StartX}->{EndX} : {Y}")]
	private struct FFSegment(int startX, int endX, int y, sbyte dir, bool scanLeft, bool scanRight)
	{
		public int StartX = startX;

		public int EndX = endX;

		public int Y = y;

		public sbyte Direction = dir;

		public bool ScanLeft = scanLeft;

		public bool ScanRight = scanRight;
	}

	private class ProcessTilesParallelForData<T, K> where T : unmanaged, IEquatable<T>
	{
		public PixelRange TileRange;

		public RasterDefinition WorldTileDef;

		public Extent DesiredExtent;

		public int BasePyramidLevel;

		public ITileProvider<T> BaseLevel;

		public IPyramidTileProvider<T> Itr;

		public bool ClipTilesToDesiredExtent;

		public Action<K, long, RasterDefinition, Memory<T>> ProcessAction;

		public K Model;

		public int TIdx = -1;
	}

	private sealed class NeighborScratch<T> where T : unmanaged, IEquatable<T>
	{
		[ThreadStatic]
		private static NeighborScratch<T> _instance;

		private bool _inUse;

		public readonly List<IMemoryOwner<T>> Owners = new List<IMemoryOwner<T>>();

		public readonly Dictionary<Pixel, RasterData<T>> Cache = new Dictionary<Pixel, RasterData<T>>();

		public readonly TileWithNeighbors<T> Tile = new TileWithNeighbors<T>();

		public static bool TryBorrow(out NeighborScratch<T> scratch)
		{
			NeighborScratch<T> neighborScratch = _instance ?? (_instance = new NeighborScratch<T>());
			if (neighborScratch._inUse)
			{
				scratch = null;
				return false;
			}
			neighborScratch._inUse = true;
			scratch = neighborScratch;
			return true;
		}

		public void Return()
		{
			Cache.Clear();
			Owners.Clear();
			_inUse = false;
		}
	}

	private class WarpData<T>
	{
		public RasterDefinition SourceRaster;

		public RasterDefinition TargetRaster;

		public Memory<T> SourceData;

		public Memory<T> TargetData;

		public TransformCoefficient8x8 CoefCol;

		public TransformCoefficient8x8 CoefRow;
	}



	private const int ColumnCountBlockCopySplit = 24;

	public const int DefaultTileParallelThreshold = 17;

	public static void Resample<T>(RasterDefinition sourceRaster, ReadOnlyMemory<T> sourceData, ResampleArgs args, Memory<T> result, T noDataValue) where T : unmanaged, IEquatable<T>
	{
		Type typeFromHandle = typeof(T);
		if (typeFromHandle == typeof(float))
		{
			ReadOnlyMemory<float> sourceData2 = Unsafe.As<ReadOnlyMemory<T>, ReadOnlyMemory<float>>(ref sourceData);
			Memory<float> destData = Unsafe.As<Memory<T>, Memory<float>>(ref result);
			Resample(sourceRaster, sourceData2, args, destData, (noDataValue as float?).GetValueOrDefault());
		}
		else if (typeFromHandle == typeof(double))
		{
			ReadOnlyMemory<double> sourceData3 = Unsafe.As<ReadOnlyMemory<T>, ReadOnlyMemory<double>>(ref sourceData);
			Memory<double> destData2 = Unsafe.As<Memory<T>, Memory<double>>(ref result);
			Resample(sourceRaster, sourceData3, args, destData2, (noDataValue as double?).GetValueOrDefault());
		}
		else if (typeof(T) == typeof(int))
		{
			ReadOnlyMemory<int> sourceData4 = Unsafe.As<ReadOnlyMemory<T>, ReadOnlyMemory<int>>(ref sourceData);
			Memory<int> destData3 = Unsafe.As<Memory<T>, Memory<int>>(ref result);
			Resample(sourceRaster, sourceData4, args, destData3, (noDataValue as int?).GetValueOrDefault());
		}
		else if (typeof(T) == typeof(byte))
		{
			ReadOnlyMemory<byte> sourceData5 = Unsafe.As<ReadOnlyMemory<T>, ReadOnlyMemory<byte>>(ref sourceData);
			Memory<byte> byteSpan = Unsafe.As<Memory<T>, Memory<byte>>(ref result);
			if (args.SampleParams.Mode == InterpolationMode.Nearest)
			{
				Resample(sourceRaster, sourceData5, args, byteSpan, (noDataValue as byte?).GetValueOrDefault());
				return;
			}
			if (args.SampleParams.Mode != InterpolationMode.Linear)
			{
				throw new NotImplementedException();
			}
			byte valueOrDefault = (noDataValue as byte?).GetValueOrDefault();
			args.EnsureBuffer(byteSpan, valueOrDefault);
			Resample_RenderBlocks(sourceRaster, sourceData5, args.Raster, byteSpan);
		}
		else
		{
			Resample_Nearest(sourceRaster, sourceData, args.Raster, result, noDataValue);
		}
	}

	public static void Resample<T>(TileWithNeighbors<T> sourceRaster, ResampleArgs args, Memory<T> destData, T noDataValue) where T : unmanaged, IEquatable<T>
	{
		args.EnsureBuffer(destData, noDataValue);
		if (sourceRaster.Center.RasterDefinition.IsRegistered(args.Raster) && TryCopyToInternal(sourceRaster.Center.RasterDefinition, sourceRaster.Center.Data, PixelRange.NullRange, args.Raster, destData, SkipNoDataOnCopy(args, noDataValue)) != RegisteredDataCopyResult.NotRegistered)
		{
			return;
		}
		SamplingParams sampleParams = args.SampleParams;
		if (sampleParams.Mode == InterpolationMode.Nearest)
		{
			Resample_Nearest(sourceRaster.Center.RasterDefinition, sourceRaster.Center.Data, args.Raster, destData, noDataValue);
			return;
		}
		if (sampleParams.Mode == InterpolationMode.Linear || sampleParams.Mode == InterpolationMode.Smooth)
		{
			Resample_LinearTiled(sourceRaster, args, destData, noDataValue);
			return;
		}
		if (sampleParams.Mode == InterpolationMode.Hybrid)
		{
			if (sourceRaster.Center.RasterDefinition.CellSize < args.Raster.CellSize * 1.0)
			{
				Resample_Nearest(sourceRaster.Center.RasterDefinition, sourceRaster.Center.Data, args.Raster, destData, noDataValue);
			}
			else
			{
				Resample_LinearTiled(sourceRaster, args, destData, noDataValue);
			}
			return;
		}
		if (sampleParams.Mode == InterpolationMode.AreaAveraged)
		{
			if (typeof(T) == typeof(float))
			{
				ReadOnlyMemory<T> source = sourceRaster.Center.Data;
				ReadOnlyMemory<float> sourceData = Unsafe.As<ReadOnlyMemory<T>, ReadOnlyMemory<float>>(ref source);
				Memory<float> destData2 = Unsafe.As<Memory<T>, Memory<float>>(ref destData);
				Resample_PhysicallyAccurate(sourceRaster.Center.RasterDefinition, sourceData, args.Raster, destData2, -9999f);
			}
			else
			{
				Resample_Nearest(sourceRaster.Center.RasterDefinition, sourceRaster.Center.Data, args.Raster, destData, noDataValue);
			}
			return;
		}
		throw new NotImplementedException();
	}

	private static void Resample_LinearTiled<T>(TileWithNeighbors<T> sourceRaster, ResampleArgs args, Memory<T> destData, T noDataValue) where T : unmanaged, IEquatable<T>
	{
		Type typeFromHandle = typeof(T);
		if (typeFromHandle == typeof(float))
		{
			Resample_RenderBlocks(sourceRaster as TileWithNeighbors<float>, destData: Unsafe.As<Memory<T>, Memory<float>>(ref destData), destRaster: args.Raster);
			return;
		}
		if (typeFromHandle == typeof(double))
		{
			Resample_RenderBlocks(sourceRaster as TileWithNeighbors<double>, destData: Unsafe.As<Memory<T>, Memory<double>>(ref destData), destRaster: args.Raster);
			return;
		}
		if (typeFromHandle == typeof(byte))
		{
			Resample_RenderBlocks(sourceRaster as TileWithNeighbors<byte>, destData: Unsafe.As<Memory<T>, Memory<byte>>(ref destData), destRaster: args.Raster);
			return;
		}
		Resample_Nearest(sourceRaster.Center.RasterDefinition, sourceRaster.Center.Data, args.Raster, destData, noDataValue);
	}

	private static void Resample(RasterDefinition sourceRaster, ReadOnlyMemory<float> sourceData, ResampleArgs args, Memory<float> destData, float noDataValue)
	{
		args.EnsureBuffer(destData, noDataValue);
		if (sourceRaster.IsRegistered(args.Raster) && TryCopyToInternal(sourceRaster, sourceData, PixelRange.NullRange, args.Raster, destData, SkipNoDataOnCopy(args, noDataValue)) != RegisteredDataCopyResult.NotRegistered)
		{
			return;
		}
		SamplingParams sampleParams = args.SampleParams;
		if (sampleParams.Mode == InterpolationMode.Nearest)
		{
			Resample_Nearest(sourceRaster, sourceData, args.Raster, destData, noDataValue);
			return;
		}
		if (sampleParams.Mode == InterpolationMode.Linear || sampleParams.Mode == InterpolationMode.Smooth)
		{
			Resample_RenderBlocks(sourceRaster, sourceData, args.Raster, destData, noDataValue);
			return;
		}
		if (sampleParams.Mode == InterpolationMode.Hybrid)
		{
			if (sourceRaster.CellSize < args.Raster.CellSize * 1.0)
			{
				Resample_Nearest(sourceRaster, sourceData, args.Raster, destData, noDataValue);
			}
			else
			{
				Resample_RenderBlocks(sourceRaster, sourceData, args.Raster, destData, noDataValue);
			}
			return;
		}
		if (sampleParams.Mode == InterpolationMode.AreaAveraged)
		{
			Resample_PhysicallyAccurate(sourceRaster, sourceData, args.Raster, destData, noDataValue);
			return;
		}
		throw new NotImplementedException();
	}

	private static void Resample(RasterDefinition sourceRaster, ReadOnlyMemory<double> sourceData, ResampleArgs args, Memory<double> destData, double noDataValue)
	{
		args.EnsureBuffer(destData, noDataValue);
		if (sourceRaster.IsRegistered(args.Raster) && TryCopyToInternal(sourceRaster, sourceData, PixelRange.NullRange, args.Raster, destData, SkipNoDataOnCopy(args, noDataValue)) != RegisteredDataCopyResult.NotRegistered)
		{
			return;
		}
		SamplingParams sampleParams = args.SampleParams;
		if (sampleParams.Mode == InterpolationMode.Nearest)
		{
			Resample_Nearest(sourceRaster, sourceData, args.Raster, destData, noDataValue);
			return;
		}
		if (sampleParams.Mode == InterpolationMode.Linear || sampleParams.Mode == InterpolationMode.Smooth)
		{
			Resample_RenderBlocks(sourceRaster, sourceData, args.Raster, destData);
			return;
		}
		if (sampleParams.Mode == InterpolationMode.Hybrid)
		{
			if (sourceRaster.CellSize < args.Raster.CellSize * 1.0)
			{
				Resample_Nearest(sourceRaster, sourceData, args.Raster, destData, noDataValue);
			}
			else
			{
				Resample_RenderBlocks(sourceRaster, sourceData, args.Raster, destData);
			}
			return;
		}
		if (sampleParams.Mode == InterpolationMode.AreaAveraged)
		{
			Resample_RenderBlocks(sourceRaster, sourceData, args.Raster, destData);
			return;
		}
		throw new NotImplementedException();
	}

	private static void Resample(RasterDefinition sourceRaster, ReadOnlyMemory<int> sourceData, ResampleArgs args, Memory<int> destData, int noDataValue)
	{
		args.EnsureBuffer(destData, noDataValue);
		Resample_Nearest(sourceRaster, sourceData, args.Raster, destData, noDataValue);
	}

	private static void Resample(RasterDefinition sourceRaster, ReadOnlyMemory<byte> sourceData, ResampleArgs args, Memory<byte> destData, byte noDataValue)
	{
		args.EnsureBuffer(destData, noDataValue);
		Resample_Nearest(sourceRaster, sourceData, args.Raster, destData, noDataValue);
	}

	public static void Resample(RasterDefinition sourceRaster, ReadOnlyMemory<float> sourceData, ResampleArgs args, Memory<float> destData)
	{
		Resample(sourceRaster, sourceData, args, destData, -9999f);
	}

	public static void Resample(RasterDefinition sourceRaster, ReadOnlyMemory<double> sourceData, ResampleArgs args, Memory<double> destData)
	{
		Resample(sourceRaster, sourceData, args, destData, -9999.0);
	}

	public static void Resample_Nearest<T>(RasterDefinition sourceRaster, ReadOnlyMemory<T> sourceData, RasterDefinition destRaster, Memory<T> destData, T NDV) where T : unmanaged, IEquatable<T>
	{
		PixelRange pixelRange = destRaster.GetPixelRange(sourceRaster.Extent, PixelRangeOptions.CenterPixelCovered);
		if (pixelRange.IsNull())
		{
			return;
		}
		ReadOnlySpan<T> sourceDataSpan = sourceData.Span;
		Span<T> destDataSpan = destData.Span;
		double cellSize = destRaster.CellSize;
		double cellSize2 = sourceRaster.CellSize;
		double minXDestRasterTerm = destRaster.MinX + 0.5 * cellSize;
		double maxYDestRasterTerm = destRaster.MaxY - 0.5 * cellSize;
		double minX = sourceRaster.MinX;
		double maxY = sourceRaster.MaxY;
		int cols = sourceRaster.Cols;
		for (int i = pixelRange.MinRow; i <= pixelRange.MaxRow; i++)
		{
			int colsDestRasterTerm = destRaster.Cols * i + pixelRange.MinCol;
			double cellSizeTerm = maxYDestRasterTerm - cellSize * (double)i;
			int rowsOffset = (int)Math.Floor(Math.Max(maxY - cellSizeTerm, 0.0) / cellSize2);
			if (rowsOffset >= sourceRaster.Rows)
			{
				rowsOffset = sourceRaster.Rows - 1;
			}
			int colsTerm = rowsOffset * cols;
			for (int j = pixelRange.MinCol; j <= pixelRange.MaxCol; j++)
			{
				int colsOffset = (int)Math.Floor((minXDestRasterTerm + cellSize * (double)j - minX) / cellSize2);
				if (colsOffset >= cols)
				{
					colsOffset = cols - 1;
				}
				if (colsOffset < 0)
				{
					colsOffset = 0;
				}
				int index = colsTerm + colsOffset;
				T sourceData2 = sourceDataSpan[index];
				if (!sourceData2.Equals(NDV))
				{
					destDataSpan[colsDestRasterTerm] = sourceData2;
				}
				colsDestRasterTerm++;
			}
		}
	}

	public static void GetResamplePoints_Nearest(RasterDefinition sourceRaster, RasterDefinition destRaster, Memory<Point> destData)
	{
		PixelRange pixelRange = destRaster.GetPixelRange(sourceRaster.Extent, PixelRangeOptions.CenterPixelCovered);
		if (pixelRange.IsNull())
		{
			return;
		}
		Span<Point> destDataSpan = destData.Span;
		double cellSize = destRaster.CellSize;
		double cellSize2 = sourceRaster.CellSize;
		double minXDestRasterTerm = destRaster.MinX + 0.5 * cellSize;
		double maxYDestRasterTerm = destRaster.MaxY - 0.5 * cellSize;
		double minX = sourceRaster.MinX;
		double maxY = sourceRaster.MaxY;
		int cols = sourceRaster.Cols;
		for (int i = pixelRange.MinRow; i <= pixelRange.MaxRow; i++)
		{
			int colsDestRasterTerm = destRaster.Cols * i + pixelRange.MinCol;
			double cellSizeTerm = maxYDestRasterTerm - cellSize * (double)i;
			int rowsOffset = (int)Math.Floor(Math.Max(maxY - cellSizeTerm, 0.0) / cellSize2);
			if (rowsOffset >= sourceRaster.Rows)
			{
				rowsOffset = sourceRaster.Rows - 1;
			}
			int colsTerm = rowsOffset * cols;
			for (int j = pixelRange.MinCol; j <= pixelRange.MaxCol; j++)
			{
				int colsOffset = (int)Math.Floor((minXDestRasterTerm + cellSize * (double)j - minX) / cellSize2);
				if (colsOffset >= cols)
				{
					colsOffset = cols - 1;
				}
				if (colsOffset < 0)
				{
					colsOffset = 0;
				}
				destDataSpan[colsDestRasterTerm] = destRaster.CellCenter(colsDestRasterTerm);
				colsDestRasterTerm++;
			}
		}
	}

	public unsafe static void Resample_Nearest<T>(RasterDefinition sourceRaster, T* sourceData, RasterDefinition destRaster, T* destData, T NDV) where T : unmanaged, IEquatable<T>
	{
		PixelRange pixelRange = destRaster.GetPixelRange(sourceRaster.Extent, PixelRangeOptions.CenterPixelCovered);
		if (pixelRange.IsNull())
		{
			return;
		}
		double cellSize = destRaster.CellSize;
		double cellSize2 = sourceRaster.CellSize;
		double minXDestRasterTerm = destRaster.MinX + 0.5 * cellSize;
		double maxYDestRasterTerm = destRaster.MaxY - 0.5 * cellSize;
		double minX = sourceRaster.MinX;
		double maxY = sourceRaster.MaxY;
		int cols = sourceRaster.Cols;
		_ = sourceRaster.Rows;
		for (int i = pixelRange.MinRow; i <= pixelRange.MaxRow; i++)
		{
			int colsDestRasterTerm = destRaster.Cols * i + pixelRange.MinCol;
			double cellSizeTerm = maxYDestRasterTerm - cellSize * (double)i;
			int maxYCellSize2Term = (int)Math.Floor(Math.Max(maxY - cellSizeTerm, 0.0) / cellSize2) * cols;
			for (int j = pixelRange.MinCol; j <= pixelRange.MaxCol; j++)
			{
				int colsOffset = (int)Math.Floor((minXDestRasterTerm + cellSize * (double)j - minX) / cellSize2);
				if (colsOffset >= cols)
				{
					colsOffset = cols - 1;
				}
				if (colsOffset < 0)
				{
					colsOffset = 0;
				}
				int ordinal = maxYCellSize2Term + colsOffset;
				T sourceData2 = sourceData[ordinal];
				if (!sourceData2.Equals(NDV))
				{
					destData[colsDestRasterTerm] = sourceData2;
				}
				colsDestRasterTerm++;
			}
		}
	}

	private static void Resample_RenderBlocks<T>(RasterDefinition sourceRaster, ReadOnlyMemory<T> sourceData, RasterDefinition destRaster, Memory<T> destData, T noDataValue) where T : unmanaged, IBinaryFloatingPointIeee754<T>
	{
		Resample_RenderBlocks(new RenderBlockFP<T>(sourceRaster, sourceData, noDataValue), destRaster, destData);
	}

	private static void Resample_RenderBlocks<T>(TileWithNeighbors<T> neighbors, RasterDefinition destRaster, Memory<T> destData) where T : unmanaged, IBinaryFloatingPointIeee754<T>
	{
		Resample_RenderBlocks(new RenderBlockFP<T>(neighbors), destRaster, destData);
	}

	private static void Resample_RenderBlocks(RasterDefinition sourceRaster, ReadOnlyMemory<byte> sourceData, RasterDefinition destRaster, Memory<byte> destData)
	{
		Resample_RenderBlocks(new RenderBlockB(sourceRaster, sourceData), destRaster, destData);
	}

	private static void Resample_RenderBlocks(TileWithNeighbors<byte> neighbors, RasterDefinition destRaster, Memory<byte> destData)
	{
		Resample_RenderBlocks(new RenderBlockB(neighbors), destRaster, destData);
	}

	private static void Resample_RenderBlocks<T>(RenderBlockFP<T> rb, RasterDefinition destRaster, Memory<T> destData) where T : unmanaged, IBinaryFloatingPointIeee754<T>
	{
		RasterDefinition renderBlockRasterDefinition = rb.GetRenderBlockRasterDefinition();
		PixelRange pixelRange = destRaster.GetPixelRange(renderBlockRasterDefinition.Extent, PixelRangeOptions.CenterPixelCovered);
		if (pixelRange.IsNull())
		{
			return;
		}
		Span<T> destDataSpan = destData.Span;
		double cellCenterX = destRaster.CellCenterX(pixelRange.MinCol);
		double cellCenterY = destRaster.CellCenterY(pixelRange.MinRow);
		double cellCenterX2 = cellCenterX;
		double y = cellCenterY;
		double cellSize = destRaster.CellSize;
		Memory<int> mem;
		using (SharedMemoryPool<int>.RentNoFill(pixelRange.ColCount, out mem))
		{
			Span<int> memSpan = mem.Span;
			for (int i = pixelRange.MinCol; i <= pixelRange.MaxCol; i++)
			{
				memSpan[i - pixelRange.MinCol] = renderBlockRasterDefinition.Col(destRaster.CellCenterX(i));
			}
			double minX = renderBlockRasterDefinition.MinX;
			double cellSize2 = renderBlockRasterDefinition.CellSize;
			for (int j = pixelRange.MinRow; j <= pixelRange.MaxRow; j++)
			{
				int row = renderBlockRasterDefinition.Row(in y);
				int colsDestRasterTerm = destRaster.Cols * j;
				double fractionalRow = renderBlockRasterDefinition.FractionalRow(y);
				T yfrac = T.CreateChecked(fractionalRow - (double)(long)fractionalRow);
				for (int k = pixelRange.MinCol; k <= pixelRange.MaxCol; k++)
				{
					int mem2 = memSpan[k - pixelRange.MinCol];
					if (row == rb.CurrentRow && mem2 == rb.CurrentCol)
					{
						if (!rb.PriorReturnValue)
						{
							cellCenterX2 += cellSize;
							continue;
						}
					}
					else if (!rb.SetPixel_SkipPrior(row, mem2))
					{
						cellCenterX2 += cellSize;
						continue;
					}
					double cellCenterX2AboveMinXOverCellSize2 = (cellCenterX2 - minX) / cellSize2;
					T xfrac = T.CreateChecked(cellCenterX2AboveMinXOverCellSize2 - (double)(long)cellCenterX2AboveMinXOverCellSize2);
					destDataSpan[colsDestRasterTerm + k] = rb.Interpolate(xfrac, yfrac);
					cellCenterX2 += cellSize;
				}
				cellCenterX2 = cellCenterX;
				y -= cellSize;
			}
		}
	}

	private static void Resample_RenderBlocks(RenderBlockB rb, RasterDefinition destRaster, Memory<byte> destData)
	{
		RasterDefinition renderBlockRasterDefinition = rb.GetRenderBlockRasterDefinition();
		PixelRange pixelRange = destRaster.GetPixelRange(renderBlockRasterDefinition.Extent);
		if (pixelRange.IsNull())
		{
			return;
		}
		Span<byte> destDataSpan = destData.Span;
		double cellCenterX = destRaster.CellCenterX(pixelRange.MinCol);
		double cellCenterY = destRaster.CellCenterY(pixelRange.MinRow);
		double cellCenterX2 = cellCenterX;
		double y = cellCenterY;
		double cellSize = destRaster.CellSize;
		Memory<int> mem;
		using (SharedMemoryPool<int>.RentNoFill(pixelRange.ColCount, out mem))
		{
			Span<int> memSpan = mem.Span;
			for (int i = pixelRange.MinCol; i <= pixelRange.MaxCol; i++)
			{
				memSpan[i - pixelRange.MinCol] = renderBlockRasterDefinition.Col(destRaster.CellCenterX(i));
			}
			double minX = renderBlockRasterDefinition.MinX;
			double cellSize2 = renderBlockRasterDefinition.CellSize;
			for (int j = pixelRange.MinRow; j <= pixelRange.MaxRow; j++)
			{
				int row = renderBlockRasterDefinition.Row(in y);
				int colsDestRasterTerm = destRaster.Cols * j;
				double fractionalRow = renderBlockRasterDefinition.FractionalRow(y);
				float fractionalRowDifference = (float)(fractionalRow - (double)(long)fractionalRow);
				for (int k = pixelRange.MinCol; k <= pixelRange.MaxCol; k++)
				{
					int mem2 = memSpan[k - pixelRange.MinCol];
					if (row == rb.CurrentRow && mem2 == rb.CurrentCol)
					{
						if (!rb.PriorReturnValue)
						{
							cellCenterX2 += cellSize;
							destDataSpan[colsDestRasterTerm + k] = byte.MaxValue;
							continue;
						}
					}
					else if (!rb.SetPixel_SkipPrior(row, mem2))
					{
						cellCenterX2 += cellSize;
						destDataSpan[colsDestRasterTerm + k] = byte.MaxValue;
						continue;
					}
					double cellCenterX2AboveMinXOverCellSize2 = (cellCenterX2 - minX) / cellSize2;
					float unity = (float)(cellCenterX2AboveMinXOverCellSize2 - (double)(long)cellCenterX2AboveMinXOverCellSize2);
					if (unity < 0f)
					{
						unity = 1f;
						fractionalRowDifference = 0f;
					}
					if (fractionalRowDifference < 0f)
					{
						unity = 0f;
						fractionalRowDifference = 1f;
					}
					byte b = rb.Interpolate(unity, fractionalRowDifference);
					destDataSpan[colsDestRasterTerm + k] = b;
					cellCenterX2 += cellSize;
				}
				cellCenterX2 = cellCenterX;
				y -= cellSize;
			}
		}
	}

	private static void Resample_RenderBlocks(RasterDefinition sourceRaster, ReadOnlyMemory<double> sourceData, RasterDefinition destRaster, Memory<double> destData)
	{
		RenderBlockD renderBlockD = new RenderBlockD(sourceRaster, sourceData);
		RasterDefinition renderBlockRasterDefinition = renderBlockD.GetRenderBlockRasterDefinition();
		PixelRange pixelRange = destRaster.GetPixelRange(renderBlockRasterDefinition.Extent, PixelRangeOptions.CenterPixelCovered);
		if (pixelRange.IsNull())
		{
			return;
		}
		Span<double> destDataSpan = destData.Span;
		double cellCenterX = destRaster.CellCenterX(pixelRange.MinCol);
		double cellCenterY = destRaster.CellCenterY(pixelRange.MinRow);
		double cellCenterX2 = cellCenterX;
		double y = cellCenterY;
		double cellSize = destRaster.CellSize;
		Memory<int> mem;
		using (SharedMemoryPool<int>.RentNoFill(pixelRange.ColCount, out mem))
		{
			Span<int> memSpan = mem.Span;
			for (int i = pixelRange.MinCol; i <= pixelRange.MaxCol; i++)
			{
				memSpan[i - pixelRange.MinCol] = renderBlockRasterDefinition.Col(destRaster.CellCenterX(i));
			}
			for (int j = pixelRange.MinRow; j <= pixelRange.MaxRow; j++)
			{
				int rbRow = renderBlockRasterDefinition.Row(in y);
				int colsDestRasterTerm = destRaster.Cols * j;
				double fractionalRow = renderBlockRasterDefinition.FractionalRow(y);
				float fractionalRowDifference = (float)(fractionalRow - (double)(long)fractionalRow);
				for (int k = pixelRange.MinCol; k <= pixelRange.MaxCol; k++)
				{
					int rbCol = memSpan[k - pixelRange.MinCol];
					if (!renderBlockD.SetPixel(rbRow, rbCol))
					{
						cellCenterX2 += cellSize;
						continue;
					}
					double fractionalCol = renderBlockRasterDefinition.FractionalCol(cellCenterX2);
					float fractionalColDifference = (float)(fractionalCol - (double)(long)fractionalCol);
					destDataSpan[colsDestRasterTerm + k] = renderBlockD.Interpolate(fractionalColDifference, fractionalRowDifference);
					cellCenterX2 += cellSize;
				}
				cellCenterX2 = cellCenterX;
				y -= cellSize;
			}
		}
	}

	public static void CopyTo<T>(RasterDefinition sourceRaster, ReadOnlyMemory<T> sourceData, RasterDefinition destRaster, Memory<T> destData) where T : unmanaged, IEquatable<T>
	{
		if (TryCopyToInternal(sourceRaster, sourceData, PixelRange.NullRange, destRaster, destData) == RegisteredDataCopyResult.NotRegistered)
		{
			throw new Exception("Source/Dest data not registered.");
		}
	}

	public static void CopyTo(RasterDefinition sourceRaster, ReadOnlyMemory<float> sourceData, PixelRange sourceMask, RasterDefinition destRaster, Memory<float> destData)
	{
		if (TryCopyToInternal(sourceRaster, sourceData, sourceMask, destRaster, destData) == RegisteredDataCopyResult.NotRegistered)
		{
			throw new Exception("Source/Dest data not registered.");
		}
	}

	public static void CopyRange<T>(RasterDefinition sourceRaster, ReadOnlyMemory<T> sourceData, PixelRange copyRange, Memory<T> destData)
	{
		if (destData.Length != copyRange.CellCount)
		{
			throw new ArgumentException("destData buffer incorrectly sized for desired range.");
		}
		int rowCount = copyRange.RowCount;
		int colCount = copyRange.ColCount;
		ReadOnlySpan<T> sourceDataSpan = sourceData.Span;
		Span<T> destDataSpan = destData.Span;
		if (colCount >= 24)
		{
			for (int i = 0; i < rowCount; i++)
			{
				int row = copyRange.MinRow + i;
				int start = sourceRaster.CellIndex(row, copyRange.MinCol);
				int start2 = i * colCount;
				sourceDataSpan.Slice(start, colCount).CopyTo(destDataSpan.Slice(start2));
			}
			return;
		}
		for (int j = 0; j < rowCount; j++)
		{
			int row2 = copyRange.MinRow + j;
			int cellIndex = sourceRaster.CellIndex(row2, copyRange.MinCol);
			int colCountTerm = j * colCount;
			for (int k = 0; k < colCount; k++)
			{
				destDataSpan[colCountTerm] = sourceDataSpan[cellIndex];
				colCountTerm++;
				cellIndex++;
			}
		}
	}

	public static void CopyRange<T>(RasterDefinition sourceRaster, ReadOnlyMemory<T> sourceData, PixelRange copyRange, Memory<T> destData, T? noDataValue) where T : unmanaged, IEquatable<T>
	{
		if (!noDataValue.HasValue)
		{
			CopyRange(sourceRaster, sourceData, copyRange, destData);
			return;
		}
		if (destData.Length != copyRange.CellCount)
		{
			throw new ArgumentException("destData buffer incorrectly sized for desired range.");
		}
		int rowCount = copyRange.RowCount;
		int colCount = copyRange.ColCount;
		ReadOnlySpan<T> sourceDataSpan = sourceData.Span;
		Span<T> destDataSpan = destData.Span;
		T noDataValueTerm = noDataValue.Value;
		for (int i = 0; i < rowCount; i++)
		{
			int row = copyRange.MinRow + i;
			int cellIndex = sourceRaster.CellIndex(row, copyRange.MinCol);
			int colCountTerm = i * colCount;
			for (int j = 0; j < colCount; j++)
			{
				T sourceData2 = sourceDataSpan[cellIndex];
				if (!sourceData2.Equals(noDataValueTerm))
				{
					destDataSpan[colCountTerm] = sourceData2;
				}
				cellIndex++;
				colCountTerm++;
			}
		}
	}

	private static T? SkipNoDataOnCopy<T>(ResampleArgs args, T noDataValue) where T : unmanaged, IEquatable<T>
	{
		if (!args.SampleParams.InitializeBufferToNoData)
		{
			return noDataValue;
		}
		return null;
	}

	private static RegisteredDataCopyResult TryCopyToInternal<T>(RasterDefinition sourceRaster, ReadOnlyMemory<T> sourceData, PixelRange sourceMask, RasterDefinition destRaster, Memory<T> destData, T? skipNoDataValue = null) where T : unmanaged, IEquatable<T>
	{
		if (destData.Length != destRaster.CellCount)
		{
			throw new ArgumentException("Destination buffer needs to be pre-allocated to the correct size.");
		}
		PixelRange range = sourceRaster.GetPixelRange(destRaster.Extent, PixelRangeOptions.CenterPixelCovered);
		Extent ext = sourceRaster.Extent;
		if (!sourceMask.IsNull())
		{
			range = range.Intersection(sourceMask);
			if (range.IsNull())
			{
				return RegisteredDataCopyResult.NoOverlap;
			}
			ext = sourceRaster.RangeExtent(range);
		}
		PixelRange pixelRange = destRaster.GetPixelRange(ext, PixelRangeOptions.CenterPixelCovered);
		if (pixelRange.IsNull() || range.IsNull())
		{
			return RegisteredDataCopyResult.NoOverlap;
		}
		if (pixelRange.RowCount != range.RowCount || pixelRange.ColCount != range.ColCount)
		{
			return RegisteredDataCopyResult.NotRegistered;
		}
		int rowCount = range.RowCount;
		int colCount = range.ColCount;
		ReadOnlySpan<T> sourceDataSpan = sourceData.Span;
		Span<T> destDataSpan = destData.Span;
		if (skipNoDataValue.HasValue)
		{
			T skipNoDataValueTerm = skipNoDataValue.Value;
			for (int i = 0; i < rowCount; i++)
			{
				int cellIndex = sourceRaster.CellIndex(range.MinRow + i, range.MinCol);
				int cellIndex2 = destRaster.CellIndex(pixelRange.MinRow + i, pixelRange.MinCol);
				for (int j = 0; j < colCount; j++)
				{
					T sourceData2 = sourceDataSpan[cellIndex];
					if (!sourceData2.Equals(skipNoDataValueTerm))
					{
						destDataSpan[cellIndex2] = sourceData2;
					}
					cellIndex++;
					cellIndex2++;
				}
			}
			return RegisteredDataCopyResult.Success;
		}
		if (colCount >= 24)
		{
			for (int k = 0; k < rowCount; k++)
			{
				int row = range.MinRow + k;
				int row2 = pixelRange.MinRow + k;
				int start = sourceRaster.CellIndex(row, range.MinCol);
				int start2 = destRaster.CellIndex(row2, pixelRange.MinCol);
				sourceDataSpan.Slice(start, colCount).CopyTo(destDataSpan.Slice(start2));
			}
		}
		else
		{
			for (int l = 0; l < rowCount; l++)
			{
				int row3 = range.MinRow + l;
				int row4 = pixelRange.MinRow + l;
				int cellIndex3 = sourceRaster.CellIndex(row3, range.MinCol);
				int cellIndex4 = destRaster.CellIndex(row4, pixelRange.MinCol);
				for (int m = 0; m < colCount; m++)
				{
					destDataSpan[cellIndex4] = sourceDataSpan[cellIndex3];
					cellIndex4++;
					cellIndex3++;
				}
			}
		}
		return RegisteredDataCopyResult.Success;
	}

	public static void Interpolate(RasterDefinition raster, Memory<float> dataA, float fractionA, Memory<float> dataB, float fractionB, Memory<float> outData, ParallelizationIntent intent = ParallelizationIntent.Serial)
	{
		if (dataA.Length != raster.CellCount || dataB.Length != raster.CellCount || outData.Length != raster.CellCount)
		{
			throw new ArgumentException();
		}
		float NDV = -9999f;
		if (fractionA == 0f)
		{
			dataB.CopyTo(outData);
			return;
		}
		if (fractionB == 0f)
		{
			dataB.CopyTo(outData);
			return;
		}
		if (intent == ParallelizationIntent.Serial)
		{
			Span<float> outDataSpan = outData.Span;
			Span<float> dataASpan = dataA.Span;
			Span<float> dataBSpan = dataB.Span;
			for (int i = 0; i < raster.CellCount; i++)
			{
				float dataA2 = dataASpan[i];
				float dataB2 = dataBSpan[i];
				if (dataA2 != NDV && dataB2 != NDV)
				{
					outDataSpan[i] = dataA2 * fractionA + dataB2 * fractionB;
				}
			}
			return;
		}
		Utility.Parallel.SmartFor(raster.CellCount, (int start, int end) =>
		{
			Span<float> outData2 = outData.Span;
			Span<float> dataA3 = dataA.Span;
			Span<float> dataB3 = dataB.Span;
			for (int j = start; j <= end; j++)
			{
				float dataA4 = dataA3[j];
				float dataB4 = dataB3[j];
				if (dataA4 != NDV && dataB4 != NDV)
				{
					outData2[j] = dataA4 * fractionA + dataB4 * fractionB;
				}
			}
		});
	}

	public static void SamplePoints<T>(RasterDefinition sourceRaster, ReadOnlyMemory<T> sourceData, Memory<Point> points, T? noDataValue, Memory<T> result) where T : unmanaged, IEquatable<T>
	{
		T valueOrDefault = noDataValue.GetValueOrDefault();
		Span<Point> pointsSpan = points.Span;
		ReadOnlySpan<T> sourceDataSpan = sourceData.Span;
		Span<T> buffer = result.Span;
		for (int i = 0; i < pointsSpan.Length; i++)
		{
			int cellIndexBounded_FavorInBounds = sourceRaster.CellIndexBounded_FavorInBounds(pointsSpan[i]);
			if (cellIndexBounded_FavorInBounds == -1)
			{
				buffer[i] = valueOrDefault;
			}
			else
			{
				buffer[i] = sourceDataSpan[cellIndexBounded_FavorInBounds];
			}
		}
	}

	public static void ComputeSlopes(RasterDefinition sourceRaster, Memory<float> data, Memory<float> result)
	{
		Span<float> floatSpan = result.Span;
		float[] floatValues = new float[data.Length];
		float[] floatValuesArray = new float[data.Length];
		PlanarRegression3x3.Solve(sourceRaster, data, floatValues, floatValuesArray, -9999f);
		float[] floatValues2 = new float[data.Length];
		VelocityMath.ConvertToDir(floatValues, floatValuesArray, floatValues2, -9999f, DirectionConvention.GoingToward);
		int cols = sourceRaster.Cols;
		for (int i = 1; i < sourceRaster.Rows - 1; i++)
		{
			for (int j = 1; j < cols - 1; j++)
			{
				int colsSourceRasterTerm = i * sourceRaster.Cols + j;
				float computed = floatValues2[colsSourceRasterTerm];
				float computed2 = 0f;
				for (int k = i - 1; k < i + 2; k++)
				{
					for (int l = j - 1; l < j + 2; l++)
					{
						float colsSourceRasterTerm2 = Math.Abs(floatValues2[k * sourceRaster.Cols + l] - computed);
						if (colsSourceRasterTerm2 > 180f)
						{
							colsSourceRasterTerm2 -= 180f;
						}
						computed2 += colsSourceRasterTerm2;
					}
				}
				floatSpan[colsSourceRasterTerm] = computed2 / 8f;
			}
		}
	}

	public static void PreserveRegion<T>(RasterDefinition raster, Memory<T> data, Extent preserveExtent, T fillValue)
	{
		if (raster.CellCount != data.Length)
		{
			throw new ArgumentException();
		}
		PixelRange pixelRange = raster.GetPixelRange(preserveExtent, PixelRangeOptions.CenterPixelCovered);
		if (pixelRange.IsNull())
		{
			data.Span.Fill(fillValue);
		}
		else
		{
			if (pixelRange.IsFullRange(raster))
			{
				return;
			}
			Span<T> buffer = data.Span;
			if (pixelRange.MinRow > 0)
			{
				buffer.Slice(0, pixelRange.MinRow * raster.Cols).Fill(fillValue);
			}
			for (int i = pixelRange.MinRow; i <= pixelRange.MaxRow; i++)
			{
				if (pixelRange.MinCol > 0)
				{
					buffer.Slice(i * raster.Cols, pixelRange.MinCol).Fill(fillValue);
				}
				if (pixelRange.MaxCol < raster.Cols - 1)
				{
					buffer.Slice(i * raster.Cols + pixelRange.MaxCol + 1, raster.Cols - pixelRange.MaxCol - 1).Fill(fillValue);
				}
			}
			if (pixelRange.MaxRow < raster.Rows - 1)
			{
				buffer.Slice((pixelRange.MaxRow + 1) * raster.Cols).Fill(fillValue);
			}
		}
	}

	public static void PreservePolygon<T>(RasterDefinition raster, Memory<T> data, IPointInPolygonChecker checker, Extent preserveExtent, T fillValue)
	{
		if (raster.CellCount != data.Length)
		{
			throw new ArgumentException();
		}
		PixelRange pixelRange = raster.GetPixelRange(preserveExtent, PixelRangeOptions.CenterPixelCovered);
		if (pixelRange.IsNull())
		{
			data.Span.Fill(fillValue);
			return;
		}
		PreserveRegion(raster, data, preserveExtent, fillValue);
		Span<T> buffer = data.Span;
		for (int i = pixelRange.MinRow; i <= pixelRange.MaxRow; i++)
		{
			double y = raster.CellCenterY(i);
			int colsRasterTerm = i * raster.Cols;
			for (int j = pixelRange.MinCol; j <= pixelRange.MaxCol; j++)
			{
				if (!checker.Contains(new Point(raster.CellCenterX(j), y)))
				{
					buffer[colsRasterTerm + j] = fillValue;
				}
			}
		}
	}

	public static void ClipRegion<T>(RasterDefinition raster, Memory<T> data, Extent clipExtent, T fillValue)
	{
		if (raster.CellCount != data.Length)
		{
			throw new ArgumentException();
		}
		PixelRange pixelRange = raster.GetPixelRange(clipExtent, PixelRangeOptions.CenterPixelCovered);
		if (pixelRange.IsNull())
		{
			return;
		}
		if (pixelRange.IsFullRange(raster))
		{
			data.Span.Fill(fillValue);
			return;
		}
		Span<T> buffer = data.Span;
		for (int i = pixelRange.MinRow; i <= pixelRange.MaxRow; i++)
		{
			buffer.Slice(i * raster.Cols + pixelRange.Left, pixelRange.ColCount).Fill(fillValue);
		}
	}

	public static void Resample_AreaAveraged(RasterDefinition sourceRaster, ReadOnlyMemory<float> sourceData, RasterDefinition destRaster, Memory<float> destData, float noDataValue)
	{
		if (destRaster.CellSize < sourceRaster.CellSize * 0.99)
		{
			Resample_RenderBlocks(sourceRaster, sourceData, destRaster, destData, noDataValue);
			return;
		}
		PixelRange pixelRange = destRaster.GetPixelRange(sourceRaster.Extent, PixelRangeOptions.CenterPixelCovered);
		if (pixelRange.IsNull())
		{
			return;
		}
		ReadOnlySpan<float> sourceDataSpan = sourceData.Span;
		Span<float> destDataSpan = destData.Span;
		double cellSize = sourceRaster.CellSize;
		double cellSize2 = destRaster.CellSize;
		double minX = sourceRaster.MinX;
		double maxY = sourceRaster.MaxY;
		int cols = sourceRaster.Cols;
		int rows = sourceRaster.Rows;
		int cols2 = destRaster.Cols;
		for (int i = pixelRange.MinRow; i <= pixelRange.MaxRow; i++)
		{
			double maxYDestRasterTerm = destRaster.MaxY - (double)i * cellSize2;
			double cellSize2Term = maxYDestRasterTerm - cellSize2;
			int maxYCellSizeTerm = Math.Max(0, (int)Math.Floor((maxY - maxYDestRasterTerm) / cellSize));
			int count = Math.Min(rows - 1, (int)Math.Floor((maxY - cellSize2Term) / cellSize));
			if (maxYCellSizeTerm > count)
			{
				continue;
			}
			int cols2Term = i * cols2;
			for (int j = pixelRange.MinCol; j <= pixelRange.MaxCol; j++)
			{
				double minXDestRasterTerm = destRaster.MinX + (double)j * cellSize2;
				double cellSize2Term2 = minXDestRasterTerm + cellSize2;
				int minXCellSizeTerm = Math.Max(0, (int)Math.Floor((minXDestRasterTerm - minX) / cellSize));
				int count2 = Math.Min(cols - 1, (int)Math.Floor((cellSize2Term2 - minX) / cellSize));
				if (minXCellSizeTerm > count2)
				{
					continue;
				}
				double sourceDataSum = 0.0;
				int ordinal = 0;
				for (int k = maxYCellSizeTerm; k <= count; k++)
				{
					int colsTerm = k * cols;
					for (int l = minXCellSizeTerm; l <= count2; l++)
					{
						float sourceData2 = sourceDataSpan[colsTerm + l];
						if (sourceData2 != noDataValue && !float.IsNaN(sourceData2))
						{
							sourceDataSum += (double)sourceData2;
							ordinal++;
						}
					}
				}
				if (ordinal > 0)
				{
					destDataSpan[cols2Term + j] = (float)(sourceDataSum / (double)ordinal);
				}
			}
		}
	}

	public static void Resample_PhysicallyAccurate(RasterDefinition sourceRaster, ReadOnlyMemory<float> sourceData, RasterDefinition destRaster, Memory<float> destData, float noDataValue)
	{
		if (destRaster.CellSize > sourceRaster.CellSize * 1.01)
		{
			Resample_AreaAveraged(sourceRaster, sourceData, destRaster, destData, noDataValue);
		}
		else
		{
			Resample_RenderBlocks(sourceRaster, sourceData, destRaster, destData, noDataValue);
		}
	}

	public static void FloodFill<TIn, TOut>(RasterDefinition raster, Memory<TIn> baseData, Pixel seed, NeighborKernel<TIn, bool> shouldCross, Memory<TOut> fillMask, TOut fillValue, bool use8PointStencil = false) where TIn : unmanaged, IEquatable<TIn> where TOut : unmanaged, IEquatable<TOut>
	{
		Span<TIn> baseDataSpan = baseData.Span;
		Span<TOut> fillMaskSpan = fillMask.Span;
		int rows = raster.Rows;
		int cols = raster.Cols;
		HashSet<int> intSet = new HashSet<int>();
		HashSet<int> intSetSet = new HashSet<int>();
		int to1D = seed.To1D(cols);
		fillMaskSpan[to1D] = fillValue;
		intSetSet.Add(to1D);
		while (intSetSet.Count != 0)
		{
			foreach (int ordinal in intSetSet)
			{
				Pixel pixel = raster.CellIndex2D(ordinal);
				TIn baseData2 = baseDataSpan[ordinal];
				bool columnIsZero = pixel.Column == 0;
				bool columnPixelTerm = pixel.Column == cols - 1;
				bool rowIsZero = pixel.Row == 0;
				bool rowPixelTerm = pixel.Row == rows - 1;
				if (!columnIsZero)
				{
					int ordinal2 = ordinal - 1;
					if (!fillMaskSpan[ordinal2].Equals(fillValue) && shouldCross(baseData2, baseDataSpan[ordinal2]))
					{
						intSet.Add(ordinal2);
						fillMaskSpan[ordinal2] = fillValue;
					}
					if (use8PointStencil && !rowIsZero)
					{
						int colsTerm = ordinal2 - cols;
						if (!fillMaskSpan[colsTerm].Equals(fillValue) && shouldCross(baseData2, baseDataSpan[colsTerm]))
						{
							intSet.Add(colsTerm);
							fillMaskSpan[colsTerm] = fillValue;
						}
					}
					if (use8PointStencil && !rowPixelTerm)
					{
						int colsTerm2 = ordinal2 + cols;
						if (!fillMaskSpan[colsTerm2].Equals(fillValue) && shouldCross(baseData2, baseDataSpan[colsTerm2]))
						{
							intSet.Add(colsTerm2);
							fillMaskSpan[colsTerm2] = fillValue;
						}
					}
				}
				if (!columnPixelTerm)
				{
					int ordinal3 = ordinal + 1;
					if (!fillMaskSpan[ordinal3].Equals(fillValue) && shouldCross(baseData2, baseDataSpan[ordinal3]))
					{
						intSet.Add(ordinal3);
						fillMaskSpan[ordinal3] = fillValue;
					}
					if (use8PointStencil && !rowIsZero)
					{
						int colsTerm3 = ordinal3 - cols;
						if (!fillMaskSpan[colsTerm3].Equals(fillValue) && shouldCross(baseData2, baseDataSpan[colsTerm3]))
						{
							intSet.Add(colsTerm3);
							fillMaskSpan[colsTerm3] = fillValue;
						}
					}
					if (use8PointStencil && !rowPixelTerm)
					{
						int colsTerm4 = ordinal3 + cols;
						if (!fillMaskSpan[colsTerm4].Equals(fillValue) && shouldCross(baseData2, baseDataSpan[colsTerm4]))
						{
							intSet.Add(colsTerm4);
							fillMaskSpan[colsTerm4] = fillValue;
						}
					}
				}
				if (!rowPixelTerm)
				{
					int colsTerm5 = ordinal + cols;
					if (!fillMaskSpan[colsTerm5].Equals(fillValue) && shouldCross(baseData2, baseDataSpan[colsTerm5]))
					{
						intSet.Add(colsTerm5);
						fillMaskSpan[colsTerm5] = fillValue;
					}
				}
				if (!rowIsZero)
				{
					int colsTerm6 = ordinal - cols;
					if (!fillMaskSpan[colsTerm6].Equals(fillValue) && shouldCross(baseData2, baseDataSpan[colsTerm6]))
					{
						intSet.Add(colsTerm6);
						fillMaskSpan[colsTerm6] = fillValue;
					}
				}
			}
			intSetSet = intSet;
			intSet = new HashSet<int>();
		}
	}

	public static void ScanlineFill<TIn, TOut>(ITileProvider<TIn> input, Pixel seed, SelfKernel<TIn, bool> shouldFill, ITileReaderWriter<TOut> output, TOut fillValue, ProgressReporter prog = null) where TIn : unmanaged, IEquatable<TIn> where TOut : unmanaged, IEquatable<TOut>
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		RasterDefinition worldTileDef = input.TiledRasterDefinition;
		RasterDefinition tiledRasterDefinition = ((ITiledRasterDefinition)output).TiledRasterDefinition;
		if (worldTileDef.Rows != tiledRasterDefinition.Rows || worldTileDef.Cols != tiledRasterDefinition.Cols)
		{
			throw new ArgumentException("TileProvider/Writer mismatch.");
		}
		Point pt = input.RasterDefinition.CellCenter(seed);
		int cellIndex = worldTileDef.CellIndex(pt);
		int tileSize = input.TileSize;
		TOut maskNDV = output.NoDataValue.GetValueOrDefault();
		Dictionary<int, TileFillWork<TOut>> newWorkList2 = null;
		Dictionary<int, TileFillWork<TOut>> newWorkList = new Dictionary<int, TileFillWork<TOut>>();
		RasterDefinition tileRasterDefinition = input.GetTileRasterDefinition(cellIndex);
		if (tileRasterDefinition.Rows != tileRasterDefinition.Cols)
		{
			throw new NotImplementedException("Rows != Cols");
		}
		Pixel seed2 = tileRasterDefinition.CellIndex2D(pt);
		TIn[] tile = input.ReadTile(cellIndex);
		TOut[] tileArray = output.ReadTile(cellIndex);
		TOut[] tOuts = new TOut[tileSize * 4];
		BuildPerimeterFlag<TOut>(tileRasterDefinition, tileArray, tOuts);
		ScanlineFill(tileRasterDefinition, tile, seed2, shouldFill, tileArray, fillValue);
		output.WriteTile(cellIndex, tileArray);
		TOut[] tOutsArray = new TOut[tileSize * 4];
		BuildPerimeterFlag<TOut>(tileRasterDefinition, tileArray, tOutsArray);
		EmitAnyWork(cellIndex, tOuts, tOutsArray);
		newWorkList2 = newWorkList;
		newWorkList = new Dictionary<int, TileFillWork<TOut>>();
		while (newWorkList2.Count > 0)
		{
			System.Threading.Tasks.Parallel.ForEach(newWorkList2, (KeyValuePair<int, TileFillWork<TOut>> kvp) =>
			{
				TileFillWork<TOut> kvpTerm = kvp.Value;
				int tileIndex = kvpTerm.TileIndex;
				RasterDefinition tileRasterDefinition2 = input.GetTileRasterDefinition(tileIndex);
				TIn[] tile2 = input.ReadTile(tileIndex);
				TOut[] tile3 = output.ReadTile(tileIndex);
				TOut[] tOuts2 = new TOut[tileSize * 4];
				BuildPerimeterFlag<TOut>(tileRasterDefinition2, tile3, tOuts2);
				if (!kvpTerm.TopWall.IsEmpty)
				{
					Span<TOut> topWallSpan = kvpTerm.TopWall.Span;
					for (int i = 0; i < topWallSpan.Length; i++)
					{
						TOut topWall2 = topWallSpan[i];
						if (topWall2.Equals(fillValue))
						{
							ScanlineFill(seed: new Pixel(0, i), raster: tileRasterDefinition2, baseData: tile2, shouldFill: shouldFill, fillMask: tile3, fillValue: fillValue);
						}
					}
				}
				if (!kvpTerm.LeftWall.IsEmpty)
				{
					Span<TOut> leftWallSpan = kvpTerm.LeftWall.Span;
					for (int j = 0; j < leftWallSpan.Length; j++)
					{
						TOut leftWall2 = leftWallSpan[j];
						if (leftWall2.Equals(fillValue))
						{
							ScanlineFill(seed: new Pixel(j, 0), raster: tileRasterDefinition2, baseData: tile2, shouldFill: shouldFill, fillMask: tile3, fillValue: fillValue);
						}
					}
				}
				if (!kvpTerm.RightWall.IsEmpty)
				{
					Span<TOut> rightWallSpan = kvpTerm.RightWall.Span;
					for (int k = 0; k < rightWallSpan.Length; k++)
					{
						TOut rightWall2 = rightWallSpan[k];
						if (rightWall2.Equals(fillValue))
						{
							ScanlineFill(seed: new Pixel(k, tileSize - 1), raster: tileRasterDefinition2, baseData: tile2, shouldFill: shouldFill, fillMask: tile3, fillValue: fillValue);
						}
					}
				}
				if (!kvpTerm.BottomWall.IsEmpty)
				{
					Span<TOut> bottomWallSpan = kvpTerm.BottomWall.Span;
					for (int l = 0; l < bottomWallSpan.Length; l++)
					{
						TOut bottomWall2 = bottomWallSpan[l];
						if (bottomWall2.Equals(fillValue))
						{
							ScanlineFill(seed: new Pixel(tileSize - 1, l), raster: tileRasterDefinition2, baseData: tile2, shouldFill: shouldFill, fillMask: tile3, fillValue: fillValue);
						}
					}
				}
				output.WriteTile(tileIndex, tile3);
				TOut[] tOuts3 = new TOut[tileSize * 4];
				BuildPerimeterFlag<TOut>(tileRasterDefinition2, tile3, tOuts3);
				EmitAnyWork(tileIndex, tOuts2, tOuts3);
			});
			newWorkList2 = newWorkList;
			newWorkList = new Dictionary<int, TileFillWork<TOut>>();
		}
		void AddWork(int tIdx, TileFillWork<TOut> work)
		{
			lock (newWorkList)
			{
				if (newWorkList.TryGetValue(tIdx, out var localValue))
				{
					newWorkList[tIdx] = localValue.Merge(work);
				}
				else
				{
					newWorkList[tIdx] = work;
				}
			}
		}
		void EmitAnyWork(int tIdx, Memory<TOut> initMask, Memory<TOut> newMask)
		{
			Memory<TOut> slice = initMask.Slice(0, tileSize);
			Memory<TOut> bottomWall = newMask.Slice(0, tileSize);
			bool compareWipePerimeterFlags = CompareWipePerimeterFlags(slice.Span, bottomWall.Span, fillValue, maskNDV);
			Memory<TOut> sliceSpan = initMask.Slice(tileSize, tileSize);
			Memory<TOut> rightWall = newMask.Slice(tileSize, tileSize);
			bool compareWipePerimeterFlags2 = CompareWipePerimeterFlags(sliceSpan.Span, rightWall.Span, fillValue, maskNDV);
			Memory<TOut> slice2 = initMask.Slice(tileSize * 2, tileSize);
			Memory<TOut> leftWall = newMask.Slice(tileSize * 2, tileSize);
			bool compareWipePerimeterFlags3 = CompareWipePerimeterFlags(slice2.Span, leftWall.Span, fillValue, maskNDV);
			Memory<TOut> slice3 = initMask.Slice(tileSize * 3, tileSize);
			Memory<TOut> topWall = newMask.Slice(tileSize * 3, tileSize);
			bool compareWipePerimeterFlags4 = CompareWipePerimeterFlags(slice3.Span, topWall.Span, fillValue, maskNDV);
			Pixel pixel = worldTileDef.CellIndex2D(tIdx);
			if (compareWipePerimeterFlags && worldTileDef.InRange(pixel.Top()))
			{
				int tileIndex2 = worldTileDef.CellIndex(pixel.Top());
				AddWork(tileIndex2, new TileFillWork<TOut>
				{
					TileIndex = tileIndex2,
					BottomWall = bottomWall
				});
			}
			if (compareWipePerimeterFlags2 && worldTileDef.InRange(pixel.Left()))
			{
				int tileIndex3 = worldTileDef.CellIndex(pixel.Left());
				AddWork(tileIndex3, new TileFillWork<TOut>
				{
					TileIndex = tileIndex3,
					RightWall = rightWall
				});
			}
			if (compareWipePerimeterFlags3 && worldTileDef.InRange(pixel.Right()))
			{
				int tileIndex4 = worldTileDef.CellIndex(pixel.Right());
				AddWork(tileIndex4, new TileFillWork<TOut>
				{
					TileIndex = tileIndex4,
					LeftWall = leftWall
				});
			}
			if (compareWipePerimeterFlags4 && worldTileDef.InRange(pixel.Bottom()))
			{
				int tileIndex5 = worldTileDef.CellIndex(pixel.Bottom());
				AddWork(tileIndex5, new TileFillWork<TOut>
				{
					TileIndex = tileIndex5,
					TopWall = topWall
				});
			}
		}
	}

	private static void BuildPerimeterFlag<T>(RasterDefinition raster, ReadOnlyMemory<T> data, Memory<T> perimeter)
	{
		int rows = raster.Rows;
		ReadOnlySpan<T> buffer = data.Span;
		Span<T> perimeterSpan = perimeter.Span;
		buffer.Slice(0, rows).CopyTo(perimeterSpan);
		for (int i = 0; i < rows; i++)
		{
			perimeterSpan[rows + i] = buffer[i * rows];
			perimeterSpan[rows * 2 + i] = buffer[(i + 1) * rows - 1];
		}
		buffer.Slice(rows * (rows - 1), rows).CopyTo(perimeterSpan.Slice(rows * 3, rows));
	}

	private static bool CompareWipePerimeterFlags<T>(Span<T> prev, Span<T> next, T fillValue, T NDV)
	{
		int length = prev.Length;
		bool result = false;
		for (int i = 0; i < length; i++)
		{
			T prev2 = prev[i];
			T next2 = next[i];
			if (!prev2.Equals(fillValue) && next2.Equals(fillValue))
			{
				result = true;
			}
			else
			{
				next[i] = NDV;
			}
		}
		return result;
	}

	public static void ScanlineFill<TIn, TOut>(RasterDefinition raster, Memory<TIn> baseData, Pixel seed, SelfKernel<TIn, bool> shouldFill, Memory<TOut> fillMask, TOut fillValue) where TIn : unmanaged, IEquatable<TIn> where TOut : unmanaged, IEquatable<TOut>
	{
		Span<TIn> baseDataSpan = baseData.Span;
		Span<TOut> fillMaskSpan = fillMask.Span;
		int index = raster.CellIndex(seed);
		if (fillMaskSpan[index].Equals(fillValue) || !shouldFill(baseDataSpan[index]))
		{
			return;
		}
		fillMaskSpan[index] = fillValue;
		int rows = raster.Rows;
		int cols = raster.Cols;
		Stack<FFSegment> ffSegments = new Stack<FFSegment>();
		ffSegments.Push(new FFSegment(seed.X, seed.X + 1, seed.Y, 0, scanLeft: true, scanRight: true));
		do
		{
			FFSegment fFSegment = ffSegments.Pop();
			int start = fFSegment.Y * cols;
			Span<TIn> slice = baseDataSpan.Slice(start, cols);
			Span<TOut> sliceSpan = fillMaskSpan.Slice(start, cols);
			int startX = fFSegment.StartX;
			if (fFSegment.ScanLeft)
			{
				while (startX > 0 && shouldFill(slice[startX - 1]))
				{
					sliceSpan[--startX] = fillValue;
				}
			}
			int endX = fFSegment.EndX;
			if (fFSegment.ScanRight)
			{
				while (endX < cols && shouldFill(slice[endX]))
				{
					sliceSpan[endX++] = fillValue;
				}
			}
			fFSegment.StartX--;
			fFSegment.EndX++;
			if (fFSegment.Y > 0)
			{
				int fFSegmentTerm = fFSegment.Y - 1;
				int start2 = cols * fFSegmentTerm;
				AddLine(baseDataSpan.Slice(start2), ffSegments, startX, endX, fFSegmentTerm, fFSegment.StartX, fFSegment.EndX, -1, fFSegment.Direction <= 0, shouldFill, fillMaskSpan.Slice(start2, cols), fillValue);
			}
			if (fFSegment.Y < rows - 1)
			{
				int fFSegmentTerm2 = fFSegment.Y + 1;
				int start3 = cols * fFSegmentTerm2;
				AddLine(baseDataSpan.Slice(start3), ffSegments, startX, endX, fFSegmentTerm2, fFSegment.StartX, fFSegment.EndX, 1, fFSegment.Direction >= 0, shouldFill, fillMaskSpan.Slice(start3, cols), fillValue);
			}
		}
		while (ffSegments.Count != 0);
	}

	private static void AddLine<TIn, TOut>(Span<TIn> inputLine, Stack<FFSegment> ffSegments, int startX, int endX, int y, int ignoreStart, int ignoreEnd, sbyte dir, bool isNextInDir, SelfKernel<TIn, bool> shouldFill, Span<TOut> maskLine, TOut fillValue) where TIn : unmanaged, IEquatable<TIn> where TOut : unmanaged, IEquatable<TOut>
	{
		int unity = -1;
		int i;
		for (i = startX; i < endX; i++)
		{
			if ((isNextInDir || i < ignoreStart || i >= ignoreEnd) && shouldFill(inputLine[i]) && !maskLine[i].Equals(fillValue))
			{
				maskLine[i] = fillValue;
				if (unity < 0)
				{
					unity = i;
				}
			}
			else if (unity >= 0)
			{
				ffSegments.Push(new FFSegment(unity, i, y, dir, unity == startX, scanRight: false));
				unity = -1;
			}
			if (!isNextInDir && i < ignoreEnd && i >= ignoreStart)
			{
				i = ignoreEnd - 1;
			}
		}
		if (unity >= 0)
		{
			ffSegments.Push(new FFSegment(unity, i, y, dir, unity == startX, scanRight: true));
		}
	}

	private static void AdamFloodFill(bool[,] boolValues, int x, int y)
	{
		if (!boolValues[y, x])
		{
			AdamFillInternal(boolValues, x, y, boolValues.GetLength(1), boolValues.GetLength(0));
		}
	}

	private static void AdamFillInternal(bool[,] boolValues, int x, int y, int width, int height)
	{
		int ordinal;
		int ordinal2;
		do
		{
			ordinal = x;
			ordinal2 = y;
			while (y != 0 && !boolValues[y - 1, x])
			{
				y--;
			}
			while (x != 0 && !boolValues[y, x - 1])
			{
				x--;
			}
		}
		while (x != ordinal || y != ordinal2);
		AdamFillCore(boolValues, x, y, width, height);
	}

	private static void AdamFillCore(bool[,] boolValues, int x, int y, int width, int height)
	{
		int ordinal = 0;
		do
		{
			int ordinal2 = 0;
			int i = x;
			if (ordinal != 0 && boolValues[y, x])
			{
				do
				{
					if (--ordinal == 0)
					{
						return;
					}
				}
				while (boolValues[y, ++x]);
				i = x;
			}
			else
			{
				while (x != 0 && !boolValues[y, x - 1])
				{
					boolValues[y, --x] = true;
					if (y != 0 && !boolValues[y - 1, x])
					{
						AdamFillInternal(boolValues, x, y - 1, width, height);
					}
					ordinal2++;
					ordinal++;
				}
			}
			for (; i < width && !boolValues[y, i]; i++)
			{
				boolValues[y, i] = true;
				ordinal2++;
			}
			if (ordinal2 < ordinal)
			{
				int ordinal3 = x + ordinal;
				while (++i < ordinal3)
				{
					if (!boolValues[y, i])
					{
						AdamFillCore(boolValues, i, y, width, height);
					}
				}
			}
			else if (ordinal2 > ordinal && y != 0)
			{
				int ordinal4 = x + ordinal;
				while (++ordinal4 < i)
				{
					if (!boolValues[y - 1, ordinal4])
					{
						AdamFillInternal(boolValues, ordinal4, y - 1, width, height);
					}
				}
			}
			ordinal = ordinal2;
		}
		while (ordinal != 0 && ++y < height);
	}

	public static void ProcessKernel<TIn, TOut>(RasterDefinition sourceRaster, Memory<TIn> data, TIn borderFillValue, int windowSize, Memory<TOut> result, WindowKernel<TIn, TOut> kernel) where TIn : unmanaged, IEquatable<TIn> where TOut : unmanaged
	{
		if (windowSize % 2 != 1)
		{
			throw new ArgumentException("windowSize must be odd.");
		}
		if (data.Length != sourceRaster.CellCount)
		{
			throw new ArgumentException("sourceRaster size mismatch with data");
		}
		if (result.Length != data.Length)
		{
			throw new ArgumentException("sourceRaster size mismatch with result");
		}
		Span<TIn> tIns = data.Span;
		Span<TOut> tOuts = result.Span;
		Span<TIn> tInsSpan = ((windowSize <= 16) ? stackalloc TIn[windowSize * windowSize] : ((Span<TIn>)new TIn[windowSize * windowSize]));
		int cols = sourceRaster.Cols;
		int rows = sourceRaster.Rows;
		int windowSizeTerm = (windowSize - 1) / 2;
		for (int i = 0; i < rows; i++)
		{
			for (int j = 0; j < cols; j++)
			{
				for (int k = 0; k < windowSize; k++)
				{
					int ordinal = i + k - windowSizeTerm;
					int windowSizeTerm2 = k * windowSize;
					if (ordinal < 0 || ordinal >= rows)
					{
						for (int l = 0; l < windowSize; l++)
						{
							tInsSpan[windowSizeTerm2 + l] = borderFillValue;
						}
						continue;
					}
					int colsTerm = ordinal * cols + j - windowSizeTerm;
					for (int m = 0; m < windowSize; m++)
					{
						int ordinal2 = j + m - windowSizeTerm;
						if (ordinal2 < 0 || ordinal2 >= cols)
						{
							tInsSpan[windowSizeTerm2 + m] = borderFillValue;
						}
						else
						{
							tInsSpan[windowSizeTerm2 + m] = tIns[colsTerm + m];
						}
					}
				}
				tOuts[i * cols + j] = kernel(tInsSpan);
			}
		}
	}

	public static void ProcessKernel<TIn, TOut>(RasterDefinition sourceRaster, Memory<TIn> data, int windowSize, Memory<TOut> result, out RasterDefinition resultRaster, WindowKernel<TIn, TOut> kernel) where TIn : unmanaged, IEquatable<TIn> where TOut : unmanaged
	{
		if (windowSize % 2 != 1)
		{
			throw new ArgumentException("windowSize must be odd.");
		}
		if (data.Length != sourceRaster.CellCount)
		{
			throw new ArgumentException("sourceRaster mismatch with data");
		}
		Span<TIn> tIns = data.Span;
		Span<TOut> tOuts = result.Span;
		int windowSizeTerm = (windowSize - 1) / 2;
		int count = sourceRaster.Rows - 2 * windowSizeTerm;
		int count2 = sourceRaster.Cols - 2 * windowSizeTerm;
		if (result.Length != count2 * count)
		{
			throw new ArgumentException("result does not match expected size: " + count2 * count);
		}
		resultRaster = sourceRaster.GetSubRaster(new PixelRange(windowSizeTerm, sourceRaster.Rows - windowSizeTerm - 1, windowSizeTerm, sourceRaster.Cols - windowSizeTerm - 1));
		Span<TIn> tInsSpan = ((windowSize <= 16) ? stackalloc TIn[windowSize * windowSize] : ((Span<TIn>)new TIn[windowSize * windowSize]));
		for (int i = 0; i < count; i++)
		{
			for (int j = 0; j < count2; j++)
			{
				for (int k = 0; k < windowSize; k++)
				{
					tIns.Slice((i + k) * sourceRaster.Cols + j, windowSize).CopyTo(tInsSpan.Slice(k * windowSize));
				}
				tOuts[i * count2 + j] = kernel(tInsSpan);
			}
		}
	}

	public static void ProcessKernel<TIn, TOut>(ITileProvider<TIn> tileProvider, WindowKernel<TIn, TOut> kernel, ITileWriter<TOut> writer, int suggestedMaxTilesInMemory = int.MaxValue, ProgressReporter prog = null) where TIn : unmanaged, IEquatable<TIn> where TOut : unmanaged, IEquatable<TOut>
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		if (suggestedMaxTilesInMemory < 9)
		{
			throw new ArgumentOutOfRangeException("suggestedMaxTilesInMemory");
		}
		RasterDefinition tileRasterDef = tileProvider.TiledRasterDefinition;
		if (tileRasterDef.Rows != writer.TiledRasterDefinition.Rows || tileRasterDef.Cols != writer.TiledRasterDefinition.Cols)
		{
			throw new ArgumentException("TileProvider/Writer mismatch.");
		}
		int nTileCols = tileProvider.TiledRasterDefinition.Cols;
		int nTileRows = tileProvider.TiledRasterDefinition.Rows;
		int count;
		int rowsPerFront;
		int rowShift;
		if (nTileRows * 3 <= suggestedMaxTilesInMemory)
		{
			count = 1;
			rowsPerFront = nTileRows;
			rowShift = 0;
		}
		else
		{
			int scaledSuggestedMaxTilesInMemory = suggestedMaxTilesInMemory / 3;
			count = (int)Math.Ceiling((double)nTileRows / (double)scaledSuggestedMaxTilesInMemory);
			rowsPerFront = (int)Math.Ceiling((double)nTileRows / (double)count);
			rowShift = 1;
		}
		int count2 = rowsPerFront + 2 * rowShift;
		int tileSize = tileProvider.TileSize;
		int nCellsPerTile = tileSize * tileSize;
		int UL_CELL = 0;
		int UR_CELL = tileSize - 1;
		int LL_CELL = tileSize * (tileSize - 1);
		int LR_CELL = tileSize * tileSize - 1;
		int UL_OFFSET = -tileSize - 1;
		int T_OFFSET = -tileSize;
		_ = tileSize;
		int L_OFFSET = -1;
		int R_OFFSET = 1;
		_ = tileSize;
		int B_OFFSET = tileSize;
		int LR_OFFSET = tileSize + 1;
		TIn NDV = tileProvider.NoDataValue.GetValueOrDefault();
		Memory<TIn> NDV_BUF;
		using (SharedMemoryPool<TIn>.Rent(nCellsPerTile, NDV, out NDV_BUF))
		{
			IMemoryOwner<TIn>[,] tiles = new IMemoryOwner<TIn>[count2, 3];
			for (int i = 0; i < count2; i++)
			{
				for (int j = 0; j < 3; j++)
				{
					tiles[i, j] = SharedMemoryPool<TIn>.RentNoFill(nCellsPerTile, out var _);
				}
			}
			for (int front = 0; front < count; front++)
			{
				int tileRowOffset = front * rowsPerFront;
				tileRowOffset -= rowShift;
				System.Threading.Tasks.Parallel.For(0, count2 * 3, (int subT) =>
				{
					int scaledSubT = subT / 3;
					int subTTerm = subT % 3;
					int tileRowOffsetTerm = scaledSubT + tileRowOffset;
					if (tileRowOffsetTerm < tileProvider.TiledRasterDefinition.Rows)
					{
						int tileIdx = tileRowOffsetTerm * nTileCols + subTTerm;
						Memory<TIn> data = tiles[scaledSubT, subTTerm].Memory.Slice(0, nCellsPerTile);
						if (subTTerm < tileRasterDef.Cols)
						{
							tileProvider.ReadTile(tileIdx, data);
						}
						else
						{
							data.Span.Fill(NDV);
						}
					}
				});
				object progLock = new object();
				int completedTiles = 0;
				for (int tileCol = 0; tileCol < nTileCols; tileCol++)
				{
					int centerC = tileCol % 3;
					int leftC = (tileCol - 1) % 3;
					int rightC = (tileCol + 1) % 3;
					if (tileCol == 0)
					{
						leftC = -1;
					}
					if (tileCol == nTileCols - 1)
					{
						rightC = -1;
					}
					System.Threading.Tasks.Parallel.For(0, rowsPerFront, (int subR) =>
					{
						int frontRowsPerFrontTerm = front * rowsPerFront + subR;
						int tileIdx = frontRowsPerFrontTerm * nTileCols + tileCol;
						Memory<TOut> mem2;
						using (SharedMemoryPool<TOut>.RentNoFill(nCellsPerTile, out mem2))
						{
							Span<TOut> mem2Span = mem2.Span;
							Span<TIn> tIns = Span<TIn>.Empty;
							Span<TIn> tInsSpan = Span<TIn>.Empty;
							Span<TIn> tIns2 = Span<TIn>.Empty;
							Span<TIn> tIns3 = Span<TIn>.Empty;
							Span<TIn> empty = Span<TIn>.Empty;
							Span<TIn> tIns4 = Span<TIn>.Empty;
							Span<TIn> tIns5 = Span<TIn>.Empty;
							Span<TIn> tIns6 = Span<TIn>.Empty;
							Span<TIn> tIns7 = Span<TIn>.Empty;
							Span<TIn> ndV_BUF = NDV_BUF.Span;
							if (tileCol == 0)
							{
								tIns = ndV_BUF;
								tIns3 = ndV_BUF;
								tIns5 = ndV_BUF;
							}
							if (tileCol == nTileCols - 1)
							{
								tIns2 = ndV_BUF;
								tIns4 = ndV_BUF;
								tIns7 = ndV_BUF;
							}
							if (frontRowsPerFrontTerm == 0)
							{
								tIns = ndV_BUF;
								tInsSpan = ndV_BUF;
								tIns2 = ndV_BUF;
							}
							if (frontRowsPerFrontTerm >= nTileRows - 1)
							{
								tIns5 = ndV_BUF;
								tIns6 = ndV_BUF;
								tIns7 = ndV_BUF;
							}
							int subRPlusRowShift = subR + rowShift;
							Memory<TIn> tIns8 = tiles[subRPlusRowShift, centerC].Memory;
							tIns8 = tIns8.Slice(0, nCellsPerTile);
							empty = tIns8.Span;
							int ordinal = subRPlusRowShift - 1;
							int ordinal2 = subRPlusRowShift + 1;
							if (tIns.IsEmpty)
							{
								tIns8 = tiles[ordinal, leftC].Memory;
								tIns8 = tIns8.Slice(0, nCellsPerTile);
								tIns = tIns8.Span;
							}
							if (tInsSpan.IsEmpty)
							{
								tIns8 = tiles[ordinal, centerC].Memory;
								tIns8 = tIns8.Slice(0, nCellsPerTile);
								tInsSpan = tIns8.Span;
							}
							if (tIns2.IsEmpty)
							{
								tIns8 = tiles[ordinal, rightC].Memory;
								tIns8 = tIns8.Slice(0, nCellsPerTile);
								tIns2 = tIns8.Span;
							}
							if (tIns3.IsEmpty)
							{
								tIns8 = tiles[subRPlusRowShift, leftC].Memory;
								tIns8 = tIns8.Slice(0, nCellsPerTile);
								tIns3 = tIns8.Span;
							}
							if (tIns4.IsEmpty)
							{
								tIns8 = tiles[subRPlusRowShift, rightC].Memory;
								tIns8 = tIns8.Slice(0, nCellsPerTile);
								tIns4 = tIns8.Span;
							}
							if (tIns5.IsEmpty)
							{
								tIns8 = tiles[ordinal2, leftC].Memory;
								tIns8 = tIns8.Slice(0, nCellsPerTile);
								tIns5 = tIns8.Span;
							}
							if (tIns6.IsEmpty)
							{
								tIns8 = tiles[ordinal2, centerC].Memory;
								tIns8 = tIns8.Slice(0, nCellsPerTile);
								tIns6 = tIns8.Span;
							}
							if (tIns7.IsEmpty)
							{
								tIns8 = tiles[ordinal2, rightC].Memory;
								tIns8 = tIns8.Slice(0, nCellsPerTile);
								tIns7 = tIns8.Span;
							}
							Span<TIn> data = stackalloc TIn[9];
							data[0] = tIns[LR_CELL];
							data[1] = tInsSpan[LL_CELL];
							data[2] = tInsSpan[LL_CELL + R_OFFSET];
							data[3] = tIns3[UR_CELL];
							data[4] = empty[0];
							data[5] = empty[1];
							data[6] = tIns3[UR_CELL + B_OFFSET];
							data[7] = empty[B_OFFSET];
							data[8] = empty[LR_OFFSET];
							mem2Span[0] = kernel(data);
							for (int k = 1; k < tileSize - 1; k++)
							{
								data[0] = tInsSpan[LL_CELL + k - 1];
								data[1] = tInsSpan[LL_CELL + k];
								data[2] = tInsSpan[LL_CELL + k + 1];
								data[3] = empty[k - 1];
								data[4] = empty[k];
								data[5] = empty[k + 1];
								data[6] = empty[k + tileSize - 1];
								data[7] = empty[k + tileSize];
								data[8] = empty[k + tileSize + 1];
								mem2Span[k] = kernel(data);
							}
							data[0] = tInsSpan[LR_CELL - 1];
							data[1] = tInsSpan[LR_CELL];
							data[2] = tIns2[LL_CELL];
							data[3] = empty[UR_CELL - 1];
							data[4] = empty[UR_CELL];
							data[5] = tIns4[UL_CELL];
							data[6] = empty[UR_CELL + tileSize - 1];
							data[7] = empty[UR_CELL + tileSize];
							data[8] = tIns4[UL_CELL + tileSize];
							mem2Span[UR_CELL] = kernel(data);
							for (int l = 1; l < tileSize - 1; l++)
							{
								data[0] = tIns3[UR_CELL + (l - 1) * tileSize];
								data[1] = empty[(l - 1) * tileSize];
								data[2] = empty[(l - 1) * tileSize + 1];
								data[3] = tIns3[UR_CELL + l * tileSize];
								data[4] = empty[l * tileSize];
								data[5] = empty[l * tileSize + 1];
								data[6] = tIns3[UR_CELL + (l + 1) * tileSize];
								data[7] = empty[(l + 1) * tileSize];
								data[8] = empty[(l + 1) * tileSize + 1];
								mem2Span[l * tileSize] = kernel(data);
							}
							for (int m = 1; m < tileSize - 1; m++)
							{
								data[0] = empty[UR_CELL + (m - 1) * tileSize - 1];
								data[1] = empty[UR_CELL + (m - 1) * tileSize];
								data[2] = tIns4[(m - 1) * tileSize];
								data[3] = empty[UR_CELL + m * tileSize - 1];
								data[4] = empty[UR_CELL + m * tileSize];
								data[5] = tIns4[m * tileSize];
								data[6] = empty[UR_CELL + (m + 1) * tileSize - 1];
								data[7] = empty[UR_CELL + (m + 1) * tileSize];
								data[8] = tIns4[(m + 1) * tileSize];
								mem2Span[m * tileSize + tileSize - 1] = kernel(data);
							}
							for (int n = 1; n < tileSize - 1; n++)
							{
								for (int unity = 1; unity < tileSize - 1; unity++)
								{
									int tileSizeTerm = n * tileSize + unity;
									data[0] = empty[tileSizeTerm - tileSize - 1];
									data[1] = empty[tileSizeTerm - tileSize];
									data[2] = empty[tileSizeTerm - tileSize + 1];
									data[3] = empty[tileSizeTerm - 1];
									data[4] = empty[tileSizeTerm];
									data[5] = empty[tileSizeTerm + 1];
									data[6] = empty[tileSizeTerm + tileSize - 1];
									data[7] = empty[tileSizeTerm + tileSize];
									data[8] = empty[tileSizeTerm + tileSize + 1];
									mem2Span[tileSizeTerm] = kernel(data);
								}
							}
							data[0] = tIns3[LR_CELL - tileSize];
							data[1] = empty[LL_CELL - tileSize];
							data[2] = empty[LL_CELL - tileSize + 1];
							data[3] = tIns3[LR_CELL];
							data[4] = empty[LL_CELL];
							data[5] = empty[LL_CELL + 1];
							data[6] = tIns5[UR_CELL];
							data[7] = tIns6[0];
							data[8] = tIns6[1];
							mem2Span[LL_CELL] = kernel(data);
							for (int unity2 = 1; unity2 < tileSize - 1; unity2++)
							{
								data[0] = empty[LL_CELL - tileSize + unity2 - 1];
								data[1] = empty[LL_CELL - tileSize + unity2];
								data[2] = empty[LL_CELL - tileSize + unity2 + 1];
								data[3] = empty[LL_CELL + unity2 - 1];
								data[4] = empty[LL_CELL + unity2];
								data[5] = empty[LL_CELL + unity2 + 1];
								data[6] = tIns6[unity2 - 1];
								data[7] = tIns6[unity2];
								data[8] = tIns6[unity2 + 1];
								mem2Span[LL_CELL + unity2] = kernel(data);
							}
							data[0] = empty[LR_CELL + UL_OFFSET];
							data[1] = empty[LR_CELL + T_OFFSET];
							data[2] = tIns4[LL_CELL + T_OFFSET];
							data[3] = empty[LR_CELL + L_OFFSET];
							data[4] = empty[LR_CELL];
							data[5] = tIns4[LL_CELL];
							data[6] = tIns6[UR_CELL + L_OFFSET];
							data[7] = tIns6[UR_CELL];
							data[8] = tIns7[0];
							mem2Span[LR_CELL] = kernel(data);
							writer.WriteTile(tileIdx, mem2);
							lock (progLock)
							{
								int completedTiles2 = completedTiles;
								completedTiles = completedTiles2 + 1;
								prog.ReportProgress((int)(100f * (float)completedTiles / (float)tileRasterDef.CellCount));
							}
						}
					});
					if (leftC == -1)
					{
						continue;
					}
					System.Threading.Tasks.Parallel.For(0, count2, (int subR) =>
					{
						int subRPlusTileRowOffset = subR + tileRowOffset;
						if (subRPlusTileRowOffset < tileProvider.TiledRasterDefinition.Rows)
						{
							int tileColOffset = tileCol + 2;
							if (tileColOffset < tileProvider.TiledRasterDefinition.Cols)
							{
								int tileIdx = subRPlusTileRowOffset * nTileCols + tileColOffset;
								Memory<TIn> data = tiles[subR, leftC].Memory.Slice(0, nCellsPerTile);
								tileProvider.ReadTile(tileIdx, data);
							}
						}
					});
				}
			}
			for (int intervalIndex = 0; intervalIndex < count2; intervalIndex++)
			{
				for (int intervalIndex2 = 0; intervalIndex2 < 3; intervalIndex2++)
				{
					tiles[intervalIndex, intervalIndex2]?.Dispose();
				}
			}
		}
	}

	public static Pixel Scan<T>(RasterDefinition raster, Memory<T> data, T localValue) where T : unmanaged, IEquatable<T>
	{
		Span<T> buffer = data.Span;
		for (int i = 0; i < raster.Rows; i++)
		{
			for (int j = 0; j < raster.Cols; j++)
			{
				T colsRasterTerm = buffer[i * raster.Cols + j];
				if (colsRasterTerm.Equals(localValue))
				{
					return new Pixel(i, j);
				}
			}
		}
		return Pixel.NullPixel;
	}

	public static Pixel Scan<T>(RasterDefinition raster, Memory<T> data, SelfKernel<T, bool> match) where T : unmanaged, IEquatable<T>
	{
		Span<T> buffer = data.Span;
		for (int i = 0; i < raster.Rows; i++)
		{
			for (int j = 0; j < raster.Cols; j++)
			{
				T colsRasterTerm = buffer[i * raster.Cols + j];
				if (match(colsRasterTerm))
				{
					return new Pixel(i, j);
				}
			}
		}
		return Pixel.NullPixel;
	}

	public static void ScanAll<T>(RasterDefinition raster, Memory<T> data, SelfKernel<T, bool> match, Action<Pixel, T> onMatch) where T : unmanaged, IEquatable<T>
	{
		Span<T> buffer = data.Span;
		for (int i = 0; i < raster.Rows; i++)
		{
			for (int j = 0; j < raster.Cols; j++)
			{
				T colsRasterTerm = buffer[i * raster.Cols + j];
				if (match(colsRasterTerm))
				{
					onMatch(new Pixel(i, j), colsRasterTerm);
				}
			}
		}
	}

	public static List<int>[] Scan<TIn>(ITileProvider<TIn> tileProvider, WindowKernel<TIn, bool> kernel, int suggestedMaxTilesInMemory = int.MaxValue, ProgressReporter prog = null) where TIn : unmanaged, IEquatable<TIn>
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		if (suggestedMaxTilesInMemory < 9)
		{
			suggestedMaxTilesInMemory = 9;
		}
		RasterDefinition tileRasterDef = tileProvider.TiledRasterDefinition;
		int nTileCols = tileProvider.TiledRasterDefinition.Cols;
		int nTileRows = tileProvider.TiledRasterDefinition.Rows;
		List<int>[] retn = new List<int>[nTileCols * nTileRows];
		int count;
		int rowsPerFront;
		int rowShift;
		if (nTileRows * 3 <= suggestedMaxTilesInMemory)
		{
			count = 1;
			rowsPerFront = nTileRows;
			rowShift = 0;
		}
		else
		{
			int scaledSuggestedMaxTilesInMemory = suggestedMaxTilesInMemory / 3;
			count = (int)Math.Ceiling((double)nTileRows / (double)scaledSuggestedMaxTilesInMemory);
			rowsPerFront = (int)Math.Ceiling((double)nTileRows / (double)count);
			rowShift = 1;
		}
		int count2 = rowsPerFront + 2 * rowShift;
		int tileSize = tileProvider.TileSize;
		int nCellsPerTile = tileSize * tileSize;
		int UL_CELL = 0;
		int UR_CELL = tileSize - 1;
		int LL_CELL = tileSize * (tileSize - 1);
		int LR_CELL = tileSize * tileSize - 1;
		int UL_OFFSET = -tileSize - 1;
		int T_OFFSET = -tileSize;
		_ = tileSize;
		int L_OFFSET = -1;
		int R_OFFSET = 1;
		_ = tileSize;
		int B_OFFSET = tileSize;
		int LR_OFFSET = tileSize + 1;
		TIn NDV = tileProvider.NoDataValue.GetValueOrDefault();
		Memory<TIn> NDV_BUF;
		using (SharedMemoryPool<TIn>.Rent(nCellsPerTile, out NDV_BUF))
		{
			IMemoryOwner<TIn>[,] tiles = new IMemoryOwner<TIn>[count2, 3];
			for (int i = 0; i < count2; i++)
			{
				for (int j = 0; j < 3; j++)
				{
					tiles[i, j] = SharedMemoryPool<TIn>.RentNoFill(nCellsPerTile, out var _);
				}
			}
			for (int front = 0; front < count; front++)
			{
				int tileRowOffset = front * rowsPerFront;
				tileRowOffset -= rowShift;
				System.Threading.Tasks.Parallel.For(0, count2 * 3, (int subT) =>
				{
					int scaledSubT = subT / 3;
					int subTTerm = subT % 3;
					int tileRowOffsetTerm = scaledSubT + tileRowOffset;
					if (tileRowOffsetTerm < tileProvider.TiledRasterDefinition.Rows)
					{
						int tileIdx = tileRowOffsetTerm * nTileCols + subTTerm;
						Memory<TIn> data = tiles[scaledSubT, subTTerm].Memory.Slice(0, nCellsPerTile);
						if (subTTerm < tileRasterDef.Cols)
						{
							tileProvider.ReadTile(tileIdx, data);
						}
						else
						{
							data.Span.Fill(NDV);
						}
					}
				});
				object progLock = new object();
				int completedTiles = 0;
				for (int tileCol = 0; tileCol < nTileCols; tileCol++)
				{
					int centerC = tileCol % 3;
					int leftC = (tileCol - 1) % 3;
					int rightC = (tileCol + 1) % 3;
					if (tileCol == 0)
					{
						leftC = -1;
					}
					if (tileCol == nTileCols - 1)
					{
						rightC = -1;
					}
					System.Threading.Tasks.Parallel.For(0, rowsPerFront, (int subR) =>
					{
						int frontRowsPerFrontTerm = front * rowsPerFront + subR;
						int tIdx = frontRowsPerFrontTerm * nTileCols + tileCol;
						Span<TIn> tIns = Span<TIn>.Empty;
						Span<TIn> tInsSpan = Span<TIn>.Empty;
						Span<TIn> tIns2 = Span<TIn>.Empty;
						Span<TIn> tIns3 = Span<TIn>.Empty;
						Span<TIn> empty = Span<TIn>.Empty;
						Span<TIn> tIns4 = Span<TIn>.Empty;
						Span<TIn> tIns5 = Span<TIn>.Empty;
						Span<TIn> tIns6 = Span<TIn>.Empty;
						Span<TIn> tIns7 = Span<TIn>.Empty;
						Span<TIn> ndV_BUF = NDV_BUF.Span;
						if (tileCol == 0)
						{
							tIns = ndV_BUF;
							tIns3 = ndV_BUF;
							tIns5 = ndV_BUF;
						}
						if (tileCol == nTileCols - 1)
						{
							tIns2 = ndV_BUF;
							tIns4 = ndV_BUF;
							tIns7 = ndV_BUF;
						}
						if (frontRowsPerFrontTerm == 0)
						{
							tIns = ndV_BUF;
							tInsSpan = ndV_BUF;
							tIns2 = ndV_BUF;
						}
						if (frontRowsPerFrontTerm >= nTileRows - 1)
						{
							tIns5 = ndV_BUF;
							tIns6 = ndV_BUF;
							tIns7 = ndV_BUF;
						}
						int subRPlusRowShift = subR + rowShift;
						Memory<TIn> tIns8 = tiles[subRPlusRowShift, centerC].Memory;
						tIns8 = tIns8.Slice(0, nCellsPerTile);
						empty = tIns8.Span;
						int ordinal = subRPlusRowShift - 1;
						int ordinal2 = subRPlusRowShift + 1;
						if (tIns.IsEmpty)
						{
							tIns8 = tiles[ordinal, leftC].Memory;
							tIns8 = tIns8.Slice(0, nCellsPerTile);
							tIns = tIns8.Span;
						}
						if (tInsSpan.IsEmpty)
						{
							tIns8 = tiles[ordinal, centerC].Memory;
							tIns8 = tIns8.Slice(0, nCellsPerTile);
							tInsSpan = tIns8.Span;
						}
						if (tIns2.IsEmpty)
						{
							tIns8 = tiles[ordinal, rightC].Memory;
							tIns8 = tIns8.Slice(0, nCellsPerTile);
							tIns2 = tIns8.Span;
						}
						if (tIns3.IsEmpty)
						{
							tIns8 = tiles[subRPlusRowShift, leftC].Memory;
							tIns8 = tIns8.Slice(0, nCellsPerTile);
							tIns3 = tIns8.Span;
						}
						if (tIns4.IsEmpty)
						{
							tIns8 = tiles[subRPlusRowShift, rightC].Memory;
							tIns8 = tIns8.Slice(0, nCellsPerTile);
							tIns4 = tIns8.Span;
						}
						if (tIns5.IsEmpty)
						{
							tIns8 = tiles[ordinal2, leftC].Memory;
							tIns8 = tIns8.Slice(0, nCellsPerTile);
							tIns5 = tIns8.Span;
						}
						if (tIns6.IsEmpty)
						{
							tIns8 = tiles[ordinal2, centerC].Memory;
							tIns8 = tIns8.Slice(0, nCellsPerTile);
							tIns6 = tIns8.Span;
						}
						if (tIns7.IsEmpty)
						{
							tIns8 = tiles[ordinal2, rightC].Memory;
							tIns8 = tIns8.Slice(0, nCellsPerTile);
							tIns7 = tIns8.Span;
						}
						Span<TIn> data = stackalloc TIn[9];
						data[0] = tIns[LR_CELL];
						data[1] = tInsSpan[LL_CELL];
						data[2] = tInsSpan[LL_CELL + R_OFFSET];
						data[3] = tIns3[UR_CELL];
						data[4] = empty[0];
						data[5] = empty[1];
						data[6] = tIns3[UR_CELL + B_OFFSET];
						data[7] = empty[B_OFFSET];
						data[8] = empty[LR_OFFSET];
						if (kernel(data))
						{
							GetTileList(tIdx).Add(0);
						}
						for (int k = 1; k < tileSize - 1; k++)
						{
							data[0] = tInsSpan[LL_CELL + k - 1];
							data[1] = tInsSpan[LL_CELL + k];
							data[2] = tInsSpan[LL_CELL + k + 1];
							data[3] = empty[k - 1];
							data[4] = empty[k];
							data[5] = empty[k + 1];
							data[6] = empty[k + tileSize - 1];
							data[7] = empty[k + tileSize];
							data[8] = empty[k + tileSize + 1];
							if (kernel(data))
							{
								GetTileList(tIdx).Add(k);
							}
						}
						data[0] = tInsSpan[LR_CELL - 1];
						data[1] = tInsSpan[LR_CELL];
						data[2] = tIns2[LL_CELL];
						data[3] = empty[UR_CELL - 1];
						data[4] = empty[UR_CELL];
						data[5] = tIns4[UL_CELL];
						data[6] = empty[UR_CELL + tileSize - 1];
						data[7] = empty[UR_CELL + tileSize];
						data[8] = tIns4[UL_CELL + tileSize];
						if (kernel(data))
						{
							GetTileList(tIdx).Add(UR_CELL);
						}
						for (int l = 1; l < tileSize - 1; l++)
						{
							data[0] = tIns3[UR_CELL + (l - 1) * tileSize];
							data[1] = empty[(l - 1) * tileSize];
							data[2] = empty[(l - 1) * tileSize + 1];
							data[3] = tIns3[UR_CELL + l * tileSize];
							data[4] = empty[l * tileSize];
							data[5] = empty[l * tileSize + 1];
							data[6] = tIns3[UR_CELL + (l + 1) * tileSize];
							data[7] = empty[(l + 1) * tileSize];
							data[8] = empty[(l + 1) * tileSize + 1];
							if (kernel(data))
							{
								GetTileList(tIdx).Add(l * tileSize);
							}
						}
						for (int m = 1; m < tileSize - 1; m++)
						{
							data[0] = empty[UR_CELL + (m - 1) * tileSize - 1];
							data[1] = empty[UR_CELL + (m - 1) * tileSize];
							data[2] = tIns4[(m - 1) * tileSize];
							data[3] = empty[UR_CELL + m * tileSize - 1];
							data[4] = empty[UR_CELL + m * tileSize];
							data[5] = tIns4[m * tileSize];
							data[6] = empty[UR_CELL + (m + 1) * tileSize - 1];
							data[7] = empty[UR_CELL + (m + 1) * tileSize];
							data[8] = tIns4[(m + 1) * tileSize];
							if (kernel(data))
							{
								GetTileList(tIdx).Add(m * tileSize + tileSize - 1);
							}
						}
						for (int n = 1; n < tileSize - 1; n++)
						{
							for (int unity = 1; unity < tileSize - 1; unity++)
							{
								int tileSizeTerm = n * tileSize + unity;
								data[0] = empty[tileSizeTerm - tileSize - 1];
								data[1] = empty[tileSizeTerm - tileSize];
								data[2] = empty[tileSizeTerm - tileSize + 1];
								data[3] = empty[tileSizeTerm - 1];
								data[4] = empty[tileSizeTerm];
								data[5] = empty[tileSizeTerm + 1];
								data[6] = empty[tileSizeTerm + tileSize - 1];
								data[7] = empty[tileSizeTerm + tileSize];
								data[8] = empty[tileSizeTerm + tileSize + 1];
								if (kernel(data))
								{
									GetTileList(tIdx).Add(tileSizeTerm);
								}
							}
						}
						data[0] = tIns3[LR_CELL - tileSize];
						data[1] = empty[LL_CELL - tileSize];
						data[2] = empty[LL_CELL - tileSize + 1];
						data[3] = tIns3[LR_CELL];
						data[4] = empty[LL_CELL];
						data[5] = empty[LL_CELL + 1];
						data[6] = tIns5[UR_CELL];
						data[7] = tIns6[0];
						data[8] = tIns6[1];
						if (kernel(data))
						{
							GetTileList(tIdx).Add(LL_CELL);
						}
						for (int unity2 = 1; unity2 < tileSize - 1; unity2++)
						{
							data[0] = empty[LL_CELL - tileSize + unity2 - 1];
							data[1] = empty[LL_CELL - tileSize + unity2];
							data[2] = empty[LL_CELL - tileSize + unity2 + 1];
							data[3] = empty[LL_CELL + unity2 - 1];
							data[4] = empty[LL_CELL + unity2];
							data[5] = empty[LL_CELL + unity2 + 1];
							data[6] = tIns6[unity2 - 1];
							data[7] = tIns6[unity2];
							data[8] = tIns6[unity2 + 1];
							if (kernel(data))
							{
								GetTileList(tIdx).Add(LL_CELL + unity2);
							}
						}
						data[0] = empty[LR_CELL + UL_OFFSET];
						data[1] = empty[LR_CELL + T_OFFSET];
						data[2] = tIns4[LL_CELL + T_OFFSET];
						data[3] = empty[LR_CELL + L_OFFSET];
						data[4] = empty[LR_CELL];
						data[5] = tIns4[LL_CELL];
						data[6] = tIns6[UR_CELL + L_OFFSET];
						data[7] = tIns6[UR_CELL];
						data[8] = tIns7[0];
						if (kernel(data))
						{
							GetTileList(tIdx).Add(LR_CELL);
						}
						lock (progLock)
						{
							int completedTiles2 = completedTiles;
							completedTiles = completedTiles2 + 1;
							prog.ReportProgress((int)(100f * (float)completedTiles / (float)tileRasterDef.CellCount));
						}
					});
					if (leftC == -1)
					{
						continue;
					}
					System.Threading.Tasks.Parallel.For(0, count2, (int subR) =>
					{
						int subRPlusTileRowOffset = subR + tileRowOffset;
						if (subRPlusTileRowOffset < tileProvider.TiledRasterDefinition.Rows)
						{
							int tileColOffset = tileCol + 2;
							if (tileColOffset < tileProvider.TiledRasterDefinition.Cols)
							{
								int tileIdx = subRPlusTileRowOffset * nTileCols + tileColOffset;
								Memory<TIn> data = tiles[subR, leftC].Memory.Slice(0, nCellsPerTile);
								tileProvider.ReadTile(tileIdx, data);
							}
						}
					});
				}
			}
			for (int intervalIndex = 0; intervalIndex < count2; intervalIndex++)
			{
				for (int intervalIndex2 = 0; intervalIndex2 < 3; intervalIndex2++)
				{
					tiles[intervalIndex, intervalIndex2]?.Dispose();
				}
			}
			return retn;
		}
		List<int> GetTileList(int tIdx)
		{
			List<int> retnList = retn[tIdx];
			if (retnList == null)
			{
				retnList = new List<int>();
				retn[tIdx] = retnList;
			}
			return retnList;
		}
	}

	public static void ProcessTiled<T>(IRaster<T> raster, Extent desiredExtent, bool clipTilesToDesiredExtent, Action<RasterDefinition, Memory<T>> processAction, ParallelizationPlan parallelization = default(ParallelizationPlan)) where T : unmanaged, IEquatable<T>
	{
		RasterDefinition subRaster = raster.RasterDefinition.GetSubRaster(desiredExtent);
		if (subRaster.IsNull())
		{
			return;
		}
		if (raster is TiledRaster<T> tiledRaster)
		{
			ProcessTiles(tiledRaster.TileProvider, desiredExtent, clipTilesToDesiredExtent, (long i, RasterDefinition rd, Memory<T> buf) =>
			{
				processAction(rd, buf);
			}, parallelization);
		}
		else if (subRaster.CellCount <= 1048576)
		{
			processAction(subRaster, raster.Resample(new ResampleArgs(subRaster)));
		}
		else
		{
			ProcessTiles(raster.AsTileProvider(), desiredExtent, clipTilesToDesiredExtent, (long i, RasterDefinition rd, Memory<T> buf) =>
			{
				processAction(rd, buf);
			}, parallelization);
		}
	}

	public static void ProcessTiles<T>(ITileProvider<T> itr, Extent desiredExtent, bool clipTilesToDesiredExtent, Action<long, RasterDefinition, Memory<T>> processAction, ParallelizationPlan parallelization = default(ParallelizationPlan)) where T : unmanaged, IEquatable<T>
	{
		RasterDefinition worldTileDef = itr.TiledRasterDefinition;
		PixelRange tileRange = worldTileDef.GetPixelRange(desiredExtent);
		if (tileRange.CellCount == 0)
		{
			return;
		}
		itr.GetTileRasterDefinition(0L);
		int tilePxCount = itr.BlockSize.PixelCount;
		if (parallelization.ShouldParallelize(tileRange.CellCount) && !Constants.DEBUG_SERIALIZE_READS)
		{
			int tIdx = -1;
			System.Threading.Tasks.Parallel.For(0, tileRange.CellCount, Utility.Parallel.GetReasonableParallelOptions(), (int threadIdx) =>
			{
				int increment = Interlocked.Increment(ref tIdx);
				int incrementOverColCount = increment / tileRange.ColCount;
				int incrementWithColCount = increment % tileRange.ColCount;
				long tIdx3 = worldTileDef.CellIndexLong(tileRange.MinRow + incrementOverColCount, tileRange.MinCol + incrementWithColCount);
				PooledArray<T> pooledArray2 = ValueSharedMemoryPool<T>.RentNoFill(tilePxCount, out var mem2);
				try
				{
					ProcessTile(tIdx3, mem2);
				}
				finally
				{
					pooledArray2.Dispose();
				}
			});
			return;
		}
		PooledArray<T> pooledArray = ValueSharedMemoryPool<T>.RentNoFill(tilePxCount, out var mem);
		try
		{
			for (int minRow = tileRange.MinRow; minRow <= tileRange.MaxRow; minRow++)
			{
				for (int minCol = tileRange.MinCol; minCol <= tileRange.MaxCol; minCol++)
				{
					long tIdx2 = worldTileDef.CellIndexLong(minRow, minCol);
					ProcessTile(tIdx2, mem);
				}
			}
		}
		finally
		{
			pooledArray.Dispose();
		}
		void ProcessTile(long ordinal, Memory<T> buf)
		{
			itr.ReadBlock(ordinal, buf);
			RasterDefinition tileRasterDefinition = itr.GetTileRasterDefinition(ordinal);
			Extent ext = desiredExtent.Intersection(itr.ValidExtent);
			if (clipTilesToDesiredExtent && !ext.Contains(tileRasterDefinition.CellCenterExtent()))
			{
				RasterDefinition subRaster = tileRasterDefinition.GetSubRaster(ext);
				if (!subRaster.IsNull())
				{
					PooledArray<T> pooledArray2 = ValueSharedMemoryPool<T>.Rent(subRaster.CellCount, out var mem2);
					try
					{
						Resample_Nearest(tileRasterDefinition, buf, subRaster, mem2, default);
						tileRasterDefinition = subRaster;
						processAction(ordinal, tileRasterDefinition, mem2);
					}
					finally
					{
						pooledArray2.Dispose();
					}
				}
			}
			else
			{
				processAction(ordinal, tileRasterDefinition, buf);
			}
		}
	}

	private static int IntPow(int baseV, int exp)
	{
		if (exp == 0)
		{
			return 1;
		}
		int unity = 1;
		for (int i = 0; i < exp; i++)
		{
			unity *= baseV;
		}
		return unity;
	}

	public static void ProcessTiles_FallbackToLowerRes<T, K>(IPyramidTileProvider<T> itr, int basePyramidLevel, Extent desiredExtent, bool clipTilesToDesiredExtent, K model, Action<K, long, RasterDefinition, Memory<T>> processAction, ParallelizationIntent intent = ParallelizationIntent.Serial) where T : unmanaged, IEquatable<T>
	{
		ITileProvider<T> tileProvider = itr.PyramidLevel[basePyramidLevel];
		RasterDefinition tiledRasterDefinition = tileProvider.TiledRasterDefinition;
		PixelRange pixelRange = tiledRasterDefinition.GetPixelRange(desiredExtent);
		if (pixelRange.CellCount == 0)
		{
			return;
		}
		int pixelCount = tileProvider.BlockSize.PixelCount;
		if (intent == ParallelizationIntent.Parallel && !Constants.DEBUG_SERIALIZE_READS)
		{
			ProcessTilesParallelForData<T, K> loopData = new ProcessTilesParallelForData<T, K>
			{
				TileRange = pixelRange,
				WorldTileDef = tiledRasterDefinition,
				DesiredExtent = desiredExtent,
				BasePyramidLevel = basePyramidLevel,
				BaseLevel = tileProvider,
				Itr = itr,
				ClipTilesToDesiredExtent = clipTilesToDesiredExtent,
				ProcessAction = processAction,
				Model = model,
				TIdx = -1
			};
			System.Threading.Tasks.Parallel.For(0, pixelRange.CellCount, Utility.Parallel.GetReasonableParallelOptions(), (int threadIdx) =>
			{
				int pixelCount2 = loopData.BaseLevel.BlockSize.PixelCount;
				int increment = Interlocked.Increment(ref loopData.TIdx);
				int incrementOverColCount = increment / loopData.TileRange.ColCount;
				int incrementWithColCount = increment % loopData.TileRange.ColCount;
				PooledArray<T> pooledArray2 = ValueSharedMemoryPool<T>.RentNoFill(pixelCount2, out var mem2);
				try
				{
					ProcessTile(loopData.WorldTileDef, loopData.DesiredExtent, loopData.BasePyramidLevel, loopData.BaseLevel, loopData.Itr, loopData.ClipTilesToDesiredExtent, loopData.ProcessAction, loopData.Model, loopData.TileRange.MinRow + incrementOverColCount, loopData.TileRange.MinCol + incrementWithColCount, mem2);
				}
				finally
				{
					pooledArray2.Dispose();
				}
			});
			return;
		}
		PooledArray<T> pooledArray = ValueSharedMemoryPool<T>.RentNoFill(pixelCount, out var mem);
		try
		{
			for (int minRow = pixelRange.MinRow; minRow <= pixelRange.MaxRow; minRow++)
			{
				for (int minCol = pixelRange.MinCol; minCol <= pixelRange.MaxCol; minCol++)
				{
					ProcessTile(tiledRasterDefinition, desiredExtent, basePyramidLevel, tileProvider, itr, clipTilesToDesiredExtent, processAction, model, minRow, minCol, mem);
				}
			}
		}
		finally
		{
			pooledArray.Dispose();
		}
		static void ProcessTile(RasterDefinition worldTileDef, Extent extent, int ordinal, ITileProvider<T> baseLvl, IPyramidTileProvider<T> pyramidTileProvider, bool condition, Action<K, long, RasterDefinition, Memory<T>> action, K localValue, int r, int c, Memory<T> buf)
		{
			long cellIndexLong = worldTileDef.CellIndexLong(r, c);
			bool block = false;
			int ordinal2 = ordinal;
			ITileProvider<T> tileProvider2 = baseLvl;
			while (true)
			{
				block = tileProvider2.ReadBlock(cellIndexLong, buf);
				if (block)
				{
					break;
				}
				ordinal2++;
				if (ordinal2 >= pyramidTileProvider.PyramidLevel.Length)
				{
					break;
				}
				tileProvider2 = pyramidTileProvider.PyramidLevel[ordinal2];
				int exp = ordinal2 - ordinal;
				int intPow = IntPow(2, exp);
				cellIndexLong = tileProvider2.TiledRasterDefinition.CellIndexLong(r / intPow, c / intPow);
			}
			if (block)
			{
				RasterDefinition tileRasterDefinition = tileProvider2.GetTileRasterDefinition(cellIndexLong);
				extent = extent.Extend(tileRasterDefinition.CellSize * 5.0);
				action(localValue, cellIndexLong, tileRasterDefinition, buf);
			}
		}
	}

	public static void ProcessTilesWithNeighbors<T>(ITileProvider<T> tileProvider, Extent desiredExtent, double bufferRadius, int suggestedMaxTilesInMemory, Action<int, TileWithNeighbors<T>> executeOp, bool preferPinnedReads = false) where T : unmanaged, IEquatable<T>
	{
		RasterDefinition renderBlockTileDefinition = tileProvider.GetRenderBlockTileDefinition();
		PixelRange pixelRange = renderBlockTileDefinition.GetPixelRange(desiredExtent);
		PixelRange pixelRange2 = renderBlockTileDefinition.GetPixelRange(desiredExtent.Extend(bufferRadius));
		if (!pixelRange.IsNull() && !pixelRange2.IsNull())
		{
			ProcessTilesWithNeighbors(tileProvider, pixelRange, suggestedMaxTilesInMemory, executeOp, pixelRange2, null, preferPinnedReads);
		}
	}

	public static void ProcessTilesWithNeighbors<T>(ITileProvider<T> tileProvider, PixelRange tileRange, int suggestedMaxTilesInMemory, Action<int, TileWithNeighbors<T>> executeOp, PixelRange? limiter = null, ProgressReporter prog = null, bool preferPinnedReads = false) where T : unmanaged, IEquatable<T>
	{
		RasterDefinition tileRasterDef = tileProvider.TiledRasterDefinition;
		if (!tileRasterDef.IsValidRange(tileRange))
		{
			throw new Exception("Bad tile range.");
		}
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		bool tryBorrow = NeighborScratch<T>.TryBorrow(out var scratch);
		List<IMemoryOwner<T>> owners = (tryBorrow ? scratch.Owners : new List<IMemoryOwner<T>>());
		IPinnedTileProvider<T> pinnedProvider = (preferPinnedReads ? (tileProvider as IPinnedTileProvider<T>) : null);
		List<long> pinnedBlocks = ((pinnedProvider == null) ? null : new List<long>());
		int blocksWide = tileProvider.BlocksWide();
		int capacity = Math.Min((tileRange.Width + 2) * (tileRange.Height + 2), suggestedMaxTilesInMemory);
		Dictionary<Pixel, RasterData<T>> cache = (tryBorrow ? scratch.Cache : new Dictionary<Pixel, RasterData<T>>(capacity));
		if (!limiter.HasValue)
		{
			limiter = tileRasterDef.FullRange();
		}
		int tileSize = tileProvider.TileSize * tileProvider.TileSize;
		TileWithNeighbors<T> neighborData = (tryBorrow ? scratch.Tile : new TileWithNeighbors<T>());
		bool suggestedMaxTilesInMemoryColCountTerm = suggestedMaxTilesInMemory >= 2 * tileRange.ColCount;
		bool suggestedMaxTilesInMemoryRowCountTerm = suggestedMaxTilesInMemory >= 2 * tileRange.RowCount;
		suggestedMaxTilesInMemoryColCountTerm = true;
		bool condition = !suggestedMaxTilesInMemoryColCountTerm & suggestedMaxTilesInMemoryRowCountTerm;
		try
		{
			if (condition)
			{
				if (!suggestedMaxTilesInMemoryRowCountTerm)
				{
					throw new NotImplementedException("Currently ColumnMajor is only supported if it's sitting resident in memory.");
				}
				SolveColumnMajor(tileRange);
			}
			else
			{
				if (!suggestedMaxTilesInMemoryColCountTerm)
				{
					throw new NotImplementedException();
				}
				SolveRowMajor(tileRange);
			}
		}
		catch
		{
			throw;
		}
		finally
		{
			foreach (IMemoryOwner<T> owner in owners)
			{
				owner.Dispose();
			}
			if (pinnedBlocks != null)
			{
				foreach (long pinnedBlock in pinnedBlocks)
				{
					pinnedProvider.ReleaseBlock(pinnedBlock);
				}
			}
			if (tryBorrow)
			{
				scratch.Return();
			}
		}
		void ExecuteHelper(Pixel px)
		{
			TileWithNeighbors<T> tileWithNeighbors = neighborData;
			tileWithNeighbors.UpperLeft = cache[px.UpperLeft()];
			tileWithNeighbors.Top = cache[px.Top()];
			tileWithNeighbors.UpperRight = cache[px.UpperRight()];
			tileWithNeighbors.Left = cache[px.Left()];
			tileWithNeighbors.Center = cache[px];
			tileWithNeighbors.Right = cache[px.Right()];
			tileWithNeighbors.LowerLeft = cache[px.LowerLeft()];
			tileWithNeighbors.Bottom = cache[px.Bottom()];
			tileWithNeighbors.LowerRight = cache[px.LowerRight()];
			executeOp(tileRasterDef.CellIndex(px), tileWithNeighbors);
		}
		void ReadIfInBounds(Pixel px)
		{
			if (tileRasterDef.InBounds(px) && limiter.Value.InRange(px))
			{
				if (pinnedProvider == null)
				{
					IMemoryOwner<T> memoryOwner = SharedMemoryPool<T>.RentNoFill(tileSize, out var mem);
					try
					{
						if (!tileProvider.ReadBlock(px, mem))
						{
							throw new Exception("Unexpected error reading block " + px);
						}
						RasterDefinition tileRasterDefinition = tileProvider.GetTileRasterDefinition(px);
						cache[px] = new RasterData<T>(tileRasterDefinition, mem)
						{
							NoDataValue = tileProvider.NoDataValue
						};
						owners.Add(memoryOwner);
						return;
					}
					catch
					{
						memoryOwner.Dispose();
						throw;
					}
				}
				long pxBlocksWideTerm = (long)px.Y * (long)blocksWide + px.X;
				if (!pinnedProvider.TryReadBlockPinned(pxBlocksWideTerm, out var data))
				{
					throw new Exception("Unexpected error reading block " + px);
				}
				pinnedBlocks.Add(pxBlocksWideTerm);
				RasterDefinition tileRasterDefinition2 = tileProvider.GetTileRasterDefinition(px);
				cache[px] = new RasterData<T>(tileRasterDefinition2, data)
				{
					NoDataValue = tileProvider.NoDataValue
				};
			}
			else
			{
				cache[px] = null;
			}
		}
		void SolveColumnMajor(PixelRange pr)
		{
			for (int i = pr.MinRow - 1; i <= pr.MaxRow + 1; i++)
			{
				int c = pr.MinCol - 1;
				int minCol = pr.MinCol;
				Pixel px = new Pixel(i, c);
				ReadIfInBounds(new Pixel(i, minCol));
				if (prog.CancelRequested)
				{
					break;
				}
				ReadIfInBounds(px);
				if (prog.CancelRequested)
				{
					break;
				}
			}
			for (int j = pr.MinCol; j <= pr.MaxCol; j++)
			{
				ReadIfInBounds(new Pixel(pr.MinRow - 1, j + 1));
				if (prog.CancelRequested)
				{
					break;
				}
				ReadIfInBounds(new Pixel(pr.MinRow, j + 1));
				if (prog.CancelRequested)
				{
					break;
				}
				for (int k = pr.MinRow; k <= pr.MaxRow; k++)
				{
					Pixel px2 = new Pixel(k, j);
					Pixel px3 = px2.LowerRight();
					Pixel key = px2.UpperLeft();
					ReadIfInBounds(px3);
					if (prog.CancelRequested)
					{
						break;
					}
					ExecuteHelper(px2);
					cache.Remove(key);
				}
				if (prog.CancelRequested)
				{
					break;
				}
			}
		}
		void SolveRowMajor(PixelRange pr)
		{
			for (int i = pr.MinCol - 1; i <= pr.MaxCol + 1; i++)
			{
				int r = pr.MinRow - 1;
				int minRow = pr.MinRow;
				Pixel px = new Pixel(minRow, i);
				Pixel px2 = new Pixel(r, i);
				ReadIfInBounds(px);
				if (prog.CancelRequested)
				{
					break;
				}
				ReadIfInBounds(px2);
				if (prog.CancelRequested)
				{
					break;
				}
			}
			if (!prog.CancelRequested)
			{
				for (int j = pr.MinRow; j <= pr.MaxRow; j++)
				{
					ReadIfInBounds(new Pixel(j + 1, pr.MinCol - 1));
					if (prog.CancelRequested)
					{
						break;
					}
					ReadIfInBounds(new Pixel(j + 1, pr.MinCol));
					if (prog.CancelRequested)
					{
						break;
					}
					for (int k = pr.MinCol; k <= pr.MaxCol; k++)
					{
						Pixel px3 = new Pixel(j, k);
						Pixel px4 = px3.LowerRight();
						Pixel key = px3.UpperLeft();
						ReadIfInBounds(px4);
						if (prog.CancelRequested)
						{
							break;
						}
						ExecuteHelper(px3);
						cache.Remove(key);
					}
					if (prog.CancelRequested)
					{
						break;
					}
				}
			}
		}
	}

	public static void WarpImage(RasterDefinition sourceRaster, byte[] band0, byte[] band1, byte[] band2, RasterDefinition targetRaster, int[] targetPixels, byte targetAlpha, ISpatialTransformer targetToSourceTransformer)
	{
		int imgAlpha = targetAlpha << 24;
		Warp(sourceRaster, targetRaster, targetToSourceTransformer, (int sn, int tn) =>
		{
			targetPixels[tn] = imgAlpha | (band0[sn] << 16) | (band1[sn] << 8) | band2[sn];
		});
	}

	public static void Warp<T>(RasterDefinition sourceRaster, Memory<T>[] sourceData, RasterDefinition targetRaster, Memory<T>[] targetData, ISpatialTransformer targetToSourceTransformer) where T : unmanaged
	{
		Warp(sourceRaster, targetRaster, targetToSourceTransformer, (int srcPx, int targetPx) =>
		{
			for (int i = 0; i < sourceData.Length; i++)
			{
				Span<T> sourceDataSpan = sourceData[i].Span;
				targetData[i].Span[targetPx] = sourceDataSpan[srcPx];
			}
		});
	}

	public static void Warp(RasterDefinition sourceRaster, RasterDefinition targetRaster, ISpatialTransformer targetToSourceTransformer, Action<int, int> payload)
	{
		PolynomialTransformer.GetTransformCoefficientsPixelSpace(sourceRaster, targetRaster, targetToSourceTransformer, out var coefCol, out var coefRow);
		int sColMax = sourceRaster.Cols - 1;
		int sRowMax = sourceRaster.Rows - 1;
		if (targetRaster.CellCount > 100 && targetRaster.Rows > 1 && !Constants.DEBUG_SERIALIZE_READS)
		{
			System.Threading.Tasks.Parallel.For(0, targetRaster.Rows, (int tr) =>
			{
				RowAction(tr);
			});
			return;
		}
		for (int ordinal = 0; ordinal < targetRaster.Rows; ordinal++)
		{
			RowAction(ordinal);
		}
		void RowAction(int tr)
		{
			coefCol.RasterRowCoefficients(tr, out var intercept, out var coefSC, out var coefSC2);
			coefRow.RasterRowCoefficients(tr, out var intercept2, out var coefSC3, out var coefSC4);
			int trTimesCols = tr * targetRaster.Cols;
			for (int i = 0; i < targetRaster.Cols; i++)
			{
				double computed = (double)i + 0.5;
				int interceptCoefSCTerm = (int)Math.Floor(intercept + (coefSC + coefSC2 * computed) * computed);
				if (interceptCoefSCTerm >= 0 && interceptCoefSCTerm <= sColMax)
				{
					int intercept2CoefSC3Term = (int)Math.Floor(intercept2 + (coefSC3 + coefSC4 * computed) * computed);
					if (intercept2CoefSC3Term >= 0 && intercept2CoefSC3Term <= sRowMax)
					{
						int cellIndex = sourceRaster.CellIndex(intercept2CoefSC3Term, interceptCoefSCTerm);
						payload(cellIndex, trTimesCols);
					}
				}
				trTimesCols++;
			}
		}
	}

	public static void Warp<T>(RasterDefinition sourceRaster, Memory<T> sourceData, RasterDefinition targetRaster, Memory<T> targetData, ISpatialTransformer targetToSourceTransformer) where T : unmanaged
	{
		PolynomialTransformer.GetTransformCoefficientsPixelSpace(sourceRaster, targetRaster, targetToSourceTransformer, out var coefCol, out var coefRow);
		bool cellCountTargetRasterTerm = targetRaster.CellCount > 100 && targetRaster.Rows > 1 && !Constants.DEBUG_SERIALIZE_READS;
		WarpData<T> data = new WarpData<T>
		{
			SourceRaster = sourceRaster,
			TargetRaster = targetRaster,
			SourceData = sourceData,
			TargetData = targetData,
			CoefCol = coefCol,
			CoefRow = coefRow
		};
		if (cellCountTargetRasterTerm)
		{
			System.Threading.Tasks.Parallel.For(0, targetRaster.Rows, RowAction);
			return;
		}
		for (int i = 0; i < targetRaster.Rows; i++)
		{
			RowAction(i);
		}
		void RowAction(int tr)
		{
			int colsOffset = data.SourceRaster.Cols - 1;
			int rowsOffset = data.SourceRaster.Rows - 1;
			Span<T> sourceDataSpan = data.SourceData.Span;
			Span<T> targetDataSpan = data.TargetData.Span;
			data.CoefCol.RasterRowCoefficients(tr, out var intercept, out var coefSC, out var coefSC2);
			data.CoefRow.RasterRowCoefficients(tr, out var intercept2, out var coefSC3, out var coefSC4);
			int trTimesCols = tr * data.TargetRaster.Cols;
			for (int j = 0; j < data.TargetRaster.Cols; j++)
			{
				double computed = (double)j + 0.5;
				int interceptCoefSCTerm = (int)Math.Floor(intercept + (coefSC + coefSC2 * computed) * computed);
				if (interceptCoefSCTerm >= 0 && interceptCoefSCTerm <= colsOffset)
				{
					int intercept2CoefSC3Term = (int)Math.Floor(intercept2 + (coefSC3 + coefSC4 * computed) * computed);
					if (intercept2CoefSC3Term >= 0 && intercept2CoefSC3Term <= rowsOffset)
					{
						int index = data.SourceRaster.CellIndex(intercept2CoefSC3Term, interceptCoefSCTerm);
						targetDataSpan[trTimesCols] = sourceDataSpan[index];
					}
				}
				trTimesCols++;
			}
		}
	}

	private static double GetWarpOversampleRatio(Extent destExtent, ISpatialTransformer targetToSourceTransformer)
	{
		Extent extent = destExtent.Transform(targetToSourceTransformer, out var transformedEdgeDivisorPoints);
		Polygon polygon = new Polygon(transformedEdgeDivisorPoints);
		return Math.Sqrt(extent.Area / Math.Abs(polygon.Area));
	}

	public static void WarpTiled<T>(IResample<T> source, ISpatialTransformer sourceToTargetTransformer, double outputCellSize, T noDataValue, ParallelizationIntent intent, Action<RasterDefinition, RasterDefinition, int, T[]> tileAction) where T : unmanaged, IEquatable<T>
	{
		Extent validExtent = source.ValidExtent;
		ISpatialTransformer reverseTransformer = sourceToTargetTransformer.GetInvertedTransformer();
		Extent destExtent = validExtent.Transform(sourceToTargetTransformer);
		double warpOversampleRatio = GetWarpOversampleRatio(destExtent, reverseTransformer);
		int oversampleTileSize = (int)Math.Round(256.0 * warpOversampleRatio);
		int widthOverOutputCellSize = (int)(destExtent.Width / outputCellSize);
		int heightOverOutputCellSize = (int)(destExtent.Height / outputCellSize);
		destExtent = new Extent(destExtent.UpperLeft, (double)widthOverOutputCellSize * outputCellSize, (double)heightOverOutputCellSize * outputCellSize);
		int ordinal = widthOverOutputCellSize / 256;
		if (widthOverOutputCellSize % 256 > 0)
		{
			ordinal++;
		}
		int ordinal2 = heightOverOutputCellSize / 256;
		if (heightOverOutputCellSize % 256 > 0)
		{
			ordinal2++;
		}
		RasterDefinition fullTileRaster = new RasterDefinition(destExtent.UpperLeft, outputCellSize * 256.0, ordinal2, ordinal);
		if (intent == ParallelizationIntent.Serial)
		{
			T[] sourceBuffer = new T[oversampleTileSize * oversampleTileSize];
			T[] outputBuffer = new T[65536];
			for (int i = 0; i < fullTileRaster.CellCount; i++)
			{
				InvokePerTile(i, sourceBuffer, outputBuffer);
			}
			return;
		}
		ThreadLocal<T[]> sourceBuffers = new ThreadLocal<T[]>(() => new T[oversampleTileSize * oversampleTileSize]);
		ThreadLocal<T[]> outputBuffers = new ThreadLocal<T[]>(() => new T[65536]);
		System.Threading.Tasks.Parallel.For(0, fullTileRaster.CellCount, (int tIdx) =>
		{
			InvokePerTile(tIdx, sourceBuffers.Value, outputBuffers.Value);
		});
		void InvokePerTile(int tIdx, T[] values, T[] valuesArray)
		{
			Extent targetExtent = fullTileRaster.CellExtent(tIdx);
			RasterDefinition rasterDefinition = new RasterDefinition(targetExtent, 256, 256);
			Extent targetExtent2 = targetExtent.Transform(reverseTransformer);
			RasterDefinition rasterDefinition2 = new RasterDefinition(targetExtent2, oversampleTileSize, oversampleTileSize);
			source.Resample(new ResampleArgs(rasterDefinition2), values);
			valuesArray.AsSpan().Fill(noDataValue);
			Warp<T>(rasterDefinition2, values, rasterDefinition, valuesArray, reverseTransformer);
			tileAction(fullTileRaster, rasterDefinition, tIdx, valuesArray);
		}
	}
}
