using System;
using System.Collections.Generic;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Threading;
using System.Threading.Tasks;
using Geospatial.Vectors;
using Utility.Progress;

namespace Geospatial.Rasters;

public static class RasterSkeletonizer
{
	private const int TileSize = 2048;

	private const int Overlap = 32;

	public static PolylineCollection Skeletonize(IRaster<float> raster, float threshold = 0.1f, int minLengthPixels = 8, double simplifyTolerance = 0.5, double cullLengthWorld = 0.0, ProgressReporter progress = null)
	{
		if (progress == null)
		{
			progress = ProgressReporter.None();
		}
		RasterDefinition rasterDef = raster.RasterDefinition;
		int rows = rasterDef.Rows;
		int cols = rasterDef.Cols;
		int innerSize = 1984;
		List<(int r, int c)> skeletonPixels = new List<(int, int)>();
		int rowsInnerSizeTerm = (int)Math.Ceiling((double)rows / (double)innerSize);
		int tileCols = (int)Math.Ceiling((double)cols / (double)innerSize);
		int tileCount = rowsInnerSizeTerm * tileCols;
		object lockObj = new object();
		int tilesCompleted = 0;
		int maxTilePixels = 4194304;
		ThreadLocal<float[]> tileDataPool = new ThreadLocal<float[]>(() => new float[maxTilePixels]);
		ThreadLocal<byte[]> tileBytesPool = new ThreadLocal<byte[]>(() => new byte[maxTilePixels]);
		progress.ReportMessage($"Thinning {tileCount} tiles ({rows}x{cols} pixels)...");
		ParallelOptions parallelOptions = new ParallelOptions
		{
			MaxDegreeOfParallelism = Environment.ProcessorCount
		};
		Parallel.For(0, tileCount, parallelOptions, (int tileIdx) =>
		{
			if (!progress.CancelRequested)
			{
				int tileIdxOverTileCols = tileIdx / tileCols;
				int tileIdxWithTileCols = tileIdx % tileCols;
				int innerSizeTerm = Math.Max(tileIdxOverTileCols * innerSize - 32, 0);
				int innerSizeTerm2 = Math.Max(tileIdxWithTileCols * innerSize - 32, 0);
				int rowsTerm = Math.Min(innerSizeTerm + 2048, rows);
				int colsTerm = Math.Min(innerSizeTerm2 + 2048, cols);
				int ordinal = rowsTerm - innerSizeTerm;
				int ordinal2 = colsTerm - innerSizeTerm2;
				PixelRange pxRange = new PixelRange(innerSizeTerm, rowsTerm - 1, innerSizeTerm2, colsTerm - 1);
				RasterDefinition subRaster = rasterDef.GetSubRaster(pxRange);
				if (!subRaster.IsNull())
				{
					float[] tileDataPoolTerm = tileDataPool.Value;
					byte[] tileBytesPoolTerm = tileBytesPool.Value;
					int ordinal3 = ordinal * ordinal2;
					ResampleArgs args = new ResampleArgs(subRaster, InterpolationMode.Nearest);
					raster.Resample(args, tileDataPoolTerm.AsMemory(0, ordinal3));
					if (Avx.IsSupported)
					{
						ThresholdAvx(tileDataPoolTerm.AsSpan(0, ordinal3), tileBytesPoolTerm.AsSpan(0, ordinal3), threshold);
					}
					else
					{
						ThresholdScalar(tileDataPoolTerm.AsSpan(0, ordinal3), tileBytesPoolTerm.AsSpan(0, ordinal3), threshold);
					}
					if (ordinal3 < maxTilePixels)
					{
						Array.Clear(tileBytesPoolTerm, ordinal3, maxTilePixels - ordinal3);
					}
					ZhangSuenThinning.Thin(tileBytesPoolTerm.AsSpan(0, ordinal3), ordinal2, ordinal);
					int ordinal4 = ((innerSizeTerm != 0) ? 32 : 0);
					int ordinal5 = ((innerSizeTerm2 != 0) ? 32 : 0);
					int count = ((rowsTerm == rows) ? ordinal : (ordinal - 32));
					int count2 = ((colsTerm == cols) ? ordinal2 : (ordinal2 - 32));
					lock (lockObj)
					{
						for (int i = ordinal4; i < count; i++)
						{
							int ordinal6 = innerSizeTerm + i;
							int ordinal7 = i * ordinal2;
							for (int j = ordinal5; j < count2; j++)
							{
								if (tileBytesPoolTerm[ordinal7 + j] != 0)
								{
									skeletonPixels.Add((ordinal6, innerSizeTerm2 + j));
								}
							}
						}
						int increment = Interlocked.Increment(ref tilesCompleted);
						progress.ReportProgress(increment * 80 / tileCount);
					}
				}
			}
		});
		tileDataPool.Dispose();
		tileBytesPool.Dispose();
		if (progress.CancelRequested)
		{
			throw new OperationCanceledException("Skeletonization cancelled.");
		}
		progress.ReportMessage($"Tracing {skeletonPixels.Count:N0} skeleton pixels to polylines...");
		PolylineCollection polylineCollection = SkeletonTracer.Trace(skeletonPixels, cols, rows, rasterDef, minLengthPixels, simplifyTolerance);
		progress.ReportProgress(95);
		if (cullLengthWorld > 0.0)
		{
			polylineCollection.RemoveShorterThan(cullLengthWorld);
		}
		progress.ReportMessage($"Done. {polylineCollection.Count} polylines.");
		progress.ReportProgress(100);
		return polylineCollection;
	}

	public static PolylineCollection Skeletonize(ReadOnlyMemory<float> probabilityRaster, RasterDefinition rasterDef, float threshold = 0.1f, int minLengthPixels = 8, double simplifyTolerance = 0.5, double cullLengthWorld = 0.0)
	{
		int cols = rasterDef.Cols;
		int rows = rasterDef.Rows;
		int count = cols * rows;
		ReadOnlySpan<float> probabilityRasterSpan = probabilityRaster.Span;
		byte[] byteValues = new byte[count];
		if (Avx.IsSupported)
		{
			ThresholdAvx(probabilityRasterSpan, byteValues, threshold);
		}
		else
		{
			ThresholdScalar(probabilityRasterSpan, byteValues, threshold);
		}
		ZhangSuenThinning.Thin(byteValues, cols, rows);
		PolylineCollection polylineCollection = SkeletonTracer.Trace(byteValues, cols, rows, rasterDef, minLengthPixels, simplifyTolerance);
		if (cullLengthWorld > 0.0)
		{
			polylineCollection.RemoveShorterThan(cullLengthWorld);
		}
		return polylineCollection;
	}

	private unsafe static void ThresholdAvx(ReadOnlySpan<float> src, Span<byte> dst, float threshold)
	{
		Vector256<float> right = Vector256.Create(threshold);
		int i = 0;
		int count = src.Length - 7;
		fixed (float* src2 = src)
		{
			fixed (byte* dst2 = dst)
			{
				for (; i <= count; i += 8)
				{
					int moveMask = Avx.MoveMask(Avx.Compare(Avx.LoadVector256(src2 + i), right, FloatComparisonMode.OrderedGreaterThanOrEqualSignaling));
					dst2[i] = (byte)(moveMask & 1);
					dst2[i + 1] = (byte)((moveMask >> 1) & 1);
					dst2[i + 2] = (byte)((moveMask >> 2) & 1);
					dst2[i + 3] = (byte)((moveMask >> 3) & 1);
					dst2[i + 4] = (byte)((moveMask >> 4) & 1);
					dst2[i + 5] = (byte)((moveMask >> 5) & 1);
					dst2[i + 6] = (byte)((moveMask >> 6) & 1);
					dst2[i + 7] = (byte)((moveMask >> 7) & 1);
				}
			}
		}
		for (; i < src.Length; i++)
		{
			dst[i] = ((src[i] >= threshold) ? ((byte)1) : ((byte)0));
		}
	}

	private static void ThresholdScalar(ReadOnlySpan<float> src, Span<byte> dst, float threshold)
	{
		for (int i = 0; i < src.Length; i++)
		{
			dst[i] = ((src[i] >= threshold) ? ((byte)1) : ((byte)0));
		}
	}
}
