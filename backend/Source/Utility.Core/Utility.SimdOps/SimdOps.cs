using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Utility.SimdOps;

public static class SimdOps
{
	[Conditional("DEBUG")]
	private static void AssertSameLength<T>(ReadOnlySpan<T> a, ReadOnlySpan<T> b)
	{
		if (a.Length != b.Length)
		{
			throw new ArgumentException($"Span length mismatch: {a.Length} vs {b.Length}");
		}
	}

	[Conditional("DEBUG")]
	private static void AssertSameLength<T>(ReadOnlySpan<T> a, Span<T> b)
	{
		if (a.Length != b.Length)
		{
			throw new ArgumentException($"Span length mismatch: {a.Length} vs {b.Length}");
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static void Multiply<T>(ReadOnlySpan<T> a, ReadOnlySpan<T> b, Span<T> result) where T : struct, IFloatingPointIeee754<T>
	{
		int length = a.Length;
		int count = Vector<T>.Count;
		int i;
		for (i = 0; i + count <= length; i += count)
		{
			(new Vector<T>(a.Slice(i, count)) * new Vector<T>(b.Slice(i, count))).CopyTo(result.Slice(i, count));
		}
		for (; i < length; i++)
		{
			result[i] = a[i] * b[i];
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static void Add<T>(ReadOnlySpan<T> a, ReadOnlySpan<T> b, Span<T> result) where T : struct, IFloatingPointIeee754<T>
	{
		int length = a.Length;
		int count = Vector<T>.Count;
		int i;
		for (i = 0; i + count <= length; i += count)
		{
			(new Vector<T>(a.Slice(i, count)) + new Vector<T>(b.Slice(i, count))).CopyTo(result.Slice(i, count));
		}
		for (; i < length; i++)
		{
			result[i] = a[i] + b[i];
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static void Subtract<T>(ReadOnlySpan<T> a, ReadOnlySpan<T> b, Span<T> result) where T : struct, IFloatingPointIeee754<T>
	{
		int length = a.Length;
		int count = Vector<T>.Count;
		int i;
		for (i = 0; i + count <= length; i += count)
		{
			(new Vector<T>(a.Slice(i, count)) - new Vector<T>(b.Slice(i, count))).CopyTo(result.Slice(i, count));
		}
		for (; i < length; i++)
		{
			result[i] = a[i] - b[i];
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static void Divide<T>(ReadOnlySpan<T> a, ReadOnlySpan<T> b, Span<T> result) where T : struct, IFloatingPointIeee754<T>
	{
		int length = a.Length;
		int count = Vector<T>.Count;
		int i;
		for (i = 0; i + count <= length; i += count)
		{
			(new Vector<T>(a.Slice(i, count)) / new Vector<T>(b.Slice(i, count))).CopyTo(result.Slice(i, count));
		}
		for (; i < length; i++)
		{
			result[i] = a[i] / b[i];
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static void Negate<T>(ReadOnlySpan<T> a, Span<T> result) where T : struct, IFloatingPointIeee754<T>
	{
		int length = a.Length;
		int count = Vector<T>.Count;
		int i;
		for (i = 0; i + count <= length; i += count)
		{
			(-new Vector<T>(a.Slice(i, count))).CopyTo(result.Slice(i, count));
		}
		for (; i < length; i++)
		{
			result[i] = -a[i];
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static void MultiplyAdd<T>(ReadOnlySpan<T> a, ReadOnlySpan<T> b, ReadOnlySpan<T> c, Span<T> result) where T : struct, IFloatingPointIeee754<T>
	{
		int length = a.Length;
		int count = Vector<T>.Count;
		int i;
		for (i = 0; i + count <= length; i += count)
		{
			(new Vector<T>(a.Slice(i, count)) * new Vector<T>(b.Slice(i, count)) + new Vector<T>(c.Slice(i, count))).CopyTo(result.Slice(i, count));
		}
		for (; i < length; i++)
		{
			result[i] = a[i] * b[i] + c[i];
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static void Scale<T>(ReadOnlySpan<T> a, T scalar, Span<T> result) where T : struct, IFloatingPointIeee754<T>
	{
		int length = a.Length;
		int count = Vector<T>.Count;
		int i = 0;
		Vector<T> vector = new Vector<T>(scalar);
		for (; i + count <= length; i += count)
		{
			(new Vector<T>(a.Slice(i, count)) * vector).CopyTo(result.Slice(i, count));
		}
		for (; i < length; i++)
		{
			result[i] = a[i] * scalar;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static void AddScaled<T>(ReadOnlySpan<T> a, T scalar, ReadOnlySpan<T> b, Span<T> result) where T : struct, IFloatingPointIeee754<T>
	{
		int length = a.Length;
		int count = Vector<T>.Count;
		int i = 0;
		Vector<T> vector = new Vector<T>(scalar);
		for (; i + count <= length; i += count)
		{
			(new Vector<T>(a.Slice(i, count)) + vector * new Vector<T>(b.Slice(i, count))).CopyTo(result.Slice(i, count));
		}
		for (; i < length; i++)
		{
			result[i] = a[i] + scalar * b[i];
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static void Sqrt<T>(ReadOnlySpan<T> a, Span<T> result) where T : struct, IFloatingPointIeee754<T>
	{
		int length = a.Length;
		int count = Vector<T>.Count;
		int i;
		for (i = 0; i + count <= length; i += count)
		{
			Vector.SquareRoot(new Vector<T>(a.Slice(i, count))).CopyTo(result.Slice(i, count));
		}
		for (; i < length; i++)
		{
			result[i] = T.Sqrt(a[i]);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static void Abs<T>(ReadOnlySpan<T> a, Span<T> result) where T : struct, IFloatingPointIeee754<T>
	{
		int length = a.Length;
		int count = Vector<T>.Count;
		int i;
		for (i = 0; i + count <= length; i += count)
		{
			Vector.Abs(new Vector<T>(a.Slice(i, count))).CopyTo(result.Slice(i, count));
		}
		for (; i < length; i++)
		{
			result[i] = T.Abs(a[i]);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static void MaxScalar<T>(ReadOnlySpan<T> a, T localValue, Span<T> result) where T : struct, IFloatingPointIeee754<T>
	{
		int length = a.Length;
		int count = Vector<T>.Count;
		int i = 0;
		Vector<T> right = new Vector<T>(localValue);
		for (; i + count <= length; i += count)
		{
			Vector.Max(new Vector<T>(a.Slice(i, count)), right).CopyTo(result.Slice(i, count));
		}
		for (; i < length; i++)
		{
			result[i] = T.Max(a[i], localValue);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static void Clamp<T>(ReadOnlySpan<T> a, T min, T max, Span<T> result) where T : struct, IFloatingPointIeee754<T>
	{
		int length = a.Length;
		int count = Vector<T>.Count;
		int i = 0;
		Vector<T> right = new Vector<T>(min);
		Vector<T> right2 = new Vector<T>(max);
		for (; i + count <= length; i += count)
		{
			Vector.Min(Vector.Max(new Vector<T>(a.Slice(i, count)), right), right2).CopyTo(result.Slice(i, count));
		}
		for (; i < length; i++)
		{
			result[i] = T.Min(T.Max(a[i], min), max);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static void Relax<T>(ReadOnlySpan<T> a, ReadOnlySpan<T> b, T alpha, Span<T> result) where T : struct, IFloatingPointIeee754<T>
	{
		int length = a.Length;
		int count = Vector<T>.Count;
		int i = 0;
		T oneAboveAlpha = T.One - alpha;
		Vector<T> vector = new Vector<T>(alpha);
		Vector<T> vector2 = new Vector<T>(oneAboveAlpha);
		for (; i + count <= length; i += count)
		{
			(new Vector<T>(a.Slice(i, count)) * vector + new Vector<T>(b.Slice(i, count)) * vector2).CopyTo(result.Slice(i, count));
		}
		for (; i < length; i++)
		{
			result[i] = alpha * a[i] + oneAboveAlpha * b[i];
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static T Sum<T>(ReadOnlySpan<T> a) where T : struct, IFloatingPointIeee754<T>
	{
		int length = a.Length;
		if (length == 0)
		{
			return T.Zero;
		}
		int count = Vector<T>.Count;
		int i = 0;
		Vector<T> zero = Vector<T>.Zero;
		for (; i + count <= length; i += count)
		{
			zero += new Vector<T>(a.Slice(i, count));
		}
		T result = Vector.Sum(zero);
		for (; i < length; i++)
		{
			result += a[i];
		}
		return result;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static T Dot<T>(ReadOnlySpan<T> a, ReadOnlySpan<T> b) where T : struct, IFloatingPointIeee754<T>
	{
		int length = a.Length;
		if (length == 0)
		{
			return T.Zero;
		}
		int count = Vector<T>.Count;
		int countDifference = count + count;
		int i = 0;
		Vector<T> zero = Vector<T>.Zero;
		Vector<T> zero2 = Vector<T>.Zero;
		for (; i + countDifference <= length; i += countDifference)
		{
			zero += new Vector<T>(a.Slice(i, count)) * new Vector<T>(b.Slice(i, count));
			zero2 += new Vector<T>(a.Slice(i + count, count)) * new Vector<T>(b.Slice(i + count, count));
		}
		Vector<T> zeroPlusZero2 = zero + zero2;
		for (; i + count <= length; i += count)
		{
			zeroPlusZero2 += new Vector<T>(a.Slice(i, count)) * new Vector<T>(b.Slice(i, count));
		}
		T result = Vector.Sum(zeroPlusZero2);
		for (; i < length; i++)
		{
			result += a[i] * b[i];
		}
		return result;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static T Max<T>(ReadOnlySpan<T> a) where T : struct, IFloatingPointIeee754<T>
	{
		int length = a.Length;
		if (length == 0)
		{
			return T.Zero;
		}
		int count = Vector<T>.Count;
		if (length < count)
		{
			T localValue = a[0];
			for (int i = 1; i < length; i++)
			{
				if (a[i] > localValue)
				{
					localValue = a[i];
				}
			}
			return localValue;
		}
		Vector<T> left = new Vector<T>(a.Slice(0, count));
		int j;
		for (j = count; j + count <= length; j += count)
		{
			left = Vector.Max(left, new Vector<T>(a.Slice(j, count)));
		}
		T left2 = left[0];
		for (int k = 1; k < count; k++)
		{
			if (left[k] > left2)
			{
				left2 = left[k];
			}
		}
		for (; j < length; j++)
		{
			if (a[j] > left2)
			{
				left2 = a[j];
			}
		}
		return left2;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static T Min<T>(ReadOnlySpan<T> a) where T : struct, IFloatingPointIeee754<T>
	{
		int length = a.Length;
		if (length == 0)
		{
			return T.Zero;
		}
		int count = Vector<T>.Count;
		if (length < count)
		{
			T localValue = a[0];
			for (int i = 1; i < length; i++)
			{
				if (a[i] < localValue)
				{
					localValue = a[i];
				}
			}
			return localValue;
		}
		Vector<T> left = new Vector<T>(a.Slice(0, count));
		int j;
		for (j = count; j + count <= length; j += count)
		{
			left = Vector.Min(left, new Vector<T>(a.Slice(j, count)));
		}
		T left2 = left[0];
		for (int k = 1; k < count; k++)
		{
			if (left[k] < left2)
			{
				left2 = left[k];
			}
		}
		for (; j < length; j++)
		{
			if (a[j] < left2)
			{
				left2 = a[j];
			}
		}
		return left2;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static T Range<T>(ReadOnlySpan<T> a) where T : struct, IFloatingPointIeee754<T>
	{
		int length = a.Length;
		if (length == 0)
		{
			return T.Zero;
		}
		int count = Vector<T>.Count;
		if (length < count)
		{
			T localValue = a[0];
			T localValue2 = a[0];
			for (int i = 1; i < length; i++)
			{
				if (a[i] < localValue)
				{
					localValue = a[i];
				}
				if (a[i] > localValue2)
				{
					localValue2 = a[i];
				}
			}
			return localValue2 - localValue;
		}
		Vector<T> vector = new Vector<T>(a.Slice(0, count));
		Vector<T> left = vector;
		int j;
		for (j = count; j + count <= length; j += count)
		{
			Vector<T> right = new Vector<T>(a.Slice(j, count));
			vector = Vector.Min(vector, right);
			left = Vector.Max(left, right);
		}
		T vector2 = vector[0];
		T left2 = left[0];
		for (int k = 1; k < count; k++)
		{
			if (vector[k] < vector2)
			{
				vector2 = vector[k];
			}
			if (left[k] > left2)
			{
				left2 = left[k];
			}
		}
		for (; j < length; j++)
		{
			if (a[j] < vector2)
			{
				vector2 = a[j];
			}
			if (a[j] > left2)
			{
				left2 = a[j];
			}
		}
		return left2 - vector2;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static void MinMax<T>(ReadOnlySpan<T> a, out T maxValue, out T minValue) where T : struct, IFloatingPointIeee754<T>
	{
		int length = a.Length;
		if (length == 0)
		{
			maxValue = T.Zero;
			minValue = T.Zero;
			return;
		}
		int count = Vector<T>.Count;
		if (length < count)
		{
			T minValue2 = a[0];
			T maxValue2 = a[0];
			for (int i = 1; i < length; i++)
			{
				if (a[i] < minValue2)
				{
					minValue2 = a[i];
				}
				if (a[i] > maxValue2)
				{
					maxValue2 = a[i];
				}
			}
			minValue = minValue2;
			maxValue = maxValue2;
			return;
		}
		Vector<T> vector = new Vector<T>(a.Slice(0, count));
		Vector<T> left = vector;
		int j;
		for (j = count; j + count <= length; j += count)
		{
			Vector<T> right = new Vector<T>(a.Slice(j, count));
			vector = Vector.Min(vector, right);
			left = Vector.Max(left, right);
		}
		T vector2 = vector[0];
		T left2 = left[0];
		for (int k = 1; k < count; k++)
		{
			if (vector[k] < vector2)
			{
				vector2 = vector[k];
			}
			if (left[k] > left2)
			{
				left2 = left[k];
			}
		}
		for (; j < length; j++)
		{
			if (a[j] < vector2)
			{
				vector2 = a[j];
			}
			if (a[j] > left2)
			{
				left2 = a[j];
			}
		}
		minValue = vector2;
		maxValue = left2;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static int ArgMax<T>(ReadOnlySpan<T> a) where T : struct, IFloatingPointIeee754<T>
	{
		int length = a.Length;
		if (length == 0)
		{
			return -1;
		}
		int result = 0;
		T localValue = a[0];
		for (int i = 1; i < length; i++)
		{
			if (a[i] > localValue)
			{
				localValue = a[i];
				result = i;
			}
		}
		return result;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static T NormalizedRmse<T>(ReadOnlySpan<T> a, ReadOnlySpan<T> b, ReadOnlySpan<bool> mask, T maxVal) where T : unmanaged, IFloatingPointIeee754<T>
	{
		int length = a.Length;
		if (length == 0 || mask.Length != length || maxVal == T.Zero)
		{
			return T.Zero;
		}
		int count = Vector<T>.Count;
		Span<T> buffer = stackalloc T[count];
		Vector<T> zero = Vector<T>.Zero;
		Vector<T> zero2 = Vector<T>.Zero;
		int i;
		for (i = 0; i + count <= length; i += count)
		{
			for (int j = 0; j < count; j++)
			{
				buffer[j] = (mask[i + j] ? T.One : T.Zero);
			}
			Vector<T> vector = new Vector<T>((ReadOnlySpan<T>)buffer);
			Vector<T> vector2 = (new Vector<T>(a.Slice(i, count)) - new Vector<T>(b.Slice(i, count))) * vector;
			zero += vector2 * vector2;
			zero2 += vector;
		}
		T sum = Vector.Sum(zero);
		T sum2 = Vector.Sum(zero2);
		for (; i < length; i++)
		{
			if (mask[i])
			{
				T localValue = a[i] - b[i];
				sum += localValue * localValue;
				sum2 += T.One;
			}
		}
		if (!(sum2 == T.Zero))
		{
			return T.Sqrt(sum / sum2) / maxVal;
		}
		return T.Zero;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static T SumWhere<T>(ReadOnlySpan<T> a, ReadOnlySpan<bool> mask) where T : unmanaged, IFloatingPointIeee754<T>
	{
		int length = a.Length;
		if (length == 0 || mask.Length != length)
		{
			return T.Zero;
		}
		int count = Vector<T>.Count;
		Span<T> buffer = stackalloc T[count];
		Vector<T> zero = Vector<T>.Zero;
		int i;
		for (i = 0; i + count <= length; i += count)
		{
			for (int j = 0; j < count; j++)
			{
				buffer[j] = (mask[i + j] ? T.One : T.Zero);
			}
			Vector<T> vector = new Vector<T>((ReadOnlySpan<T>)buffer);
			zero += new Vector<T>(a.Slice(i, count)) * vector;
		}
		T result = Vector.Sum(zero);
		for (; i < length; i++)
		{
			if (mask[i])
			{
				result += a[i];
			}
		}
		return result;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static T MeanWhere<T>(ReadOnlySpan<T> a, ReadOnlySpan<bool> mask) where T : unmanaged, IFloatingPointIeee754<T>
	{
		int length = a.Length;
		if (length == 0 || mask.Length != length)
		{
			return T.Zero;
		}
		int count = Vector<T>.Count;
		Span<T> buffer = stackalloc T[count];
		Vector<T> zero = Vector<T>.Zero;
		Vector<T> zero2 = Vector<T>.Zero;
		int i;
		for (i = 0; i + count <= length; i += count)
		{
			for (int j = 0; j < count; j++)
			{
				buffer[j] = (mask[i + j] ? T.One : T.Zero);
			}
			Vector<T> vector = new Vector<T>((ReadOnlySpan<T>)buffer);
			zero += new Vector<T>(a.Slice(i, count)) * vector;
			zero2 += vector;
		}
		T sum = Vector.Sum(zero);
		T sum2 = Vector.Sum(zero2);
		for (; i < length; i++)
		{
			if (mask[i])
			{
				sum += a[i];
				sum2 += T.One;
			}
		}
		if (!(sum2 == T.Zero))
		{
			return sum / sum2;
		}
		return T.Zero;
	}
}
