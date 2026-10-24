using System;

namespace Utility.Hashing;

public static class Adler32
{
	private const int BASE = 65521;

	private const int NMAX = 5552;

	public static byte[] ComputeAdler32(ReadOnlySpan<byte> buffer)
	{
		byte[] adler32 = new byte[4];
		ComputeAdler32(buffer, adler32);
		return adler32;
	}

	public static void ComputeAdler32(ReadOnlySpan<byte> buffer, Span<byte> result)
	{
		long partialAdler32 = ComputePartialAdler32(1L, buffer);
		PutShortMSB((int)URShift(partialAdler32, 16), result);
		PutShortMSB((int)(partialAdler32 & 0xFFFF), result.Slice(2));
	}

	private static long ComputePartialAdler32(long adler, ReadOnlySpan<byte> buf)
	{
		if (buf.IsEmpty)
		{
			return 1L;
		}
		long bufSum = adler & 0xFFFF;
		long bufSum2 = (adler >> 16) & 0xFFFF;
		int bufLength = buf.Length;
		int ordinal = 0;
		while (bufLength > 0)
		{
			int bufLength2 = ((bufLength < 5552) ? bufLength : 5552);
			bufLength -= bufLength2;
			while (bufLength2 >= 16)
			{
				bufSum += buf[ordinal++];
				bufSum2 += bufSum;
				bufSum += buf[ordinal++];
				bufSum2 += bufSum;
				bufSum += buf[ordinal++];
				bufSum2 += bufSum;
				bufSum += buf[ordinal++];
				bufSum2 += bufSum;
				bufSum += buf[ordinal++];
				bufSum2 += bufSum;
				bufSum += buf[ordinal++];
				bufSum2 += bufSum;
				bufSum += buf[ordinal++];
				bufSum2 += bufSum;
				bufSum += buf[ordinal++];
				bufSum2 += bufSum;
				bufSum += buf[ordinal++];
				bufSum2 += bufSum;
				bufSum += buf[ordinal++];
				bufSum2 += bufSum;
				bufSum += buf[ordinal++];
				bufSum2 += bufSum;
				bufSum += buf[ordinal++];
				bufSum2 += bufSum;
				bufSum += buf[ordinal++];
				bufSum2 += bufSum;
				bufSum += buf[ordinal++];
				bufSum2 += bufSum;
				bufSum += buf[ordinal++];
				bufSum2 += bufSum;
				bufSum += buf[ordinal++];
				bufSum2 += bufSum;
				bufLength2 -= 16;
			}
			if (bufLength2 != 0)
			{
				do
				{
					bufSum += buf[ordinal++];
					bufSum2 += bufSum;
				}
				while (--bufLength2 != 0);
			}
			bufSum %= 65521;
			bufSum2 %= 65521;
		}
		return (bufSum2 << 16) | bufSum;
	}

	private static long URShift(long number, int bits)
	{
		if (number >= 0)
		{
			return number >> bits;
		}
		return (number >> bits) + (2L << ~bits);
	}

	private static void PutShortMSB(int b, Span<byte> res)
	{
		res[0] = (byte)(b >> 8);
		res[1] = (byte)b;
	}
}
