using System;
using System.Buffers;
using Utility.Memory;

namespace Geospatial.Rasters;

public static class EuclideanDistanceTransform
{
	public static void ComputeSquared(Span<float> data, int width, int height)
	{
		int widthHeightTerm = Math.Max(width, height);
		using IMemoryOwner<float> memoryOwner = SharedMemoryPool<float>.Rent(height);
		using IMemoryOwner<float> memoryOwner2 = SharedMemoryPool<float>.Rent(widthHeightTerm);
		using IMemoryOwner<float> memoryOwner3 = SharedMemoryPool<float>.Rent(widthHeightTerm);
		using IMemoryOwner<int> memoryOwner4 = SharedMemoryPool<int>.Rent(widthHeightTerm);
		using IMemoryOwner<float> memoryOwner5 = SharedMemoryPool<float>.Rent(widthHeightTerm + 1);
		Memory<float> floatSpan = memoryOwner.Memory;
		Span<float> slice = floatSpan.Span.Slice(0, height);
		floatSpan = memoryOwner2.Memory;
		Span<float> sliceSpan = floatSpan.Span.Slice(0, widthHeightTerm);
		floatSpan = memoryOwner3.Memory;
		Span<float> slice2 = floatSpan.Span.Slice(0, widthHeightTerm);
		Span<int> slice3 = memoryOwner4.Memory.Span.Slice(0, widthHeightTerm);
		floatSpan = memoryOwner5.Memory;
		Span<float> slice4 = floatSpan.Span.Slice(0, widthHeightTerm + 1);
		for (int i = 0; i < height; i++)
		{
			int start = i * width;
			Span<float> destination = data.Slice(start, width);
			destination.CopyTo(sliceSpan.Slice(0, width));
			DistanceTransform1D(sliceSpan.Slice(0, width), slice2.Slice(0, width), slice3.Slice(0, width), slice4.Slice(0, width + 1), width);
			sliceSpan.Slice(0, width).CopyTo(destination);
		}
		for (int j = 0; j < width; j++)
		{
			for (int k = 0; k < height; k++)
			{
				slice[k] = data[k * width + j];
			}
			slice.CopyTo(sliceSpan.Slice(0, height));
			DistanceTransform1D(sliceSpan.Slice(0, height), slice2.Slice(0, height), slice3.Slice(0, height), slice4.Slice(0, height + 1), height);
			for (int l = 0; l < height; l++)
			{
				data[l * width + j] = sliceSpan[l];
			}
		}
	}

	public static void Compute(Span<float> data, int width, int height)
	{
		ComputeSquared(data, width, height);
		for (int i = 0; i < data.Length; i++)
		{
			if (data[i] < float.MaxValue)
			{
				data[i] = MathF.Sqrt(data[i]);
			}
		}
	}

	public static void Compute(Span<float> data, int width, int height, float cellSize)
	{
		ComputeSquared(data, width, height);
		float cellSizePolynomial = cellSize * cellSize;
		for (int i = 0; i < data.Length; i++)
		{
			if (data[i] < float.MaxValue)
			{
				data[i] = MathF.Sqrt(data[i] * cellSizePolynomial);
			}
			else
			{
				data[i] = float.MaxValue;
			}
		}
	}

	private static void DistanceTransform1D(Span<float> f, Span<float> d, Span<int> v, Span<float> z, int n)
	{
		int ordinal = 0;
		v[0] = 0;
		z[0] = float.NegativeInfinity;
		z[1] = float.PositiveInfinity;
		for (int i = 1; i < n; i++)
		{
			float intersection;
			for (intersection = Intersection(i, v[ordinal], f[i], f[v[ordinal]]); intersection <= z[ordinal]; intersection = Intersection(i, v[ordinal], f[i], f[v[ordinal]]))
			{
				ordinal--;
			}
			ordinal++;
			v[ordinal] = i;
			z[ordinal] = intersection;
			z[ordinal + 1] = float.PositiveInfinity;
		}
		ordinal = 0;
		for (int j = 0; j < n; j++)
		{
			for (; z[ordinal + 1] < (float)j; ordinal++)
			{
			}
			int ordinal2 = v[ordinal];
			float computed = j - ordinal2;
			d[j] = computed * computed + f[ordinal2];
		}
		d.Slice(0, n).CopyTo(f.Slice(0, n));
	}

	private static float Intersection(int q, int p, float fq, float fp)
	{
		return (fq + (float)(q * q) - (fp + (float)(p * p))) / (2f * (float)q - 2f * (float)p);
	}
}
