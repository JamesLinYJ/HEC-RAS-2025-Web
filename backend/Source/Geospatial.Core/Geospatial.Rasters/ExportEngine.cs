using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.Logging;
using Utility.Memory;
using Utility.Progress;

namespace Geospatial.Rasters;

public static class ExportEngine
{
	private static readonly ILogger _logger = LogHelper.GetLogger("ExportEngine");

	private static int[] OverviewProgressValues = new int[6] { 0, 75, 94, 98, 99, 100 };

	public static ProgressReporter GetBaseLevelProgress(ProgressReporter input)
	{
		return input.WithProgressModifier(0, 0.8f);
	}

	public static ProgressReporter GetAllOverviewProgress(ProgressReporter input)
	{
		return input.WithProgressModifier(80, 0.2f);
	}

	public static ProgressReporter GetOverviewProgress(ProgressReporter input, int ov)
	{
		if (ov < OverviewProgressValues.Length - 1)
		{
			int overviewProgressValue = OverviewProgressValues[ov];
			int overviewProgressValue2 = OverviewProgressValues[ov + 1];
			return input.WithProgressModifier(overviewProgressValue, (float)(overviewProgressValue2 - overviewProgressValue) / 100f);
		}
		return input.WithProgressModifier(100, 0f);
	}

	public static void Write<T>(IResample<T> raster, ITileWriter<T> writer, ProgressReporter pr = null, ParallelizationIntent intent = ParallelizationIntent.Parallel) where T : unmanaged, IEquatable<T>
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		if (intent == ParallelizationIntent.Serial || !writer.CanWriteTilesInParallel)
		{
			WriteInternal_ST(raster, writer, pr);
		}
		else
		{
			WriteInternal_MT(raster, writer, pr);
		}
	}

	private static void WriteInternal_ST<T>(IResample<T> raster, ITileWriter<T> writer, ProgressReporter pr) where T : unmanaged, IEquatable<T>
	{
		int tileSize = writer.TileSize;
		RasterDefinition tiledRasterDefinition = writer.TiledRasterDefinition;
		if (!raster.NoDataValue.GetValueOrDefault().Equals(writer.NoDataValue.GetValueOrDefault()))
		{
			throw new ArgumentException("Raster/Writer NoData disagreement - not currently supported.");
		}
		T valueOrDefault = raster.NoDataValue.GetValueOrDefault();
		Memory<T> mem;
		using (SharedMemoryPool<T>.Rent(tileSize * tileSize, default, out mem))
		{
			int cellCount = tiledRasterDefinition.CellCount;
			for (int i = 0; i < tiledRasterDefinition.Rows; i++)
			{
				for (int j = 0; j < tiledRasterDefinition.Cols; j++)
				{
					int cellIndex = tiledRasterDefinition.CellIndex(i, j);
					RasterDefinition cellRasterDefinition = tiledRasterDefinition.GetCellRasterDefinition(cellIndex, tileSize);
					mem.Span.Fill(valueOrDefault);
					ResampleArgs args = new ResampleArgs(cellRasterDefinition, SamplingParams.WithoutBufferClear);
					raster.Resample(args, mem);
					writer.WriteBlock(cellIndex, mem);
					pr.ReportProgress((int)(100f * (float)cellIndex / (float)cellCount));
				}
			}
		}
	}

	private static void WriteInternal_MT<T>(IResample<T> raster, ITileWriter<T> writer, ProgressReporter pr) where T : unmanaged, IEquatable<T>
	{
		int tileSize = writer.TileSize;
		RasterDefinition worldTileDef = writer.TiledRasterDefinition;
		if (!raster.NoDataValue.GetValueOrDefault().Equals(writer.NoDataValue.GetValueOrDefault()))
		{
			throw new ArgumentException("Raster/Writer NoData disagreement - not currently supported.");
		}
		T ndv = raster.NoDataValue.GetValueOrDefault();
		int tileCt = worldTileDef.CellCount;
		int nextTile = -1;
		int tilesDone = 0;
		object reportLock = new object();
		System.Threading.Tasks.Parallel.For(0, tileCt, (int i) =>
		{
			int increment = Interlocked.Increment(ref nextTile);
			RasterDefinition cellRasterDefinition = worldTileDef.GetCellRasterDefinition(increment, tileSize);
			if (pr.CancelRequested)
			{
				return;
			}
			Memory<T> mem;
			using (SharedMemoryPool<T>.Rent(tileSize * tileSize, ndv, out mem))
			{
				ResampleArgs args = new ResampleArgs(cellRasterDefinition, SamplingParams.WithoutBufferClear);
				raster.Resample(args, mem);
				writer.WriteBlock(increment, mem);
				lock (reportLock)
				{
					tilesDone++;
					pr.ReportProgress((int)(100f * (float)tilesDone / (float)tileCt));
				}
			}
		});
	}

	public static void CreateOverviews<T, RW>(ITileProvider<T> baseData, IList<RW> pyramidLevels, ProgressReporter pr = null, ParallelizationIntent intent = ParallelizationIntent.Parallel) where T : unmanaged, INumber<T> where RW : ITileReaderWriter<T>
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		CheckPyramidLevels(baseData, pyramidLevels);
		for (int i = 0; i < pyramidLevels.Count; i++)
		{
			ITileProvider<T> baseData2 = (ITileProvider<T>)((i != 0) ? ((object)pyramidLevels[i - 1]) : baseData);
			if (intent == ParallelizationIntent.Serial || !pyramidLevels[i].CanWriteTilesInParallel)
			{
				CreateOverviewInternal_ST(baseData2, pyramidLevels[i], GetOverviewProgress(pr, i));
			}
			else
			{
				CreateOverviewInternal_MT(baseData2, pyramidLevels[i], GetOverviewProgress(pr, i));
			}
		}
		pr.ReportProgress(100);
	}

	public static void CreateOverviews<T, RW>(IMultiBandTileProvider<T> baseData, IList<RW> pyramidLevels, ProgressReporter pr = null, ParallelizationIntent intent = ParallelizationIntent.Parallel) where T : unmanaged, INumber<T> where RW : IMultiBandTileReaderWriter<T>
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		CheckPyramidLevels(baseData, pyramidLevels);
		for (int i = 0; i < pyramidLevels.Count; i++)
		{
			IMultiBandTileProvider<T> tp = (IMultiBandTileProvider<T>)((i != 0) ? ((object)pyramidLevels[i - 1]) : baseData);
			IMultiBandTileReaderWriter<T> multiBandTileReaderWriter = pyramidLevels[i];
			for (int j = 0; j < baseData.ProfileCount; j++)
			{
				ITileProvider<T> baseData2 = tp.AsSingleProfileProviderRef(j);
				ITileReaderWriter<T> tileReaderWriter = multiBandTileReaderWriter.AsSingleProfileWriter(j);
				if (intent == ParallelizationIntent.Serial || !tileReaderWriter.CanWriteTilesInParallel)
				{
					CreateOverviewInternal_ST(baseData2, tileReaderWriter, GetOverviewProgress(pr, i));
				}
				else
				{
					CreateOverviewInternal_MT(baseData2, tileReaderWriter, GetOverviewProgress(pr, i));
				}
			}
			multiBandTileReaderWriter.Flush();
		}
		pr.ReportProgress(100);
	}

	private static void CheckPyramidLevels<RW>(IRasterDefinition baseData, IList<RW> pyramids) where RW : IRasterDefinition
	{
		double cellSize = baseData.RasterDefinition.CellSize;
		for (int i = 0; i < pyramids.Count; i++)
		{
			double cellSize2 = pyramids[i].RasterDefinition.CellSize;
			double cellSizeTerm = cellSize * Math.Pow(2.0, i + 1);
			if (Math.Abs(cellSizeTerm - cellSize2) / cellSizeTerm > 0.001)
			{
				throw new Exception("Pyramid cellsize disagreement. Must be incrementing powers of 2.");
			}
		}
	}

	public static void CreateDiscreteOverviews<T, RW>(ITileProvider<T> baseData, IList<RW> pyramidLevels, ProgressReporter pr = null) where T : unmanaged, IBinaryInteger<T> where RW : ITileReaderWriter<T>
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		double cellSize = baseData.RasterDefinition.CellSize;
		for (int i = 0; i < pyramidLevels.Count; i++)
		{
			double cellSize2 = pyramidLevels[i].RasterDefinition.CellSize;
			double cellSizeTerm = cellSize * Math.Pow(2.0, i + 1);
			if (Math.Abs(cellSizeTerm - cellSize2) / cellSizeTerm > 0.001)
			{
				throw new Exception("Pyramid cellsize disagreement. Must be incrementing powers of 2.");
			}
		}
		for (int j = 0; j < pyramidLevels.Count; j++)
		{
			ITileProvider<T> baseData2 = (ITileProvider<T>)((j != 0) ? ((object)pyramidLevels[j - 1]) : baseData);
			CreateDiscreteOverviewInternal_MT(baseData2, pyramidLevels[j], GetOverviewProgress(pr, j));
		}
		pr.ReportProgress(100);
	}

	private static void CreateOverviewInternal_ST<T>(ITileProvider<T> baseData, ITileWriter<T> writer, ProgressReporter pr) where T : unmanaged, INumber<T>
	{
		int tileSize = baseData.TileSize;
		int sz = tileSize * tileSize;
		int tileCount = writer.TileCount;
		Memory<T> mem;
		using (SharedMemoryPool<T>.RentNoFill(sz, out mem))
		{
			Memory<T> mem2;
			using (SharedMemoryPool<T>.RentNoFill(sz, out mem2))
			{
				Memory<T> mem3;
				using (SharedMemoryPool<T>.RentNoFill(sz, out mem3))
				{
					Memory<T> mem4;
					using (SharedMemoryPool<T>.RentNoFill(sz, out mem4))
					{
						Memory<T> mem5;
						using (SharedMemoryPool<T>.RentNoFill(sz, out mem5))
						{
							for (int i = 0; i < tileCount; i++)
							{
								SolveSinglePyramid(baseData, writer, i, mem, mem2, mem3, mem4, mem5);
								writer.WriteBlock(i, mem5);
								pr.ReportProgress((int)(100f * (float)i / (float)tileCount));
							}
						}
					}
				}
			}
		}
	}

	private static void CreateOverviewInternal_MT<T>(ITileProvider<T> baseData, ITileWriter<T> writer, ProgressReporter pr) where T : unmanaged, INumber<T>
	{
		int tileSize = baseData.TileSize;
		int tileNPx = tileSize * tileSize;
		int targetTileCount = writer.TileCount;
		T ndv = baseData.NoDataValue.GetValueOrDefault();
		object reportLock = new object();
		int tilesDone = -1;
		System.Threading.Tasks.Parallel.For(0, targetTileCount, (int _) =>
		{
			int increment = Interlocked.Increment(ref tilesDone);
			Memory<T> mem;
			using (SharedMemoryPool<T>.Rent(tileNPx, ndv, out mem))
			{
				Memory<T> mem2;
				using (SharedMemoryPool<T>.Rent(tileNPx, ndv, out mem2))
				{
					Memory<T> mem3;
					using (SharedMemoryPool<T>.Rent(tileNPx, ndv, out mem3))
					{
						Memory<T> mem4;
						using (SharedMemoryPool<T>.Rent(tileNPx, ndv, out mem4))
						{
							Memory<T> mem5;
							using (SharedMemoryPool<T>.RentNoFill(tileNPx, out mem5))
							{
								SolveSinglePyramid(baseData, writer, increment, mem, mem2, mem3, mem4, mem5);
								writer.WriteBlock(increment, mem5);
								if (increment % 10 == 0)
								{
									lock (reportLock)
									{
										pr.ReportProgress((int)(100f * (float)increment / (float)targetTileCount));
										return;
									}
								}
							}
						}
					}
				}
			}
		});
	}

	private static void SolveSinglePyramid<T>(ITileProvider<T> baseData, ITileWriter<T> writer, int tIdx, Memory<T> bufUL, Memory<T> bufUR, Memory<T> bufLL, Memory<T> bufLR, Memory<T> bufOut) where T : unmanaged, INumber<T>
	{
		int tileSize = baseData.TileSize;
		T valueOrDefault = baseData.NoDataValue.GetValueOrDefault();
		Pixel pixel = writer.TiledRasterDefinition.CellIndex2D(tIdx);
		Pixel pixel2 = new Pixel(pixel.Row * 2, pixel.Column * 2);
		Pixel pixel3 = pixel2.Right();
		Pixel pixel4 = pixel2.Bottom();
		Pixel pixel5 = pixel4.Right();
		bool inRange = baseData.TiledRasterDefinition.InRange(pixel2);
		bool inRange2 = baseData.TiledRasterDefinition.InRange(pixel3);
		bool inRange3 = baseData.TiledRasterDefinition.InRange(pixel4);
		bool inRange4 = baseData.TiledRasterDefinition.InRange(pixel5);
		if (!inRange)
		{
			throw new Exception("Logic error: Tile not in range.");
		}
		baseData.ReadBlock(pixel2, bufUL);
		AverageInternal(bufUL.Span, bufOut.Span, tileSize, valueOrDefault, 0, 0);
		if (inRange2)
		{
			baseData.ReadBlock(pixel3, bufUR);
			AverageInternal(bufUR.Span, bufOut.Span, tileSize, valueOrDefault, 0, tileSize / 2);
		}
		if (inRange3)
		{
			baseData.ReadBlock(pixel4, bufLL);
			AverageInternal(bufLL.Span, bufOut.Span, tileSize, valueOrDefault, tileSize / 2, 0);
		}
		if (inRange4)
		{
			baseData.ReadBlock(pixel5, bufLR);
			AverageInternal(bufLR.Span, bufOut.Span, tileSize, valueOrDefault, tileSize / 2, tileSize / 2);
		}
		RasterOperations.PreserveRegion(writer.GetTileRaster(tIdx), bufOut, writer.ValidExtent, valueOrDefault);
	}

	private static void AverageInternal<T>(Span<T> spIn, Span<T> spOut, int tileSize, T ndv, int rowOffset, int colOffset) where T : unmanaged, INumber<T>
	{
		for (int i = 0; i < tileSize; i += 2)
		{
			for (int j = 0; j < tileSize; j += 2)
			{
				int tileSizeTerm = i * tileSize + j;
				T spIn2 = spIn[tileSizeTerm];
				T spIn3 = spIn[tileSizeTerm + 1];
				T spIn4 = spIn[tileSizeTerm + tileSize];
				T spIn5 = spIn[tileSizeTerm + tileSize + 1];
				T zero = T.Zero;
				int ordinal = 0;
				if (spIn2 != ndv)
				{
					zero += spIn2;
					ordinal++;
				}
				if (spIn3 != ndv)
				{
					zero += spIn3;
					ordinal++;
				}
				if (spIn4 != ndv)
				{
					zero += spIn4;
					ordinal++;
				}
				if (spIn5 != ndv)
				{
					zero += spIn5;
					ordinal++;
				}
				T saturating = T.CreateSaturating(ordinal);
				int index = (rowOffset + i / 2) * tileSize + colOffset + j / 2;
				if (ordinal == 0)
				{
					spOut[index] = ndv;
				}
				else
				{
					spOut[index] = zero / saturating;
				}
			}
		}
	}

	private static void CreateDiscreteOverviewInternal_MT<T>(ITileProvider<T> baseData, ITileWriter<T> writer, ProgressReporter pr) where T : unmanaged, IBinaryInteger<T>
	{
		int tileSize = baseData.TileSize;
		int tileNPx = tileSize * tileSize;
		int targetTileCount = writer.TileCount;
		T ndv = baseData.NoDataValue.GetValueOrDefault();
		object reportLock = new object();
		int curTile = -1;
		int tilesDone = 0;
		System.Threading.Tasks.Parallel.For(0, targetTileCount, (int _) =>
		{
			int increment = Interlocked.Increment(ref curTile);
			Pixel pixel = writer.TiledRasterDefinition.CellIndex2D(increment);
			Pixel pixel2 = new Pixel(pixel.Row * 2, pixel.Column * 2);
			Pixel pixel3 = pixel2.Right();
			Pixel pixel4 = pixel2.Bottom();
			Pixel pixel5 = pixel4.Right();
			bool inRange = baseData.TiledRasterDefinition.InRange(pixel2);
			bool inRange2 = baseData.TiledRasterDefinition.InRange(pixel3);
			bool inRange3 = baseData.TiledRasterDefinition.InRange(pixel4);
			bool inRange4 = baseData.TiledRasterDefinition.InRange(pixel5);
			if (!inRange)
			{
				throw new Exception("Logic error: Tile not in range.");
			}
			Memory<T> mem;
			using (SharedMemoryPool<T>.Rent(tileNPx, ndv, out mem))
			{
				Memory<T> mem2;
				using (SharedMemoryPool<T>.Rent(tileNPx, ndv, out mem2))
				{
					Memory<T> mem3;
					using (SharedMemoryPool<T>.Rent(tileNPx, ndv, out mem3))
					{
						Memory<T> mem4;
						using (SharedMemoryPool<T>.Rent(tileNPx, ndv, out mem4))
						{
							Memory<T> mem5;
							using (SharedMemoryPool<T>.RentNoFill(tileNPx, out mem5))
							{
								baseData.ReadBlock(pixel2, mem);
								SampleInternal(mem.Span, mem5.Span, tileSize, ndv, 0, 0);
								if (inRange2)
								{
									baseData.ReadBlock(pixel3, mem2);
									SampleInternal(mem2.Span, mem5.Span, tileSize, ndv, 0, tileSize / 2);
								}
								if (inRange3)
								{
									baseData.ReadBlock(pixel4, mem3);
									SampleInternal(mem3.Span, mem5.Span, tileSize, ndv, tileSize / 2, 0);
								}
								if (inRange4)
								{
									baseData.ReadBlock(pixel5, mem4);
									SampleInternal(mem4.Span, mem5.Span, tileSize, ndv, tileSize / 2, tileSize / 2);
								}
								writer.WriteBlock(increment, mem5);
								lock (reportLock)
								{
									tilesDone++;
									pr.ReportProgress((int)(100f * (float)tilesDone / (float)targetTileCount));
								}
							}
						}
					}
				}
			}
		});
	}

	private static void SampleInternal<T>(Span<T> spIn, Span<T> spOut, int tileSize, T ndv, int rowOffset, int colOffset) where T : unmanaged, IBinaryInteger<T>
	{
		for (int i = 0; i < tileSize; i += 2)
		{
			for (int j = 0; j < tileSize; j += 2)
			{
				int tileSizeTerm = i * tileSize + j;
				int index = (rowOffset + i / 2) * tileSize + colOffset + j / 2;
				T spIn2 = spIn[tileSizeTerm];
				T spIn3 = spIn[tileSizeTerm + 1];
				T spIn4 = spIn[tileSizeTerm + tileSize];
				T spIn5 = spIn[tileSizeTerm + tileSize + 1];
				if (spIn2 != ndv && (spIn2 == spIn3 || spIn2 == spIn4 || spIn2 == spIn5))
				{
					spOut[index] = spIn2;
				}
				else if (spIn3 != ndv && (spIn3 == spIn4 || spIn3 == spIn5))
				{
					spOut[index] = spIn3;
				}
				else if (spIn4 != ndv && spIn4 == spIn5)
				{
					spOut[index] = spIn4;
				}
				else if (spIn2 != ndv)
				{
					spOut[index] = spIn2;
				}
				else if (spIn3 != ndv)
				{
					spOut[index] = spIn3;
				}
				else if (spIn4 != ndv)
				{
					spOut[index] = spIn4;
				}
				else if (spIn5 != ndv)
				{
					spOut[index] = spIn5;
				}
				else
				{
					spOut[index] = ndv;
				}
			}
		}
	}

	public static void Write<T>(IMultiBandResampler<T> raster, ITileWriter<T> writer) where T : unmanaged, IEquatable<T>
	{
		WriteInternal_ST(raster, writer);
	}

	private static void WriteInternal_ST<T>(IMultiBandResampler<T> raster, ITileWriter<T> writer) where T : unmanaged, IEquatable<T>
	{
		if (raster.ProfileCount != 3)
		{
			throw new Exception("Hardcoded to rgb images for now.");
		}
		int tileSize = writer.TileSize;
		RasterDefinition tiledRasterDefinition = writer.TiledRasterDefinition;
		if (!raster.NoDataValue.GetValueOrDefault().Equals(writer.NoDataValue.GetValueOrDefault()))
		{
			throw new ArgumentException("Raster/Writer NoData disagreement - not currently supported.");
		}
		T valueOrDefault = raster.NoDataValue.GetValueOrDefault();
		Memory<T> mem;
		using (SharedMemoryPool<T>.Rent(tileSize * tileSize * raster.ProfileCount, default, out mem))
		{
			Memory<T> mem2;
			using (SharedMemoryPool<T>.Rent(tileSize * tileSize, default, out mem2))
			{
				Memory<T> mem3;
				using (SharedMemoryPool<T>.Rent(tileSize * tileSize, default, out mem3))
				{
					Memory<T> mem4;
					using (SharedMemoryPool<T>.Rent(tileSize * tileSize, default, out mem4))
					{
						Memory<T>[] outData = new Memory<T>[3] { mem2, mem3, mem4 };
						Span<T> memSpan = mem.Span;
						Span<T> mem2Span = mem2.Span;
						Span<T> mem3Span = mem3.Span;
						Span<T> mem4Span = mem4.Span;
						for (int i = 0; i < tiledRasterDefinition.Rows; i++)
						{
							for (int j = 0; j < tiledRasterDefinition.Cols; j++)
							{
								int cellIndex = tiledRasterDefinition.CellIndex(i, j);
								RasterDefinition cellRasterDefinition = tiledRasterDefinition.GetCellRasterDefinition(cellIndex, tileSize);
								mem.Span.Fill(valueOrDefault);
								ResampleArgs args = new ResampleArgs(cellRasterDefinition, SamplingParams.WithoutBufferClear);
								raster.Resample(args, 0, raster.ProfileCount, outData);
								for (int k = 0; k < tileSize * tileSize; k++)
								{
									memSpan[k * 3] = mem2Span[k];
									memSpan[k * 3 + 1] = mem3Span[k];
									memSpan[k * 3 + 2] = mem4Span[k];
								}
								writer.WriteBlock(cellIndex, mem);
							}
						}
					}
				}
			}
		}
	}
}
