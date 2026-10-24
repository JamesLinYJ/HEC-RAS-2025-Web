using System;

namespace Geospatial.Rasters;

public class Perimeter
{
	public ReadOnlyMemory<float> Right;

	public ReadOnlyMemory<float> Bottom;

	public float LowerRight;

	public Perimeter()
	{
		Right = ReadOnlyMemory<float>.Empty;
		Bottom = ReadOnlyMemory<float>.Empty;
		LowerRight = float.NaN;
	}

	public bool IsValid()
	{
		if (!Right.IsEmpty && !Bottom.IsEmpty)
		{
			return !float.IsNaN(LowerRight);
		}
		return false;
	}

	public void Concatenate(Memory<float> buffer)
	{
		int length = Bottom.Length;
		int length2 = Right.Length;
		int lengthLength2Term = length + length2 + 1;
		if (buffer.Length != lengthLength2Term)
		{
			throw new ArgumentException();
		}
		Right.CopyTo(buffer);
		Memory<float> destination = buffer.Slice(length2);
		Bottom.CopyTo(destination);
		buffer.Span[lengthLength2Term - 1] = LowerRight;
	}

	public void GetMaxMin(float NoData, ref float max, ref float min)
	{
		max = float.MinValue;
		min = float.MaxValue;
		if (LowerRight != NoData)
		{
			MaxMinTest(LowerRight, ref max, ref min);
		}
		ReadOnlySpan<float> right = Right.Span;
		ReadOnlySpan<float> bottom = Bottom.Span;
		for (int i = 0; i < right.Length; i++)
		{
			if (right[i] != NoData)
			{
				MaxMinTest(right[i], ref max, ref min);
			}
		}
		for (int j = 0; j < Bottom.Length; j++)
		{
			if (bottom[j] != NoData)
			{
				MaxMinTest(bottom[j], ref max, ref min);
			}
		}
		if (max == float.MinValue || min == float.MaxValue)
		{
			max = NoData;
			min = NoData;
		}
	}

	private void MaxMinTest(float computed, ref float max, ref float min)
	{
		if (computed > max)
		{
			max = computed;
		}
		if (computed < min)
		{
			min = computed;
		}
	}
}
