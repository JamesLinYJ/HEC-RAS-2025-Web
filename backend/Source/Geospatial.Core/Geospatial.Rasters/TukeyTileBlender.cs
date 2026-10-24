using System;
using System.Numerics;

namespace Geospatial.Rasters;

public static class TukeyTileBlender
{
	public static float[] CreateTukeyWindow(int size, int overlap)
	{
		float[] tukeyWindow = new float[size * size];
		float[] tukeyWindow1D = CreateTukeyWindow1D(size, overlap);
		for (int i = 0; i < size; i++)
		{
			for (int j = 0; j < size; j++)
			{
				tukeyWindow[i * size + j] = tukeyWindow1D[j] * tukeyWindow1D[i];
			}
		}
		return tukeyWindow;
	}

	private static float[] CreateTukeyWindow1D(int size, int overlap)
	{
		float[] tukeyWindow1D = new float[size];
		Array.Fill(tukeyWindow1D, 1f);
		if (overlap <= 0)
		{
			return tukeyWindow1D;
		}
		int count = (int)((float)overlap / (float)size * (float)size / 2f);
		if (count <= 0)
		{
			return tukeyWindow1D;
		}
		for (int i = 0; i < count; i++)
		{
			tukeyWindow1D[i] = 0.5f * (1f - MathF.Cos((float)Math.PI * (float)i / (float)count));
		}
		for (int j = 0; j < count; j++)
		{
			tukeyWindow1D[size - 1 - j] = 0.5f * (1f - MathF.Cos((float)Math.PI * (float)j / (float)count));
		}
		return tukeyWindow1D;
	}

	public static void BlendTile(ReadOnlySpan<float> tile, ReadOnlySpan<float> weights, Span<float> accumulator, Span<float> weightAccumulator, int tileSize, int destX, int destY, int destWidth, int destHeight)
	{
		for (int i = 0; i < tileSize; i++)
		{
			int destYTerm = destY + i;
			if (destYTerm < 0 || destYTerm >= destHeight)
			{
				continue;
			}
			for (int j = 0; j < tileSize; j++)
			{
				int destXTerm = destX + j;
				if (destXTerm >= 0 && destXTerm < destWidth)
				{
					int index = i * tileSize + j;
					int index2 = destYTerm * destWidth + destXTerm;
					float weight = weights[index];
					float tile2 = tile[index];
					accumulator[index2] += tile2 * weight;
					weightAccumulator[index2] += weight;
				}
			}
		}
	}

	public static void BlendIntoOutputTile(ReadOnlySpan<float> inferenceTile, ReadOnlySpan<float> weights, Span<float> outputValueTile, Span<float> outputWeightTile, int tileSize, int inferenceStartX, int inferenceStartY, int outputTileStartX, int outputTileStartY, int outputTileSize, int rasterWidth, int rasterHeight)
	{
		int inferenceStartXOutputTileStartXTerm = Math.Max(inferenceStartX, outputTileStartX);
		int inferenceStartYOutputTileStartYTerm = Math.Max(inferenceStartY, outputTileStartY);
		int inferenceStartXTileSizeTerm = Math.Min(inferenceStartX + tileSize, Math.Min(outputTileStartX + outputTileSize, rasterWidth));
		int count2 = Math.Min(inferenceStartY + tileSize, Math.Min(outputTileStartY + outputTileSize, rasterHeight));
		if (inferenceStartXOutputTileStartXTerm >= inferenceStartXTileSizeTerm || inferenceStartYOutputTileStartYTerm >= count2)
		{
			return;
		}
		int count3 = inferenceStartXTileSizeTerm - inferenceStartXOutputTileStartXTerm;
		int inferenceStartXTerm = inferenceStartXOutputTileStartXTerm - inferenceStartX;
		int outputTileStartXTerm = inferenceStartXOutputTileStartXTerm - outputTileStartX;
		int count = Vector<float>.Count;
		for (int i = inferenceStartYOutputTileStartYTerm; i < count2; i++)
		{
			int inferenceStartYTileSizeTerm = (i - inferenceStartY) * tileSize + inferenceStartXTerm;
			int outputTileStartYOutputTileSizeTerm = (i - outputTileStartY) * outputTileSize + outputTileStartXTerm;
			int j;
			for (j = 0; j <= count3 - count; j += count)
			{
				Vector<float> vector = new Vector<float>(weights.Slice(inferenceStartYTileSizeTerm + j, count));
				Vector<float> vector2 = new Vector<float>(inferenceTile.Slice(inferenceStartYTileSizeTerm + j, count));
				Vector<float> vector3 = new Vector<float>((ReadOnlySpan<float>)outputValueTile.Slice(outputTileStartYOutputTileSizeTerm + j, count));
				Vector<float> vector4 = new Vector<float>((ReadOnlySpan<float>)outputWeightTile.Slice(outputTileStartYOutputTileSizeTerm + j, count));
				(vector3 + vector2 * vector).CopyTo(outputValueTile.Slice(outputTileStartYOutputTileSizeTerm + j, count));
				(vector4 + vector).CopyTo(outputWeightTile.Slice(outputTileStartYOutputTileSizeTerm + j, count));
			}
			for (; j < count3; j++)
			{
				float weight = weights[inferenceStartYTileSizeTerm + j];
				outputValueTile[outputTileStartYOutputTileSizeTerm + j] += inferenceTile[inferenceStartYTileSizeTerm + j] * weight;
				outputWeightTile[outputTileStartYOutputTileSizeTerm + j] += weight;
			}
		}
	}

	public static void Finalize(Span<float> accumulator, ReadOnlySpan<float> weightAccumulator, float threshold, float nodata)
	{
		for (int i = 0; i < accumulator.Length; i++)
		{
			float weightAccumulator2 = weightAccumulator[i];
			if (weightAccumulator2 > 0f)
			{
				float accumulatorOverWeightAccumulator2 = accumulator[i] / weightAccumulator2;
				accumulator[i] = ((accumulatorOverWeightAccumulator2 >= threshold) ? accumulatorOverWeightAccumulator2 : nodata);
			}
			else
			{
				accumulator[i] = nodata;
			}
		}
	}
}
