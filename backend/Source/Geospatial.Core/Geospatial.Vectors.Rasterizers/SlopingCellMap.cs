using System;
using System.Diagnostics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Geospatial.Vectors.Rasterizers;

[DebuggerDisplay("Cell {CellIndex}: {TotalPointCount} points")]
public struct SlopingCellMap
{
	public static int PaintPixelsPerThread = 256;

	public readonly int CellIndex;

	public readonly Memory<PointRange> PixelRanges;

	public readonly Memory<float> FullStencilWeights;

	public int TotalPointCount
	{
		get
		{
			int pixelRangeCountSum = 0;
			Span<PointRange> pixelRanges = PixelRanges.Span;
			for (int i = 0; i < pixelRanges.Length; i++)
			{
				pixelRangeCountSum += pixelRanges[i].Count;
			}
			return pixelRangeCountSum;
		}
	}

	public int HighestPointIndex
	{
		get
		{
			int queryEndIndexPixelRangesTerm = 0;
			Span<PointRange> pixelRanges = PixelRanges.Span;
			for (int i = 0; i < pixelRanges.Length; i++)
			{
				queryEndIndexPixelRangesTerm = Math.Max(queryEndIndexPixelRangesTerm, pixelRanges[i].QueryEndIndex);
			}
			return queryEndIndexPixelRangesTerm;
		}
	}

	public SlopingCellMap(int cellIndex, Memory<PointRange> cellPoints, Memory<float> fullStencilWeights)
	{
		CellIndex = cellIndex;
		PixelRanges = cellPoints;
		FullStencilWeights = fullStencilWeights;
	}

	public static SlopingCellMap SinglePoint(int cIdx, Memory<float> stencilWeights)
	{
		return new SlopingCellMap(cIdx, new PointRange[1]
		{
			new PointRange(0, 0, 0)
		}, stencilWeights);
	}

	public void PaintByFacepoints(float[] fpLocalValues, Memory<float> output, Memory<float> clipPixelDepths)
	{
		throw new NotImplementedException();
	}

	public void PaintByFacepoints(Vector[] fpLocalValues, Vector[] output, Memory<float> clipPixelDepth)
	{
		throw new NotImplementedException();
	}

	public void SimplePaint(float outputSpan2, Memory<float> output, Memory<float> clipPixelDepth)
	{
		Span<PointRange> pixelRanges = PixelRanges.Span;
		Span<float> outputSpan = output.Span;
		if (clipPixelDepth.IsEmpty)
		{
			for (int i = 0; i < pixelRanges.Length; i++)
			{
				PointRange pointRange = pixelRanges[i];
				outputSpan.Slice(pointRange.QueryStartIndex, pointRange.Count).Fill(outputSpan2);
			}
			return;
		}
		Span<float> clipPixelDepthSpan = clipPixelDepth.Span;
		for (int j = 0; j < pixelRanges.Length; j++)
		{
			PointRange pointRange2 = pixelRanges[j];
			for (int k = pointRange2.QueryStartIndex; k <= pointRange2.QueryEndIndex; k++)
			{
				if (clipPixelDepthSpan[k] > 0f)
				{
					outputSpan[k] = outputSpan2;
				}
			}
		}
	}

	public void PaintCell_8Stencil(Span<float> fpLocalValues, Span<float> fLocalValues, Span<float> output)
	{
		int length = fLocalValues.Length;
		int scaledLength = length * 2;
		Span<PointRange> pixelRanges = PixelRanges.Span;
		Span<float> fullStencilWeights = FullStencilWeights.Span;
		for (int i = 0; i < pixelRanges.Length; i++)
		{
			PointRange pointRange = pixelRanges[i];
			int scaledLengthSum = pointRange.LocalStartIndex * scaledLength;
			for (int j = pointRange.QueryStartIndex; j <= pointRange.QueryEndIndex; j++)
			{
				float output2 = 0f;
				for (int k = 0; k < length; k++)
				{
					float fpLocalValue = fpLocalValues[k];
					output2 += fpLocalValue * fullStencilWeights[scaledLengthSum + k];
				}
				for (int l = 0; l < length; l++)
				{
					float fLocalValue = fLocalValues[l];
					output2 += fLocalValue * fullStencilWeights[scaledLengthSum + length + l];
				}
				output[j] = output2;
				scaledLengthSum += scaledLength;
			}
		}
	}

	public unsafe void PaintCell_8Stencil_4FaceSSE(float* fpLocalValues, float* fLocalValues, Memory<float> output)
	{
		Span<float> outputSpan = output.Span;
		if (!Sse.IsSupported || !Sse41.IsSupported)
		{
			PaintCell_8Stencil(new Span<float>(fpLocalValues, 4), new Span<float>(fLocalValues, 4), outputSpan);
			return;
		}
		Span<PointRange> pixelRanges = PixelRanges.Span;
		_ = FullStencilWeights.Span;
		fixed (float* fullStencilWeights = FullStencilWeights.Span)
		{
			for (int i = 0; i < pixelRanges.Length; i++)
			{
				PointRange pointRange = pixelRanges[i];
				int scaledLocalStartIndex = pointRange.LocalStartIndex * 8;
				for (int j = pointRange.QueryStartIndex; j <= pointRange.QueryEndIndex; j++)
				{
					Vector128<float> left = Sse.LoadVector128(fullStencilWeights + scaledLocalStartIndex);
					Vector128<float> right = Sse.LoadVector128(fpLocalValues);
					Vector128<float> vector = Sse41.DotProduct(left, right, byte.MaxValue);
					float toScalar = vector.ToScalar();
					Vector128<float> right2 = Sse.LoadVector128(fLocalValues);
					vector = Sse41.DotProduct(Sse.LoadVector128(fullStencilWeights + scaledLocalStartIndex + 4), right2, byte.MaxValue);
					toScalar += vector.ToScalar();
					outputSpan[j] = toScalar;
					scaledLocalStartIndex += 8;
				}
			}
		}
	}

	public unsafe void PaintCell_8Stencil_4FaceAVX(float* fpThenFaceLocalValues, Span<float> output)
	{
		if (!Avx.IsSupported)
		{
			PaintCell_8Stencil(new Span<float>(fpThenFaceLocalValues, 4),
				new Span<float>(fpThenFaceLocalValues + 4, 4), output);
			return;
		}
		Span<PointRange> pixelRanges = PixelRanges.Span;
		_ = FullStencilWeights.Span;
		float* localValue = stackalloc float[8];
		fixed (float* fullStencilWeights = FullStencilWeights.Span)
		{
			for (int i = 0; i < pixelRanges.Length; i++)
			{
				PointRange pointRange = pixelRanges[i];
				int scaledLocalStartIndex = pointRange.LocalStartIndex * 8;
				for (int j = pointRange.QueryStartIndex; j <= pointRange.QueryEndIndex; j++)
				{
					Vector256<float> left = Avx.LoadVector256(fullStencilWeights + scaledLocalStartIndex);
					Vector256<float> right = Avx.LoadVector256(fpThenFaceLocalValues);
					Vector256<float> source = Avx.DotProduct(left, right, byte.MaxValue);
					Avx.Store(localValue, source);
					output[j] = *localValue + localValue[4];
					scaledLocalStartIndex += 8;
				}
			}
		}
	}

	public void PaintCell_8Stencil(Span<Vector> fpLocalValues, Span<Vector> fLocalValues, Memory<float> clipPixelDepths, VectorMeshOutputs outputs)
	{
		int length = fLocalValues.Length;
		int scaledLength = length * 2;
		Span<PointRange> pixelRanges = PixelRanges.Span;
		Span<float> fullStencilWeights = FullStencilWeights.Span;
		Span<float> clipPixelDepthsSpan = clipPixelDepths.Span;
		Memory<float> outputVxes = outputs.OutputVx;
		Span<float> outputVxesSpan = outputVxes.Span;
		outputVxes = outputs.OutputVy;
		Span<float> outputVxes2 = outputVxes.Span;
		outputVxes = outputs.OutputMagnitude;
		Span<float> outputVxes3 = outputVxes.Span;
		for (int i = 0; i < pixelRanges.Length; i++)
		{
			PointRange pointRange = pixelRanges[i];
			int localStartIndexPointRangeTerm = pointRange.LocalStartIndex * scaledLength;
			for (int j = pointRange.QueryStartIndex; j <= pointRange.QueryEndIndex; j++)
			{
				if (!(clipPixelDepthsSpan[j] <= 0f))
				{
					Span<float> slice = fullStencilWeights.Slice(localStartIndexPointRangeTerm + (j - pointRange.QueryStartIndex) * scaledLength, scaledLength);
					double outputVxesSpan2 = 0.0;
					double outputVxes22 = 0.0;
					for (int k = 0; k < length; k++)
					{
						Vector vector = fpLocalValues[k];
						outputVxesSpan2 += vector.X * (double)slice[k];
						outputVxes22 += vector.Y * (double)slice[k];
					}
					for (int l = 0; l < length; l++)
					{
						Vector vector2 = fLocalValues[l];
						outputVxesSpan2 += vector2.X * (double)slice[length + l];
						outputVxes22 += vector2.Y * (double)slice[length + l];
					}
					outputVxesSpan[j] = (float)outputVxesSpan2;
					outputVxes2[j] = (float)outputVxes22;
					outputVxes3[j] = (float)Math.Sqrt(outputVxesSpan2 * outputVxesSpan2 + outputVxes22 * outputVxes22);
				}
			}
		}
	}

	public void PaintCell_4Stencil(Vector[] fpLocalValues, float[] clipPixelDepths, VectorMeshOutputs outputs)
	{
		throw new NotImplementedException();
	}
}
