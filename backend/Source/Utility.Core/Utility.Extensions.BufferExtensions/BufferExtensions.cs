using System;

namespace Utility.Extensions.BufferExtensions;

public static class BufferExtensions
{
	public static void AssignRandomData(this float[] buffer, int seed = 0)
	{
		Random random = new Random(seed);
		for (int i = 0; i < buffer.Length; i++)
		{
			double nextDouble = random.NextDouble();
			buffer[i] = (float)nextDouble;
		}
	}

	public static void AssignRandomData(this double[] buffer, int seed = 0)
	{
		Random random = new Random(seed);
		for (int i = 0; i < buffer.Length; i++)
		{
			double nextDouble = random.NextDouble();
			buffer[i] = nextDouble;
		}
	}

	public static void AssignRandomData(this byte[] buffer, int seed = 0)
	{
		Random random = new Random(seed);
		for (int i = 0; i < buffer.Length; i++)
		{
			buffer[i] = (byte)random.Next(0, 255);
		}
	}

	public static void AssignRandomData(this short[] buffer, int seed = 0)
	{
		Random random = new Random(seed);
		for (int i = 0; i < buffer.Length; i++)
		{
			buffer[i] = (short)random.Next(-32768, 32767);
		}
	}

	public static void AssignRandomData(this int[] buffer, int seed = 0)
	{
		Random random = new Random(seed);
		for (int i = 0; i < buffer.Length; i++)
		{
			buffer[i] = random.Next(int.MinValue, int.MaxValue);
		}
	}

	public static void AssignRandomData(this long[] buffer, int seed = 0)
	{
		Random random = new Random(seed);
		for (int i = 0; i < buffer.Length; i++)
		{
			buffer[i] = random.Next(int.MinValue, int.MaxValue);
		}
	}
}
