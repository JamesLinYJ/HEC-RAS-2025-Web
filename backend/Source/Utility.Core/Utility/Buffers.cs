using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Threading.Tasks;
using Utility.Extensions;

namespace Utility;

public static class Buffers
{
	public static void EnsureArraySize<T>(ref T[] a, int count)
	{
		if (a == null || a.Length != count)
		{
			a = new T[count];
		}
	}

	public static void EnsureArraySize<T>(ref T[,] a, int height, int width)
	{
		if (a == null || a.GetLength(0) != height || a.GetLength(1) != width)
		{
			a = new T[height, width];
		}
	}

	public static void ArraySetAll<T>(ref T[] a, T v, int count) where T : unmanaged
	{
		EnsureArraySize(ref a, count);
		a.Fill(v);
	}

	public static void ArraySetAllVB<T>(ref T[] a, T v, int count) where T : struct
	{
		EnsureArraySize(ref a, count);
		ArraySetAllVB(a, v);
	}

	public static void ArraySetAllVB<T>(T[] a, T v) where T : struct
	{
		a.AsSpan().Fill(v);
	}

	public static void ArraySetIfEqual<T, K>(Memory<T> inBuffer, T matchValue, Memory<K> outBuffer, K outValue) where T : unmanaged, IEquatable<T> where K : unmanaged, IEquatable<K>
	{
		Span<T> inBufferSpan = inBuffer.Span;
		Span<K> outBufferSpan = outBuffer.Span;
		for (int i = 0; i < inBufferSpan.Length; i++)
		{
			if (inBufferSpan[i].Equals(matchValue))
			{
				outBufferSpan[i] = outValue;
			}
		}
	}

	public static void ArraySetIfNotEqual<T, K>(Memory<T> inBuffer, T matchValue, Memory<K> outBuffer, K outValue) where T : unmanaged, IEquatable<T> where K : unmanaged, IEquatable<K>
	{
		Span<T> inBufferSpan = inBuffer.Span;
		Span<K> outBufferSpan = outBuffer.Span;
		for (int i = 0; i < inBufferSpan.Length; i++)
		{
			if (!inBufferSpan[i].Equals(matchValue))
			{
				outBufferSpan[i] = outValue;
			}
		}
	}

	private unsafe static void ArraySetIfEqual_AVX2_4Byte<T, K>(Span<T> inBuffer, T matchValue, Span<K> outBuffer, K outValue) where T : unmanaged, IEquatable<T> where K : unmanaged, IEquatable<K>
	{
		int ordinal = sizeof(T);
		int ordinal2 = sizeof(K);
		if (ordinal != 4 || ordinal2 != 4)
		{
			throw new ArgumentException("Input arguments must currently be 4-byte values (float, int).");
		}
		if (inBuffer.Length != outBuffer.Length)
		{
			throw new ArgumentException();
		}
		int length = inBuffer.Length;
		if (length < 1024)
		{
			for (int i = 0; i < length; i++)
			{
				if (inBuffer[i].Equals(matchValue))
				{
					outBuffer[i] = outValue;
				}
			}
		}
		fixed (T* inBuffer2 = inBuffer)
		{
			fixed (K* outBuffer2 = outBuffer)
			{
				int* inBuffer3 = (int*)inBuffer2;
				int* outBuffer3 = (int*)outBuffer2;
				int count = 32 / ordinal;
				int matchValueTerm = *(int*)(&matchValue);
				int outValueTerm = *(int*)(&outValue);
				int* countTerm = stackalloc int[count];
				for (int j = 0; j < count; j++)
				{
					countTerm[j] = matchValueTerm;
				}
				Vector256<int> right = Avx.LoadVector256(countTerm);
				for (int k = 0; k < count; k++)
				{
					countTerm[k] = outValueTerm;
				}
				Vector256<int> source = Avx.LoadVector256(countTerm);
				int count2 = length / count;
				for (int l = 0; l < count2; l++)
				{
					int countTerm2 = l * count;
					Vector256<int> mask = Avx2.CompareEqual(Avx.LoadVector256(inBuffer3 + countTerm2), right);
					Avx2.MaskStore(outBuffer3 + countTerm2, mask, source);
				}
				for (int m = (count2 - 1) * count; m < length; m++)
				{
					if (inBuffer[m].Equals(matchValue))
					{
						outBuffer[m] = outValue;
					}
				}
			}
		}
	}

	public static void ArraySetAll<T>(T[,] a, T v) where T : struct
	{
		for (int i = 0; i < a.GetLength(0); i++)
		{
			for (int j = 0; j < a.GetLength(1); j++)
			{
				a[i, j] = v;
			}
		}
	}

	public static void ArrayCopyUnsafe<TFrom, TTo>(Memory<TFrom> source, Memory<TTo> dest) where TFrom : unmanaged where TTo : unmanaged
	{
		ArrayCopyUnsafe(source.Span, dest.Span);
	}

	public unsafe static void ArrayCopyUnsafe<TFrom, TTo>(Span<TFrom> source, Span<TTo> dest) where TFrom : unmanaged where TTo : unmanaged
	{
		int sourceSizeOfTerm = source.Length * Marshal.SizeOf<TFrom>();
		if (dest.Length * Marshal.SizeOf<TTo>() < sourceSizeOfTerm)
		{
			throw new ArgumentException("Not enough space in destination buffer.");
		}
		fixed (TFrom* source2 = source)
		{
			fixed (TTo* destination = dest)
			{
				Buffer.MemoryCopy(source2, destination, sourceSizeOfTerm, sourceSizeOfTerm);
			}
		}
	}

	public static void ConvertAll(Memory<int> from, Memory<float> to)
	{
		if (Avx.IsSupported)
		{
			ArrayConvertAllSIMD_AVX(from, to);
			return;
		}
		Span<int> intSpan = from.Span;
		Span<float> toSpan = to.Span;
		int length = intSpan.Length;
		for (int i = 0; i < length; i++)
		{
			toSpan[i] = intSpan[i];
		}
	}

	public static void ConvertAll(Memory<float> from, Memory<double> to)
	{
		if (Avx.IsSupported)
		{
			ArrayConvertAllSIMD_AVX(from, to);
			return;
		}
		Span<float> floatSpan = from.Span;
		Span<double> toSpan = to.Span;
		int length = floatSpan.Length;
		for (int i = 0; i < length; i++)
		{
			toSpan[i] = floatSpan[i];
		}
	}

	public static void ConvertAll(Memory<double> from, Memory<float> to)
	{
		if (Avx.IsSupported)
		{
			ArrayConvertAllSIMD_AVX(from, to);
			return;
		}
		Span<double> doubleSpan = from.Span;
		Span<float> toSpan = to.Span;
		int length = doubleSpan.Length;
		for (int i = 0; i < length; i++)
		{
			toSpan[i] = (float)doubleSpan[i];
		}
	}

	private unsafe static void ArrayConvertAllSIMD_AVX(Memory<int> fromBuffer, Memory<float> toBuffer)
	{
		Span<int> fromBufferSpan = fromBuffer.Span;
		Span<float> toBufferSpan = toBuffer.Span;
		int length = fromBufferSpan.Length;
		int scaledLength = length * 4;
		if (length < 1024)
		{
			for (int i = 0; i < length; i++)
			{
				toBufferSpan[i] = fromBufferSpan[i];
			}
			return;
		}
		int count = scaledLength / 32;
		fixed (int* fromBuffer2 = fromBufferSpan)
		{
			fixed (float* toBuffer2 = toBufferSpan)
			{
				for (int j = 0; j < count; j++)
				{
					int ordinal = j * 8;
					Vector256<float> source = Avx.ConvertToVector256Single(Avx.LoadVector256(fromBuffer2 + ordinal));
					Avx.Store(toBuffer2 + ordinal, source);
				}
				for (int k = (count - 1) * 8; k < length; k++)
				{
					toBufferSpan[k] = fromBuffer2[k];
				}
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public unsafe static void Convert4_AVX(float* from, double* to)
	{
		Vector256<double> source = Avx.ConvertToVector256Double(Sse.LoadVector128(from));
		Avx.Store(to, source);
	}

	private unsafe static void ArrayConvertAllSIMD_AVX(Memory<float> fromBuffer, Memory<double> toBuffer)
	{
		Span<float> fromBufferSpan = fromBuffer.Span;
		Span<double> toBufferSpan = toBuffer.Span;
		int length = fromBufferSpan.Length;
		if (length < 1024)
		{
			for (int i = 0; i < length; i++)
			{
				toBufferSpan[i] = fromBufferSpan[i];
			}
			return;
		}
		int count = length / Vector128<float>.Count;
		fixed (float* fromBuffer2 = fromBufferSpan)
		{
			fixed (double* toBuffer2 = toBufferSpan)
			{
				for (int j = 0; j < count; j++)
				{
					int ordinal = j * Vector128<float>.Count;
					Vector256<double> source = Avx.ConvertToVector256Double(Sse.LoadVector128(fromBuffer2 + ordinal));
					Avx.Store(toBuffer2 + ordinal, source);
				}
				for (int k = (count - 1) * Vector128<float>.Count; k < length; k++)
				{
					toBufferSpan[k] = fromBuffer2[k];
				}
			}
		}
	}

	private unsafe static void ArrayConvertAllSIMD_AVX(Memory<double> fromBuffer, Memory<float> toBuffer)
	{
		Span<double> fromBufferSpan = fromBuffer.Span;
		Span<float> toBufferSpan = toBuffer.Span;
		int length = fromBufferSpan.Length;
		if (length < 1024)
		{
			for (int i = 0; i < length; i++)
			{
				toBufferSpan[i] = (float)fromBufferSpan[i];
			}
			return;
		}
		int count = Vector256<double>.Count;
		int count2 = length / count;
		fixed (double* fromBuffer2 = fromBufferSpan)
		{
			fixed (float* toBuffer2 = toBufferSpan)
			{
				for (int j = 0; j < count2; j++)
				{
					int countTerm = j * count;
					Vector128<float> source = Avx.ConvertToVector128Single(Avx.LoadVector256(fromBuffer2 + countTerm));
					Sse.Store(toBuffer2 + countTerm, source);
				}
				for (int k = (count2 - 1) * count; k < length; k++)
				{
					toBufferSpan[k] = (float)fromBuffer2[k];
				}
			}
		}
	}

	public static void ReplaceValue<T>(Span<T> sp, T from, T to) where T : unmanaged, IEquatable<T>
	{
		for (int i = 0; i < sp.Length; i++)
		{
			if (sp[i].Equals(from))
			{
				sp[i] = to;
			}
		}
	}

	public unsafe static void ReplaceValue(Span<float> bufferIO, float from, float to)
	{
		int length = bufferIO.Length;
		int lengthCountTerm = 0;
		if (Avx.IsSupported)
		{
			int count = Vector128<float>.Count;
			if (length >= count)
			{
				Vector128<float> right = Vector128.Create(from);
				Vector128<float> source = Vector128.Create(to);
				fixed (float* bufferIO2 = bufferIO)
				{
					for (int i = 0; i <= length - count; i += count)
					{
						float* bufferIO2Term = bufferIO2 + i;
						Vector128<float> mask = Sse.CompareEqual(Vector128.Load(bufferIO2Term), right);
						Avx.MaskStore(bufferIO2Term, mask, source);
					}
				}
			}
			lengthCountTerm = length - length % count;
		}
		for (int j = lengthCountTerm; j < length; j++)
		{
			if (bufferIO[j] == from)
			{
				bufferIO[j] = to;
			}
		}
	}

	public static void ReplaceIfLTE<T>(Span<T> sp, T from, T to) where T : unmanaged, INumber<T>
	{
		for (int i = 0; i < sp.Length; i++)
		{
			if (sp[i] <= from)
			{
				sp[i] = to;
			}
		}
	}

	public static void ReplaceIfGTE<T>(Span<T> sp, T from, T to) where T : unmanaged, INumber<T>
	{
		for (int i = 0; i < sp.Length; i++)
		{
			if (sp[i] >= from)
			{
				sp[i] = to;
			}
		}
	}

	public static bool Compare<T>(Span<T> a, Span<T> b) where T : unmanaged, IEquatable<T>
	{
		bool isEmpty = a.IsEmpty;
		bool isEmpty2 = b.IsEmpty;
		if (isEmpty & isEmpty2)
		{
			return true;
		}
		if (isEmpty | isEmpty2)
		{
			return false;
		}
		if (a.Length != b.Length)
		{
			return false;
		}
		if (Avx2.IsSupported)
		{
			return ArrayCompare_AVX2(a, b);
		}
		return CompareSerial(a, b);
	}

	public static bool CompareStrictIEEEFP<T>(Span<T> a, Span<T> b) where T : unmanaged, INumber<T>
	{
		if (typeof(T) != typeof(float) && typeof(T) != typeof(double))
		{
			return Compare(a, b);
		}
		bool isEmpty = a.IsEmpty;
		bool isEmpty2 = b.IsEmpty;
		if (isEmpty & isEmpty2)
		{
			return true;
		}
		if (isEmpty | isEmpty2)
		{
			return false;
		}
		if (a.Length != b.Length)
		{
			return false;
		}
		int lengthCountTerm = 0;
		int length = a.Length;
		if (Vector.IsHardwareAccelerated)
		{
			int count = Vector<T>.Count;
			if (length >= count)
			{
				for (int i = 0; i <= length - count; i += count)
				{
					Vector<T> vector = new Vector<T>((ReadOnlySpan<T>)a.Slice(i));
					Vector<T> vector2 = new Vector<T>((ReadOnlySpan<T>)b.Slice(i));
					if (!(vector == vector2))
					{
						return false;
					}
				}
			}
			lengthCountTerm = length - length % count;
		}
		for (int j = lengthCountTerm; j < length; j++)
		{
			if (!(a[j] == b[j]))
			{
				return false;
			}
		}
		return true;
	}

	public static bool EqualsScalar<T>(Span<T> a, T b) where T : unmanaged, IEquatable<T>
	{
		for (int i = 0; i < a.Length; i++)
		{
			if (!a[i].Equals(b))
			{
				return false;
			}
		}
		return true;
	}

	private unsafe static bool ArrayCompare_AVX2<T>(Span<T> a, Span<T> b) where T : unmanaged, IEquatable<T>
	{
		int length = a.Length;
		int ordinal = sizeof(T);
		int lengthTerm = length * ordinal;
		if (lengthTerm < 1024)
		{
			return CompareSerial(a, b);
		}
		int ordinal2 = 32 / ordinal;
		fixed (T* localValue = a)
		{
			fixed (T* localValue2 = b)
			{
				int count = lengthTerm / 32;
				byte* address = (byte*)localValue;
				byte* address2 = (byte*)localValue2;
				int ordinal3 = 0;
				for (int i = 0; i < count; i++)
				{
					Vector256<byte> left = Avx.LoadVector256(address);
					Vector256<byte> right = Avx.LoadVector256(address2);
					if (Avx2.MoveMask(Avx2.CompareEqual(left, right)) != -1)
					{
						return false;
					}
					ordinal3 += 32;
				}
				for (int j = (count - 1) * ordinal2; j < length; j++)
				{
					if (!localValue[j].Equals(localValue2[j]))
					{
						return false;
					}
				}
				return true;
			}
		}
	}

	private static bool CompareSerial<T>(Span<T> asp, Span<T> bsp) where T : unmanaged, IEquatable<T>
	{
		for (int i = 0; i < asp.Length; i++)
		{
			if (!asp[i].Equals(bsp[i]))
			{
				return false;
			}
		}
		return true;
	}

	private unsafe static void ArrayGT0(Span<float> buf)
	{
		int length = buf.Length;
		int scaledLength = length * 4;
		if (length < 64)
		{
			throw new NotImplementedException();
		}
		int count = scaledLength / 32;
		fixed (float* buf2 = buf)
		{
			for (int i = 0; i < count; i++)
			{
				int ordinal = i * 8;
				Vector256<float> source = Avx.Compare(Avx.LoadVector256(buf2 + ordinal), Vector256<float>.Zero, FloatComparisonMode.OrderedGreaterThanSignaling);
				Avx.Store(buf2 + ordinal, source);
			}
		}
		throw new NotImplementedException();
	}

	public static void MaxMasked(Span<double> values, Span<byte> mask, out double max)
	{
		MaxMasked_Direct(values, mask, out max);
	}

	public static void MaxMasked(Span<double> values, Span<byte> mask, out double max, out int index)
	{
		max = double.MinValue;
		index = -1;
		for (int i = 0; i < values.Length; i++)
		{
			if (mask[i] != 0 && values[i] > max)
			{
				max = values[i];
				index = i;
			}
		}
	}

	private static void MaxMasked_Direct(Span<double> values, Span<byte> mask, out double max)
	{
		max = double.MinValue;
		for (int i = 0; i < values.Length; i++)
		{
			if (mask[i] != 0 && values[i] > max)
			{
				max = values[i];
			}
		}
	}

	private unsafe static void MaxMasked_AVX(Span<double> values, Span<byte> mask, out double max)
	{
		max = double.MinValue;
		int length = values.Length;
		int count = length / 4;
		fixed (double* localValue = values)
		{
			fixed (byte* mask2 = mask)
			{
				double minValue = double.MinValue;
				Vector256<double> vector = Avx.BroadcastScalarToVector256(&minValue);
				for (int i = 0; i < count; i++)
				{
					int ordinal = i * 4;
					byte b = mask2[ordinal];
					byte b2 = mask2[ordinal + 1];
					byte b3 = mask2[ordinal + 2];
					byte b4 = mask2[ordinal + 3];
					switch ((int)(((b != 0) ? 1u : 0u) | (uint)((b2 != 0) ? 2 : 0) | (uint)((b3 != 0) ? 4 : 0) | (uint)((b4 != 0) ? 8 : 0)))
					{
					case 15:
					{
						Vector256<double> right = Avx.LoadVector256(localValue + ordinal);
						vector = Avx.Max(vector, right);
						continue;
					}
					case 0:
						continue;
					}
					for (int j = 0; j < 4; j++)
					{
						if (mask2[ordinal + j] != 0)
						{
							double max2 = localValue[ordinal + j];
							if (max2 > max)
							{
								max = max2;
							}
						}
					}
				}
				double* localValue2 = stackalloc double[4];
				Avx.Store(localValue2, vector);
				for (int k = 0; k < 4; k++)
				{
					if (localValue2[k] > max)
					{
						max = localValue2[k];
					}
				}
				for (int l = count * 4; l < length; l++)
				{
					if (mask2[l] != 0 && localValue[l] > max)
					{
						max = localValue[l];
					}
				}
			}
		}
	}

	private unsafe static void MaxMaskedWithIndex_AVX2(Span<double> values, Span<byte> mask, out double max, out int index)
	{
		max = double.MinValue;
		index = -1;
		int length = values.Length;
		int count = length / 4;
		fixed (double* localValue = values)
		{
			fixed (byte* mask2 = mask)
			{
				double minValue = double.MinValue;
				Vector256<double> vector = Avx.BroadcastScalarToVector256(&minValue);
				Vector256<long> vector2 = Vector256.Create(-1L);
				Vector256<long> vector3 = Vector256.Create(0L, 1L, 2L, 3L);
				Vector256<long> right = Vector256.Create(4L);
				for (int i = 0; i < count; i++)
				{
					int ordinal = i * 4;
					byte b = mask2[ordinal];
					byte b2 = mask2[ordinal + 1];
					byte b3 = mask2[ordinal + 2];
					byte b4 = mask2[ordinal + 3];
					switch ((int)(((b != 0) ? 1u : 0u) | (uint)((b2 != 0) ? 2 : 0) | (uint)((b3 != 0) ? 4 : 0) | (uint)((b4 != 0) ? 8 : 0)))
					{
					case 0:
						vector3 = Avx2.Add(vector3, right);
						continue;
					case 15:
					{
						Vector256<double> vector6 = Avx.LoadVector256(localValue + ordinal);
						Vector256<double> vector7 = Avx.CompareGreaterThan(vector6, vector);
						vector = Avx.BlendVariable(vector, vector6, vector7);
						vector2 = Avx2.BlendVariable(vector2, vector3, vector7.AsInt64());
						break;
					}
					default:
					{
						Vector256<double> vector4 = Avx.LoadVector256(localValue + ordinal);
						Vector256<double> left = Vector256.Create((b != 0) ? (-1) : 0, (b2 != 0) ? (-1) : 0, (b3 != 0) ? (-1) : 0, (b4 != 0) ? (-1) : 0).AsDouble();
						Vector256<double> right2 = Avx.CompareGreaterThan(vector4, vector);
						Vector256<double> vector5 = Avx.And(left, right2);
						vector = Avx.BlendVariable(vector, vector4, vector5);
						vector2 = Avx2.BlendVariable(vector2, vector3, vector5.AsInt64());
						break;
					}
					}
					vector3 = Avx2.Add(vector3, right);
				}
				double* localValue2 = stackalloc double[4];
				long* localValue3 = stackalloc long[4];
				Avx.Store(localValue2, vector);
				Avx.Store(localValue3, vector2);
				for (int j = 0; j < 4; j++)
				{
					if (localValue2[j] > max)
					{
						max = localValue2[j];
						index = (int)localValue3[j];
					}
				}
				for (int k = count * 4; k < length; k++)
				{
					if (mask2[k] != 0 && localValue[k] > max)
					{
						max = localValue[k];
						index = k;
					}
				}
			}
		}
	}

	public static void MaxMasked_Parallel(Memory<double> values, Memory<byte> mask, out double max, int loopSize = 2048)
	{
		int len = values.Length;
		int count = (int)Math.Ceiling((double)len / (double)loopSize);
		if (count <= 1)
		{
			MaxMasked(values.Span, mask.Span, out max);
			return;
		}
		double[] chunkMaxes = new double[count];
		for (int i = 0; i < count; i++)
		{
			chunkMaxes[i] = double.MinValue;
		}
		System.Threading.Tasks.Parallel.For(0, count, (int n) =>
		{
			int loopSizeTerm = n * loopSize;
			int count2 = Math.Min(loopSize, len - loopSizeTerm);
			Span<double> slice = values.Span.Slice(loopSizeTerm, count2);
			Span<byte> sliceSpan = mask.Span.Slice(loopSizeTerm, count2);
			double minValue = double.MinValue;
			for (int j = 0; j < count2; j++)
			{
				if (sliceSpan[j] != 0 && slice[j] > minValue)
				{
					minValue = slice[j];
				}
			}
			chunkMaxes[n] = minValue;
		});
		max = double.MinValue;
		for (int intervalIndex = 0; intervalIndex < count; intervalIndex++)
		{
			if (chunkMaxes[intervalIndex] > max)
			{
				max = chunkMaxes[intervalIndex];
			}
		}
	}

	public static void MaxMasked_Parallel(Memory<double> values, Memory<byte> mask, out double max, out int index, int loopSize = 2048)
	{
		int len = values.Length;
		int count = (int)Math.Ceiling((double)len / (double)loopSize);
		if (count <= 1)
		{
			MaxMasked(values.Span, mask.Span, out max, out index);
			return;
		}
		double[] chunkMaxes = new double[count];
		int[] chunkIndices = new int[count];
		for (int i = 0; i < count; i++)
		{
			chunkMaxes[i] = double.MinValue;
			chunkIndices[i] = -1;
		}
		System.Threading.Tasks.Parallel.For(0, count, (int n) =>
		{
			int loopSizeTerm = n * loopSize;
			int count2 = Math.Min(loopSize, len - loopSizeTerm);
			Span<double> slice = values.Span.Slice(loopSizeTerm, count2);
			Span<byte> sliceSpan = mask.Span.Slice(loopSizeTerm, count2);
			double minValue = double.MinValue;
			int chunkIndices2 = -1;
			for (int j = 0; j < count2; j++)
			{
				if (sliceSpan[j] != 0 && slice[j] > minValue)
				{
					minValue = slice[j];
					chunkIndices2 = loopSizeTerm + j;
				}
			}
			chunkMaxes[n] = minValue;
			chunkIndices[n] = chunkIndices2;
		});
		max = double.MinValue;
		index = -1;
		for (int intervalIndex = 0; intervalIndex < count; intervalIndex++)
		{
			if (chunkMaxes[intervalIndex] > max)
			{
				max = chunkMaxes[intervalIndex];
				index = chunkIndices[intervalIndex];
			}
		}
	}

	public static void MaxMin<T>(Span<T> a, out T max, out T min) where T : unmanaged, INumber<T>, IMinMaxValue<T>
	{
		int lengthCountTerm = 0;
		int length = a.Length;
		min = T.MaxValue;
		max = T.MinValue;
		if (Vector.IsHardwareAccelerated)
		{
			int count = Vector<T>.Count;
			if (length >= count)
			{
				Vector<T> left = new Vector<T>(T.MaxValue);
				Vector<T> left2 = new Vector<T>(T.MinValue);
				for (int i = 0; i <= length - count; i += count)
				{
					Span<T> slice = a.Slice(i);
					Vector<T> right = new Vector<T>((ReadOnlySpan<T>)slice);
					left = Vector.Min(left, right);
					left2 = Vector.Max(left2, right);
				}
				for (int j = 0; j < count; j++)
				{
					min = T.Min(left[j], min);
					max = T.Max(left2[j], max);
				}
			}
			lengthCountTerm = length - length % count;
		}
		for (int k = lengthCountTerm; k < length; k++)
		{
			T x = a[k];
			min = T.Min(x, min);
			max = T.Max(x, max);
		}
	}

	public static void MaxMin(Span<float> a, float noDataValue, out float max, out float min)
	{
		MaxMin((ReadOnlySpan<float>)a, noDataValue, out max, out min);
	}

	public static void MaxMin(ReadOnlySpan<float> a, float noDataValue, out float max, out float min)
	{
		if (Avx.IsSupported)
		{
			ArrayMaxMin_AVX(a, noDataValue, out max, out min);
		}
		else
		{
			ArrayMaxMin_Direct(a, noDataValue, out max, out min);
		}
	}

	public static void MaxMin(Span<double> a, double noDataValue, out double max, out double min)
	{
		if (Avx.IsSupported)
		{
			ArrayMaxMin_AVX(a, noDataValue, out max, out min);
		}
		else
		{
			ArrayMaxMin_Direct(a, noDataValue, out max, out min);
		}
	}

	private static void ArrayMaxMin_Direct<T>(ReadOnlySpan<T> a, T noDataValue, out T max, out T min) where T : INumber<T>, IMinMaxValue<T>
	{
		max = T.MinValue;
		min = T.MaxValue;
		for (int i = 0; i < a.Length; i++)
		{
			T localValue = a[i];
			if (!(localValue == noDataValue))
			{
				if (localValue > max)
				{
					max = localValue;
				}
				if (localValue < min)
				{
					min = localValue;
				}
			}
		}
	}

	private unsafe static void ArrayMaxMin_AVX(ReadOnlySpan<float> buf, float noDataValue, out float max, out float min)
	{
		max = float.MinValue;
		min = float.MaxValue;
		int length = buf.Length;
		int count = length * 4 / 32;
		fixed (float* buf2 = buf)
		{
			Vector256<float> right = Avx.BroadcastScalarToVector256(&noDataValue);
			float minValue = float.MinValue;
			Vector256<float> vector = Avx.BroadcastScalarToVector256(&minValue);
			minValue = float.MaxValue;
			Vector256<float> vector2 = Avx.BroadcastScalarToVector256(&minValue);
			for (int i = 0; i < count; i++)
			{
				int ordinal = i * 8;
				Vector256<float> vector3 = Avx.LoadVector256(buf2 + ordinal);
				switch (Avx.MoveMask(Avx.CompareEqual(vector3, right)))
				{
				case 0:
					vector = Avx.Max(vector, vector3);
					vector2 = Avx.Min(vector2, vector3);
					continue;
				case 255:
					continue;
				}
				for (int j = ordinal; j < ordinal + 8; j++)
				{
					float buf3 = buf2[j];
					if (buf3 != noDataValue)
					{
						max = Math.Max(max, buf3);
						min = Math.Min(min, buf3);
					}
				}
			}
			float* localValue = stackalloc float[8];
			float* localValue2 = stackalloc float[8];
			Avx.Store(localValue, vector);
			Avx.Store(localValue2, vector2);
			for (int k = 0; k < 8; k++)
			{
				max = Math.Max(max, localValue[k]);
				min = Math.Min(min, localValue2[k]);
			}
			for (int l = count * 8; l < length; l++)
			{
				float buf4 = buf[l];
				if (buf4 != noDataValue)
				{
					max = Math.Max(max, buf4);
					min = Math.Min(min, buf4);
				}
			}
		}
	}

	private unsafe static void ArrayMaxMin_AVX(Span<double> buf, double noDataValue, out double max, out double min)
	{
		max = double.MinValue;
		min = double.MaxValue;
		int length = buf.Length;
		int count = length * 8 / 32;
		fixed (double* buf2 = buf)
		{
			Vector256<double> right = Avx.BroadcastScalarToVector256(&noDataValue);
			double minValue = double.MinValue;
			Vector256<double> vector = Avx.BroadcastScalarToVector256(&minValue);
			minValue = double.MaxValue;
			Vector256<double> vector2 = Avx.BroadcastScalarToVector256(&minValue);
			for (int i = 0; i < count; i++)
			{
				int ordinal = i * 4;
				Vector256<double> vector3 = Avx.LoadVector256(buf2 + ordinal);
				switch (Avx.MoveMask(Avx.CompareEqual(vector3, right)))
				{
				case 0:
					vector = Avx.Max(vector, vector3);
					vector2 = Avx.Min(vector2, vector3);
					continue;
				case 512:
					continue;
				}
				for (int j = ordinal; j < ordinal + 8; j++)
				{
					double buf3 = buf2[j];
					if (buf3 != noDataValue)
					{
						max = Math.Max(max, buf3);
						min = Math.Min(min, buf3);
					}
				}
			}
			double* localValue = stackalloc double[4];
			double* localValue2 = stackalloc double[4];
			Avx.Store(localValue, vector);
			Avx.Store(localValue2, vector2);
			for (int k = 0; k < 4; k++)
			{
				max = Math.Max(max, localValue[k]);
				min = Math.Min(min, localValue2[k]);
			}
			for (int l = count * 4; l < length; l++)
			{
				double buf4 = buf[l];
				if (buf4 != noDataValue)
				{
					max = Math.Max(max, buf4);
					min = Math.Min(min, buf4);
				}
			}
		}
	}

	private unsafe static void IndexOfMax_AVX2(Span<float> buf)
	{
		int count = buf.Length * 4 / 32;
		fixed (float* buf2 = buf)
		{
			float minValue = float.MinValue;
			Vector256<float> vector = Avx.BroadcastScalarToVector256(&minValue);
			Vector256.Create(-1);
			int* localValue = stackalloc int[8];
			for (int i = 0; i < 8; i++)
			{
				localValue[i] = i;
			}
			Vector256<int> left = Avx.LoadAlignedVector256(localValue);
			Vector256<int> right = Vector256.Create(8);
			for (int j = 0; j < count; j++)
			{
				int ordinal = j * 8;
				Vector256<float> vector2 = Avx.LoadVector256(buf2 + ordinal);
				left = Avx2.Add(left, right);
				Avx.MoveMask(Avx.CompareGreaterThan(vector2, vector));
				vector = Avx.Max(vector, vector2);
			}
			float* maximumValues = stackalloc float[8];
			Avx.Store(maximumValues, vector);
		}
		throw new NotImplementedException();
	}

	public static void MultiplyConstant<T>(Span<T> bufferIO, T ratio) where T : unmanaged, INumber<T>
	{
		int lengthCountTerm = 0;
		int length = bufferIO.Length;
		if (Vector.IsHardwareAccelerated)
		{
			int count = Vector<T>.Count;
			if (length >= count)
			{
				Vector<T> vector = new Vector<T>(ratio);
				for (int i = 0; i <= length - count; i += count)
				{
					Span<T> slice = bufferIO.Slice(i);
					(new Vector<T>((ReadOnlySpan<T>)slice) * vector).CopyTo(slice);
				}
			}
			lengthCountTerm = length - length % count;
		}
		for (int j = lengthCountTerm; j < length; j++)
		{
			bufferIO[j] *= ratio;
		}
	}

	public unsafe static void MultiplyConstant(Span<float> bufferIO, float ratio, float noDataValue)
	{
		int lengthCountTerm = 0;
		int length = bufferIO.Length;
		if (Avx.IsSupported)
		{
			int count = Vector256<float>.Count;
			if (length >= count)
			{
				Vector256<float> right = Vector256.Create(noDataValue);
				Vector256<float> vector = Vector256.Create(ratio);
				fixed (float* bufferIO2 = bufferIO)
				{
					for (int i = 0; i <= length - count; i += count)
					{
						float* bufferIO2Term = bufferIO2 + i;
						Vector256<float> vector2 = Vector256.Load(bufferIO2Term);
						Vector256<float> mask = Avx.CompareNotEqual(vector2, right);
						Vector256<float> source = vector2 * vector;
						Avx.MaskStore(bufferIO2Term, mask, source);
					}
				}
			}
			lengthCountTerm = length - length % count;
		}
		for (int j = lengthCountTerm; j < length; j++)
		{
			float bufferIO3 = bufferIO[j];
			if (bufferIO3 != noDataValue)
			{
				bufferIO[j] = bufferIO3 * ratio;
			}
		}
	}

	public unsafe static void MultiplyConstant(Span<double> bufferIO, double ratio, double noDataValue)
	{
		int lengthCountTerm = 0;
		int length = bufferIO.Length;
		if (Avx.IsSupported)
		{
			int count = Vector256<double>.Count;
			if (length >= count)
			{
				Vector256<double> right = Vector256.Create(noDataValue);
				Vector256<double> vector = Vector256.Create(ratio);
				fixed (double* bufferIO2 = bufferIO)
				{
					for (int i = 0; i <= length - count; i += count)
					{
						double* bufferIO2Term = bufferIO2 + i;
						Vector256<double> vector2 = Vector256.Load(bufferIO2Term);
						Vector256<double> mask = Avx.CompareNotEqual(vector2, right);
						Vector256<double> source = vector2 * vector;
						Avx.MaskStore(bufferIO2Term, mask, source);
					}
				}
			}
			lengthCountTerm = length - length % count;
		}
		for (int j = lengthCountTerm; j < length; j++)
		{
			double bufferIO3 = bufferIO[j];
			if (bufferIO3 != noDataValue)
			{
				bufferIO[j] = bufferIO3 * ratio;
			}
		}
	}

	public static void MultiplyVector(Span<double> sp1, Span<double> sp2, Span<double> spOut)
	{
		int length = sp1.Length;
		int count = Vector<double>.Count;
		if (length >= count)
		{
			for (int i = 0; i <= length - count; i += count)
			{
				Vector<double> vector = new Vector<double>((ReadOnlySpan<double>)sp1.Slice(i));
				Vector<double> vector2 = new Vector<double>((ReadOnlySpan<double>)sp2.Slice(i));
				(vector * vector2).CopyTo(spOut.Slice(i));
			}
		}
		int lengthWithCount = length % count;
		for (int j = length - lengthWithCount; j < length; j++)
		{
			spOut[j] = sp1[j] * sp2[j];
		}
	}

	public static void AddConstant<T>(Span<T> bufferIO, T addValue) where T : unmanaged, INumber<T>
	{
		int lengthCountTerm = 0;
		int length = bufferIO.Length;
		if (Vector.IsHardwareAccelerated)
		{
			int count = Vector<T>.Count;
			if (length >= count)
			{
				Vector<T> vector = new Vector<T>(addValue);
				for (int i = 0; i <= length - count; i += count)
				{
					Span<T> slice = bufferIO.Slice(i);
					(new Vector<T>((ReadOnlySpan<T>)slice) + vector).CopyTo(slice);
				}
			}
			lengthCountTerm = length - length % count;
		}
		for (int j = lengthCountTerm; j < length; j++)
		{
			bufferIO[j] += addValue;
		}
	}

	public unsafe static void AddConstant(Span<float> bufferIO, float addValue, float noDataValue)
	{
		int lengthCountTerm = 0;
		int length = bufferIO.Length;
		if (Avx.IsSupported)
		{
			int count = Vector256<float>.Count;
			if (length >= count)
			{
				Vector256<float> right = Vector256.Create(noDataValue);
				Vector256<float> vector = Vector256.Create(addValue);
				fixed (float* bufferIO2 = bufferIO)
				{
					for (int i = 0; i <= length - count; i += count)
					{
						float* bufferIO2Term = bufferIO2 + i;
						Vector256<float> vector2 = Vector256.Load(bufferIO2Term);
						Vector256<float> mask = Avx.CompareNotEqual(vector2, right);
						Vector256<float> source = vector2 + vector;
						Avx.MaskStore(bufferIO2Term, mask, source);
					}
				}
			}
			lengthCountTerm = length - length % count;
		}
		for (int j = lengthCountTerm; j < length; j++)
		{
			float bufferIO3 = bufferIO[j];
			if (bufferIO3 != noDataValue)
			{
				bufferIO[j] = bufferIO3 + addValue;
			}
		}
	}

	public unsafe static void TakeHigher(Span<float> bufferIO, float floorValue, float noDataValue, bool overwriteNoDataValues = true)
	{
		int lengthCountTerm = 0;
		int length = bufferIO.Length;
		if (Avx.IsSupported)
		{
			int count = Vector256<float>.Count;
			if (length >= count)
			{
				Vector256<float> right = Vector256.Create(noDataValue);
				Vector256<float> vector = Vector256.Create(floorValue);
				fixed (float* bufferIO2 = bufferIO)
				{
					if (overwriteNoDataValues)
					{
						if (noDataValue <= floorValue)
						{
							for (int i = 0; i <= length - count; i += count)
							{
								float* bufferIO2Term = bufferIO2 + i;
								Vector256<float> source = Avx.Max(Vector256.Load(bufferIO2Term), vector);
								Avx.Store(bufferIO2Term, source);
							}
						}
						else
						{
							for (int j = 0; j <= length - count; j += count)
							{
								float* bufferIO2Term2 = bufferIO2 + j;
								Vector256<float> left = Vector256.Load(bufferIO2Term2);
								Vector256<float> mask = Avx.CompareEqual(left, right);
								Vector256<float> source2 = Avx.Max(left, vector);
								Avx.Store(bufferIO2Term2, source2);
								Avx.MaskStore(bufferIO2Term2, mask, vector);
							}
						}
					}
					else
					{
						for (int k = 0; k <= length - count; k += count)
						{
							float* bufferIO2Term3 = bufferIO2 + k;
							Vector256<float> left2 = Vector256.Load(bufferIO2Term3);
							Vector256<float> mask2 = Avx.CompareNotEqual(left2, right);
							Vector256<float> source3 = Avx.Max(left2, vector);
							Avx.MaskStore(bufferIO2Term3, mask2, source3);
						}
					}
				}
			}
			lengthCountTerm = length - length % count;
		}
		if (overwriteNoDataValues)
		{
			for (int l = lengthCountTerm; l < length; l++)
			{
				float bufferIO3 = bufferIO[l];
				if (bufferIO3 == noDataValue)
				{
					bufferIO[l] = floorValue;
				}
				else
				{
					bufferIO[l] = Math.Max(bufferIO3, floorValue);
				}
			}
			return;
		}
		for (int m = lengthCountTerm; m < length; m++)
		{
			float bufferIO4 = bufferIO[m];
			if (bufferIO4 != noDataValue)
			{
				bufferIO[m] = Math.Max(bufferIO4, floorValue);
			}
		}
	}

	public unsafe static void TakeLower(Span<float> bufferIO, float ceilingValue, float noDataValue, bool overwriteNoDataValues = true)
	{
		int lengthCountTerm = 0;
		int length = bufferIO.Length;
		if (Avx.IsSupported)
		{
			int count = Vector256<float>.Count;
			if (length >= count)
			{
				Vector256<float> right = Vector256.Create(noDataValue);
				Vector256<float> vector = Vector256.Create(ceilingValue);
				fixed (float* bufferIO2 = bufferIO)
				{
					if (overwriteNoDataValues)
					{
						if (noDataValue >= ceilingValue)
						{
							for (int i = 0; i <= length - count; i += count)
							{
								float* bufferIO2Term = bufferIO2 + i;
								Vector256<float> source = Avx.Min(Vector256.Load(bufferIO2Term), vector);
								Avx.Store(bufferIO2Term, source);
							}
						}
						else
						{
							for (int j = 0; j <= length - count; j += count)
							{
								float* bufferIO2Term2 = bufferIO2 + j;
								Vector256<float> left = Vector256.Load(bufferIO2Term2);
								Vector256<float> mask = Avx.CompareEqual(left, right);
								Vector256<float> source2 = Avx.Min(left, vector);
								Avx.Store(bufferIO2Term2, source2);
								Avx.MaskStore(bufferIO2Term2, mask, vector);
							}
						}
					}
					else
					{
						for (int k = 0; k <= length - count; k += count)
						{
							float* bufferIO2Term3 = bufferIO2 + k;
							Vector256<float> left2 = Vector256.Load(bufferIO2Term3);
							Vector256<float> mask2 = Avx.CompareNotEqual(left2, right);
							Vector256<float> source3 = Avx.Min(left2, vector);
							Avx.MaskStore(bufferIO2Term3, mask2, source3);
						}
					}
				}
			}
			lengthCountTerm = length - length % count;
		}
		if (overwriteNoDataValues)
		{
			for (int l = lengthCountTerm; l < length; l++)
			{
				float bufferIO3 = bufferIO[l];
				if (bufferIO3 == noDataValue)
				{
					bufferIO[l] = ceilingValue;
				}
				else
				{
					bufferIO[l] = Math.Min(bufferIO3, ceilingValue);
				}
			}
			return;
		}
		for (int m = lengthCountTerm; m < length; m++)
		{
			float bufferIO4 = bufferIO[m];
			if (bufferIO4 != noDataValue)
			{
				bufferIO[m] = Math.Min(bufferIO4, ceilingValue);
			}
		}
	}

	public static void AddVector<T>(Span<T> buffer1, Span<T> buffer2, Span<T> bufferOut) where T : unmanaged, INumber<T>
	{
		AssertLengthsMatch(buffer1, buffer2, bufferOut);
		for (int i = 0; i < buffer1.Length; i++)
		{
			bufferOut[i] = buffer1[i] + buffer2[i];
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void AssertLengthsMatch<T>(Span<T> buf1, Span<T> buf2, Span<T> buf3)
	{
		if (buf1.Length != buf2.Length || buf1.Length != buf3.Length)
		{
			throw new ArgumentException();
		}
	}

	public static void SubtractVector<T>(Span<T> subtractFrom, Span<T> subtractThis, Span<T> bufferOut) where T : unmanaged, INumber<T>
	{
		AssertLengthsMatch(subtractFrom, subtractThis, bufferOut);
		for (int i = 0; i < subtractFrom.Length; i++)
		{
			bufferOut[i] = subtractFrom[i] - subtractThis[i];
		}
	}

	public static void SubtractVector<T>(Span<T> subtractFrom, Span<T> subtractThis, Span<T> bufferOut, T noDataValue) where T : unmanaged, INumber<T>
	{
		AssertLengthsMatch(subtractFrom, subtractThis, bufferOut);
		for (int i = 0; i < subtractFrom.Length; i++)
		{
			T subtractFrom2 = subtractFrom[i];
			T subtractThi = subtractThis[i];
			if (subtractFrom2 == noDataValue || subtractThi == noDataValue)
			{
				bufferOut[i] = noDataValue;
			}
			else
			{
				bufferOut[i] = subtractFrom[i] - subtractThis[i];
			}
		}
	}

	public static T Sum<T>(Span<T> buffer) where T : unmanaged, INumber<T>
	{
		if (Vector.IsHardwareAccelerated)
		{
			int length = buffer.Length;
			int count = Vector<T>.Count;
			T zero = T.Zero;
			int count2 = length / count;
			for (int i = 0; i < count2; i++)
			{
				Vector<T> slice = new Vector<T>((ReadOnlySpan<T>)buffer.Slice(i * count, count));
				zero += Vector.Sum(slice);
			}
			for (int j = count2 * count; j < length; j++)
			{
				zero += buffer[j];
			}
			return zero;
		}
		T zero2 = T.Zero;
		for (int k = 0; k < buffer.Length; k++)
		{
			zero2 += buffer[k];
		}
		return zero2;
	}

	public static void NormalizeToByteArray(this Memory<float> input, Memory<byte> output, float noDataValue)
	{
		if (input.Length != output.Length)
		{
			throw new ArgumentException("Input and output don't match.");
		}
		if (!input.IsEmpty)
		{
			MaxMin(input.Span, noDataValue, out var max, out var min);
			input.NormalizeToByteArray(output, noDataValue, min, max);
		}
	}

	public static void NormalizeToByteArray(this Memory<float> input, Memory<byte> output, float noDataValue, float min, float max)
	{
		if (input.Length != output.Length)
		{
			throw new ArgumentException("Input and output don't match.");
		}
		if (input.IsEmpty)
		{
			return;
		}
		Span<float> inputSpan = input.Span;
		Span<byte> outputSpan = output.Span;
		if (max == min)
		{
			input.Span.Fill(0f);
			return;
		}
		float maxMinTerm = 255f / (max - min);
		for (int i = 0; i < inputSpan.Length; i++)
		{
			float inputSpanMinTerm = (inputSpan[i] - min) * maxMinTerm;
			outputSpan[i] = (byte)((inputSpanMinTerm < 0f) ? 0f : ((inputSpanMinTerm > 255f) ? 255f : inputSpanMinTerm));
		}
	}

	public static void TruncateBinary(Span<float> bufferIO, int nBitsPreserve)
	{
		if (nBitsPreserve < 23)
		{
			if (nBitsPreserve < 0)
			{
				throw new ArgumentException("nBitsPreserve");
			}
			if (!BitConverter.IsLittleEndian)
			{
				throw new Exception("Expected LE IEEEFP32");
			}
			int source = -1 << 23 - nBitsPreserve;
			float andMask = System.Runtime.CompilerServices.Unsafe.As<int, float>(ref source);
			AndConstant(bufferIO, andMask);
		}
	}

	public static void TruncateBinary(Span<float> bufferIO, int nBitsPreserve, float noDataValue)
	{
		if (nBitsPreserve < 23)
		{
			if (nBitsPreserve < 0)
			{
				throw new ArgumentException("nBitsPreserve");
			}
			if (!BitConverter.IsLittleEndian)
			{
				throw new Exception("Expected LE IEEEFP32");
			}
			int source = -1 << 23 - nBitsPreserve;
			float andMask = System.Runtime.CompilerServices.Unsafe.As<int, float>(ref source);
			AndConstant(bufferIO, andMask, noDataValue);
		}
	}

	public static void AndConstant<T>(Span<T> bufferIO, T andMask) where T : unmanaged, INumber<T>, IBitwiseOperators<T, T, T>
	{
		int lengthCountTerm = 0;
		int length = bufferIO.Length;
		if (Vector.IsHardwareAccelerated)
		{
			int count = Vector<T>.Count;
			if (length >= count)
			{
				Vector<T> right = new Vector<T>(andMask);
				for (int i = 0; i <= length - count; i += count)
				{
					Span<T> slice = bufferIO.Slice(i);
					Vector.BitwiseAnd(new Vector<T>((ReadOnlySpan<T>)slice), right).CopyTo(slice);
				}
			}
			lengthCountTerm = length - length % count;
		}
		for (int j = lengthCountTerm; j < length; j++)
		{
			bufferIO[j] &= andMask;
		}
	}

	public unsafe static void AndConstant(Span<float> bufferIO, float andMask, float noDataValue)
	{
		int lengthCountTerm = 0;
		int length = bufferIO.Length;
		if (Avx.IsSupported)
		{
			int count = Vector256<float>.Count;
			if (length >= count)
			{
				Vector256<float> right = Vector256.Create(noDataValue);
				Vector256<float> vector = Vector256.Create(andMask);
				fixed (float* bufferIO2 = bufferIO)
				{
					for (int i = 0; i <= length - count; i += count)
					{
						float* bufferIO2Term = bufferIO2 + i;
						Vector256<float> vector2 = Vector256.Load(bufferIO2Term);
						Vector256<float> mask = Avx.CompareNotEqual(vector2, right);
						Vector256<float> source = vector2 & vector;
						Avx.MaskStore(bufferIO2Term, mask, source);
					}
				}
			}
			lengthCountTerm = length - length % count;
		}
		int compilerServicesRuntimeTerm = System.Runtime.CompilerServices.Unsafe.As<float, int>(ref andMask);
		for (int j = lengthCountTerm; j < length; j++)
		{
			if (bufferIO[j] != noDataValue)
			{
				int source2 = System.Runtime.CompilerServices.Unsafe.As<float, int>(ref bufferIO[j]) & compilerServicesRuntimeTerm;
				bufferIO[j] = System.Runtime.CompilerServices.Unsafe.As<int, float>(ref source2);
			}
		}
	}

	public static void OrConstant<T>(Span<T> bufferIO, T andMask) where T : unmanaged, INumber<T>, IBitwiseOperators<T, T, T>
	{
		int lengthCountTerm = 0;
		int length = bufferIO.Length;
		if (Vector.IsHardwareAccelerated)
		{
			int count = Vector<T>.Count;
			if (length >= count)
			{
				Vector<T> right = new Vector<T>(andMask);
				for (int i = 0; i <= length - count; i += count)
				{
					Span<T> slice = bufferIO.Slice(i);
					Vector.BitwiseOr(new Vector<T>((ReadOnlySpan<T>)slice), right).CopyTo(slice);
				}
			}
			lengthCountTerm = length - length % count;
		}
		for (int j = lengthCountTerm; j < length; j++)
		{
			bufferIO[j] |= andMask;
		}
	}

	public static void LinearInterpolate<T>(Span<T> bufferA, Span<T> bufferB, Span<T> bufferOut, T ratio) where T : unmanaged, INumber<T>
	{
		int lengthCountTerm = 0;
		int length = bufferA.Length;
		if (Vector.IsHardwareAccelerated)
		{
			int count = Vector<T>.Count;
			if (length >= count)
			{
				for (int i = 0; i <= length - count; i += count)
				{
					Span<T> slice = bufferA.Slice(i);
					Span<T> sliceSpan = bufferB.Slice(i);
					Span<T> destination = bufferOut.Slice(i);
					Vector<T> vector = new Vector<T>((ReadOnlySpan<T>)slice);
					Vector<T> vector2 = new Vector<T>((ReadOnlySpan<T>)sliceSpan) - vector;
					(vector + vector2 * ratio).CopyTo(destination);
				}
			}
			lengthCountTerm = length - length % count;
		}
		for (int j = lengthCountTerm; j < length; j++)
		{
			T bufferA2 = bufferA[j];
			T bufferBBufferA2Term = bufferB[j] - bufferA2;
			bufferOut[j] = bufferA2 + bufferBBufferA2Term * ratio;
		}
	}

	public unsafe static void LinearInterpolate(Span<float> bufferA, Span<float> bufferB, Span<float> bufferOut, float ratio)
	{
		int lengthCountTerm = 0;
		int length = bufferA.Length;
		if (Avx.IsSupported)
		{
			int count = Vector256<float>.Count;
			if (length >= count)
			{
				fixed (float* bufferA2 = bufferA)
				{
					fixed (float* bufferB2 = bufferB)
					{
						fixed (float* bufferOut2 = bufferOut)
						{
							for (int i = 0; i <= length - count; i += count)
							{
								Vector256<float> vector = Vector256.Load(bufferA2 + i);
								Vector256<float> vector2 = Vector256.Load(bufferB2 + i) - vector;
								Vector256<float> source = vector + vector2 * ratio;
								Avx.Store(bufferOut2 + i, source);
							}
						}
					}
				}
			}
			lengthCountTerm = length - length % count;
		}
		for (int j = lengthCountTerm; j < length; j++)
		{
			float bufferA3 = bufferA[j];
			float bufferBBufferA3Term = bufferB[j] - bufferA3;
			bufferOut[j] = bufferA3 + bufferBBufferA3Term * ratio;
		}
	}

	public unsafe static void LinearInterpolate(Span<float> bufferA, Span<float> bufferB, Span<float> bufferOut, float ratio, float ndv)
	{
		int lengthCountTerm = 0;
		int length = bufferA.Length;
		if (Avx.IsSupported)
		{
			int count = Vector256<float>.Count;
			if (length >= count)
			{
				Vector256<float> right = Vector256.Create(ndv);
				fixed (float* bufferA2 = bufferA)
				{
					fixed (float* bufferB2 = bufferB)
					{
						fixed (float* bufferOut2 = bufferOut)
						{
							for (int i = 0; i <= length - count; i += count)
							{
								Vector256<float> vector = Vector256.Load(bufferA2 + i);
								Vector256<float> vector2 = Vector256.Load(bufferB2 + i);
								Vector256<float> mask = Avx.CompareEqual(vector, right);
								mask |= Avx.CompareEqual(vector2, right);
								Vector256<float> vector3 = vector2 - vector;
								Vector256<float> source = Avx.BlendVariable(vector + vector3 * ratio, right, mask);
								Avx.Store(bufferOut2 + i, source);
							}
						}
					}
				}
			}
			lengthCountTerm = length - length % count;
		}
		for (int j = lengthCountTerm; j < length; j++)
		{
			float bufferA3 = bufferA[j];
			float bufferB3 = bufferB[j];
			if (bufferA3 == ndv || bufferB3 == ndv)
			{
				bufferOut[j] = ndv;
				continue;
			}
			float bufferB3AboveBufferA3 = bufferB3 - bufferA3;
			bufferOut[j] = bufferA3 + bufferB3AboveBufferA3 * ratio;
		}
	}

	public unsafe static void CrossProduct(Span<float> bufferA, Span<float> bufferB, Span<float> bufferOut)
	{
		int lengthCountTerm = 0;
		int length = bufferA.Length;
		if (Avx.IsSupported)
		{
			int count = Vector128<float>.Count;
			if (length >= count)
			{
				fixed (float* bufferA2 = bufferA)
				{
					fixed (float* bufferB2 = bufferB)
					{
						fixed (float* bufferOut2 = bufferOut)
						{
							for (int i = 0; i <= length - count; i += count)
							{
								Vector128<float> vector = Vector128.Load(bufferA2 + i);
								Vector128<float> vector2 = Vector128.Load(bufferB2 + i);
								Vector128<float> left = Sse.Shuffle(vector, vector, 201);
								Vector128<float> right = Sse.Shuffle(vector2, vector2, 210);
								Vector128<float> vector3 = Sse.Multiply(left, vector);
								Vector128<float> left2 = Sse.Multiply(left, right);
								Vector128<float> right2 = Sse.Shuffle(vector3, vector3, 201);
								Vector128<float> source = Sse.Subtract(left2, right2);
								Sse.Store(bufferOut2 + i, source);
							}
						}
					}
				}
			}
			lengthCountTerm = length - length % count;
		}
		for (int j = lengthCountTerm; j < length; j++)
		{
			_ = ref bufferA[j];
			_ = ref bufferB[j];
		}
		throw new NotImplementedException();
	}

	public static void ArraySqrt(Memory<float> buf)
	{
		if (buf.IsEmpty)
		{
			return;
		}
		if (Avx.IsSupported)
		{
			ArraySqrtSIMD_AVX(buf);
			return;
		}
		Span<float> bufSpan = buf.Span;
		for (int i = 0; i < bufSpan.Length; i++)
		{
			bufSpan[i] = (float)Math.Sqrt(bufSpan[i]);
		}
	}

	private unsafe static void ArraySqrtSIMD_AVX(Memory<float> buffer)
	{
		Span<float> floatSpan = buffer.Span;
		int length = floatSpan.Length;
		int scaledLength = length * 4;
		if (length < 64)
		{
			for (int i = 0; i < length; i++)
			{
				floatSpan[i] = (float)Math.Sqrt(floatSpan[i]);
			}
			return;
		}
		int count = scaledLength / 32;
		fixed (float* localValue = floatSpan)
		{
			for (int j = 0; j < count; j++)
			{
				int ordinal = j * 8;
				Vector256<float> source = Avx.Sqrt(Avx.LoadVector256(localValue + ordinal));
				Avx.Store(localValue + ordinal, source);
			}
			for (int k = (count - 1) * 8; k < length; k++)
			{
				floatSpan[k] = (float)Math.Sqrt(floatSpan[k]);
			}
		}
	}

	private unsafe static void ArrayFastSqrtSIMD_AVX(Memory<float> buffer)
	{
		Span<float> floatSpan = buffer.Span;
		int length = floatSpan.Length;
		int scaledLength = length * 4;
		if (length < 64)
		{
			for (int i = 0; i < length; i++)
			{
				floatSpan[i] = MathHelpers.FastSqrt(floatSpan[i]);
			}
			return;
		}
		int count = scaledLength / 32;
		fixed (float* localValue = floatSpan)
		{
			int* localValue2 = (int*)localValue;
			byte* localValue3 = stackalloc byte[64];
			*(int*)localValue3 = 8388608;
			((int*)localValue3)[1] = 8388608;
			((int*)localValue3)[2] = 8388608;
			((int*)localValue3)[3] = 8388608;
			((int*)localValue3)[4] = 8388608;
			((int*)localValue3)[5] = 8388608;
			((int*)localValue3)[6] = 8388608;
			((int*)localValue3)[7] = 8388608;
			((int*)localValue3)[8] = 536870912;
			((int*)localValue3)[9] = 536870912;
			((int*)localValue3)[10] = 536870912;
			((int*)localValue3)[11] = 536870912;
			((int*)localValue3)[12] = 536870912;
			((int*)localValue3)[13] = 536870912;
			((int*)localValue3)[14] = 536870912;
			((int*)localValue3)[15] = 536870912;
			Avx.LoadAlignedVector256((int*)localValue3);
			Avx.LoadAlignedVector256((int*)localValue3 + 8);
			for (int j = 0; j < count; j++)
			{
				int ordinal = j * 8;
				Avx.LoadVector256(localValue2 + ordinal);
			}
			for (int k = (count - 1) * 8; k < length; k++)
			{
				floatSpan[k] = MathHelpers.FastSqrt(floatSpan[k]);
			}
		}
		throw new NotImplementedException();
	}

	public unsafe static void ComputeVelocity(Span<float> xValues, Span<float> yValues, Span<float> outMagnitudes)
	{
		int length = xValues.Length;
		fixed (float* xValues2 = xValues)
		{
			fixed (float* yValues2 = yValues)
			{
				fixed (float* outMagnitudes2 = outMagnitudes)
				{
					int count = length / 8;
					int ordinal = 0;
					for (int i = 0; i < count; i++)
					{
						Vector256<float> vector = Avx.LoadVector256(xValues2 + ordinal);
						Vector256<float> vector2 = Avx.LoadVector256(yValues2 + ordinal);
						Vector256<float> left = Avx.Multiply(vector, vector);
						Vector256<float> right = Avx.Multiply(vector2, vector2);
						Vector256<float> source = Avx.Sqrt(Avx.Add(left, right));
						Avx.Store(outMagnitudes2 + ordinal, source);
						ordinal += 8;
					}
					for (int j = count * 8; j < length; j++)
					{
						float xValue = xValues[j];
						float yValue = yValues[j];
						outMagnitudes[j] = MathF.Sqrt(xValue * xValue + yValue * yValue);
					}
				}
			}
		}
	}

	public unsafe static void ComputeVelocity(Span<float> xValues, Span<float> yValues, Span<float> outMagnitudes, float noData)
	{
		int length = xValues.Length;
		fixed (float* xValues2 = xValues)
		{
			fixed (float* yValues2 = yValues)
			{
				fixed (float* outMagnitudes2 = outMagnitudes)
				{
					Vector256<float> vector = Vector256.Create(noData);
					int count = length / 8;
					int ordinal = 0;
					for (int i = 0; i < count; i++)
					{
						Vector256<float> vector2 = Avx.LoadVector256(xValues2 + ordinal);
						Vector256<float> vector3 = Avx.LoadVector256(yValues2 + ordinal);
						Vector256<float> vector4 = Avx.CompareEqual(vector2, vector);
						Vector256<float> left = Avx.Multiply(vector2, vector2);
						Vector256<float> right = Avx.Multiply(vector3, vector3);
						Vector256<float> source = Avx.Sqrt(Avx.Add(left, right));
						Avx.MaskStore(outMagnitudes2 + ordinal, vector4, vector);
						Vector256<float> mask = Avx.Xor(vector4, Vector256<float>.AllBitsSet);
						Avx.MaskStore(outMagnitudes2 + ordinal, mask, source);
						ordinal += 8;
					}
					for (int j = count * 8; j < length; j++)
					{
						float xValue = xValues[j];
						float yValue = yValues[j];
						if (xValue == noData)
						{
							outMagnitudes[j] = noData;
						}
						else
						{
							outMagnitudes[j] = MathF.Sqrt(xValue * xValue + yValue * yValue);
						}
					}
				}
			}
		}
	}

	public static int CountZero<T>(Span<T> buffer) where T : unmanaged, INumber<T>
	{
		int ordinal = 0;
		for (int i = 0; i < buffer.Length; i++)
		{
			if (buffer[i] == T.Zero)
			{
				ordinal++;
			}
		}
		return ordinal;
	}

	public static int CountNonZero<T>(Span<T> buffer) where T : unmanaged, INumber<T>
	{
		return buffer.Length - CountZero(buffer);
	}

	public static void ClipToFloor(Span<float> inVector, Span<float> clipVector, float clipTolerance, float noData)
	{
		if (inVector.Length != clipVector.Length)
		{
			throw new ArgumentException("inVector size mismatch with clipVector");
		}
		if (float.IsNaN(noData))
		{
			throw new ArgumentException("noData cannot be NaN.");
		}
		if (Avx.IsSupported && inVector.Length >= 256)
		{
			ClipToFloorAVX(inVector, clipVector, clipTolerance, noData);
		}
		else
		{
			ClipToFloorSerial(inVector, clipVector, clipTolerance, noData);
		}
	}

	private static void ClipToFloorSerial<T>(Span<T> inVector, Span<T> clipVector, T clipTolerance, T noData) where T : unmanaged, IBinaryFloatingPointIeee754<T>
	{
		int length = inVector.Length;
		for (int i = 0; i < length; i++)
		{
			if (inVector[i] < clipVector[i] + clipTolerance || clipVector[i] == noData)
			{
				inVector[i] = noData;
			}
		}
	}

	private unsafe static void ClipToFloorAVX(Span<float> inVector, Span<float> clipVector, float clipTolerance, float noData)
	{
		if (inVector.Length != clipVector.Length)
		{
			throw new ArgumentException("inVector size mismatch with clipVector");
		}
		if (clipTolerance == 0f || float.IsNaN(clipTolerance))
		{
			ClipToFloorAVX(inVector, clipVector, noData);
			return;
		}
		int length = inVector.Length;
		if (length < 256)
		{
			ClipToFloorSerial(inVector, clipVector, clipTolerance, noData);
		}
		fixed (float* inVector2 = inVector)
		{
			fixed (float* clipVector2 = clipVector)
			{
				Vector256<float> vector = Avx.BroadcastScalarToVector256(&noData);
				Vector256<float> right = Avx.BroadcastScalarToVector256(&clipTolerance);
				int count = length / 8;
				int ordinal = 0;
				for (int i = 0; i < count; i++)
				{
					Vector256<float> left = Avx.LoadVector256(inVector2 + ordinal);
					Vector256<float> left2 = Avx.LoadVector256(clipVector2 + ordinal);
					Vector256<float> right2 = Avx.CompareEqual(left2, vector);
					left2 = Avx.Add(left2, right);
					Vector256<float> mask = Avx.Or(Avx.CompareLessThanOrEqual(left, left2), right2);
					Avx.MaskStore(inVector2 + ordinal, mask, vector);
					ordinal += 8;
				}
				for (int j = (count - 1) * 8; j < length; j++)
				{
					if (inVector2[j] < clipVector2[j] + clipTolerance || clipVector2[j] == noData)
					{
						inVector2[j] = noData;
					}
				}
			}
		}
	}

	public static void ClipToFloor(Span<float> inVector, Span<float> clipVector, float noData)
	{
		if (inVector.Length != clipVector.Length)
		{
			throw new ArgumentException("inVector size mismatch with clipVector");
		}
		if (float.IsNaN(noData))
		{
			throw new ArgumentException("noData cannot be NaN.");
		}
		if (Avx.IsSupported && inVector.Length >= 256)
		{
			ClipToFloorAVX(inVector, clipVector, noData);
		}
		else
		{
			ClipToFloorSerial(inVector, clipVector, noData);
		}
	}

	private static void ClipToFloorSerial<T>(Span<T> inVector, Span<T> clipVector, T noData) where T : unmanaged, IBinaryFloatingPointIeee754<T>
	{
		int length = inVector.Length;
		for (int i = 0; i < length; i++)
		{
			if (inVector[i] < clipVector[i] || clipVector[i] == noData)
			{
				inVector[i] = noData;
			}
		}
	}

	private unsafe static void ClipToFloorAVX(Span<float> inVector, Span<float> clipVector, float noData)
	{
		int length = inVector.Length;
		fixed (float* inVector2 = inVector)
		{
			fixed (float* clipVector2 = clipVector)
			{
				Vector256<float> vector = Avx.BroadcastScalarToVector256(&noData);
				int count = length / 8;
				int ordinal = 0;
				for (int i = 0; i < count; i++)
				{
					Vector256<float> left = Avx.LoadVector256(inVector2 + ordinal);
					Vector256<float> vector2 = Avx.LoadVector256(clipVector2 + ordinal);
					Vector256<float> left2 = Avx.CompareLessThanOrEqual(left, vector2);
					Vector256<float> right = Avx.CompareEqual(vector2, vector);
					Vector256<float> mask = Avx.Or(left2, right);
					Avx.MaskStore(inVector2 + ordinal, mask, vector);
					ordinal += 8;
				}
				for (int j = count * 8; j < length; j++)
				{
					if (inVector2[j] < clipVector2[j] || clipVector2[j] == noData)
					{
						inVector2[j] = noData;
					}
				}
			}
		}
	}

	public static void ClipToFloor(Span<float> inVector, float clipValue, float noData)
	{
		if (float.IsNaN(noData))
		{
			throw new ArgumentException("noData cannot be NaN.");
		}
		if (Avx.IsSupported && inVector.Length >= 256)
		{
			ClipToFloorAVX(inVector, clipValue, noData);
		}
		else
		{
			ClipToFloorSerial(inVector, clipValue, noData);
		}
	}

	private static void ClipToFloorSerial(Span<float> inVector, float clipValue, float noData)
	{
		int length = inVector.Length;
		for (int i = 0; i < length; i++)
		{
			if (inVector[i] < clipValue || inVector[i] == noData)
			{
				inVector[i] = noData;
			}
		}
	}

	private unsafe static void ClipToFloorAVX(Span<float> inVector, float clipValue, float noData)
	{
		int length = inVector.Length;
		fixed (float* inVector2 = inVector)
		{
			Vector256<float> vector = Avx.BroadcastScalarToVector256(&noData);
			Vector256<float> right = Avx.BroadcastScalarToVector256(&clipValue);
			int count = length / 8;
			int ordinal = 0;
			if (noData <= clipValue)
			{
				for (int i = 0; i < count; i++)
				{
					Vector256<float> mask = Avx.CompareLessThanOrEqual(Avx.LoadVector256(inVector2 + ordinal), right);
					Avx.MaskStore(inVector2 + ordinal, mask, vector);
					ordinal += 8;
				}
			}
			else
			{
				for (int j = 0; j < count; j++)
				{
					Vector256<float> left = Avx.LoadVector256(inVector2 + ordinal);
					Vector256<float> left2 = Avx.CompareLessThanOrEqual(left, right);
					Vector256<float> right2 = Avx.CompareEqual(left, vector);
					Vector256<float> mask2 = Avx.Or(left2, right2);
					Avx.MaskStore(inVector2 + ordinal, mask2, vector);
					ordinal += 8;
				}
			}
			for (int k = count * 8; k < length; k++)
			{
				if (inVector2[k] < clipValue || inVector2[k] == noData)
				{
					inVector2[k] = noData;
				}
			}
		}
	}
}
