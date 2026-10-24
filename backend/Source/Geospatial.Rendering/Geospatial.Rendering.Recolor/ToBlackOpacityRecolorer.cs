using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Geospatial.Rendering.Recolor;

public class ToBlackOpacityRecolorer : IRecolorer
{
	public void RecolorARGB(Memory<int> input, Memory<int> output)
	{
		if (input.Length != output.Length)
		{
			throw new ArgumentException();
		}
		Span<int> inputSpan = input.Span;
		Span<int> outputSpan = output.Span;
		int i = 0;
		if (Vector.IsHardwareAccelerated)
		{
			int count = Vector<int>.Count;
			Vector<int> vector = new Vector<int>(255);
			Vector<int> vector2 = new Vector<int>(77);
			Vector<int> vector3 = new Vector<int>(150);
			Vector<int> vector4 = new Vector<int>(29);
			Vector<int> vector5 = new Vector<int>(255);
			for (int count2 = input.Length - count; i <= count2; i += count)
			{
				Vector<int> vector6 = new Vector<int>((ReadOnlySpan<int>)inputSpan.Slice(i, count));
				Vector<int> vector7 = (vector6 >> 16) & vector;
				Vector<int> vector8 = (vector6 >> 8) & vector;
				Vector<int> vector9 = vector6 & vector;
				Vector<int> vector10 = vector2 * vector7 + vector3 * vector8 + vector4 * vector9 >> 8;
				Vector<int> vector11 = vector5 - vector10;
				Vector<int> left = vector11 << 24;
				Vector<int> right = Vector.Equals(vector11, vector5);
				Vector.AndNot(left, right).CopyTo(outputSpan.Slice(i, count));
			}
		}
		for (; i < input.Length; i++)
		{
			int input2 = inputSpan[i];
			int input2Term = (input2 >> 16) & 0xFF;
			int input2Term2 = (input2 >> 8) & 0xFF;
			int input2Term3 = input2 & 0xFF;
			int ordinal = 77 * input2Term + 150 * input2Term2 + 29 * input2Term3 >> 8;
			int ordinal2 = 255 - ordinal;
			outputSpan[i] = ((ordinal2 != 255) ? (ordinal2 << 24) : 0);
		}
	}

	public void RecolorARGB_Slow(Memory<int> input, Memory<int> output)
	{
		if (input.Length != output.Length)
		{
			throw new ArgumentException();
		}
		Span<int> inputSpan = input.Span;
		Span<int> outputSpan = output.Span;
		for (int i = 0; i < input.Length; i++)
		{
			int input2 = inputSpan[i];
			byte b = (byte)((input2 >> 16) & 0xFF);
			byte b2 = (byte)((input2 >> 8) & 0xFF);
			byte b3 = (byte)(input2 & 0xFF);
			double b2B3Term = 0.3 * (double)(int)b + 0.59 * (double)(int)b2 + 0.11 * (double)(int)b3;
			int ordinal = 255 - (int)b2B3Term;
			int ordinal2 = 0;
			int outputSpan2 = (ordinal << 24) | (ordinal2 << 16) | (ordinal2 << 8) | ordinal2;
			if (ordinal == 255)
			{
				outputSpan2 = 0;
			}
			outputSpan[i] = outputSpan2;
		}
	}

	public void RecolorBrightness(Memory<byte> input, Memory<int> output)
	{
		if (input.Length != output.Length)
		{
			throw new ArgumentException();
		}
		Span<byte> inputSpan = input.Span;
		Span<int> outputSpan = output.Span;
		int i = 0;
		if (Vector.IsHardwareAccelerated)
		{
			Span<uint> outSpan = MemoryMarshal.Cast<int, uint>(outputSpan);
			int count = Vector<byte>.Count;
			int count2 = Vector<uint>.Count;
			Vector<uint> vec = new Vector<uint>(255u);
			for (int count3 = input.Length - count; i <= count3; i += count)
			{
				Vector.Widen(new Vector<byte>((ReadOnlySpan<byte>)inputSpan.Slice(i, count)), out var low, out var high);
				Vector.Widen(low, out var low2, out var high2);
				Vector.Widen(high, out var low3, out var high3);
				StoreChunk(low2, vec, outSpan, i);
				StoreChunk(high2, vec, outSpan, i + count2);
				StoreChunk(low3, vec, outSpan, i + count2 * 2);
				StoreChunk(high3, vec, outSpan, i + count2 * 3);
			}
		}
		for (; i < input.Length; i++)
		{
			int inputSpanTerm = 255 - inputSpan[i];
			outputSpan[i] = ((inputSpanTerm != 255) ? (inputSpanTerm << 24) : 0);
		}
	}

	public void RecolorBrightness_Slow(Memory<byte> input, Memory<int> output)
	{
		if (input.Length != output.Length)
		{
			throw new ArgumentException();
		}
		Span<byte> inputSpan = input.Span;
		Span<int> outputSpan = output.Span;
		for (int i = 0; i < input.Length; i++)
		{
			int inputSpanTerm = 255 - inputSpan[i];
			outputSpan[i] = ((inputSpanTerm != 255) ? (inputSpanTerm << 24) : 0);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void StoreChunk(Vector<uint> pixels, Vector<uint> vec255, Span<uint> outSpan, int offset)
	{
		Vector<uint> vector = vec255 - pixels;
		Vector<uint> left = vector << 24;
		Vector<uint> right = Vector.Equals(vector, vec255);
		Vector.AndNot(left, right).CopyTo(outSpan.Slice(offset, Vector<uint>.Count));
	}
}
