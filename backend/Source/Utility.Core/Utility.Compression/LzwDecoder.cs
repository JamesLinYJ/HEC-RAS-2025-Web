using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Utility.Logging;

namespace Utility.Compression;

public class LzwDecoder
{
	private ref struct BitReader(ReadOnlySpan<byte> data, BitPacking packing)
	{
		private ReadOnlySpan<byte> _data = data;

		private int _index = 0;

		private uint _bitBuffer = 0u;

		private int _bitCount = 0;

		private readonly BitPacking _packing = packing;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int ReadBits(int n)
		{
			if ((uint)n > 24u)
			{
				throw new ArgumentOutOfRangeException("n", "n must be <= 24.");
			}
			if (_packing == BitPacking.LsbFirst)
			{
				if (_bitCount < n)
				{
					if (n <= 16 && _index + 1 < _data.Length)
					{
						_bitBuffer |= (uint)(_data[_index++] << _bitCount);
						_bitCount += 8;
						if (_bitCount < n)
						{
							_bitBuffer |= (uint)(_data[_index++] << _bitCount);
							_bitCount += 8;
						}
					}
					else
					{
						while (_bitCount < n)
						{
							if (_index >= _data.Length)
							{
								throw new InvalidOperationException("Unexpected end of compressed data.");
							}
							_bitBuffer |= (uint)(_data[_index++] << _bitCount);
							_bitCount += 8;
						}
					}
				}
				uint ordinal = (uint)((1 << n) - 1);
				uint result = _bitBuffer & ordinal;
				_bitBuffer >>= n;
				_bitCount -= n;
				return (int)result;
			}
			if (_bitCount < n)
			{
				if (n <= 16 && _index + 1 < _data.Length)
				{
					_bitBuffer = (_bitBuffer << 8) | _data[_index++];
					_bitCount += 8;
					if (_bitCount < n)
					{
						_bitBuffer = (_bitBuffer << 8) | _data[_index++];
						_bitCount += 8;
					}
				}
				else
				{
					while (_bitCount < n)
					{
						if (_index >= _data.Length)
						{
							throw new InvalidOperationException("Unexpected end of compressed data.");
						}
						_bitBuffer = (_bitBuffer << 8) | _data[_index++];
						_bitCount += 8;
					}
				}
			}
			int bitCountTerm = _bitCount - n;
			uint ordinal2 = (uint)((1 << n) - 1);
			uint result2 = (_bitBuffer >> bitCountTerm) & ordinal2;
			_bitCount -= n;
			_bitBuffer &= (uint)((1 << _bitCount) - 1);
			return (int)result2;
		}
	}

	public enum BitPacking
	{
		LsbFirst,
		MsbFirst
	}

	public readonly struct Options(int alphabetSize, bool hasClearCode, bool hasEndCode, int clearCode, int endCode, int initialCodeSize, int maxCodeBits, BitPacking packing, int predictor, int samplesPerPixel, int bitsPerSample)
	{
		public readonly int AlphabetSize = alphabetSize;

		public readonly bool HasClearCode = hasClearCode;

		public readonly bool HasEndCode = hasEndCode;

		public readonly int ClearCode = clearCode;

		public readonly int EndCode = endCode;

		public readonly int InitialCodeSize = initialCodeSize;

		public readonly int MaxCodeBits = maxCodeBits;

		public readonly BitPacking Packing = packing;

		public readonly int Predictor = predictor;

		public readonly int SamplesPerPixel = samplesPerPixel;

		public readonly int BitsPerSample = bitsPerSample;

		public static Options Tiff(int samplesPerPixel, int bitsPerSample, int predictor)
		{
			int msbFirstIds = 12;
			if (msbFirstIds < 9 || msbFirstIds > 16)
			{
				throw new ArgumentOutOfRangeException("maxCodeBits", "maxCodeBits must be between 9 and 16.");
			}
			return new Options(256, hasClearCode: true, hasEndCode: true, 256, 257, 9, msbFirstIds, BitPacking.MsbFirst, predictor, samplesPerPixel, bitsPerSample);
		}
	}

	private static OperationResult CanDecode(Options opts)
	{
		OperationResult operationResult = new OperationResult();
		if (opts.BitsPerSample != 32)
		{
			operationResult.AddFailure("Data isn't FP32");
		}
		if (opts.SamplesPerPixel != 1)
		{
			operationResult.AddFailure("Data has multiple channels (must be single band)");
		}
		if (opts.Predictor != 1 && opts.Predictor != 2 && opts.Predictor != 3)
		{
			operationResult.AddFailure("Predictor not recognized: " + opts.Predictor);
		}
		return operationResult;
	}

	public static int Decode(ReadOnlySpan<byte> compressed, Span<byte> output, Options options, int pixelsPerRow)
	{
		if (options.AlphabetSize < 2 || options.AlphabetSize > 256)
		{
			throw new ArgumentOutOfRangeException("options", "AlphabetSize must be in [2, 256].");
		}
		if (options.MaxCodeBits < options.InitialCodeSize || options.MaxCodeBits > 24)
		{
			throw new ArgumentOutOfRangeException("options", "MaxCodeBits must be >= InitialCodeSize and <= 24.");
		}
		int decodeInternal = DecodeInternal(compressed, output, options);
		if (options.Predictor != 1)
		{
			if (options.Predictor == 2)
			{
				UndoHorizontalPredictor_1SPP(output.Slice(0, decodeInternal), options.BitsPerSample / 8, pixelsPerRow);
			}
			else
			{
				if (options.Predictor != 3)
				{
					throw new NotSupportedException("Predictor not recognized: " + options.Predictor);
				}
				UndoFpPredictor_1SPP(output.Slice(0, decodeInternal), options.BitsPerSample / 8, pixelsPerRow);
			}
		}
		return decodeInternal;
	}

	private static void Decode(ReadOnlySpan<byte> compressed, IBufferWriter<byte> output, Options options)
	{
		if (output == null)
		{
			throw new ArgumentNullException("output");
		}
		if (options.AlphabetSize < 2 || options.AlphabetSize > 256)
		{
			throw new ArgumentOutOfRangeException("options", "AlphabetSize must be in [2, 256].");
		}
		if (options.MaxCodeBits < options.InitialCodeSize || options.MaxCodeBits > 24)
		{
			throw new ArgumentOutOfRangeException("options", "MaxCodeBits must be >= InitialCodeSize and <= 24.");
		}
		if (options.Predictor == 2)
		{
			ArrayBufferWriter<byte> arrayBufferWriter = new ArrayBufferWriter<byte>(Math.Max(64, compressed.Length * 2));
			DecodeInternal(compressed, arrayBufferWriter, options);
			byte[] writtenSpan = arrayBufferWriter.WrittenSpan.ToArray();
			UndoHorizontalPredictor_1SPP(writtenSpan, options.SamplesPerPixel, options.BitsPerSample);
			output.Write(writtenSpan);
		}
		else
		{
			DecodeInternal(compressed, output, options);
		}
	}

	private static int DecodeInternal(ReadOnlySpan<byte> compressed, Span<byte> output, Options options)
	{
		int count = 1 << options.MaxCodeBits;
		int[] rent = null;
		byte[] rentArray = null;
		Span<int> intSpan = ((count <= 65536) ? (rent = ArrayPool<int>.Shared.Rent(count)).AsSpan(0, count) : ((Span<int>)new int[count]));
		Span<byte> byteSpan = ((count <= 65536) ? (rentArray = ArrayPool<byte>.Shared.Rent(count)).AsSpan(0, count) : ((Span<byte>)new byte[count]));
		for (int i = 0; i < options.AlphabetSize; i++)
		{
			intSpan[i] = -1;
			byteSpan[i] = (byte)i;
		}
		int codeSize = options.InitialCodeSize;
		int oldCode = -1;
		int clearCode = options.ClearCode;
		int endCode = options.EndCode;
		int codeSizeLimit = 1 << codeSize;
		int intervalIndex = 0;
		Span<byte> outputSpan = output;
		int nextCode = default;
		Reset();
		Span<byte> byteSpan2 = ((count > 4096) ? default(Span<byte>) : stackalloc byte[count]);
		Span<byte> byteSpan3 = byteSpan2;
		byte[] rent2 = null;
		Span<byte> byteSpan4 = ((count <= 4096) ? byteSpan3 : (rent2 = ArrayPool<byte>.Shared.Rent(count)).AsSpan(0, count));
		try
		{
			BitReader bitReader = new BitReader(compressed, options.Packing);
			while (true)
			{
				int oldCode2 = bitReader.ReadBits(codeSize);
				if (options.HasClearCode && oldCode2 == clearCode)
				{
					Reset();
					continue;
				}
				if (options.HasEndCode && oldCode2 == endCode)
				{
					break;
				}
				int bits = oldCode2;
				int intervalIndex2 = 0;
				if (bits < nextCode)
				{
					int bits2 = bits;
					while (bits2 >= options.AlphabetSize)
					{
						byteSpan4[intervalIndex2++] = byteSpan[bits2];
						bits2 = intSpan[bits2];
						if (bits2 < 0)
						{
							throw new InvalidOperationException("Invalid prefix chain in LZW stream.");
						}
					}
					byte b = (byte)bits2;
					byteSpan4[intervalIndex2++] = b;
					if (oldCode >= 0 && nextCode < count)
					{
						intSpan[nextCode] = oldCode;
						byteSpan[nextCode] = b;
						nextCode++;
						MaybeGrowCodeSize();
					}
				}
				else
				{
					if (bits != nextCode || oldCode < 0)
					{
						throw new InvalidOperationException("Invalid LZW code encountered.");
					}
					intervalIndex2 = 1;
					int oldCode3 = oldCode;
					while (oldCode3 >= options.AlphabetSize)
					{
						byteSpan4[intervalIndex2++] = byteSpan[oldCode3];
						oldCode3 = intSpan[oldCode3];
						if (oldCode3 < 0)
						{
							throw new InvalidOperationException("Invalid prefix chain in LZW stream.");
						}
					}
					byte b = (byte)oldCode3;
					byteSpan4[intervalIndex2++] = b;
					byteSpan4[0] = b;
					if (nextCode < count)
					{
						intSpan[nextCode] = oldCode;
						byteSpan[nextCode] = b;
						nextCode++;
						MaybeGrowCodeSize();
					}
				}
				if (intervalIndex + intervalIndex2 > outputSpan.Length)
				{
					throw new InvalidOperationException("Output buffer is too small for decompressed data.");
				}
				int ordinal = intervalIndex2 - 1;
				int ordinal2 = 0;
				while (ordinal >= 0)
				{
					outputSpan[intervalIndex + ordinal2] = byteSpan4[ordinal];
					ordinal--;
					ordinal2++;
				}
				intervalIndex += intervalIndex2;
				oldCode = oldCode2;
			}
			return intervalIndex;
		}
		finally
		{
			if (rent2 != null)
			{
				ArrayPool<byte>.Shared.Return(rent2);
			}
			if (rent != null)
			{
				ArrayPool<int>.Shared.Return(rent, clearArray: true);
			}
			if (rentArray != null)
			{
				ArrayPool<byte>.Shared.Return(rentArray, clearArray: true);
			}
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		void MaybeGrowCodeSize()
		{
			if (options.Packing == BitPacking.MsbFirst && options.HasClearCode)
			{
				if (nextCode >= codeSizeLimit - 1 && codeSize < options.MaxCodeBits)
				{
					codeSize++;
					codeSizeLimit <<= 1;
				}
			}
			else if (nextCode == codeSizeLimit && codeSize < options.MaxCodeBits)
			{
				codeSize++;
				codeSizeLimit <<= 1;
			}
		}
		void Reset()
		{
			if (options.HasClearCode)
			{
				nextCode = endCode + 1;
			}
			else
			{
				nextCode = options.AlphabetSize;
			}
			codeSize = options.InitialCodeSize;
			codeSizeLimit = 1 << codeSize;
			oldCode = -1;
		}
	}

	private static void DecodeInternal(ReadOnlySpan<byte> compressed, IBufferWriter<byte> output, Options options)
	{
		int count = 1 << options.MaxCodeBits;
		int[] rent = null;
		byte[] rentArray = null;
		Span<int> intSpan = ((count <= 65536) ? (rent = ArrayPool<int>.Shared.Rent(count)).AsSpan(0, count) : ((Span<int>)new int[count]));
		Span<byte> byteSpan = ((count <= 65536) ? (rentArray = ArrayPool<byte>.Shared.Rent(count)).AsSpan(0, count) : ((Span<byte>)new byte[count]));
		for (int i = 0; i < options.AlphabetSize; i++)
		{
			intSpan[i] = -1;
			byteSpan[i] = (byte)i;
		}
		int codeSize = options.InitialCodeSize;
		int oldCode = -1;
		int clearCode = options.ClearCode;
		int endCode = options.EndCode;
		int codeSizeLimit = 1 << codeSize;
		int nextCode = default;
		Reset();
		Span<byte> byteSpan2 = ((count > 4096) ? default(Span<byte>) : stackalloc byte[count]);
		Span<byte> byteSpan3 = byteSpan2;
		byte[] rent2 = null;
		Span<byte> byteSpan4 = ((count <= 4096) ? byteSpan3 : (rent2 = ArrayPool<byte>.Shared.Rent(count)).AsSpan(0, count));
		try
		{
			BitReader bitReader = new BitReader(compressed, options.Packing);
			while (true)
			{
				int oldCode2 = bitReader.ReadBits(codeSize);
				if (options.HasClearCode && oldCode2 == clearCode)
				{
					Reset();
					continue;
				}
				if (options.HasEndCode && oldCode2 == endCode)
				{
					return;
				}
				int bits = oldCode2;
				int intervalIndex = 0;
				if (bits < nextCode)
				{
					int bits2 = bits;
					while (bits2 >= options.AlphabetSize)
					{
						byteSpan4[intervalIndex++] = byteSpan[bits2];
						bits2 = intSpan[bits2];
						if (bits2 < 0)
						{
							throw new InvalidOperationException("Invalid prefix chain in LZW stream.");
						}
					}
					byte b = (byte)bits2;
					byteSpan4[intervalIndex++] = b;
					if (oldCode >= 0 && nextCode < count)
					{
						intSpan[nextCode] = oldCode;
						byteSpan[nextCode] = b;
						nextCode++;
						MaybeGrowCodeSize();
					}
				}
				else
				{
					if (bits != nextCode || oldCode < 0)
					{
						break;
					}
					intervalIndex = 1;
					int oldCode3 = oldCode;
					while (oldCode3 >= options.AlphabetSize)
					{
						byteSpan4[intervalIndex++] = byteSpan[oldCode3];
						oldCode3 = intSpan[oldCode3];
						if (oldCode3 < 0)
						{
							throw new InvalidOperationException("Invalid prefix chain in LZW stream.");
						}
					}
					byte b = (byte)oldCode3;
					byteSpan4[intervalIndex++] = b;
					byteSpan4[0] = b;
					if (nextCode < count)
					{
						intSpan[nextCode] = oldCode;
						byteSpan[nextCode] = b;
						nextCode++;
						MaybeGrowCodeSize();
					}
				}
				Span<byte> getSpan = output.GetSpan(intervalIndex);
				int ordinal = intervalIndex - 1;
				int ordinal2 = 0;
				while (ordinal >= 0)
				{
					getSpan[ordinal2] = byteSpan4[ordinal];
					ordinal--;
					ordinal2++;
				}
				output.Advance(intervalIndex);
				oldCode = oldCode2;
			}
			throw new InvalidOperationException("Invalid LZW code encountered.");
		}
		finally
		{
			if (rent2 != null)
			{
				ArrayPool<byte>.Shared.Return(rent2);
			}
			if (rent != null)
			{
				ArrayPool<int>.Shared.Return(rent, clearArray: true);
			}
			if (rentArray != null)
			{
				ArrayPool<byte>.Shared.Return(rentArray, clearArray: true);
			}
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		void MaybeGrowCodeSize()
		{
			if (options.Packing == BitPacking.MsbFirst && options.HasClearCode)
			{
				if (nextCode >= codeSizeLimit - 1 && codeSize < options.MaxCodeBits)
				{
					codeSize++;
					codeSizeLimit <<= 1;
				}
			}
			else if (nextCode == codeSizeLimit && codeSize < options.MaxCodeBits)
			{
				codeSize++;
				codeSizeLimit <<= 1;
			}
		}
		void Reset()
		{
			if (options.HasClearCode)
			{
				nextCode = endCode + 1;
			}
			else
			{
				nextCode = options.AlphabetSize;
			}
			codeSize = options.InitialCodeSize;
			codeSizeLimit = 1 << codeSize;
			oldCode = -1;
		}
	}

	public static byte[] Decode(ReadOnlySpan<byte> compressed, Options options)
	{
		ArrayBufferWriter<byte> arrayBufferWriter = new ArrayBufferWriter<byte>(Math.Max(64, compressed.Length * 2));
		Decode(compressed, arrayBufferWriter, options);
		return arrayBufferWriter.WrittenSpan.ToArray();
	}

	public static void UndoHorizontalPredictor_1SPP(Span<byte> data, int bytesPerPixel, int pixelsPerRow)
	{
		int bytesPerPixelTimesPixelsPerRow = bytesPerPixel * pixelsPerRow;
		int count = data.Length / bytesPerPixelTimesPixelsPerRow;
		if (data.Length % bytesPerPixelTimesPixelsPerRow != 0)
		{
			throw new ArgumentException("Row/Column values don't match given data span.");
		}
		for (int i = 0; i < count; i++)
		{
			UndoHorizontalPredictorRow_1SPP(data.Slice(i * bytesPerPixelTimesPixelsPerRow, bytesPerPixelTimesPixelsPerRow), bytesPerPixel);
		}
	}

	public static void UndoFpPredictor_1SPP(Span<byte> data, int bytesPerPixel, int pixelsPerRow)
	{
		int count = bytesPerPixel * pixelsPerRow;
		int count2 = data.Length / count;
		if (data.Length % count != 0)
		{
			throw new ArgumentException("Row/Column values don't match given data span.");
		}
		Span<byte> byteSpan = ((count <= 4096) ? stackalloc byte[count] : ((Span<byte>)new byte[count]));
		Span<byte> scratchSpace = byteSpan;
		for (int i = 0; i < count2; i++)
		{
			UndoFpPredictorRow_1SPP(data.Slice(i * count, count), scratchSpace, bytesPerPixel);
		}
	}

	private static void UndoHorizontalPredictorRow_1SPP(Span<byte> row, int bytesPerPixel)
	{
		int count = row.Length / bytesPerPixel;
		Span<int> cast = MemoryMarshal.Cast<byte, int>(row);
		for (int i = 1; i < count; i++)
		{
			cast[i] += cast[i - 1];
		}
	}

	private static void UndoFpPredictorRow_1SPP(Span<byte> row, Span<byte> scratchSpace, int bytesPerPixel)
	{
		int length = row.Length;
		int count = length / bytesPerPixel;
		for (int i = 0; i < length - 1; i++)
		{
			row[i + 1] += row[i];
		}
		row.CopyTo(scratchSpace);
		for (int j = 0; j < count; j++)
		{
			int bytesPerPixelTerm = bytesPerPixel * j;
			for (int k = 0; k < bytesPerPixel; k++)
			{
				row[bytesPerPixelTerm + k] = scratchSpace[(bytesPerPixel - k - 1) * count + j];
			}
		}
	}
}
