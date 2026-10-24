using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Geospatial.Rasters;

public static class ImageOperations
{
	public static void MergeColorBuffers(Memory<int> bufA, Memory<int> bufBInOut)
	{
		MergeColorBuffers_IntegerSpace(bufA, bufBInOut);
	}

	public static void MergeColorBuffers(Span<int> bufA, Span<int> bufBInOut)
	{
		MergeColorBuffers_IntegerSpace(bufA, bufBInOut);
	}

	public static void MergeColorBuffersPremultiplied(Span<int> bufA, Span<int> bufBInOut)
	{
		MergeColorBuffersWithAPremultiplied_IntegerSpace(bufA, bufBInOut);
	}

	public static void MergeColorBuffers_FloatSpace(Memory<int> bufA, Memory<int> bufBInOut)
	{
		MergeColorBuffers_FloatSpace(bufA.Span, bufBInOut.Span);
	}

	public unsafe static void MergeColorBuffers_FloatSpace(Span<int> bufA, Span<int> bufBInOut)
	{
		if (bufA.Length != bufBInOut.Length)
		{
			throw new ArgumentException("Buffers don't match");
		}
		int count = bufA.Length * 4;
		Span<int> bufASpan = bufA;
		Span<int> bufBInOutSpan = bufBInOut;
		fixed (int* bufA2 = bufASpan)
		{
			fixed (int* bufBInOut2 = bufBInOutSpan)
			{
				byte* bufA3 = (byte*)bufA2;
				byte* bufBInOut3 = (byte*)bufBInOut2;
				for (int i = 0; i < count; i += 4)
				{
					byte b = bufA3[i];
					byte b2 = bufA3[i + 1];
					byte b3 = bufA3[i + 2];
					byte b4 = bufA3[i + 3];
					byte b5 = bufBInOut3[i];
					byte b6 = bufBInOut3[i + 1];
					byte b7 = bufBInOut3[i + 2];
					byte b8 = bufBInOut3[i + 3];
					float scaledB4 = (float)(int)b4 / 255f;
					float scaledB3 = (float)(int)b3 / 255f;
					float scaledB2 = (float)(int)b2 / 255f;
					float computed = (float)(int)b / 255f;
					float scaledB8 = (float)(int)b8 / 255f;
					float scaledB7 = (float)(int)b7 / 255f;
					float scaledB6 = (float)(int)b6 / 255f;
					float scaledB5 = (float)(int)b5 / 255f;
					float computed2 = 1f - (1f - scaledB4) * (1f - scaledB8);
					float computed3 = scaledB3 * scaledB4 / computed2 + scaledB7 * scaledB8 * (1f - scaledB4) / computed2;
					float computed4 = scaledB2 * scaledB4 / computed2 + scaledB6 * scaledB8 * (1f - scaledB4) / computed2;
					float computed5 = computed * scaledB4 / computed2 + scaledB5 * scaledB8 * (1f - scaledB4) / computed2;
					bufBInOut3[i] = (byte)(computed5 * 255f);
					bufBInOut3[i + 1] = (byte)(computed4 * 255f);
					bufBInOut3[i + 2] = (byte)(computed3 * 255f);
					bufBInOut3[i + 3] = (byte)(computed2 * 255f);
				}
			}
		}
	}

	public static void MergeColorBuffers_IntegerSpace(Memory<int> bufA, Memory<int> bufBInOut)
	{
		MergeColorBuffers_IntegerSpace(bufA.Span, bufBInOut.Span);
	}

	public unsafe static void MergeColorBuffers_IntegerSpace(Span<int> bufA, Span<int> bufBInOut)
	{
		if (bufA.Length != bufBInOut.Length)
		{
			throw new ArgumentException("Buffers don't match");
		}
		int count = bufA.Length * 4;
		fixed (int* bufA2 = bufA)
		{
			fixed (int* bufBInOut2 = bufBInOut)
			{
				byte* bufA3 = (byte*)bufA2;
				byte* bufBInOut3 = (byte*)bufBInOut2;
				for (int i = 0; i < count; i += 4)
				{
					byte b = bufA3[i + 3];
					if (b == 0)
					{
						continue;
					}
					byte b2 = bufBInOut3[i + 3];
					if (b2 == 0)
					{
						*(int*)(bufBInOut3 + i) = *(int*)(bufA3 + i);
						continue;
					}
					int ordinal = b + 1;
					int ordinal2 = 256 - ordinal;
					int ordinal3;
					int ordinal4;
					int ordinal5;
					if (b2 == byte.MaxValue)
					{
						ordinal3 = 256;
						ordinal4 = ordinal;
						ordinal5 = ordinal2;
					}
					else
					{
						int b2Offset = b2 + 1;
						ordinal3 = ordinal + (b2Offset * ordinal2 >> 8);
						ordinal4 = ordinal * 256 / ordinal3;
						ordinal5 = b2Offset * ordinal2 / ordinal3;
					}
					bufBInOut3[i] = (byte)(ordinal4 * bufA3[i] + ordinal5 * bufBInOut3[i] >> 8);
					bufBInOut3[i + 1] = (byte)(ordinal4 * bufA3[i + 1] + ordinal5 * bufBInOut3[i + 1] >> 8);
					bufBInOut3[i + 2] = (byte)(ordinal4 * bufA3[i + 2] + ordinal5 * bufBInOut3[i + 2] >> 8);
					bufBInOut3[i + 3] = (byte)(ordinal3 - 1);
				}
			}
		}
	}

	public unsafe static void MergeColorBuffersPremultipliedMultiply(Span<int> bufA, Span<int> bufBInOut)
	{
		if (bufA.Length != bufBInOut.Length)
		{
			throw new ArgumentException("Buffers don't match");
		}
		int count = bufA.Length * 4;
		fixed (int* bufA2 = bufA)
		{
			fixed (int* bufBInOut2 = bufBInOut)
			{
				byte* bufA3 = (byte*)bufA2;
				byte* bufBInOut3 = (byte*)bufBInOut2;
				for (int i = 0; i < count; i += 4)
				{
					int bufA4 = bufA3[i + 3];
					if (bufA4 == 0)
					{
						continue;
					}
					int bufBInOut4 = bufBInOut3[i + 3];
					int bufA4Offset = 255 - bufA4;
					if (bufBInOut4 == 255)
					{
						for (int j = 0; j < 3; j++)
						{
							int bufA5 = bufA3[i + j];
							int bufBInOut5 = bufBInOut3[i + j];
							bufBInOut3[i + j] = (byte)Math.Min(255, (bufA5 * bufBInOut5 + bufBInOut5 * bufA4Offset) / 255);
						}
						continue;
					}
					int bufBInOut32 = bufA4 + bufBInOut4 * bufA4Offset / 255;
					if (bufBInOut32 != 0)
					{
						int bufBInOut4Offset = 255 - bufBInOut4;
						for (int k = 0; k < 3; k++)
						{
							int bufA6 = bufA3[i + k];
							int bufBInOut3BufBInOut4Term = bufBInOut3[i + k] * bufBInOut4 / 255;
							int bufA6Term = (bufA6 * bufBInOut3BufBInOut4Term + bufA6 * bufBInOut4Offset + bufBInOut3BufBInOut4Term * bufA4Offset) / 255;
							bufBInOut3[i + k] = (byte)Math.Min(255, bufA6Term * 255 / bufBInOut32);
						}
						bufBInOut3[i + 3] = (byte)bufBInOut32;
					}
				}
			}
		}
	}

	public unsafe static void MergeColorBuffersWithAPremultiplied_IntegerSpace(Span<int> bufA, Span<int> bufBInOut)
	{
		if (bufA.Length != bufBInOut.Length)
		{
			throw new ArgumentException("Buffers don't match");
		}
		int count = bufA.Length * 4;
		fixed (int* bufA2 = bufA)
		{
			fixed (int* bufBInOut2 = bufBInOut)
			{
				byte* bufA3 = (byte*)bufA2;
				byte* bufBInOut3 = (byte*)bufBInOut2;
				for (int i = 0; i < count; i += 4)
				{
					int bufA3Term = bufA3[i + 3] + 1;
					int ordinal = 256 - bufA3Term;
					int bufBInOut3Term = bufBInOut3[i + 3] + 1;
					int ordinal2 = 256 - bufBInOut3Term;
					int bufBInOut32 = 65535 - ordinal * ordinal2 >> 8;
					int ordinal3 = 256;
					int ordinal4 = bufBInOut3Term * ordinal / bufBInOut32;
					bufBInOut3[i] = (byte)(ordinal3 * bufA3[i] + ordinal4 * bufBInOut3[i] >> 8);
					bufBInOut3[i + 1] = (byte)(ordinal3 * bufA3[i + 1] + ordinal4 * bufBInOut3[i + 1] >> 8);
					bufBInOut3[i + 2] = (byte)(ordinal3 * bufA3[i + 2] + ordinal4 * bufBInOut3[i + 2] >> 8);
					bufBInOut3[i + 3] = (byte)bufBInOut32;
				}
			}
		}
	}

	public unsafe static void MergeColorBuffers_IntegerSpace_2(int[] bufA, int[] bufBInOut)
	{
		if (bufA == null || bufBInOut == null || bufA.Length != bufBInOut.Length)
		{
			throw new ArgumentException("Buffers don't match");
		}
		int bufALength = bufA.Length;
		fixed (int* bufA2 = bufA)
		{
			fixed (int* bufBInOut2 = bufBInOut)
			{
				uint* bufA3 = (uint*)bufA2;
				uint* bufBInOut3 = (uint*)bufBInOut2;
				for (int i = 0; i < bufALength; i++)
				{
					uint bufA4 = bufA3[i];
					uint bufBInOut4 = bufBInOut3[i];
					uint bufBInOut4Term = (bufBInOut4 & 0xFF) >> 24;
					uint ordinal = 255 - bufBInOut4Term;
					uint bufA4BufBInOut4Term = (uint)(ordinal * (bufA4 & -16711936) + bufBInOut4Term * (bufBInOut4 & -16711936) >> 8);
					uint bufA4BufBInOut4Term2 = ordinal * ((bufA4 & 0xFF00FF) >> 8) + bufBInOut4Term * (0x1000000 | ((bufBInOut4 & 0xFF0000) >> 8));
					bufBInOut3[i] = (uint)((bufA4BufBInOut4Term & -16711936) | (bufA4BufBInOut4Term2 & 0xFF00FF));
				}
			}
		}
	}

	public unsafe static void MergeColorBuffers(Memory<byte> bufR, Memory<byte> bufG, Memory<byte> bufB, Memory<int> clrBufInOut, byte inputAlpha = byte.MaxValue)
	{
		if (bufR.Length != bufG.Length || bufR.Length != bufB.Length || bufR.Length != clrBufInOut.Length)
		{
			throw new ArgumentException("Buffers don't match");
		}
		float scaledInputAlpha = (float)(int)inputAlpha / 255f;
		int length = clrBufInOut.Length;
		fixed (byte* bufR2 = bufR.Span)
		{
			fixed (byte* bufG2 = bufG.Span)
			{
				fixed (byte* bufB2 = bufB.Span)
				{
					fixed (int* clrBufInOut2 = clrBufInOut.Span)
					{
						byte* clrBufInOut3 = (byte*)clrBufInOut2;
						for (int i = 0; i < length; i++)
						{
							byte b = bufB2[i];
							byte b2 = bufG2[i];
							byte b3 = bufR2[i];
							int ordinal = i * 4;
							byte b4 = clrBufInOut3[ordinal];
							byte b5 = clrBufInOut3[ordinal + 1];
							byte b6 = clrBufInOut3[ordinal + 2];
							byte b7 = clrBufInOut3[ordinal + 3];
							float scaledB3 = (float)(int)b3 / 255f;
							float scaledB2 = (float)(int)b2 / 255f;
							float computed = (float)(int)b / 255f;
							float scaledB7 = (float)(int)b7 / 255f;
							float scaledB6 = (float)(int)b6 / 255f;
							float scaledB5 = (float)(int)b5 / 255f;
							float scaledB4 = (float)(int)b4 / 255f;
							float computed2 = 1f - (1f - scaledInputAlpha) * (1f - scaledB7);
							float computed3 = scaledB3 * scaledInputAlpha / computed2 + scaledB6 * scaledB7 * (1f - scaledInputAlpha) / computed2;
							float computed4 = scaledB2 * scaledInputAlpha / computed2 + scaledB5 * scaledB7 * (1f - scaledInputAlpha) / computed2;
							float computed5 = computed * scaledInputAlpha / computed2 + scaledB4 * scaledB7 * (1f - scaledInputAlpha) / computed2;
							clrBufInOut3[ordinal] = (byte)(computed5 * 255f);
							clrBufInOut3[ordinal + 1] = (byte)(computed4 * 255f);
							clrBufInOut3[ordinal + 2] = (byte)(computed3 * 255f);
							clrBufInOut3[ordinal + 3] = (byte)(computed2 * 255f);
						}
					}
				}
			}
		}
	}

	public unsafe static void MergeColorBuffers(Memory<byte> brightness, Memory<int> clrBufInOut, byte inputAlpha = byte.MaxValue)
	{
		if (brightness.Length != clrBufInOut.Length)
		{
			throw new ArgumentException("Buffers don't match");
		}
		float scaledInputAlpha = (float)(int)inputAlpha / 255f;
		int length = clrBufInOut.Length;
		fixed (byte* brightness2 = brightness.Span)
		{
			fixed (int* clrBufInOut2 = clrBufInOut.Span)
			{
				byte* clrBufInOut3 = (byte*)clrBufInOut2;
				for (int i = 0; i < length; i++)
				{
					byte b = brightness2[i];
					if (inputAlpha == byte.MaxValue)
					{
						int ordinal = i * 4;
						byte b2 = clrBufInOut3[ordinal];
						byte b3 = clrBufInOut3[ordinal + 1];
						byte b4 = clrBufInOut3[ordinal + 2];
						float clrBufInOut3Term = (float)(int)clrBufInOut3[ordinal + 3] / 255f;
						float scaledB4 = (float)(int)b4 / 255f;
						_ = (float)(int)b3 / 255f;
						_ = (float)(int)b2 / 255f;
						float clrBufInOut32 = ((float)(int)b * scaledInputAlpha / 255f + scaledB4 * clrBufInOut3Term * (1f - scaledInputAlpha)) * 255f;
						clrBufInOut3[ordinal] = (byte)clrBufInOut32;
						clrBufInOut3[ordinal + 1] = (byte)clrBufInOut32;
						clrBufInOut3[ordinal + 2] = (byte)clrBufInOut32;
						clrBufInOut3[ordinal + 3] = byte.MaxValue;
					}
					else
					{
						int ordinal2 = i * 4;
						byte b5 = clrBufInOut3[ordinal2];
						byte b6 = clrBufInOut3[ordinal2 + 1];
						byte b7 = clrBufInOut3[ordinal2 + 2];
						float clrBufInOut3Term2 = (float)(int)clrBufInOut3[ordinal2 + 3] / 255f;
						float scaledB7 = (float)(int)b7 / 255f;
						_ = (float)(int)b6 / 255f;
						_ = (float)(int)b5 / 255f;
						float computed = 1f - (1f - scaledInputAlpha) * (1f - clrBufInOut3Term2);
						float computed2 = (float)(int)b * scaledInputAlpha / computed + scaledB7 * clrBufInOut3Term2 * (1f - scaledInputAlpha) / computed;
						clrBufInOut3[ordinal2] = (byte)(computed2 * 255f);
						clrBufInOut3[ordinal2 + 1] = (byte)(computed2 * 255f);
						clrBufInOut3[ordinal2 + 2] = (byte)(computed2 * 255f);
						clrBufInOut3[ordinal2 + 3] = (byte)(computed * 255f);
					}
				}
			}
		}
	}

	public unsafe static void MergeColorBuffers(byte[,] bufRGB, Memory<int> clrBufInOut)
	{
		if (bufRGB == null || bufRGB.GetLength(0) != 3 || bufRGB.GetLength(1) != clrBufInOut.Length)
		{
			throw new ArgumentException("Buffers don't match");
		}
		int length = clrBufInOut.Length;
		int scaledLength = length * 2;
		fixed (byte* bufRGB2 = bufRGB)
		{
			fixed (int* clrBufInOut2 = clrBufInOut.Span)
			{
				byte* clrBufInOut3 = (byte*)clrBufInOut2;
				for (int i = 0; i < length; i++)
				{
					byte b = bufRGB2[i];
					byte b2 = bufRGB2[i + length];
					byte b3 = bufRGB2[i + scaledLength];
					byte b4 = byte.MaxValue;
					int ordinal = i * 4;
					byte b5 = clrBufInOut3[ordinal];
					byte b6 = clrBufInOut3[ordinal + 1];
					byte b7 = clrBufInOut3[ordinal + 2];
					byte b8 = clrBufInOut3[ordinal + 3];
					float scaledB4 = (float)(int)b4 / 255f;
					float computed = (float)(int)b / 255f;
					float scaledB2 = (float)(int)b2 / 255f;
					float scaledB3 = (float)(int)b3 / 255f;
					float scaledB8 = (float)(int)b8 / 255f;
					float scaledB7 = (float)(int)b7 / 255f;
					float scaledB6 = (float)(int)b6 / 255f;
					float scaledB5 = (float)(int)b5 / 255f;
					float computed2 = 1f - (1f - scaledB4) * (1f - scaledB8);
					float computed3 = computed * scaledB4 / computed2 + scaledB7 * scaledB8 * (1f - scaledB4) / computed2;
					float computed4 = scaledB2 * scaledB4 / computed2 + scaledB6 * scaledB8 * (1f - scaledB4) / computed2;
					float computed5 = scaledB3 * scaledB4 / computed2 + scaledB5 * scaledB8 * (1f - scaledB4) / computed2;
					clrBufInOut3[ordinal] = (byte)(computed5 * 255f);
					clrBufInOut3[ordinal + 1] = (byte)(computed4 * 255f);
					clrBufInOut3[ordinal + 2] = (byte)(computed3 * 255f);
					clrBufInOut3[ordinal + 3] = (byte)(computed2 * 255f);
				}
			}
		}
	}

	public unsafe static void MergeColorBuffers_AVX(int[] bufA, int[] bufBInOut)
	{
		_ = bufA.Length;
		fixed (int* bufA2 = bufA)
		{
			fixed (int* bufBInOut2 = bufBInOut)
			{
				byte* bufA3 = (byte*)bufA2;
				byte* bufBInOut3 = (byte*)bufBInOut2;
				float* localValue = stackalloc float[8];
				float* localValue2 = stackalloc float[8];
				float* localValue3 = stackalloc float[8];
				float* localValue4 = stackalloc float[8];
				float* localValue5 = stackalloc float[24];
				float* localValue6 = stackalloc float[24];
				float computed = 255f;
				float unity = 1f;
				Vector256<float> right = Avx.BroadcastScalarToVector256(&computed);
				Vector256<float> vector = Avx.BroadcastScalarToVector256(&unity);
				int count = bufA.Length / 8;
				for (int i = 0; i < count; i++)
				{
					for (int j = 0; j < 8; j++)
					{
						int ordinal = 4 * j + i * 8 * 4;
						int ordinal2 = 3 * j;
						localValue5[ordinal2] = (int)bufA3[ordinal];
						localValue5[ordinal2 + 1] = (int)bufA3[ordinal + 1];
						localValue5[ordinal2 + 2] = (int)bufA3[ordinal + 2];
						localValue[j] = (int)bufA3[ordinal + 3];
						localValue6[ordinal2] = (int)bufBInOut3[ordinal];
						localValue6[ordinal2 + 1] = (int)bufBInOut3[ordinal + 1];
						localValue6[ordinal2 + 2] = (int)bufBInOut3[ordinal + 2];
						localValue2[j] = (int)bufBInOut3[ordinal + 3];
					}
					Vector256<float> left = Avx.LoadVector256(localValue);
					Vector256<float> left2 = Avx.LoadVector256(localValue2);
					left = Avx.Divide(left, right);
					left2 = Avx.Divide(left2, right);
					Vector256<float> vector2 = Avx.Subtract(vector, left);
					Vector256<float> b = Avx.Subtract(vector, left2);
					Vector256<float> vector3 = Fma.MultiplyAddNegated(vector2, b, vector);
					Vector256<float> source = Avx.Divide(left, vector3);
					Vector256<float> left3 = Avx.Divide(vector2, vector3);
					left3 = Avx.Multiply(left3, left2);
					Avx.Store(localValue, source);
					Avx.Store(localValue2, left3);
					vector3 = Avx.Multiply(vector3, right);
					Avx.Store(localValue3, vector3);
					for (int k = 0; k < 3; k++)
					{
						int ordinal3 = k * 8;
						Vector256<float> left4 = Avx.LoadVector256(localValue5 + ordinal3);
						Vector256<float> left5 = Avx.LoadVector256(localValue6 + ordinal3);
						left4 = Avx.Divide(left4, right);
						Vector256<float> left6 = Avx.Divide(left5, right);
						*localValue4 = localValue[ordinal3 / 3];
						localValue4[1] = localValue[(ordinal3 + 1) / 3];
						localValue4[2] = localValue[(ordinal3 + 2) / 3];
						localValue4[3] = localValue[(ordinal3 + 3) / 3];
						localValue4[4] = localValue[(ordinal3 + 4) / 3];
						localValue4[5] = localValue[(ordinal3 + 5) / 3];
						localValue4[6] = localValue[(ordinal3 + 6) / 3];
						localValue4[7] = localValue[(ordinal3 + 7) / 3];
						Vector256<float> b2 = Avx.LoadVector256(localValue4);
						*localValue4 = localValue2[ordinal3 / 3];
						localValue4[1] = localValue2[(ordinal3 + 1) / 3];
						localValue4[2] = localValue2[(ordinal3 + 2) / 3];
						localValue4[3] = localValue2[(ordinal3 + 3) / 3];
						localValue4[4] = localValue2[(ordinal3 + 4) / 3];
						localValue4[5] = localValue2[(ordinal3 + 5) / 3];
						localValue4[6] = localValue2[(ordinal3 + 6) / 3];
						localValue4[7] = localValue2[(ordinal3 + 7) / 3];
						Vector256<float> right2 = Avx.LoadVector256(localValue4);
						Vector256<float> c = Avx.Multiply(left6, right2);
						Vector256<float> left7 = Fma.MultiplyAdd(left4, b2, c);
						left7 = Avx.Multiply(left7, right);
						Avx.Store(localValue5 + ordinal3, left7);
					}
					for (int l = 0; l < 8; l++)
					{
						int ordinal4 = 4 * l + i * 8 * 4;
						int ordinal5 = 3 * l;
						bufBInOut3[ordinal4] = (byte)localValue5[ordinal5];
						bufBInOut3[ordinal4 + 1] = (byte)localValue5[ordinal5 + 1];
						bufBInOut3[ordinal4 + 2] = (byte)localValue5[ordinal5 + 2];
						bufBInOut3[ordinal4 + 3] = (byte)localValue3[l];
					}
				}
			}
		}
	}

	public unsafe static void MergeColorBuffers_AVX_V2(int[] bufA, int[] bufBInOut)
	{
		_ = bufA.Length;
		fixed (int* bufA2 = bufA)
		{
			fixed (int* bufBInOut2 = bufBInOut)
			{
				byte* bufA3 = (byte*)bufA2;
				byte* bufBInOut3 = (byte*)bufBInOut2;
				float* localValue = stackalloc float[8];
				float* localValue2 = stackalloc float[8];
				float* localValue3 = stackalloc float[8];
				float* localValue4 = stackalloc float[8];
				float computed = 255f;
				float unity = 1f;
				Vector256<float> right = Avx.BroadcastScalarToVector256(&computed);
				Avx.BroadcastScalarToVector256(&unity);
				int count = bufA.Length / 2;
				for (int i = 0; i < count; i++)
				{
					int ordinal = i * 2 * 4;
					*localValue = (int)bufA3[ordinal];
					localValue[1] = (int)bufA3[ordinal + 1];
					localValue[2] = (int)bufA3[ordinal + 2];
					localValue[4] = (int)bufA3[ordinal + 4];
					localValue[5] = (int)bufA3[ordinal + 5];
					localValue[6] = (int)bufA3[ordinal + 6];
					*localValue2 = (int)bufBInOut3[ordinal];
					localValue2[1] = (int)bufBInOut3[ordinal + 1];
					localValue2[2] = (int)bufBInOut3[ordinal + 2];
					localValue2[4] = (int)bufBInOut3[ordinal + 4];
					localValue2[5] = (int)bufBInOut3[ordinal + 5];
					localValue2[6] = (int)bufBInOut3[ordinal + 6];
					float bufA3Term = (float)(int)bufA3[ordinal + 3] / 255f;
					float bufA3Term2 = (float)(int)bufA3[ordinal + 7] / 255f;
					float bufBInOut3Term = (float)(int)bufBInOut3[ordinal + 3] / 255f;
					float bufBInOut3Term2 = (float)(int)bufBInOut3[ordinal + 7] / 255f;
					float computed2 = 1f - (1f - bufA3Term) * (1f - bufBInOut3Term);
					float computed3 = 1f - (1f - bufA3Term2) * (1f - bufBInOut3Term2);
					Vector256<float> left = Avx.LoadVector256(localValue);
					left = Avx.Divide(left, right);
					Vector256<float> left2 = Avx.LoadVector256(localValue2);
					left2 = Avx.Divide(left2, right);
					float computed4 = bufA3Term / computed2;
					float computed5 = bufA3Term2 / computed3;
					float computed6 = bufBInOut3Term * (1f - bufA3Term) / computed2;
					float computed7 = bufBInOut3Term2 * (1f - bufA3Term2) / computed3;
					*localValue3 = computed4;
					localValue3[1] = computed4;
					localValue3[2] = computed4;
					localValue3[4] = computed5;
					localValue3[5] = computed5;
					localValue3[6] = computed5;
					Vector256<float> b = Avx.LoadVector256(localValue3);
					*localValue4 = computed6;
					localValue4[1] = computed6;
					localValue4[2] = computed6;
					localValue4[4] = computed7;
					localValue4[5] = computed7;
					localValue4[6] = computed7;
					Vector256<float> c = Avx.Multiply(Avx.LoadVector256(localValue4), left2);
					c = Fma.MultiplyAdd(left, b, c);
					c = Avx.Multiply(c, right);
					Avx.Store(localValue, c);
					bufBInOut3[ordinal] = (byte)(*localValue);
					bufBInOut3[ordinal + 1] = (byte)localValue[1];
					bufBInOut3[ordinal + 2] = (byte)localValue[2];
					bufBInOut3[ordinal + 3] = (byte)(computed2 * 255f);
					bufBInOut3[ordinal + 4] = (byte)localValue[4];
					bufBInOut3[ordinal + 5] = (byte)localValue[5];
					bufBInOut3[ordinal + 6] = (byte)localValue[6];
					bufBInOut3[ordinal + 7] = (byte)(computed3 * 255f);
				}
			}
		}
	}

	public unsafe static void MergeColorBuffers_AVX_V3(int[] bufA, int[] bufBInOut)
	{
		if (bufA == null || bufBInOut == null || bufA.Length != bufBInOut.Length)
		{
			throw new ArgumentException("Buffers don't match");
		}
		int count = bufA.Length * 4;
		fixed (int* bufA2 = bufA)
		{
			fixed (int* bufBInOut2 = bufBInOut)
			{
				byte* bufA3 = (byte*)bufA2;
				byte* bufBInOut3 = (byte*)bufBInOut2;
				float computed = 255f;
				Vector256<float> right = Avx.BroadcastScalarToVector256(&computed);
				float* localValue = stackalloc float[8];
				for (int i = 0; i < count; i += 4)
				{
					byte b = bufA3[i];
					byte b2 = bufA3[i + 1];
					byte b3 = bufA3[i + 2];
					byte b4 = bufA3[i + 3];
					byte b5 = bufBInOut3[i];
					byte b6 = bufBInOut3[i + 1];
					byte b7 = bufBInOut3[i + 2];
					byte b8 = bufBInOut3[i + 3];
					*localValue = (int)b4;
					localValue[1] = (int)b3;
					localValue[2] = (int)b2;
					localValue[3] = (int)b;
					localValue[4] = (int)b8;
					localValue[5] = (int)b7;
					localValue[6] = (int)b6;
					localValue[7] = (int)b5;
					Vector256<float> left = Avx.LoadVector256(localValue);
					left = Avx.Divide(left, right);
					Avx.Store(localValue, left);
					float computed2 = 1f - (1f - *localValue) * (1f - localValue[4]);
					float computed3 = *localValue / computed2;
					float computed4 = localValue[4] * (1f - *localValue) / computed2;
					float computed5 = localValue[1] * computed3 + localValue[5] * computed4;
					float computed6 = localValue[2] * computed3 + localValue[6] * computed4;
					float computed7 = localValue[3] * computed3 + localValue[7] * computed4;
					bufBInOut3[i] = (byte)(computed7 * 255f);
					bufBInOut3[i + 1] = (byte)(computed6 * 255f);
					bufBInOut3[i + 2] = (byte)(computed5 * 255f);
					bufBInOut3[i + 3] = (byte)(computed2 * 255f);
				}
			}
		}
	}

	public unsafe static void MergeColorBuffers_IntegerSpace_AVX(int[] bufA, int[] bufBInOut)
	{
		if (bufA == null || bufBInOut == null || bufA.Length != bufBInOut.Length)
		{
			throw new ArgumentException("Buffers don't match");
		}
		if (bufA.Length % 4 != 0)
		{
			throw new NotImplementedException("Need to solve the final N pixels serially");
		}
		int count = bufA.Length * 4;
		fixed (int* bufA2 = bufA)
		{
			fixed (int* bufBInOut2 = bufBInOut)
			{
				byte* bufA3 = (byte*)bufA2;
				byte* bufBInOut3 = (byte*)bufBInOut2;
				ushort* localValue = stackalloc ushort[16];
				ushort* localValue2 = stackalloc ushort[16];
				ushort* localValue3 = stackalloc ushort[16];
				Vector128<ushort> right = Vector128.Create((ushort)1);
				Vector128<ushort> left = Vector128.Create((ushort)256);
				Vector128<ushort> allBitsSet = Vector128<ushort>.AllBitsSet;
				for (int i = 0; i < count; i += 16)
				{
					for (int j = 0; j < 4; j++)
					{
						int ordinal = i + j * 4 + 3;
						localValue2[j] = bufA3[ordinal];
						localValue2[j + 4] = bufBInOut3[ordinal];
					}
					Vector128<ushort> left2 = Sse2.LoadVector128(localValue2);
					left2 = Sse2.Add(left2, right);
					Vector128<ushort> vector = Sse2.Subtract(left, left2);
					Vector128<ushort> right2 = Avx.Permute(vector.AsDouble(), 1).AsUInt16();
					Vector128<ushort> right3 = Sse2.MultiplyLow(vector, right2);
					right3 = Sse2.Subtract(allBitsSet, right3);
					Sse2.Store(localValue2, right3);
					Sse2.Store(localValue2 + 4, left2);
					Sse2.Store(localValue2 + 8, vector);
					Vector256<ushort> left3 = Avx2.ConvertToVector256Int16(bufA3 + i).AsUInt16();
					Vector256<ushort> left4 = Avx2.ConvertToVector256Int16(bufBInOut3 + i).AsUInt16();
					localValue3[3] = (localValue3[2] = (localValue3[1] = (*localValue3 = localValue2[4])));
					localValue3[7] = (localValue3[6] = (localValue3[5] = (localValue3[4] = localValue2[5])));
					localValue3[11] = (localValue3[10] = (localValue3[9] = (localValue3[8] = localValue2[6])));
					localValue3[15] = (localValue3[14] = (localValue3[13] = (localValue3[12] = localValue2[7])));
					Vector256<ushort> right4 = Avx.LoadVector256(localValue3);
					localValue3[3] = (localValue3[2] = (localValue3[1] = (*localValue3 = localValue2[8])));
					localValue3[7] = (localValue3[6] = (localValue3[5] = (localValue3[4] = localValue2[9])));
					localValue3[11] = (localValue3[10] = (localValue3[9] = (localValue3[8] = localValue2[10])));
					localValue3[15] = (localValue3[14] = (localValue3[13] = (localValue3[12] = localValue2[11])));
					Vector256<ushort> right5 = Avx.LoadVector256(localValue3);
					Vector256<ushort> left5 = Avx2.MultiplyLow(left3, right4);
					Vector256<ushort> right6 = Avx2.MultiplyLow(left4, right5);
					Vector256<ushort> source = Avx2.AddSaturate(left5, right6);
					Avx.Store(localValue, source);
					for (int k = 0; k < 4; k++)
					{
						int ordinal2 = k * 8;
						int ordinal3 = k * 4;
						int ordinal4 = k * 2;
						bufBInOut3[i + ordinal3] = ((byte*)localValue)[ordinal2 + 1];
						bufBInOut3[i + ordinal3 + 1] = ((byte*)localValue)[ordinal2 + 3];
						bufBInOut3[i + ordinal3 + 2] = ((byte*)localValue)[ordinal2 + 5];
						bufBInOut3[i + ordinal3 + 3] = ((byte*)localValue2)[ordinal4];
					}
				}
			}
		}
	}

	public static int Color(byte a, byte r, byte g, byte b)
	{
		return b + (g << 8) + (r << 16) + (a << 24);
	}

	public static void DecomposeColor(int color, out byte a, out byte r, out byte g, out byte b)
	{
		b = (byte)(color & 0xFF);
		g = (byte)((color & 0xFF00) >> 8);
		r = (byte)((color & 0xFF0000) >> 16);
		a = (byte)((color & -16777216) >> 24);
	}

	public static void CopyAndPremultiply(Span<int> source, Span<int> destination)
	{
		if (Avx2.IsSupported && source.Length >= 8)
		{
			CopyAndPremultiply_Avx2(source, destination);
		}
		else
		{
			CopyAndPremultiply_Scalar(source, destination);
		}
	}

	private static void CopyAndPremultiply_Scalar(Span<int> source, Span<int> destination)
	{
		ref byte source2 = ref Unsafe.As<int, byte>(ref MemoryMarshal.GetReference(source));
		ref byte source3 = ref Unsafe.As<int, byte>(ref MemoryMarshal.GetReference(destination));
		int count = source.Length * 4;
		for (int i = 0; i < count; i += 4)
		{
			int source2Term = Unsafe.Add(ref source2, i + 3) + 1;
			Unsafe.Add(ref source3, i) = (byte)(Unsafe.Add(ref source2, i) * source2Term >> 8);
			Unsafe.Add(ref source3, i + 1) = (byte)(Unsafe.Add(ref source2, i + 1) * source2Term >> 8);
			Unsafe.Add(ref source3, i + 2) = (byte)(Unsafe.Add(ref source2, i + 2) * source2Term >> 8);
			Unsafe.Add(ref source3, i + 3) = (byte)(source2Term - 1);
		}
	}

	private static void CopyAndPremultiply_Avx2(Span<int> source, Span<int> destination)
	{
		ref byte source2 = ref Unsafe.As<int, byte>(ref MemoryMarshal.GetReference(source));
		ref byte reference = ref Unsafe.As<int, byte>(ref MemoryMarshal.GetReference(destination));
		int count = source.Length * 4;
		int i = 0;
		Vector256<byte> mask = Vector256.Create((byte)3, (byte)3, (byte)3, (byte)3, (byte)7, (byte)7, (byte)7, (byte)7, (byte)11, (byte)11, (byte)11, (byte)11, (byte)15, (byte)15, (byte)15, (byte)15, (byte)3, (byte)3, (byte)3, (byte)3, (byte)7, (byte)7, (byte)7, (byte)7, (byte)11, (byte)11, (byte)11, (byte)11, (byte)15, (byte)15, (byte)15, (byte)15);
		Vector256<byte> mask2 = Vector256.Create(0, 0, 0, byte.MaxValue, 0, 0, 0, byte.MaxValue, 0, 0, 0, byte.MaxValue, 0, 0, 0, byte.MaxValue, 0, 0, 0, byte.MaxValue, 0, 0, 0, byte.MaxValue, 0, 0, 0, byte.MaxValue, 0, 0, 0, byte.MaxValue);
		Vector256<byte> zero = Vector256<byte>.Zero;
		Vector256<ushort> right = Vector256.Create((ushort)1);
		for (; i <= count - 32; i += 32)
		{
			Vector256<byte> vector = Vector256.LoadUnsafe(in source2, (nuint)i);
			Vector256<byte> left = Avx2.Shuffle(vector, mask);
			Vector256<ushort> left2 = Avx2.UnpackLow(vector, zero).AsUInt16();
			Vector256<ushort> left3 = Avx2.UnpackHigh(vector, zero).AsUInt16();
			Vector256<ushort> right2 = Avx2.Add(Avx2.UnpackLow(left, zero).AsUInt16(), right);
			Vector256<ushort> right3 = Avx2.Add(Avx2.UnpackHigh(left, zero).AsUInt16(), right);
			Vector256<ushort> vector2 = Avx2.ShiftRightLogical(Avx2.MultiplyLow(left2, right2), 8);
			Avx2.BlendVariable(Avx2.PackUnsignedSaturate(right: Avx2.ShiftRightLogical(Avx2.MultiplyLow(left3, right3), 8).AsInt16(), left: vector2.AsInt16()), vector, mask2).StoreUnsafe(ref reference, (nuint)i);
		}
		for (; i < count; i += 4)
		{
			int source2Term = Unsafe.Add(ref source2, i + 3) + 1;
			Unsafe.Add(ref reference, i) = (byte)(Unsafe.Add(ref source2, i) * source2Term >> 8);
			Unsafe.Add(ref reference, i + 1) = (byte)(Unsafe.Add(ref source2, i + 1) * source2Term >> 8);
			Unsafe.Add(ref reference, i + 2) = (byte)(Unsafe.Add(ref source2, i + 2) * source2Term >> 8);
			Unsafe.Add(ref reference, i + 3) = (byte)(source2Term - 1);
		}
	}
}
