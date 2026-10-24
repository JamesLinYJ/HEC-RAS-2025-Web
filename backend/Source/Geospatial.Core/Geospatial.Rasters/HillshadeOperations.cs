using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Threading.Tasks;
using Utility;

namespace Geospatial.Rasters;

public static class HillshadeOperations
{
	private ref struct HillshadeParams(int ccols, int flat, float invC, int alph, int alphBl, float zf, float az, float sin, float cos, float ndv)
	{
		public readonly int cols = ccols;

		public readonly int flatCellColor = flat;

		public readonly float invcellsize8 = invC;

		public readonly int alpha = alph;

		public readonly int alphaBlack = alphBl;

		public readonly float zfactor = zf;

		public readonly float azimuth = az;

		public readonly float sin_zenith = sin;

		public readonly float cos_zenith = cos;

		public readonly float cos_azimuth = (float)Math.Cos(az);

		public readonly float sin_azimuth = (float)Math.Sin(az);

		public readonly float NoDataValue = ndv;
	}

	public const float AzimuthDefault = 315f;

	public const float ZenithDefault = 45f;

	public const float ZfactorDefault = 3f;

	public const int DefaultChunkRows = 64;

	public static bool UseOptimizedHillshade { get; set; } = true;

	public static void ComputeHillShade(RasterDefinition raster, float[] values, byte alpha, float altitude_deg, float azimuth_deg, float zfactor, float noDataValue, int[] outputGreyscaleBuffer)
	{
		float cellSize = (float)raster.CellSize;
		float invcellsize8 = (float)(1.0 / (double)(cellSize * 8f));
		double altitude_degPiTerm = (double)(90f - altitude_deg) * Math.PI / 180.0;
		float cos_zenith = (float)Math.Cos(altitude_degPiTerm);
		float sin_zenith = (float)Math.Sin(altitude_degPiTerm);
		float azimuth_degTerm = 360f - azimuth_deg + 90f;
		if (azimuth_degTerm > 360f)
		{
			azimuth_degTerm -= 360f;
		}
		float azimuth = azimuth_degTerm * ((float)Math.PI / 180f);
		uint alphaCos_zenithTerm = (uint)((float)(int)alpha * (1f - cos_zenith)) << 24;
		uint[] src = new uint[1] { alphaCos_zenithTerm };
		int[] intValues = new int[1];
		Buffer.BlockCopy(src, 0, intValues, 0, 4);
		int flatCellColor = intValues[0];
		int rows = raster.Rows;
		int cols = raster.Cols;
		Buffer.BlockCopy(new byte[4] { 0, 0, 0, alpha }, 0, intValues, 0, 4);
		int alphaBlack = intValues[0];
		System.Threading.Tasks.Parallel.For(1, rows - 1, (int row) =>
		{
			for (int i = 1; i < cols - 1; i++)
			{
				int rowColsTerm = row * cols + i;
				float colsTerm = values[rowColsTerm - cols - 1];
				float colsTerm2 = values[rowColsTerm - cols];
				float colsTerm3 = values[rowColsTerm - cols + 1];
				float computed = values[rowColsTerm - 1];
				float computed2 = values[rowColsTerm + 1];
				float colsTerm4 = values[rowColsTerm + cols - 1];
				float colsTerm5 = values[rowColsTerm + cols];
				float colsTerm6 = values[rowColsTerm + cols + 1];
				if (colsTerm != noDataValue && colsTerm2 != noDataValue && colsTerm3 != noDataValue && computed != noDataValue && computed2 != noDataValue && colsTerm4 != noDataValue && colsTerm5 != noDataValue && colsTerm6 != noDataValue)
				{
					float computed3 = colsTerm3 + computed2 + computed2 + colsTerm6 - (colsTerm + computed + computed + colsTerm4);
					float computed4 = colsTerm4 + colsTerm5 + colsTerm5 + colsTerm6 - (colsTerm + colsTerm2 + colsTerm2 + colsTerm3);
					if (computed3 == 0f && computed4 == 0f)
					{
						outputGreyscaleBuffer[rowColsTerm] = flatCellColor;
					}
					else
					{
						computed3 *= invcellsize8;
						computed4 *= invcellsize8;
						float zfactorTerm = zfactor * (float)Math.Pow(computed3 * computed3 + computed4 * computed4, 0.5);
						float computed5 = (float)Math.Pow(zfactorTerm * zfactorTerm + 1f, 0.5);
						float computed6 = (float)(1.0 / (double)computed5);
						float computed7 = computed6;
						float computed8 = zfactorTerm * computed6;
						double computed9 = 0.0;
						if (computed3 != 0f)
						{
							computed9 = Math.Atan2(computed4, 0f - computed3);
							if (computed9 < 0.0)
							{
								computed9 += Math.PI * 2.0;
							}
						}
						else if (computed4 > 0f)
						{
							computed9 = Math.PI / 2.0;
						}
						else if (computed4 < 0f)
						{
							computed9 = 4.71238898038469;
						}
						float cos_zenithSin_zenithTerm = cos_zenith * computed7 + sin_zenith * computed8 * (float)Math.Cos((double)azimuth - computed9);
						if (cos_zenithSin_zenithTerm > 0f)
						{
							int alphaTerm = (int)Math.Round((float)(int)alpha * (1f - cos_zenithSin_zenithTerm));
							outputGreyscaleBuffer[rowColsTerm] = alphaTerm << 24;
						}
						else
						{
							outputGreyscaleBuffer[rowColsTerm] = alphaBlack;
						}
					}
				}
				else
				{
					outputGreyscaleBuffer[rowColsTerm] = 0;
				}
			}
		});
		if (rows > 2)
		{
			Buffer.BlockCopy(outputGreyscaleBuffer, cols * 4, outputGreyscaleBuffer, 0, cols * 4);
			Buffer.BlockCopy(outputGreyscaleBuffer, cols * (rows - 2) * 4, outputGreyscaleBuffer, cols * (rows - 1) * 4, cols * 4);
		}
		if (cols > 2)
		{
			for (int ordinal = 0; ordinal < rows; ordinal++)
			{
				int rowsOffsetSum = ordinal * cols;
				outputGreyscaleBuffer[rowsOffsetSum] = outputGreyscaleBuffer[rowsOffsetSum + 1];
				rowsOffsetSum += rows - 1;
				outputGreyscaleBuffer[rowsOffsetSum] = outputGreyscaleBuffer[rowsOffsetSum - 1];
			}
		}
	}

	public static void ComputeHillShadeUnsafe(RasterDefinition raster, Memory<float> values, byte alpha, float altitude_deg, float azimuth_deg, float zfactor, float noDataValue, Memory<int> outputGreyscaleBuffer, ParallelizationIntent intent = ParallelizationIntent.Parallel)
	{
		ComputeHillShadeUnsafe_Internal(raster, values, alpha, altitude_deg, azimuth_deg, zfactor, noDataValue, outputGreyscaleBuffer, intent, useOptimized: false);
	}

	public static void ComputeHillShadeUnsafe_Optimized(RasterDefinition raster, Memory<float> values, byte alpha, float altitude_deg, float azimuth_deg, float zfactor, float noDataValue, Memory<int> outputGreyscaleBuffer, ParallelizationIntent intent = ParallelizationIntent.Parallel)
	{
		ComputeHillShadeUnsafe_Internal(raster, values, alpha, altitude_deg, azimuth_deg, zfactor, noDataValue, outputGreyscaleBuffer, intent, useOptimized: true);
	}

	public static void ComputeHillShadeUnsafe_SmartFor(RasterDefinition raster, Memory<float> values, byte alpha, float altitude_deg, float azimuth_deg, float zfactor, float noDataValue, Memory<int> outputGreyscaleBuffer, int chunkRows = 64)
	{
		ComputeHillShadeUnsafe_Internal(raster, values, alpha, altitude_deg, azimuth_deg, zfactor, noDataValue, outputGreyscaleBuffer, ParallelizationIntent.Parallel, useOptimized: false, chunkRows);
	}

	public static void ComputeHillShadeUnsafe_Optimized_SmartFor(RasterDefinition raster, Memory<float> values, byte alpha, float altitude_deg, float azimuth_deg, float zfactor, float noDataValue, Memory<int> outputGreyscaleBuffer, int chunkRows = 64)
	{
		ComputeHillShadeUnsafe_Internal(raster, values, alpha, altitude_deg, azimuth_deg, zfactor, noDataValue, outputGreyscaleBuffer, ParallelizationIntent.Parallel, useOptimized: true, chunkRows);
	}

	private unsafe static void ComputeHillShadeUnsafe_Internal(RasterDefinition raster, Memory<float> values, byte alpha, float altitude_deg, float azimuth_deg, float zfactor, float noDataValue, Memory<int> outputGreyscaleBuffer, ParallelizationIntent intent, bool useOptimized, int smartForChunkRows = 0)
	{
		float cellSize = (float)raster.CellSize;
		float invcellsize8 = 1f / cellSize * 0.125f;
		float altitude_degPiTerm = (90f - altitude_deg) * ((float)Math.PI / 180f);
		float cos_zenith = (float)Math.Cos(altitude_degPiTerm);
		float sin_zenith = (float)Math.Sin(altitude_degPiTerm);
		float azimuth_degTerm = 360f - azimuth_deg + 90f;
		if (azimuth_degTerm > 360f)
		{
			azimuth_degTerm -= 360f;
		}
		float azimuth = azimuth_degTerm * ((float)Math.PI / 180f);
		uint alphaCos_zenithTerm = (uint)((float)(int)alpha * (1f - cos_zenith)) << 24;
		int flatCellColor = (int)alphaCos_zenithTerm;
		int rows = raster.Rows;
		int cols = raster.Cols;
		byte* localValue = stackalloc byte[4];
		localValue[3] = alpha;
		int alphaBlack = *(int*)localValue;
		fixed (float* localValue2 = values.Span)
		{
			fixed (int* outputGreyscaleBuffer2 = outputGreyscaleBuffer.Span)
			{
				int* tmpO = outputGreyscaleBuffer2;
				float* tmpV = localValue2;
				switch (intent)
				{
				case ParallelizationIntent.Serial:
				{
					HillshadeParams hs = new HillshadeParams(cols, flatCellColor, invcellsize8, alpha, alphaBlack, zfactor, azimuth, sin_zenith, cos_zenith, noDataValue);
					if (useOptimized)
					{
						for (int unity = 1; unity < rows; unity++)
						{
							HillshadeRowAction_Optimized(unity, localValue2, outputGreyscaleBuffer2, hs);
						}
					}
					else
					{
						for (int unity2 = 1; unity2 < rows; unity2++)
						{
							HillshadeRowAction(unity2, localValue2, outputGreyscaleBuffer2, hs);
						}
					}
					goto end_IL_0117;
				}
				case ParallelizationIntent.Parallel:
					if (smartForChunkRows <= 0)
					{
						break;
					}
					Utility.Parallel.SmartFor(rows - 2, (int start, int end) =>
					{
						HillshadeParams hs2 = new HillshadeParams(cols, flatCellColor, invcellsize8, alpha, alphaBlack, zfactor, azimuth, sin_zenith, cos_zenith, noDataValue);
						if (useOptimized)
						{
							for (int i = start + 1; i <= end + 1; i++)
							{
								HillshadeRowAction_Optimized(i, tmpV, tmpO, hs2);
							}
						}
						else
						{
							for (int j = start + 1; j <= end + 1; j++)
							{
								HillshadeRowAction(j, tmpV, tmpO, hs2);
							}
						}
					}, smartForChunkRows);
					goto end_IL_0117;
				}
				if (intent != ParallelizationIntent.Parallel)
				{
					throw new NotImplementedException();
				}
				System.Threading.Tasks.Parallel.For(1, rows - 1, (int row) =>
				{
					HillshadeParams hs2 = new HillshadeParams(cols, flatCellColor, invcellsize8, alpha, alphaBlack, zfactor, azimuth, sin_zenith, cos_zenith, noDataValue);
					if (useOptimized)
					{
						HillshadeRowAction_Optimized(row, tmpV, tmpO, hs2);
					}
					else
					{
						HillshadeRowAction(row, tmpV, tmpO, hs2);
					}
				});
				end_IL_0117:;
			}
		}
		if (rows > 2)
		{
			Memory<int> slice = outputGreyscaleBuffer.Slice(cols, cols);
			Memory<int> destination = outputGreyscaleBuffer.Slice(0, cols);
			slice.CopyTo(destination);
			slice = outputGreyscaleBuffer.Slice((rows - 2) * cols, cols);
			destination = outputGreyscaleBuffer.Slice((rows - 1) * cols, cols);
			slice.CopyTo(destination);
		}
		if (cols > 2)
		{
			Span<int> outputGreyscaleBufferSpan = outputGreyscaleBuffer.Span;
			for (int ordinal = 0; ordinal < rows; ordinal++)
			{
				int colsOffsetSum = ordinal * cols;
				outputGreyscaleBufferSpan[colsOffsetSum] = outputGreyscaleBufferSpan[colsOffsetSum + 1];
				colsOffsetSum += cols - 1;
				outputGreyscaleBufferSpan[colsOffsetSum] = outputGreyscaleBufferSpan[colsOffsetSum - 1];
			}
		}
	}

	private unsafe static void HillshadeRowAction(int row, float* values, int* output, HillshadeParams hs)
	{
		float noDataValue = hs.NoDataValue;
		for (int i = 1; i < hs.cols - 1; i++)
		{
			int rowColsTerm = row * hs.cols + i;
			float colsHsTerm = values[rowColsTerm - hs.cols - 1];
			float colsHsTerm2 = values[rowColsTerm - hs.cols];
			float colsHsTerm3 = values[rowColsTerm - hs.cols + 1];
			float computed = values[rowColsTerm - 1];
			float computed2 = values[rowColsTerm + 1];
			float colsHsTerm4 = values[rowColsTerm + hs.cols - 1];
			float colsHsTerm5 = values[rowColsTerm + hs.cols];
			float colsHsTerm6 = values[rowColsTerm + hs.cols + 1];
			if (colsHsTerm == noDataValue || colsHsTerm2 == noDataValue || colsHsTerm3 == noDataValue || computed == noDataValue || computed2 == noDataValue || colsHsTerm4 == noDataValue || colsHsTerm5 == noDataValue || colsHsTerm6 == noDataValue)
			{
				output[rowColsTerm] = 0;
				continue;
			}
			float computed3 = colsHsTerm3 + computed2 + computed2 + colsHsTerm6 - (colsHsTerm + computed + computed + colsHsTerm4);
			float computed4 = colsHsTerm4 + colsHsTerm5 + colsHsTerm5 + colsHsTerm6 - (colsHsTerm + colsHsTerm2 + colsHsTerm2 + colsHsTerm3);
			if (computed3 == 0f && computed4 == 0f)
			{
				output[rowColsTerm] = hs.flatCellColor;
				continue;
			}
			computed3 *= hs.invcellsize8;
			computed4 *= hs.invcellsize8;
			float zfactorHsTerm = hs.zfactor * MathHelpers.FastSqrt(computed3 * computed3 + computed4 * computed4);
			float fastSqrt = MathHelpers.FastSqrt(zfactorHsTerm * zfactorHsTerm + 1f);
			float scaledFastSqrt = 1f / fastSqrt;
			float scaledFastSqrt2 = scaledFastSqrt;
			float computed5 = zfactorHsTerm * scaledFastSqrt;
			double computed6 = 0.0;
			if (computed3 != 0f)
			{
				computed6 = Math.Atan2(computed4, 0f - computed3);
				if (computed6 < 0.0)
				{
					computed6 += Math.PI * 2.0;
				}
			}
			else if (computed4 > 0f)
			{
				computed6 = Math.PI / 2.0;
			}
			else if (computed4 < 0f)
			{
				computed6 = 4.71238898038469;
			}
			float cos_zenithHsTerm = hs.cos_zenith * scaledFastSqrt2 + hs.sin_zenith * computed5 * (float)Math.Cos((double)hs.azimuth - computed6);
			if (cos_zenithHsTerm > 0f)
			{
				int alphaHsTerm = (int)Math.Round((float)hs.alpha * (1f - cos_zenithHsTerm));
				output[rowColsTerm] = alphaHsTerm << 24;
			}
			else
			{
				output[rowColsTerm] = hs.alphaBlack;
			}
		}
	}

	private unsafe static void HillshadeRowAction_Optimized(int row, float* values, int* output, HillshadeParams hs)
	{
		float noDataValue = hs.NoDataValue;
		for (int i = 1; i < hs.cols - 1; i++)
		{
			int rowColsTerm = row * hs.cols + i;
			float colsHsTerm = values[rowColsTerm - hs.cols - 1];
			float colsHsTerm2 = values[rowColsTerm - hs.cols];
			float colsHsTerm3 = values[rowColsTerm - hs.cols + 1];
			float computed = values[rowColsTerm - 1];
			float computed2 = values[rowColsTerm + 1];
			float colsHsTerm4 = values[rowColsTerm + hs.cols - 1];
			float colsHsTerm5 = values[rowColsTerm + hs.cols];
			float colsHsTerm6 = values[rowColsTerm + hs.cols + 1];
			if (colsHsTerm == noDataValue || colsHsTerm2 == noDataValue || colsHsTerm3 == noDataValue || computed == noDataValue || computed2 == noDataValue || colsHsTerm4 == noDataValue || colsHsTerm5 == noDataValue || colsHsTerm6 == noDataValue)
			{
				output[rowColsTerm] = 0;
				continue;
			}
			float computed3 = colsHsTerm3 + computed2 + computed2 + colsHsTerm6 - (colsHsTerm + computed + computed + colsHsTerm4);
			float computed4 = colsHsTerm4 + colsHsTerm5 + colsHsTerm5 + colsHsTerm6 - (colsHsTerm + colsHsTerm2 + colsHsTerm2 + colsHsTerm3);
			if (computed3 == 0f && computed4 == 0f)
			{
				output[rowColsTerm] = hs.flatCellColor;
				continue;
			}
			computed3 *= hs.invcellsize8;
			computed4 *= hs.invcellsize8;
			float fastSqrt = MathHelpers.FastSqrt(computed3 * computed3 + computed4 * computed4);
			float zfactorTimesFastSqrt = hs.zfactor * fastSqrt;
			float fastSqrt2 = MathHelpers.FastSqrt(zfactorTimesFastSqrt * zfactorTimesFastSqrt + 1f);
			float scaledFastSqrt2 = 1f / fastSqrt2;
			float scaledFastSqrt22 = scaledFastSqrt2;
			float computed5 = zfactorTimesFastSqrt * scaledFastSqrt2;
			float computed6;
			float computed7;
			if (computed3 != 0f)
			{
				float scaledFastSqrt = 1f / fastSqrt;
				computed6 = (0f - computed3) * scaledFastSqrt;
				computed7 = computed4 * scaledFastSqrt;
			}
			else if (computed4 > 0f)
			{
				computed6 = 0f;
				computed7 = 1f;
			}
			else
			{
				computed6 = 0f;
				computed7 = -1f;
			}
			float cos_azimuthHsTerm = hs.cos_azimuth * computed6 + hs.sin_azimuth * computed7;
			float cos_zenithHsTerm = hs.cos_zenith * scaledFastSqrt22 + hs.sin_zenith * computed5 * cos_azimuthHsTerm;
			if (cos_zenithHsTerm > 0f)
			{
				int alphaHsTerm = (int)((float)hs.alpha * (1f - cos_zenithHsTerm) + 0.5f);
				output[rowColsTerm] = alphaHsTerm << 24;
			}
			else
			{
				output[rowColsTerm] = hs.alphaBlack;
			}
		}
	}

	private unsafe static void HillshadeRowAction_SSE(int row, float* values, int* output, HillshadeParams hs)
	{
		Vector128<float> right = Vector128.Create(-9999f);
		for (int i = 1; i < hs.cols - 1; i++)
		{
			int rowColsTerm = row * hs.cols + i;
			if ((Sse.MoveMask(Sse.CompareEqual(Sse.LoadVector128(values + rowColsTerm - hs.cols - 1), right)) & 0xFF) > 0)
			{
				output[rowColsTerm] = 0;
				continue;
			}
			if ((Sse.MoveMask(Sse.CompareEqual(Sse.LoadVector128(values + rowColsTerm - 1), right)) & 0xFF) > 0)
			{
				output[rowColsTerm] = 0;
				continue;
			}
			if ((Sse.MoveMask(Sse.CompareEqual(Sse.LoadVector128(values + rowColsTerm + hs.cols - 1), right)) & 0xFF) > 0)
			{
				output[rowColsTerm] = 0;
				continue;
			}
			float computed = 0f;
			float computed2 = 0f;
			if (computed == 0f && computed2 == 0f)
			{
				output[rowColsTerm] = hs.flatCellColor;
				continue;
			}
			computed *= hs.invcellsize8;
			computed2 *= hs.invcellsize8;
			float zfactorHsTerm = hs.zfactor * MathHelpers.FastSqrt(computed * computed + computed2 * computed2);
			float fastSqrt = MathHelpers.FastSqrt(zfactorHsTerm * zfactorHsTerm + 1f);
			float scaledFastSqrt = 1f / fastSqrt;
			float scaledFastSqrt2 = scaledFastSqrt;
			float computed3 = zfactorHsTerm * scaledFastSqrt;
			double computed4 = 0.0;
			if (computed != 0f)
			{
				computed4 = Math.Atan2(computed2, 0f - computed);
				if (computed4 < 0.0)
				{
					computed4 += Math.PI * 2.0;
				}
			}
			else if (computed2 > 0f)
			{
				computed4 = Math.PI / 2.0;
			}
			else if (computed2 < 0f)
			{
				computed4 = 4.71238898038469;
			}
			float cos_zenithHsTerm = hs.cos_zenith * scaledFastSqrt2 + hs.sin_zenith * computed3 * (float)Math.Cos((double)hs.azimuth - computed4);
			if (cos_zenithHsTerm > 0f)
			{
				int alphaHsTerm = (int)Math.Round((float)hs.alpha * (1f - cos_zenithHsTerm));
				output[rowColsTerm] = alphaHsTerm << 24;
			}
			else
			{
				output[rowColsTerm] = hs.alphaBlack;
			}
		}
	}

	private static void ComputeRidges(RasterDefinition raster, float[] values, int contourColor, int[] colors)
	{
		int rows = raster.Rows;
		int cols = raster.Cols;
		System.Threading.Tasks.Parallel.For(1, rows - 1, (int row) =>
		{
			for (int i = 1; i < cols - 1; i++)
			{
				int rowColsTerm = row * cols + i;
				float colsTerm = values[rowColsTerm - cols - 1];
				float colsTerm2 = values[rowColsTerm - cols];
				float colsTerm3 = values[rowColsTerm - cols + 1];
				float computed = values[rowColsTerm - 1];
				float computed2 = values[rowColsTerm];
				float computed3 = values[rowColsTerm + 1];
				float colsTerm4 = values[rowColsTerm + cols - 1];
				float colsTerm5 = values[rowColsTerm + cols];
				float colsTerm6 = values[rowColsTerm + cols + 1];
				if (colsTerm != -9999f && colsTerm2 != -9999f && colsTerm3 != -9999f && computed != -9999f && computed2 != -9999f && computed3 != -9999f && colsTerm4 != -9999f && colsTerm5 != -9999f && colsTerm6 != -9999f)
				{
					if (colsTerm < computed2 && colsTerm6 < computed2)
					{
						colors[rowColsTerm] = contourColor;
					}
					else if (colsTerm3 < computed2 && colsTerm4 < computed2)
					{
						colors[rowColsTerm] = contourColor;
					}
					else if (colsTerm2 < computed2 && colsTerm5 < computed2)
					{
						colors[rowColsTerm] = contourColor;
					}
					else if (computed < computed2 && computed3 < computed2)
					{
						colors[rowColsTerm] = contourColor;
					}
				}
			}
		});
		throw new NotImplementedException();
	}

	public static void ComputeRasterContours(RasterDefinition raster, float[] values, float[] bufferValues, float contourInterval, int contourColor, int[] outColors)
	{
		bufferValues.AsSpan().Fill(-9999f);
		int rows = raster.Rows;
		int cols = raster.Cols;
		float invContour = 1f / contourInterval;
		System.Threading.Tasks.Parallel.For(1, rows, (int row) =>
		{
			int rowTimesCols = row * cols;
			for (int i = 1; i < cols - 1; i++)
			{
				rowTimesCols++;
				if (values[rowTimesCols] != -9999f)
				{
					bufferValues[rowTimesCols] = values[rowTimesCols] * invContour;
				}
			}
		});
		System.Threading.Tasks.Parallel.For(1, rows, (int row) =>
		{
			int rowTimesCols2 = row * cols;
			for (int i = 1; i < cols - 1; i++)
			{
				rowTimesCols2++;
				float bufferValue = bufferValues[rowTimesCols2];
				if (bufferValue != -9999f)
				{
					int rowTimesColsAboveCols = rowTimesCols2 - cols;
					float bufferValue2 = bufferValues[rowTimesColsAboveCols];
					if (bufferValue2 != -9999f)
					{
						ContourRasterEdge(contourColor, bufferValue, bufferValue2, ref outColors[rowTimesCols2], ref outColors[rowTimesColsAboveCols]);
					}
					int ordinal = rowTimesCols2 + 1;
					float bufferValue3 = bufferValues[ordinal];
					if (bufferValue3 != -9999f)
					{
						ContourRasterEdge(contourColor, bufferValue, bufferValue3, ref outColors[rowTimesCols2], ref outColors[ordinal]);
					}
				}
			}
		});
	}

	private static void ContourRasterEdge(int contourColor, float va, float vb, ref int ca, ref int cb)
	{
		if (va > vb)
		{
			float vaTerm = (float)Math.Floor(va);
			if (vb < vaTerm)
			{
				if (va - vaTerm < vaTerm - vb)
				{
					ca = contourColor;
				}
				else
				{
					cb = contourColor;
				}
			}
		}
		else
		{
			if (!(va < vb))
			{
				return;
			}
			float vbTerm = (float)Math.Floor(vb);
			if (va < vbTerm)
			{
				if (vb - vbTerm < vbTerm - va)
				{
					cb = contourColor;
				}
				else
				{
					ca = contourColor;
				}
			}
		}
	}
}
