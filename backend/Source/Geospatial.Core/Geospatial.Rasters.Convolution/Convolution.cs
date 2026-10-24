using System;
using System.Buffers;
using System.Linq;
using System.Threading.Tasks;
using Utility.Extensions;

namespace Geospatial.Rasters.Convolution;

public static class Convolution
{
	public unsafe static float[] ConvolveBoxSAT(float[] data, RasterDefinition def, int boxRadius = 5, float scale = 0.3f, int offset = 160, int taper = 5)
	{
		boxRadius = Math.Abs(boxRadius);
		if (boxRadius == 0)
		{
			return data.ToArray();
		}
		float[] floatValues = new float[data.Length];
		fixed (float* writePtr = floatValues)
		{
			fixed (float* localValue = data)
			{
				SummedAreaTable(localValue, def.Cols, def.Rows, boxRadius, out var SAT);
				ComputeFromSAT(localValue, writePtr, SAT, boxRadius, taper, def.Cols, def.Rows, scale, offset);
			}
		}
		return floatValues;
	}

	public static void ConvolveBinomial3x3_NoDataAware(ReadOnlySpan<float> src, Span<float> dst, RasterDefinition def)
	{
		int rows = def.Rows;
		int cols = def.Cols;
		int count = rows * cols;
		if (src.Length < count || dst.Length < count || count == 0)
		{
			return;
		}
		if (src.Slice(0, count).IndexOf(-9999f) < 0)
		{
			Binomial3x3AllValid(src, dst, rows, cols);
			return;
		}
		float[] rent = ArrayPool<float>.Shared.Rent(4 * count);
		try
		{
			Span<float> rentSpan = rent.AsSpan(0, count);
			Span<float> countSpan = rent.AsSpan(count, count);
			Span<float> dst2 = rent.AsSpan(2 * count, count);
			Span<float> dst3 = rent.AsSpan(3 * count, count);
			for (int i = 0; i < count; i++)
			{
				float src2 = src[i];
				bool src2Term = src2 != -9999f;
				rentSpan[i] = (src2Term ? src2 : 0f);
				countSpan[i] = (src2Term ? 1f : 0f);
			}
			Horizontal121(rentSpan, dst2, rows, cols);
			Horizontal121(countSpan, dst3, rows, cols);
			for (int j = 0; j < rows; j++)
			{
				int colsTerm = j * cols;
				int colsTerm2 = ((j > 0) ? (colsTerm - cols) : (-1));
				int rowsColsTerm = ((j < rows - 1) ? (colsTerm + cols) : (-1));
				for (int k = 0; k < cols; k++)
				{
					int index = colsTerm + k;
					float src3 = src[index];
					if (src3 == -9999f)
					{
						dst[index] = -9999f;
						continue;
					}
					float dst2Sum = 2f * dst2[index];
					float dst3Sum = 2f * dst3[index];
					if (colsTerm2 >= 0)
					{
						dst2Sum += dst2[colsTerm2 + k];
						dst3Sum += dst3[colsTerm2 + k];
					}
					if (rowsColsTerm >= 0)
					{
						dst2Sum += dst2[rowsColsTerm + k];
						dst3Sum += dst3[rowsColsTerm + k];
					}
					dst[index] = ((dst3Sum > 0f) ? (dst2Sum / dst3Sum) : src3);
				}
			}
		}
		finally
		{
			ArrayPool<float>.Shared.Return(rent);
		}
	}

	private static void Binomial3x3AllValid(ReadOnlySpan<float> src, Span<float> dst, int rows, int cols)
	{
		int minimumLength = rows * cols;
		float[] rent = ArrayPool<float>.Shared.Rent(minimumLength);
		try
		{
			Horizontal121(src, rent, rows, cols);
			for (int i = 0; i < rows; i++)
			{
				int colsTerm = i * cols;
				int colsTerm2 = ((i > 0) ? (colsTerm - cols) : (-1));
				int rowsColsTerm = ((i < rows - 1) ? (colsTerm + cols) : (-1));
				float computed = 2f + ((colsTerm2 >= 0) ? 1f : 0f) + ((rowsColsTerm >= 0) ? 1f : 0f);
				float computed2 = 4f * computed;
				float colsTerm3 = ((cols == 1) ? 2f : 3f) * computed;
				for (int j = 0; j < cols; j++)
				{
					int ordinal = colsTerm + j;
					float rentSum = 2f * rent[ordinal];
					if (colsTerm2 >= 0)
					{
						rentSum += rent[colsTerm2 + j];
					}
					if (rowsColsTerm >= 0)
					{
						rentSum += rent[rowsColsTerm + j];
					}
					float colsTerm4 = ((j > 0 && j < cols - 1) ? computed2 : colsTerm3);
					dst[ordinal] = rentSum / colsTerm4;
				}
			}
		}
		finally
		{
			ArrayPool<float>.Shared.Return(rent);
		}
	}

	private static void Horizontal121(ReadOnlySpan<float> src, Span<float> dst, int rows, int cols)
	{
		for (int i = 0; i < rows; i++)
		{
			int colsTerm = i * cols;
			if (cols == 1)
			{
				dst[colsTerm] = 2f * src[colsTerm];
				continue;
			}
			dst[colsTerm] = 2f * src[colsTerm] + src[colsTerm + 1];
			for (int j = 1; j < cols - 1; j++)
			{
				int ordinal = colsTerm + j;
				dst[ordinal] = src[ordinal - 1] + 2f * src[ordinal] + src[ordinal + 1];
			}
			dst[colsTerm + cols - 1] = src[colsTerm + cols - 2] + 2f * src[colsTerm + cols - 1];
		}
	}

	public unsafe static float[] ConvolveBoxSAT_NoDataAware(float[] data, RasterDefinition def, int boxRadius = 5, float scale = 0.3f, int offset = 160, int taper = 5)
	{
		boxRadius = Math.Abs(boxRadius);
		if (boxRadius == 0)
		{
			return data.ToArray();
		}
		float[] floatValues = new float[data.Length];
		fixed (float* outDataPtr = floatValues)
		{
			fixed (float* localValue = data)
			{
				SummedAreaTable(localValue, def.Cols, def.Rows, boxRadius, out var SAT, out var invalidDataCount);
				ComputeFromSAT(localValue, outDataPtr, SAT, invalidDataCount, boxRadius, taper, def.Cols, def.Rows, scale, offset);
			}
		}
		return floatValues;
	}

	private unsafe static void ComputeFromSAT(float* readPtr, float* writePtr, double[,] sat, int radius, int taper, int nWidth, int nHeight, float scale, int offset)
	{
		int scaledRadius = radius * 2;
		int area = (scaledRadius + 1) * (scaledRadius + 1);
		bool allowTaper = taper > 0;
		int lBoundTaper = taper;
		int rBoundTaper = nWidth - taper - 1;
		int uBoundTaper = taper;
		int bBoundTaper = nHeight - taper - 1;
		int strideBytes = nWidth;
		Parallel.For(0, nHeight, (int y) =>
		{
			int strideBytesTerm = y * strideBytes;
			int radiusTerm = radius * 2 + 1;
			int radiusTerm2 = y + radius * 2 + 1;
			for (int i = 0; i < nWidth; i++)
			{
				double satTerm = sat[y, i];
				double satTerm2 = sat[y, radiusTerm];
				double satTerm3 = sat[radiusTerm2, i];
				float writePtr2 = (float)((sat[radiusTerm2, radiusTerm] - satTerm2 - satTerm3 + satTerm) * (double)scale / (double)area + (double)offset);
				writePtr[strideBytesTerm] = writePtr2;
				if (allowTaper)
				{
					float computed = 0f;
					if (i < lBoundTaper)
					{
						float taperTerm = (float)(taper - i) / (float)taper;
						if (y < uBoundTaper)
						{
							float taperTerm2 = (float)(taper - y) / (float)taper;
							computed = 1f - (1f - taperTerm) * (1f - taperTerm2);
						}
						else if (y > bBoundTaper)
						{
							float taperNHeightTerm = (float)(taper - (nHeight - y - 1)) / (float)taper;
							computed = 1f - (1f - taperTerm) * (1f - taperNHeightTerm);
						}
						else
						{
							computed = taperTerm;
						}
					}
					else if (i > rBoundTaper)
					{
						float taperNWidthTerm = (float)(taper - (nWidth - 1 - i)) / (float)taper;
						if (y < uBoundTaper)
						{
							float taperTerm3 = (float)(taper - y) / (float)taper;
							computed = 1f - (1f - taperNWidthTerm) * (1f - taperTerm3);
						}
						else if (y > bBoundTaper)
						{
							float taperNHeightTerm2 = (float)(taper - (nHeight - y - 1)) / (float)taper;
							computed = 1f - (1f - taperNWidthTerm) * (1f - taperNHeightTerm2);
						}
						else
						{
							computed = taperNWidthTerm;
						}
					}
					else if (y < uBoundTaper)
					{
						computed = (float)(taper - y) / (float)taper;
					}
					else if (y > bBoundTaper)
					{
						computed = (float)(taper - (nHeight - y - 1)) / (float)taper;
					}
					computed *= 0.8f;
					float computed2 = 1f - computed;
					writePtr[strideBytesTerm] = writePtr[strideBytesTerm] * computed2 + readPtr[strideBytesTerm] * computed;
				}
				strideBytesTerm++;
				radiusTerm++;
			}
		});
	}

	private unsafe static void ComputeFromSAT(float* baseDataPtr, float* outDataPtr, double[,] sat, int[,] invalidDataCount, int radius, int taper, int nWidth, int nHeight, float scale, int offset)
	{
		int scaledRadius = radius * 2;
		int area = (scaledRadius + 1) * (scaledRadius + 1);
		bool allowTaper = taper > 0;
		int lBoundTaper = taper;
		int rBoundTaper = nWidth - taper - 1;
		int uBoundTaper = taper;
		int bBoundTaper = nHeight - taper - 1;
		int strideBytes = nWidth;
		Parallel.For(0, nHeight, (int y) =>
		{
			int strideBytesTerm = y * strideBytes;
			int radiusTerm = radius * 2 + 1;
			int radiusTerm2 = y + radius * 2 + 1;
			for (int i = 0; i < nWidth; i++)
			{
				if (baseDataPtr[strideBytesTerm] == -9999f)
				{
					outDataPtr[strideBytesTerm] = -9999f;
				}
				else
				{
					double satTerm = sat[y, i];
					double satTerm2 = sat[y, radiusTerm];
					double satTerm3 = sat[radiusTerm2, i];
					double satTerm4 = sat[radiusTerm2, radiusTerm];
					int invalidDataCountTerm = invalidDataCount[y, i];
					int invalidDataCountTerm2 = invalidDataCount[y, radiusTerm];
					int invalidDataCountTerm3 = invalidDataCount[radiusTerm2, i];
					int invalidDataCountTerm4 = invalidDataCount[radiusTerm2, radiusTerm] - invalidDataCountTerm2 - invalidDataCountTerm3 + invalidDataCountTerm;
					int areaTerm = area - invalidDataCountTerm4;
					float outDataPtr2 = ((areaTerm != 0) ? ((float)((satTerm4 - satTerm2 - satTerm3 + satTerm) * (double)scale / (double)areaTerm + (double)offset)) : 0f);
					outDataPtr[strideBytesTerm] = outDataPtr2;
					if (allowTaper)
					{
						float computed = 0f;
						if (i < lBoundTaper)
						{
							float taperTerm = (float)(taper - i) / (float)taper;
							if (y < uBoundTaper)
							{
								float taperTerm2 = (float)(taper - y) / (float)taper;
								computed = 1f - (1f - taperTerm) * (1f - taperTerm2);
							}
							else if (y > bBoundTaper)
							{
								float taperNHeightTerm = (float)(taper - (nHeight - y - 1)) / (float)taper;
								computed = 1f - (1f - taperTerm) * (1f - taperNHeightTerm);
							}
							else
							{
								computed = taperTerm;
							}
						}
						else if (i > rBoundTaper)
						{
							float taperNWidthTerm = (float)(taper - (nWidth - 1 - i)) / (float)taper;
							if (y < uBoundTaper)
							{
								float taperTerm3 = (float)(taper - y) / (float)taper;
								computed = 1f - (1f - taperNWidthTerm) * (1f - taperTerm3);
							}
							else if (y > bBoundTaper)
							{
								float taperNHeightTerm2 = (float)(taper - (nHeight - y - 1)) / (float)taper;
								computed = 1f - (1f - taperNWidthTerm) * (1f - taperNHeightTerm2);
							}
							else
							{
								computed = taperNWidthTerm;
							}
						}
						else if (y < uBoundTaper)
						{
							computed = (float)(taper - y) / (float)taper;
						}
						else if (y > bBoundTaper)
						{
							computed = (float)(taper - (nHeight - y - 1)) / (float)taper;
						}
						computed *= 0.8f;
						float computed2 = 1f - computed;
						outDataPtr[strideBytesTerm] = outDataPtr[strideBytesTerm] * computed2 + baseDataPtr[strideBytesTerm] * computed;
					}
				}
				strideBytesTerm++;
				radiusTerm++;
			}
		});
	}

	private unsafe static void SummedAreaTable(byte* read, int w, int h, int stride, out int[,] r, out int[,] g, out int[,] b)
	{
		int[,] red = new int[h, w];
		int[,] grn = new int[h, w];
		int[,] blu = new int[h, w];
		Parallel.For(0, h, (int y) =>
		{
			int readSum = 0;
			int readSum2 = 0;
			int readSum3 = 0;
			int strideTerm = y * stride;
			for (int i = 0; i < w; i++)
			{
				readSum += read[strideTerm];
				readSum2 += read[strideTerm + 1];
				readSum3 += read[strideTerm + 2];
				blu[y, i] = readSum;
				grn[y, i] = readSum2;
				red[y, i] = readSum3;
				strideTerm += 3;
			}
		});
		Parallel.For(0, w, (int x) =>
		{
			for (int i = 1; i < h; i++)
			{
				blu[i, x] += blu[i - 1, x];
				grn[i, x] += grn[i - 1, x];
				red[i, x] += red[i - 1, x];
			}
		});
		r = red;
		g = grn;
		b = blu;
	}

	private unsafe static void SummedAreaTableWithBoundary(byte* read, int w, int h, int radius, int stride, out int[,] r, out int[,] g, out int[,] b)
	{
		int topLeftBoundaryPx = radius + 1;
		int radius2 = radius;
		int extraPxWH = topLeftBoundaryPx + radius2;
		int[,] red = new int[h + extraPxWH, w + extraPxWH];
		int[,] grn = new int[h + extraPxWH, w + extraPxWH];
		int[,] blu = new int[h + extraPxWH, w + extraPxWH];
		Parallel.For(0, h + extraPxWH, (int y) =>
		{
			int readSum = 0;
			int readSum2 = 0;
			int readSum3 = 0;
			int a = (y - radius) * stride;
			a = a.Bound(0, (h - 1) * stride);
			for (int i = 0; i < topLeftBoundaryPx; i++)
			{
				readSum += read[a];
				readSum2 += read[a + 1];
				readSum3 += read[a + 2];
				blu[y, i] = readSum;
				grn[y, i] = readSum2;
				red[y, i] = readSum3;
			}
			for (int j = topLeftBoundaryPx; j < w + topLeftBoundaryPx; j++)
			{
				readSum += read[a];
				readSum2 += read[a + 1];
				readSum3 += read[a + 2];
				blu[y, j] = readSum;
				grn[y, j] = readSum2;
				red[y, j] = readSum3;
				a += 3;
			}
			a -= 3;
			for (int k = w + topLeftBoundaryPx; k < w + extraPxWH; k++)
			{
				readSum += read[a];
				readSum2 += read[a + 1];
				readSum3 += read[a + 2];
				blu[y, k] = readSum;
				grn[y, k] = readSum2;
				red[y, k] = readSum3;
			}
		});
		Parallel.For(0, w + extraPxWH, (int x) =>
		{
			for (int i = 1; i < h + extraPxWH; i++)
			{
				blu[i, x] += blu[i - 1, x];
				grn[i, x] += grn[i - 1, x];
				red[i, x] += red[i - 1, x];
			}
		});
		r = red;
		g = grn;
		b = blu;
	}

	public unsafe static void SummedAreaTable(float* read, int w, int h, int radius, out double[,] SAT)
	{
		int topLeftBoundaryPx = radius + 1;
		int radius2 = radius;
		int extraPxWH = topLeftBoundaryPx + radius2;
		double[,] sumTemp = new double[h + extraPxWH, w + extraPxWH];
		int stride = w;
		Parallel.For(0, h + extraPxWH, (int y) =>
		{
			double readSum = 0.0;
			int a = (y - radius) * stride;
			a = a.Bound(0, (h - 1) * stride);
			for (int i = 0; i < topLeftBoundaryPx; i++)
			{
				readSum += (double)read[a];
				sumTemp[y, i] = readSum;
			}
			for (int j = topLeftBoundaryPx; j < w + topLeftBoundaryPx; j++)
			{
				readSum += (double)read[a];
				sumTemp[y, j] = readSum;
				a++;
			}
			a--;
			for (int k = w + topLeftBoundaryPx; k < w + extraPxWH; k++)
			{
				readSum += (double)read[a];
				sumTemp[y, k] = readSum;
			}
		});
		Parallel.For(0, w + extraPxWH, (int x) =>
		{
			for (int i = 1; i < h + extraPxWH; i++)
			{
				sumTemp[i, x] += sumTemp[i - 1, x];
			}
		});
		SAT = sumTemp;
	}

	public unsafe static void SummedAreaTable(float* read, int w, int h, int radius, out double[,] SAT, out int[,] invalidDataCount)
	{
		int topLeftBoundaryPx = radius + 1;
		int radius2 = radius;
		int extraPxWH = topLeftBoundaryPx + radius2;
		double[,] sumTemp = new double[h + extraPxWH, w + extraPxWH];
		int[,] invalidTemp = new int[h + extraPxWH, w + extraPxWH];
		int stride = w;
		Parallel.For(0, h + extraPxWH, (int y) =>
		{
			double readSum = 0.0;
			int invalidTemp2 = 0;
			int a = (y - radius) * stride;
			a = a.Bound(0, (h - 1) * stride);
			for (int i = 0; i < topLeftBoundaryPx; i++)
			{
				float read2 = read[a];
				if (read2 == -9999f)
				{
					invalidTemp2++;
				}
				else
				{
					readSum += (double)read2;
				}
				sumTemp[y, i] = readSum;
				invalidTemp[y, i] = invalidTemp2;
			}
			for (int j = topLeftBoundaryPx; j < w + topLeftBoundaryPx; j++)
			{
				float read3 = read[a];
				if (read3 == -9999f)
				{
					invalidTemp2++;
				}
				else
				{
					readSum += (double)read3;
				}
				sumTemp[y, j] = readSum;
				invalidTemp[y, j] = invalidTemp2;
				a++;
			}
			a--;
			for (int k = w + topLeftBoundaryPx; k < w + extraPxWH; k++)
			{
				float read4 = read[a];
				if (read4 == -9999f)
				{
					invalidTemp2++;
				}
				else
				{
					readSum += (double)read4;
				}
				sumTemp[y, k] = readSum;
				invalidTemp[y, k] = invalidTemp2;
			}
		});
		Parallel.For(0, w + extraPxWH, (int x) =>
		{
			for (int i = 1; i < h + extraPxWH; i++)
			{
				sumTemp[i, x] += sumTemp[i - 1, x];
				invalidTemp[i, x] += invalidTemp[i - 1, x];
			}
		});
		SAT = sumTemp;
		invalidDataCount = invalidTemp;
	}
}
