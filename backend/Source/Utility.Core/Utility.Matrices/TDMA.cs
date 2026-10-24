using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Utility.Matrices;

public static class TDMA
{
	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static void SolveInPlace<T>(ReadOnlySpan<T> a, Span<T> b, ReadOnlySpan<T> c, Span<T> d, Span<T> x) where T : struct, IFloatingPointIeee754<T>
	{
		int length = x.Length;
		if (length > 0)
		{
			for (int i = 1; i < length; i++)
			{
				T localValue = a[i] / b[i - 1];
				b[i] -= localValue * c[i - 1];
				d[i] -= localValue * d[i - 1];
			}
			x[length - 1] = d[length - 1] / b[length - 1];
			for (int lengthOffset = length - 2; lengthOffset >= 0; lengthOffset--)
			{
				x[lengthOffset] = (d[lengthOffset] - c[lengthOffset] * x[lengthOffset + 1]) / b[lengthOffset];
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static void Solve<T>(T[] a, T[] b, T[] c, T[] d, T[] x, T[] scratchB, T[] scratchD, int n) where T : struct, IFloatingPointIeee754<T>
	{
		Array.Copy(b, 0, scratchB, 0, n);
		Array.Copy(d, 0, scratchD, 0, n);
		for (int i = 1; i < n; i++)
		{
			T scratchBTerm = a[i] / scratchB[i - 1];
			T[] scratchBArray = scratchB;
			int ordinal = i;
			scratchBArray[ordinal] -= scratchBTerm * c[i - 1];
			scratchBArray = scratchD;
			ordinal = i;
			scratchBArray[ordinal] -= scratchBTerm * scratchD[i - 1];
		}
		x[n - 1] = scratchD[n - 1] / scratchB[n - 1];
		for (int ordinal2 = n - 2; ordinal2 >= 0; ordinal2--)
		{
			x[ordinal2] = (scratchD[ordinal2] - c[ordinal2] * x[ordinal2 + 1]) / scratchB[ordinal2];
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static void Solve<T>(T[] a, T[] b, T[] c, T[] d, T[] x, T[] scratchB, T[] scratchD, int start, int count) where T : struct, IFloatingPointIeee754<T>
	{
		if (count > 0)
		{
			int count2 = start + count - 1;
			Array.Copy(b, start, scratchB, start, count);
			Array.Copy(d, start, scratchD, start, count);
			for (int i = start + 1; i <= count2; i++)
			{
				T scratchBTerm = a[i] / scratchB[i - 1];
				T[] scratchBArray = scratchB;
				int ordinal = i;
				scratchBArray[ordinal] -= scratchBTerm * c[i - 1];
				scratchBArray = scratchD;
				ordinal = i;
				scratchBArray[ordinal] -= scratchBTerm * scratchD[i - 1];
			}
			x[count2] = scratchD[count2] / scratchB[count2];
			for (int count2Offset = count2 - 1; count2Offset >= start; count2Offset--)
			{
				x[count2Offset] = (scratchD[count2Offset] - c[count2Offset] * x[count2Offset + 1]) / scratchB[count2Offset];
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static void SolvePcr<T>(T[] a, T[] b, T[] c, T[] d, T[] x, int n, T[] a0, T[] b0, T[] c0, T[] d0, T[] a1, T[] b1, T[] c1, T[] d1) where T : struct, IFloatingPointIeee754<T>
	{
		Array.Copy(a, a0, n);
		Array.Copy(b, b0, n);
		Array.Copy(c, c0, n);
		Array.Copy(d, d0, n);
		int ceilLog2 = CeilLog2(n);
		int count = Vector<T>.Count;
		for (int i = 0; i < ceilLog2; i++)
		{
			int ordinal = 1 << i;
			int ordinal2 = ordinal;
			int count2 = n - ordinal;
			int count3 = Math.Min(ordinal, n);
			for (int j = 0; j < count3; j++)
			{
				int ordinal3 = j + ordinal;
				if (ordinal3 < n)
				{
					T c0OverB0 = -c0[j] / b0[ordinal3];
					a1[j] = T.Zero;
					c1[j] = c0OverB0 * c0[ordinal3];
					b1[j] = b0[j] + c0OverB0 * a0[ordinal3];
					d1[j] = d0[j] + c0OverB0 * d0[ordinal3];
				}
				else
				{
					a1[j] = T.Zero;
					c1[j] = T.Zero;
					b1[j] = b0[j];
					d1[j] = d0[j];
				}
			}
			if (ordinal2 < count2)
			{
				int k;
				for (k = ordinal2; k + count <= count2; k += count)
				{
					Vector<T> vector = new Vector<T>(a0, k);
					Vector<T> vector2 = new Vector<T>(b0, k);
					Vector<T> vector3 = new Vector<T>(c0, k);
					Vector<T> vector4 = new Vector<T>(d0, k);
					Vector<T> vector5 = new Vector<T>(b0, k - ordinal);
					Vector<T> vector6 = new Vector<T>(c0, k - ordinal);
					Vector<T> vector7 = new Vector<T>(a0, k - ordinal);
					Vector<T> vector8 = new Vector<T>(d0, k - ordinal);
					Vector<T> vector9 = new Vector<T>(b0, k + ordinal);
					Vector<T> vector10 = new Vector<T>(a0, k + ordinal);
					Vector<T> vector11 = new Vector<T>(c0, k + ordinal);
					Vector<T> vector12 = new Vector<T>(d0, k + ordinal);
					Vector<T> vector13 = -vector / vector5;
					Vector<T> vector14 = -vector3 / vector9;
					(vector13 * vector7).CopyTo(a1, k);
					(vector14 * vector11).CopyTo(c1, k);
					(vector2 + vector13 * vector6 + vector14 * vector10).CopyTo(b1, k);
					(vector4 + vector13 * vector8 + vector14 * vector12).CopyTo(d1, k);
				}
				for (; k < count2; k++)
				{
					T a0OverB0 = -a0[k] / b0[k - ordinal];
					T c0OverB02 = -c0[k] / b0[k + ordinal];
					a1[k] = a0OverB0 * a0[k - ordinal];
					c1[k] = c0OverB02 * c0[k + ordinal];
					b1[k] = b0[k] + a0OverB0 * c0[k - ordinal] + c0OverB02 * a0[k + ordinal];
					d1[k] = d0[k] + a0OverB0 * d0[k - ordinal] + c0OverB02 * d0[k + ordinal];
				}
			}
			for (int l = Math.Max(count2, count3); l < n; l++)
			{
				int ordinal4 = l - ordinal;
				if (ordinal4 >= 0)
				{
					T a0OverB02 = -a0[l] / b0[ordinal4];
					a1[l] = a0OverB02 * a0[ordinal4];
					c1[l] = T.Zero;
					b1[l] = b0[l] + a0OverB02 * c0[ordinal4];
					d1[l] = d0[l] + a0OverB02 * d0[ordinal4];
				}
				else
				{
					a1[l] = T.Zero;
					c1[l] = T.Zero;
					b1[l] = b0[l];
					d1[l] = d0[l];
				}
			}
			T[] a1Array = a1;
			a1 = a0;
			a0 = a1Array;
			T[] b1Array = b1;
			b1 = b0;
			b0 = b1Array;
			T[] c1Array = c1;
			c1 = c0;
			c0 = c1Array;
			T[] d1Array = d1;
			d1 = d0;
			d0 = d1Array;
		}
		int m;
		for (m = 0; m + count <= n; m += count)
		{
			(new Vector<T>(d0, m) / new Vector<T>(b0, m)).CopyTo(x, m);
		}
		for (; m < n; m++)
		{
			x[m] = d0[m] / b0[m];
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int CeilLog2(int n)
	{
		int ordinal = 0;
		int unity = 1;
		while (unity < n)
		{
			unity <<= 1;
			ordinal++;
		}
		return ordinal;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static void MatMul<T>(T[] a, T[] b, T[] c, T[] x, T[] d, int n) where T : struct, IFloatingPointIeee754<T>
	{
		if (n <= 1)
		{
			if (n == 1)
			{
				d[0] = b[0] * x[0];
			}
			return;
		}
		d[0] = b[0] * x[0] + c[0] * x[1];
		int count = Vector<T>.Count;
		int i;
		for (i = 1; i + count <= n - 1; i += count)
		{
			Vector<T> vector = new Vector<T>(a, i);
			Vector<T> vector2 = new Vector<T>(x, i - 1);
			Vector<T> vector3 = new Vector<T>(b, i);
			Vector<T> vector4 = new Vector<T>(x, i);
			Vector<T> vector5 = new Vector<T>(c, i);
			Vector<T> vector6 = new Vector<T>(x, i + 1);
			(vector * vector2 + vector3 * vector4 + vector5 * vector6).CopyTo(d, i);
		}
		for (; i < n - 1; i++)
		{
			d[i] = a[i] * x[i - 1] + b[i] * x[i] + c[i] * x[i + 1];
		}
		d[n - 1] = a[n - 1] * x[n - 2] + b[n - 1] * x[n - 1];
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static void MatMul<T>(T[] a, T[] b, T[] c, T[] x, T[] d, int start, int count) where T : struct, IFloatingPointIeee754<T>
	{
		if (count <= 0)
		{
			return;
		}
		int count3 = start + count - 1;
		if (count == 1)
		{
			d[start] = b[start] * x[start];
			return;
		}
		d[start] = b[start] * x[start] + c[start] * x[start + 1];
		int count2 = Vector<T>.Count;
		int i;
		for (i = start + 1; i + count2 <= count3; i += count2)
		{
			Vector<T> vector = new Vector<T>(a, i);
			Vector<T> vector2 = new Vector<T>(x, i - 1);
			Vector<T> vector3 = new Vector<T>(b, i);
			Vector<T> vector4 = new Vector<T>(x, i);
			Vector<T> vector5 = new Vector<T>(c, i);
			Vector<T> vector6 = new Vector<T>(x, i + 1);
			(vector * vector2 + vector3 * vector4 + vector5 * vector6).CopyTo(d, i);
		}
		for (; i < count3; i++)
		{
			d[i] = a[i] * x[i - 1] + b[i] * x[i] + c[i] * x[i + 1];
		}
		d[count3] = a[count3] * x[count3 - 1] + b[count3] * x[count3];
	}
}
