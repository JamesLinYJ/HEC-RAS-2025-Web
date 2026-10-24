using System;
using System.Collections.Generic;
using System.Numerics;
using Utility;

namespace Geospatial.Rendering.Editing;

public sealed class HessianRidgenessWalker
{
	private static readonly double _sqrt2 = Math.Sqrt(2.0);

	private static readonly (int dr, int dc, double len)[] _neighbors = new (int, int, double)[8]
	{
		(-1, -1, _sqrt2),
		(-1, 0, 1.0),
		(-1, 1, _sqrt2),
		(0, -1, 1.0),
		(0, 1, 1.0),
		(1, -1, _sqrt2),
		(1, 0, 1.0),
		(1, 1, _sqrt2)
	};

	private static readonly double[] _scales = new double[2] { 2.0, 4.0 };

	private const double _percentileForNorm = 0.99;

	private const int _parallelRowChunk = 32;

	private bool _featureCacheValid;

	private int _cacheRows;

	private int _cacheCols;

	private double _cacheCellSize;

	private double _cacheMinX;

	private double _cacheMaxY;

	private double _cacheFrangiBeta;

	private float[] _featureBuf;

	private bool[] _untraversable;

	private float[] _smoothBuf;

	private float[] _smoothScratch;

	private float[] _frangiPerScale;

	private float[] _samplePool;

	private float[][] _kernelCache;

	private int[] _parentBuf;

	private double[] _gScoreBuf;

	private int[] _gGen;

	private int _generation;

	private PriorityQueue<int, double> _openPq;

	private List<(int row, int col)> _pixelPathBuf;

	public bool TryFindPath(float[] zGrid, int rows, int cols, double cellSize, double minX, double maxY, double zMin, double zMax, int aRow, int aCol, int bRow, int bCol, double frangiBeta, double featurePenaltyKappa, double offFeatureBailMultiplier, int minExpansionsBudget, int expansionsPerEuclidPx, int maxEuclidCells, out List<(int row, int col)> path)
	{
		path = null;
		if (zGrid == null)
		{
			return false;
		}
		if (rows <= 0 || cols <= 0)
		{
			return false;
		}
		int rowsTimesCols = rows * cols;
		if (zGrid.Length < rowsTimesCols)
		{
			return false;
		}
		if (!InBounds(aRow, aCol, rows, cols))
		{
			return false;
		}
		if (!InBounds(bRow, bCol, rows, cols))
		{
			return false;
		}
		double heuristic = Heuristic(aRow, aCol, bRow, bCol);
		if (heuristic > (double)maxEuclidCells)
		{
			return false;
		}
		if (!CacheMatches(rows, cols, cellSize, minX, maxY, frangiBeta))
		{
			RebuildFeatureSurface(zGrid, rows, cols, frangiBeta);
			_cacheRows = rows;
			_cacheCols = cols;
			_cacheCellSize = cellSize;
			_cacheMinX = minX;
			_cacheMaxY = maxY;
			_cacheFrangiBeta = frangiBeta;
			_featureCacheValid = true;
		}
		int aRowColsTerm = aRow * cols + aCol;
		int bRowColsTerm = bRow * cols + bCol;
		if (_untraversable[aRowColsTerm] || _untraversable[bRowColsTerm])
		{
			return false;
		}
		List<(int, int)> pixelPathBuf = _pixelPathBuf ?? (_pixelPathBuf = new List<(int, int)>(256));
		pixelPathBuf.Clear();
		if (aRowColsTerm == bRowColsTerm)
		{
			pixelPathBuf.Add((aRow, aCol));
			path = pixelPathBuf;
			return true;
		}
		Buffers.EnsureArraySize(ref _parentBuf, rowsTimesCols);
		Buffers.EnsureArraySize(ref _gScoreBuf, rowsTimesCols);
		Buffers.EnsureArraySize(ref _gGen, rowsTimesCols);
		_generation++;
		if (_generation == 0)
		{
			Array.Clear(_gGen, 0, _gGen.Length);
			_generation = 1;
		}
		PriorityQueue<int, double> priorityQueue = _openPq ?? (_openPq = new PriorityQueue<int, double>());
		priorityQueue.Clear();
		_gScoreBuf[aRowColsTerm] = 0.0;
		_parentBuf[aRowColsTerm] = -1;
		_gGen[aRowColsTerm] = _generation;
		priorityQueue.Enqueue(aRowColsTerm, Heuristic(aRow, aCol, bRow, bCol));
		int minExpansionsBudgetExpansionsPerEuclidPxTerm = Math.Max(minExpansionsBudget, (int)((double)expansionsPerEuclidPx * heuristic));
		double offFeatureBailMultiplierTimesHeuristic = offFeatureBailMultiplier * heuristic;
		int ordinal = 0;
		while (priorityQueue.Count > 0)
		{
			int parentBuf = priorityQueue.Dequeue();
			if (parentBuf == bRowColsTerm)
			{
				Reconstruct(_parentBuf, parentBuf, cols, pixelPathBuf);
				path = pixelPathBuf;
				return true;
			}
			double gScoreBuf = _gScoreBuf[parentBuf];
			if (gScoreBuf > offFeatureBailMultiplierTimesHeuristic)
			{
				continue;
			}
			ordinal++;
			if (ordinal > minExpansionsBudgetExpansionsPerEuclidPxTerm)
			{
				return false;
			}
			int parentBufOverCols = parentBuf / cols;
			int parentBufColsTerm = parentBuf - parentBufOverCols * cols;
			for (int i = 0; i < _neighbors.Length; i++)
			{
				(int, int, double) tuple = _neighbors[i];
				int tupleTerm = parentBufOverCols + tuple.Item1;
				int tupleTerm2 = parentBufColsTerm + tuple.Item2;
				if (!InBounds(tupleTerm, tupleTerm2, rows, cols))
				{
					continue;
				}
				int parentBufTupleTerm = parentBuf + tuple.Item1 * cols + tuple.Item2;
				if (!_untraversable[parentBufTupleTerm])
				{
					double featureBuf = _featureBuf[parentBufTupleTerm];
					double gScoreBuf2 = gScoreBuf + tuple.Item3 * (1.0 + featurePenaltyKappa * (1.0 - featureBuf));
					double gScoreBuf3 = ((_gGen[parentBufTupleTerm] == _generation) ? _gScoreBuf[parentBufTupleTerm] : double.PositiveInfinity);
					if (!(gScoreBuf2 >= gScoreBuf3))
					{
						_gScoreBuf[parentBufTupleTerm] = gScoreBuf2;
						_parentBuf[parentBufTupleTerm] = parentBuf;
						_gGen[parentBufTupleTerm] = _generation;
						double priority = gScoreBuf2 + Heuristic(tupleTerm, tupleTerm2, bRow, bCol);
						priorityQueue.Enqueue(parentBufTupleTerm, priority);
					}
				}
			}
		}
		return false;
	}

	private bool CacheMatches(int rows, int cols, double cellSize, double minX, double maxY, double frangiBeta)
	{
		if (!_featureCacheValid)
		{
			return false;
		}
		if (_cacheRows != rows || _cacheCols != cols)
		{
			return false;
		}
		double cellSizeTerm = Math.Abs(cellSize) * 1E-06;
		if (Math.Abs(_cacheCellSize - cellSize) > cellSizeTerm)
		{
			return false;
		}
		if (Math.Abs(_cacheMinX - minX) > cellSizeTerm)
		{
			return false;
		}
		if (Math.Abs(_cacheMaxY - maxY) > cellSizeTerm)
		{
			return false;
		}
		if (_cacheFrangiBeta != frangiBeta)
		{
			return false;
		}
		return true;
	}

	private void RebuildFeatureSurface(float[] zGrid, int rows, int cols, double frangiBeta)
	{
		int rowsTimesCols = rows * cols;
		Buffers.EnsureArraySize(ref _featureBuf, rowsTimesCols);
		Buffers.EnsureArraySize(ref _untraversable, rowsTimesCols);
		Buffers.EnsureArraySize(ref _smoothBuf, rowsTimesCols);
		Buffers.EnsureArraySize(ref _smoothScratch, rowsTimesCols);
		Buffers.EnsureArraySize(ref _frangiPerScale, rowsTimesCols);
		Array.Clear(_featureBuf, 0, rowsTimesCols);
		for (int i = 0; i < _scales.Length; i++)
		{
			double scale = _scales[i];
			float[] orBuildKernel = GetOrBuildKernel(i, scale);
			NaNAwareSeparableGaussian(zGrid, _smoothBuf, _smoothScratch, rows, cols, orBuildKernel, (i == 0) ? _untraversable : null);
			ComputeFrangiAtScale(_smoothBuf, _frangiPerScale, rows, cols, frangiBeta);
			double scalePolynomial = scale * scale;
			float gammaWeightF = (float)scalePolynomial;
			float[] featureBuf = _featureBuf;
			float[] frangiPerScale = _frangiPerScale;
			Parallel.SmartFor(rows, (int rStart, int rEnd) =>
			{
				for (int j = rStart; j <= rEnd; j++)
				{
					int colsTerm = j * cols;
					for (int k = 0; k < cols; k++)
					{
						int ordinal = colsTerm + k;
						float featureBuf2 = gammaWeightF * frangiPerScale[ordinal];
						if (featureBuf2 > featureBuf[ordinal])
						{
							featureBuf[ordinal] = featureBuf2;
						}
					}
				}
			}, 32);
		}
		NormalizeFeatureBuffer(rows, cols);
	}

	private float[] GetOrBuildKernel(int scaleIdx, double sigma)
	{
		if (_kernelCache == null || _kernelCache.Length != _scales.Length)
		{
			_kernelCache = new float[_scales.Length][];
		}
		float[] kernelCache = _kernelCache[scaleIdx];
		if (kernelCache != null)
		{
			return kernelCache;
		}
		int count = Math.Max(1, (int)Math.Ceiling(sigma * 2.5));
		float[] kernelCacheArray = new float[count * 2 + 1];
		double sigmaTerm = 1.0 / (2.0 * sigma * sigma);
		for (int i = -count; i <= count; i++)
		{
			kernelCacheArray[i + count] = (float)Math.Exp((double)(-(i * i)) * sigmaTerm);
		}
		_kernelCache[scaleIdx] = kernelCacheArray;
		return kernelCacheArray;
	}

	private static void NaNAwareSeparableGaussian(float[] src, float[] dst, float[] scratch, int rows, int cols, float[] kernel, bool[] untraversableOut)
	{
		int kernelLength = kernel.Length;
		int radius = kernelLength - 1 >> 1;
		double kernelSum = 0.0;
		for (int i = 0; i < kernelLength; i++)
		{
			kernelSum += (double)kernel[i];
		}
		float invKernelSum = ((kernelSum > 0.0) ? ((float)(1.0 / kernelSum)) : 0f);
		Parallel.SmartFor(rows, (int rStart, int rEnd) =>
		{
			for (int j = rStart; j <= rEnd; j++)
			{
				HorizontalRow(src, scratch, j, cols, kernel, radius, invKernelSum, untraversableOut);
			}
		}, 32);
		Parallel.SmartFor(rows, (int rStart, int rEnd) =>
		{
			for (int j = rStart; j <= rEnd; j++)
			{
				VerticalRow(scratch, dst, j, rows, cols, kernel, radius, invKernelSum);
			}
		}, 32);
	}

	private static void HorizontalRow(float[] src, float[] scratch, int r, int cols, float[] kernel, int radius, float invKernelSum, bool[] untraversableOut)
	{
		int colsTerm = r * cols;
		if (untraversableOut != null)
		{
			for (int i = 0; i < cols; i++)
			{
				untraversableOut[colsTerm + i] = float.IsNaN(src[colsTerm + i]);
			}
		}
		bool condition = false;
		for (int j = 0; j < cols; j++)
		{
			if (float.IsNaN(src[colsTerm + j]))
			{
				condition = true;
				break;
			}
		}
		if (!condition)
		{
			HorizontalFastRow(src, scratch, colsTerm, cols, kernel, radius, invKernelSum);
		}
		else
		{
			HorizontalSlowRow(src, scratch, colsTerm, cols, kernel, radius);
		}
	}

	private static void HorizontalFastRow(float[] src, float[] scratch, int rowBase, int cols, float[] kernel, int radius, float invKernelSum)
	{
		int count2 = cols - radius;
		int kernelLength = kernel.Length;
		int count3 = Math.Min(radius, cols);
		for (int i = 0; i < count3; i++)
		{
			float computed = 0f;
			for (int j = -radius; j <= radius; j++)
			{
				int mirrorIndex = MirrorIndex(i + j, cols);
				computed += src[rowBase + mirrorIndex] * kernel[j + radius];
			}
			scratch[rowBase + i] = computed * invKernelSum;
		}
		int count = Vector<float>.Count;
		int k = radius;
		if (count2 > radius)
		{
			for (int count4 = count2 - count; k <= count4; k += count)
			{
				Vector<float> zero = Vector<float>.Zero;
				int rowBaseRadiusTerm = rowBase + k - radius;
				for (int l = 0; l < kernelLength; l++)
				{
					Vector<float> vector = new Vector<float>(kernel[l]);
					Vector<float> vector2 = new Vector<float>(src, rowBaseRadiusTerm + l);
					zero += vector2 * vector;
				}
				(zero * invKernelSum).CopyTo(scratch, rowBase + k);
			}
			for (; k < count2; k++)
			{
				float computed2 = 0f;
				int rowBaseRadiusTerm2 = rowBase + k - radius;
				for (int m = 0; m < kernelLength; m++)
				{
					computed2 += src[rowBaseRadiusTerm2 + m] * kernel[m];
				}
				scratch[rowBase + k] = computed2 * invKernelSum;
			}
		}
		for (int n = Math.Max(count2, count3); n < cols; n++)
		{
			float computed3 = 0f;
			for (int radius2 = -radius; radius2 <= radius; radius2++)
			{
				int mirrorIndex2 = MirrorIndex(n + radius2, cols);
				computed3 += src[rowBase + mirrorIndex2] * kernel[radius2 + radius];
			}
			scratch[rowBase + n] = computed3 * invKernelSum;
		}
	}

	private static void HorizontalSlowRow(float[] src, float[] scratch, int rowBase, int cols, float[] kernel, int radius)
	{
		for (int i = 0; i < cols; i++)
		{
			float src2TimesKernel2Sum = 0f;
			double kernelSum = 0.0;
			for (int j = -radius; j <= radius; j++)
			{
				int mirrorIndex = MirrorIndex(i + j, cols);
				float src2 = src[rowBase + mirrorIndex];
				if (!float.IsNaN(src2))
				{
					float kernel2 = kernel[j + radius];
					src2TimesKernel2Sum += src2 * kernel2;
					kernelSum += (double)kernel2;
				}
			}
			scratch[rowBase + i] = ((kernelSum > 0.0) ? ((float)((double)src2TimesKernel2Sum / kernelSum)) : float.NaN);
		}
	}

	private static void VerticalRow(float[] scratch, float[] dst, int r, int rows, int cols, float[] kernel, int radius, float invKernelSum)
	{
		int rowBase = r * cols;
		int radiusTerm = r - radius;
		int count = r + radius;
		if (radiusTerm < 0)
		{
			radiusTerm = 0;
		}
		if (count >= rows)
		{
			count = rows - 1;
		}
		bool condition = false;
		for (int i = radiusTerm; i <= count; i++)
		{
			if (condition)
			{
				break;
			}
			int colsTerm = i * cols;
			for (int j = 0; j < cols; j++)
			{
				if (float.IsNaN(scratch[colsTerm + j]))
				{
					condition = true;
					break;
				}
			}
		}
		if (!condition)
		{
			VerticalFastRow(scratch, dst, r, rows, cols, kernel, radius, invKernelSum, rowBase);
		}
		else
		{
			VerticalSlowRow(scratch, dst, r, rows, cols, kernel, radius, rowBase);
		}
	}

	private static void VerticalFastRow(float[] scratch, float[] dst, int r, int rows, int cols, float[] kernel, int radius, float invKernelSum, int rowBase)
	{
		int kernelLength = kernel.Length;
		int count = Vector<float>.Count;
		bool radiusRowsTerm = r >= radius && r < rows - radius;
		int i = 0;
		for (int count2 = cols - count; i <= count2; i += count)
		{
			Vector<float> zero = Vector<float>.Zero;
			for (int j = 0; j < kernelLength; j++)
			{
				int mirrorIndex = (radiusRowsTerm ? (r + j - radius) : MirrorIndex(r + j - radius, rows));
				Vector<float> vector = new Vector<float>(kernel[j]);
				Vector<float> vector2 = new Vector<float>(scratch, mirrorIndex * cols + i);
				zero += vector2 * vector;
			}
			(zero * invKernelSum).CopyTo(dst, rowBase + i);
		}
		for (; i < cols; i++)
		{
			float computed = 0f;
			for (int k = 0; k < kernelLength; k++)
			{
				int mirrorIndex2 = (radiusRowsTerm ? (r + k - radius) : MirrorIndex(r + k - radius, rows));
				computed += scratch[mirrorIndex2 * cols + i] * kernel[k];
			}
			dst[rowBase + i] = computed * invKernelSum;
		}
	}

	private static void VerticalSlowRow(float[] scratch, float[] dst, int r, int rows, int cols, float[] kernel, int radius, int rowBase)
	{
		for (int i = 0; i < cols; i++)
		{
			float scratch2TimesKernel2Sum = 0f;
			double kernelSum = 0.0;
			for (int j = -radius; j <= radius; j++)
			{
				int mirrorIndex = MirrorIndex(r + j, rows);
				float scratch2 = scratch[mirrorIndex * cols + i];
				if (!float.IsNaN(scratch2))
				{
					float kernel2 = kernel[j + radius];
					scratch2TimesKernel2Sum += scratch2 * kernel2;
					kernelSum += (double)kernel2;
				}
			}
			dst[rowBase + i] = ((kernelSum > 0.0) ? ((float)((double)scratch2TimesKernel2Sum / kernelSum)) : float.NaN);
		}
	}

	private static void ComputeFrangiAtScale(float[] smooth, float[] dst, int rows, int cols, double frangiBeta)
	{
		int count = rows * cols;
		Array.Fill(dst, 0f, 0, count);
		if (rows < 3 || cols < 3)
		{
			return;
		}
		int rowsOffset = rows - 2;
		int count2 = (rowsOffset + 32 - 1) / 32;
		if (count2 < 1)
		{
			count2 = 1;
		}
		double[] perChunkMaxSq = new double[count2];
		Parallel.SmartFor(rowsOffset, (int rStart, int rEnd) =>
		{
			double perChunkMaxSq2 = 0.0;
			for (int i = rStart; i <= rEnd; i++)
			{
				int r = i + 1;
				for (int j = 1; j < cols - 1; j++)
				{
					if (TryHessianEig(smooth, r, j, cols, out var _, out var _, out var sSq, out var _) && sSq > perChunkMaxSq2)
					{
						perChunkMaxSq2 = sSq;
					}
				}
			}
			int scaledRStart = rStart / 32;
			perChunkMaxSq[scaledRStart] = perChunkMaxSq2;
		}, 32);
		double perChunkMaxSq3 = 0.0;
		for (int intervalIndex = 0; intervalIndex < perChunkMaxSq.Length; intervalIndex++)
		{
			if (perChunkMaxSq[intervalIndex] > perChunkMaxSq3)
			{
				perChunkMaxSq3 = perChunkMaxSq[intervalIndex];
			}
		}
		if (perChunkMaxSq3 <= 0.0)
		{
			return;
		}
		double perChunkMaxSq3Term = 0.5 * Math.Sqrt(perChunkMaxSq3);
		float inv2BetaSq = (float)(1.0 / (2.0 * frangiBeta * frangiBeta));
		float inv2cSq = (float)(1.0 / (2.0 * perChunkMaxSq3Term * perChunkMaxSq3Term));
		Parallel.SmartFor(rowsOffset, (int rStart, int rEnd) =>
		{
			for (int i = rStart; i <= rEnd; i++)
			{
				int ordinal = i + 1;
				int colsTerm = ordinal * cols;
				for (int j = 1; j < cols - 1; j++)
				{
					if (TryHessianEig(smooth, ordinal, j, cols, out var _, out var _, out var sSq, out var rbSq))
					{
						float rbSq2 = (float)rbSq;
						float sSq2 = (float)sSq;
						float dst2 = MathF.Exp((0f - rbSq2) * inv2BetaSq) * (1f - MathF.Exp((0f - sSq2) * inv2cSq));
						if (dst2 < 0f)
						{
							dst2 = 0f;
						}
						if (dst2 > 1f)
						{
							dst2 = 1f;
						}
						dst[colsTerm + j] = dst2;
					}
				}
			}
		}, 32);
	}

	private static bool TryHessianEig(float[] smooth, int r, int c, int cols, out double l1, out double l2, out double sSq, out double rbSq)
	{
		l1 = 0.0;
		l2 = 0.0;
		sSq = 0.0;
		rbSq = 0.0;
		int colsTerm = r * cols + c;
		float smooth2 = smooth[colsTerm];
		float smooth3 = smooth[colsTerm - 1];
		float smooth4 = smooth[colsTerm + 1];
		float smooth5 = smooth[colsTerm - cols];
		float smooth6 = smooth[colsTerm + cols];
		float smooth7 = smooth[colsTerm - cols - 1];
		float smooth8 = smooth[colsTerm - cols + 1];
		float smooth9 = smooth[colsTerm + cols - 1];
		float smooth10 = smooth[colsTerm + cols + 1];
		if (float.IsNaN(smooth2) || float.IsNaN(smooth3) || float.IsNaN(smooth4) || float.IsNaN(smooth5) || float.IsNaN(smooth6) || float.IsNaN(smooth7) || float.IsNaN(smooth8) || float.IsNaN(smooth9) || float.IsNaN(smooth10))
		{
			return false;
		}
		double smooth4Smooth2Term = (double)smooth4 - 2.0 * (double)smooth2 + (double)smooth3;
		double smooth6Smooth2Term = (double)smooth6 - 2.0 * (double)smooth2 + (double)smooth5;
		double smooth10Smooth9Term = 0.25 * (double)(smooth10 - smooth9 - smooth8 + smooth7);
		double computed = smooth4Smooth2Term + smooth6Smooth2Term;
		double computed2 = smooth4Smooth2Term * smooth6Smooth2Term - smooth10Smooth9Term * smooth10Smooth9Term;
		double computed3 = computed * computed * 0.25 - computed2;
		if (computed3 < 0.0)
		{
			computed3 = 0.0;
		}
		double computed4 = Math.Sqrt(computed3);
		double computed5 = computed * 0.5 + computed4;
		double computed6 = computed * 0.5 - computed4;
		if (Math.Abs(computed5) >= Math.Abs(computed6))
		{
			l1 = computed5;
			l2 = computed6;
		}
		else
		{
			l1 = computed6;
			l2 = computed5;
		}
		sSq = l1 * l1 + l2 * l2;
		if (Math.Abs(l1) < 1E-30)
		{
			rbSq = 0.0;
		}
		else
		{
			double l2OverL1 = l2 / l1;
			rbSq = l2OverL1 * l2OverL1;
		}
		return true;
	}

	private void NormalizeFeatureBuffer(int rows, int cols)
	{
		int count = rows * cols;
		int count2 = Math.Min(count, 65536);
		Buffers.EnsureArraySize(ref _samplePool, count2);
		int ordinal = 0;
		if (count <= count2)
		{
			for (int i = 0; i < count; i++)
			{
				float featureBuf2 = _featureBuf[i];
				if (featureBuf2 > 0f && !float.IsNaN(featureBuf2))
				{
					_samplePool[ordinal++] = featureBuf2;
				}
			}
		}
		else
		{
			int countOverCount = count / count2;
			if (countOverCount < 1)
			{
				countOverCount = 1;
			}
			for (int j = 0; j < count; j += countOverCount)
			{
				if (ordinal >= count2)
				{
					break;
				}
				float featureBuf3 = _featureBuf[j];
				if (featureBuf3 > 0f && !float.IsNaN(featureBuf3))
				{
					_samplePool[ordinal++] = featureBuf3;
				}
			}
		}
		if (ordinal < 8)
		{
			Array.Fill(_featureBuf, 0f, 0, count);
			return;
		}
		int ordinal2 = (int)(0.99 * (double)(ordinal - 1));
		if (ordinal2 < 0)
		{
			ordinal2 = 0;
		}
		if (ordinal2 >= ordinal)
		{
			ordinal2 = ordinal - 1;
		}
		double quickSelect = QuickSelect(_samplePool, 0, ordinal - 1, ordinal2);
		if (quickSelect <= 0.0)
		{
			Array.Fill(_featureBuf, 0f, 0, count);
			return;
		}
		float invF = (float)(1.0 / quickSelect);
		float[] featureBuf = _featureBuf;
		bool[] untraversable = _untraversable;
		Parallel.SmartFor(rows, (int rStart, int rEnd) =>
		{
			for (int k = rStart; k <= rEnd; k++)
			{
				int colsTerm = k * cols;
				for (int l = 0; l < cols; l++)
				{
					int ordinal3 = colsTerm + l;
					float featureBuf4 = featureBuf[ordinal3];
					if (float.IsNaN(featureBuf4) || untraversable[ordinal3])
					{
						featureBuf[ordinal3] = 0f;
					}
					else
					{
						float featureBuf5 = featureBuf4 * invF;
						if (featureBuf5 < 0f)
						{
							featureBuf5 = 0f;
						}
						if (featureBuf5 > 1f)
						{
							featureBuf5 = 1f;
						}
						featureBuf[ordinal3] = featureBuf5;
					}
				}
			}
		}, 32);
	}

	private static float QuickSelect(float[] arr, int lo, int hi, int k)
	{
		while (lo < hi)
		{
			int partition = Partition(arr, lo, hi);
			if (k == partition)
			{
				return arr[partition];
			}
			if (k < partition)
			{
				hi = partition - 1;
			}
			else
			{
				lo = partition + 1;
			}
		}
		return arr[lo];
	}

	private static int Partition(float[] arr, int lo, int hi)
	{
		int loHiTerm = lo + (hi - lo >> 1);
		if (arr[loHiTerm] < arr[lo])
		{
			Swap(arr, lo, loHiTerm);
		}
		if (arr[hi] < arr[lo])
		{
			Swap(arr, lo, hi);
		}
		if (arr[loHiTerm] < arr[hi])
		{
			Swap(arr, loHiTerm, hi);
		}
		float arr2 = arr[hi];
		int lo2 = lo;
		for (int i = lo; i < hi; i++)
		{
			if (arr[i] < arr2)
			{
				Swap(arr, i, lo2);
				lo2++;
			}
		}
		Swap(arr, lo2, hi);
		return lo2;
	}

	private static void Swap(float[] arr, int i, int j)
	{
		float arr2 = arr[i];
		arr[i] = arr[j];
		arr[j] = arr2;
	}

	private static int MirrorIndex(int i, int len)
	{
		if (len <= 1)
		{
			return 0;
		}
		int lenTerm = 2 * (len - 1);
		int ordinal = i % lenTerm;
		if (ordinal < 0)
		{
			ordinal += lenTerm;
		}
		if (ordinal >= len)
		{
			return lenTerm - ordinal;
		}
		return ordinal;
	}

	private static bool InBounds(int row, int col, int rows, int cols)
	{
		if (row >= 0 && row < rows && col >= 0)
		{
			return col < cols;
		}
		return false;
	}

	private static double Heuristic(int r0, int c0, int r1, int c1)
	{
		double r1AboveR0 = r1 - r0;
		double c1AboveC0 = c1 - c0;
		return Math.Sqrt(r1AboveR0 * r1AboveR0 + c1AboveC0 * c1AboveC0);
	}

	private static void Reconstruct(int[] parent, int goalIdx, int cols, List<(int row, int col)> output)
	{
		for (int goalIdx2 = goalIdx; goalIdx2 != -1; goalIdx2 = parent[goalIdx2])
		{
			int goalIdx2OverCols = goalIdx2 / cols;
			int goalIdx2ColsTerm = goalIdx2 - goalIdx2OverCols * cols;
			output.Add((goalIdx2OverCols, goalIdx2ColsTerm));
		}
		output.Reverse();
	}
}
