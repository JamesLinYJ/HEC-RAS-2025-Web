using System;

namespace Utility;

public static class RandomHelpers
{
	public static float[] GenerateSingle(int arraySize)
	{
		Random random = new Random(0);
		float[] floatValues = new float[arraySize];
		for (int i = 0; i < arraySize; i++)
		{
			floatValues[i] = (float)random.NextDouble();
		}
		return floatValues;
	}

	public static double[] GenerateDouble(int arraySize)
	{
		Random random = new Random(0);
		double[] doubleValues = new double[arraySize];
		for (int i = 0; i < arraySize; i++)
		{
			doubleValues[i] = random.NextDouble();
		}
		return doubleValues;
	}
}
